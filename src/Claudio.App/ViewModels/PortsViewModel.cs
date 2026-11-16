using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// Drives the Ports tab, as Claudy's <c>PortsViewModel</c>: what is open, the MCP servers left
/// behind, how old they are, and what happened to a kill. State changes only on the UI thread, and
/// <see cref="Changed"/> follows each one, every scan included, so the ages on screen keep up.
///
/// The scans run off the UI thread: the system calls are cheap but not free, and the card must
/// never wait on them. The cadence is slower in the background than on screen: the only thing a
/// hidden tab owes the user is a correct badge. Confirming a bulk kill is the view's job.
/// </summary>
internal sealed class PortsViewModel : IDisposable
{
    private static readonly TimeSpan VisibleInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BackgroundInterval = TimeSpan.FromSeconds(30);

    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _timer;
    private readonly PortScanner _scanner = new(WindowsProcesses.Listeners, WindowsProcesses.Table, WindowsProcesses.Markers);
    private readonly McpScanner _mcpScanner;
    private readonly WindowsProcesses.Signals _signals = new();
    private readonly PortReaper _reaper;
    private readonly Dictionary<string, string> _failures = [];
    private int _isScanning;
    private int _isRequested;
    private volatile bool _isDisposed;

    /// <param name="liveSessionIds">
    /// The ids of the Claude Code sessions open now, as the sessions scan reads them: a server
    /// whose <c>CLAUDE_CODE_SESSION_ID</c> names one of them is still in use, even when the process
    /// tree no longer links it to its session. Called off the UI thread, so it must be safe there.
    /// Without it, no session is known to be live and only the tree protects a live session's
    /// servers.
    /// </param>
    public PortsViewModel(DispatcherQueue ui, Func<IReadOnlySet<string>>? liveSessionIds = null)
    {
        _ui = ui;
        _reaper = new PortReaper(_signals, Environment.ProcessId);
        _mcpScanner = new McpScanner(WindowsProcesses.Table, WindowsProcesses.Probe, liveSessionIds ?? (() => new HashSet<string>()));
        _timer = ui.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    /// <summary>Raised on the UI thread after every scan and every kill.</summary>
    public event Action? Changed;

    public PortScanState State { get; private set; } = PortScanState.Scanning.Instance;

    /// <summary>MCP servers whose session is gone, the heaviest first. They hold no port, so they come from their own scan.</summary>
    public IReadOnlyList<McpOrphan> McpOrphans { get; private set; } = [];

    /// <summary>Kill failures, keyed by row id (a port's or a server's), shown inline on the row that failed.</summary>
    public IReadOnlyDictionary<string, string> Failures => _failures;

    public IReadOnlyList<ListeningPort> Ports => State is PortScanState.Ready ready ? ready.Ports : [];

    /// <summary>The bulk action's ports: never a live session's, only what Claude left behind.</summary>
    public IReadOnlyList<ListeningPort> Orphans => Ports.Where(port => port.Attribution == PortAttribution.Orphan).ToList();

    /// <summary>Everything Claude left behind, ports and servers alike: the number on the tab's badge.</summary>
    public int OrphanCount => PortsText.OrphanCount(Orphans, McpOrphans);

    /// <summary>What the bulk kill is about to take down, as the confirmation names it: ports by number, servers by name.</summary>
    public string BulkKillTargets => PortsText.BulkKillTargets(Orphans, McpOrphans);

    /// <summary>What the servers listed hold, in bytes; <see cref="PortsText.Memory"/> writes it.</summary>
    public ulong McpMemory => PortsText.McpMemory(McpOrphans);

#if DEBUG
    /// <summary><c>--film</c>: a made-up reading, and no scan of the real ports ever after.</summary>
    internal void Film(PortScanState state, IReadOnlyList<McpOrphan> servers)
    {
        _isDisposed = true;
        _timer.Stop();
        State = state;
        McpOrphans = servers;
        Changed?.Invoke();
    }
#endif

    public void Start()
    {
        Schedule(BackgroundInterval);
        _ = RefreshAsync();
    }

    public void SetVisible(bool isVisible)
    {
        Schedule(isVisible ? VisibleInterval : BackgroundInterval);
        if (isVisible)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// One scan at a time. A request made while one runs is served by another pass right after
    /// it, so a refresh that follows a kill never shows the process it killed.
    ///
    /// The MCP scan runs after the ports scan because it skips every PID the ports list already
    /// shows. It runs even when the ports scan could not: one does not need the other's table.
    /// </summary>
    public async Task RefreshAsync()
    {
        Interlocked.Exchange(ref _isRequested, 1);
        while (Volatile.Read(ref _isRequested) == 1 && Interlocked.CompareExchange(ref _isScanning, 1, 0) == 0)
        {
            try
            {
                while (Interlocked.Exchange(ref _isRequested, 0) == 1 && !_isDisposed)
                {
                    var (ports, servers) = await Task.Run(() =>
                    {
                        var ports = _scanner.Scan();
                        return (ports, _mcpScanner.Scan(ports.ListedPids));
                    }).ConfigureAwait(false);
                    await OnUiAsync(() => Publish(ports, servers)).ConfigureAwait(false);
                }
            }
            finally
            {
                Volatile.Write(ref _isScanning, 0);
            }
        }
    }

    public async Task KillAsync(ListeningPort port)
    {
        ArgumentNullException.ThrowIfNull(port);
        await ReapAsync(port.Id, table => _reaper.Kill(port, table)).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>The server and every process under it.</summary>
    public async Task KillAsync(McpOrphan server)
    {
        ArgumentNullException.ThrowIfNull(server);
        await ReapAsync(server.Id, table => _reaper.Kill(server, table)).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Kills what the confirmation named, and nothing that appeared since. Each kill refreshes the
    /// lists, so a target an earlier kill already took down is skipped rather than reported as a
    /// failure.
    /// </summary>
    public async Task KillAllOrphansAsync()
    {
        var confirmedPorts = Orphans;
        var confirmedServers = McpOrphans;
        foreach (var port in confirmedPorts)
        {
            if (Orphans.Any(listed => listed.Id == port.Id))
            {
                await KillAsync(port).ConfigureAwait(false);
            }
        }
        foreach (var server in confirmedServers)
        {
            if (McpOrphans.Any(listed => listed.Id == server.Id))
            {
                await KillAsync(server).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _timer.Stop();
        _signals.Dispose();
    }

    /// <summary>The table is read again at the click: the identity check needs the present, not the last scan.</summary>
    private async Task ReapAsync(string rowId, Func<ProcessTable, KillRefusal?> attempt)
    {
        var refusal = await Task.Run(() => WindowsProcesses.Table() is { } table ? attempt(table) : KillRefusal.IdentityChanged)
                                .ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            if (refusal is { } reason)
            {
                _failures[rowId] = PortsText.Explain(reason);
            }
            else
            {
                _failures.Remove(rowId);
            }
            Changed?.Invoke();
        }).ConfigureAwait(false);
    }

    private void Publish(PortScanState state, IReadOnlyList<McpOrphan> servers)
    {
        State = state;
        McpOrphans = servers;
        // A failure stays on its row; once the row is gone, so is the failure.
        var shown = Ports.Select(port => port.Id).Concat(servers.Select(server => server.Id)).ToHashSet();
        foreach (var id in _failures.Keys.Where(id => !shown.Contains(id)).ToList())
        {
            _failures.Remove(id);
        }
        Changed?.Invoke();
    }

    private void Schedule(TimeSpan interval)
    {
        if (_isDisposed)
        {
            return;
        }
        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }

    private Task OnUiAsync(Action action)
    {
        if (_ui.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = _ui.TryEnqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                done.TrySetResult();
            }
        });
        if (!queued)
        {
            // The UI is shutting down: there is no one left to tell.
            done.TrySetResult();
        }
        return done.Task;
    }
}
