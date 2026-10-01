using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// Drives the Ports tab, as Claudy's <c>PortsViewModel</c>: what is open, how old it is, and what
/// happened to a kill. State changes only on the UI thread, and <see cref="Changed"/> follows each
/// one, every scan included, so the ages on screen keep up.
///
/// The scan runs off the UI thread: the system calls are cheap but not free, and the card must
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
    private readonly WindowsProcesses.Signals _signals = new();
    private readonly PortReaper _reaper;
    private readonly Dictionary<string, string> _failures = [];
    private int _isScanning;
    private int _isRequested;
    private volatile bool _isDisposed;

    public PortsViewModel(DispatcherQueue ui)
    {
        _ui = ui;
        _reaper = new PortReaper(_signals, Environment.ProcessId);
        _timer = ui.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    /// <summary>Raised on the UI thread after every scan and every kill.</summary>
    public event Action? Changed;

    public PortScanState State { get; private set; } = PortScanState.Scanning.Instance;

    /// <summary>Kill failures, keyed by port id, shown inline on the row that failed.</summary>
    public IReadOnlyDictionary<string, string> Failures => _failures;

    public IReadOnlyList<ListeningPort> Ports => State is PortScanState.Ready ready ? ready.Ports : [];

    /// <summary>The bulk action's targets: never a live session's port, only what Claude left behind.</summary>
    public IReadOnlyList<ListeningPort> Orphans => Ports.Where(port => port.Attribution == PortAttribution.Orphan).ToList();

    public int OrphanCount => Ports.Count(port => port.Attribution == PortAttribution.Orphan);

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
    /// it, so a refresh that follows a kill never shows the port it killed.
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
                    var scanned = await Task.Run(_scanner.Scan).ConfigureAwait(false);
                    await OnUiAsync(() => Publish(scanned)).ConfigureAwait(false);
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
        await KillOneAsync(port).ConfigureAwait(false);
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task KillAllOrphansAsync()
    {
        foreach (var port in Orphans)
        {
            await KillOneAsync(port).ConfigureAwait(false);
        }
        await RefreshAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        _isDisposed = true;
        _timer.Stop();
        _signals.Dispose();
    }

    /// <summary>The table is read again at the click: the identity check needs the present, not the last scan.</summary>
    private async Task KillOneAsync(ListeningPort port)
    {
        var refusal = await Task.Run(() => WindowsProcesses.Table() is { } table ? _reaper.Kill(port, table) : KillRefusal.IdentityChanged)
                                .ConfigureAwait(false);
        await OnUiAsync(() =>
        {
            if (refusal is { } reason)
            {
                _failures[port.Id] = PortsText.Explain(reason);
            }
            else
            {
                _failures.Remove(port.Id);
            }
            Changed?.Invoke();
        }).ConfigureAwait(false);
    }

    private void Publish(PortScanState state)
    {
        State = state;
        // A failure stays on its row; once the row is gone, so is the failure.
        var shown = Ports.Select(port => port.Id).ToHashSet();
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
