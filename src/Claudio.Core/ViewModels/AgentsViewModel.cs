using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.ViewModels;

/// <summary>
/// What the sessions' rows ask of Windows, which the App implements
/// (<c>Claudio.App/Services/WindowsSessions.cs</c>) and the tests fake, so that no test signals or
/// brings forward anything real. Claudy injects the same three as closures.
/// </summary>
public interface ISessionActions
{
    /// <summary>
    /// Whether a session's process is still that session, checked again the moment Ctrl+C is about
    /// to go out: the list may be seconds old and a pid is reused.
    /// </summary>
    bool IsSession(int pid, DateTimeOffset? startedAt);

    /// <summary>Ctrl+C to the session's console, its start time checked once more as it goes out.</summary>
    SignalResult Interrupt(int pid, DateTimeOffset startedAt);

    /// <summary>Brings an app's window forward; false when there is none to bring.</summary>
    bool BringForward(HostApp host);
}

/// <summary>
/// Drives every face of the sessions and their agents: the card's Sessions tab, the popover's and
/// the island's short lists and waiting rows, the menu bar item's count, as Claudy's
/// <c>AgentsViewModel</c>.
///
/// The scan is cheap once each transcript has been read through (a few file sizes, a few appended
/// lines), so it runs every few seconds while a session works or agents are listed, the tracks
/// moving with them, and slows down when everything rests.
///
/// <para><b>Threading.</b> Claudy's is a <c>@MainActor</c> object that runs its own timer. This one
/// holds no UI type and no timer: it is used from one thread, the UI thread, which drives it.
/// <see cref="RefreshAsync"/> runs the scan on the thread pool and resumes on the caller's
/// synchronisation context, so every property changes, and <see cref="Changed"/> is raised, on the
/// thread that called it. After each pass <see cref="Interval"/> says when the next is due, busy
/// or quiet; the App's timer reads it. The notice an action leaves clears itself after
/// <see cref="NoticeDuration"/>, also on the context the action was called on.</para>
/// </summary>
// The row's words stay members, as in Claudy, so a view asks its model for all of them alike.
#pragma warning disable CA1822
public sealed class AgentsViewModel : IDisposable
{
    public static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan QuietInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(5);

    private readonly IAgentScanning _scanner;
    private readonly ISessionActions _actions;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>Each pass is numbered: a pass that finishes after a newer one changes nothing.</summary>
    private int _generation;

    /// <summary>Each running agent's place in <see cref="Theme.AgentTints"/>, kept for as long as it is listed.</summary>
    private Dictionary<string, int> _colours = new(StringComparer.Ordinal);

    private CancellationTokenSource? _noticeClear;
    private string? _expandedId;
    private string? _expandedSessionId;
    private string? _interruptPending;

    public AgentsViewModel(IAgentScanning scanner, ISessionActions actions, Func<DateTimeOffset>? now = null)
    {
        _scanner = scanner;
        _actions = actions;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Now = _now();
    }

    /// <summary>Raised after any property changes, on the thread that changed it.</summary>
    public event Action? Changed;

    /// <summary>Called after each pass with what it found: the notifications read it.</summary>
    public Action<ClaudeActivity>? OnPass { get; set; }

    public ValueList<RunningAgent> Agents { get; private set; } = ValueList<RunningAgent>.Empty;

    /// <summary>Every Claude Code session open on this PC, background ones included.</summary>
    public ValueList<ClaudeSession> Sessions { get; private set; } = ValueList<ClaudeSession>.Empty;

    /// <summary>
    /// The moment of the last pass. Rows read their elapsed time and their track against it, so
    /// they all agree and move together.
    /// </summary>
    public DateTimeOffset Now { get; private set; }

    /// <summary>When the next pass is due: <see cref="BusyInterval"/> while anything works, else <see cref="QuietInterval"/>. Null before the first pass.</summary>
    public TimeSpan? Interval { get; private set; }

    /// <summary>The agent whose row is unfolded in the card: what it wrote, its last calls.</summary>
    public string? ExpandedId
    {
        get => _expandedId;
        set => Set(ref _expandedId, value);
    }

    /// <summary>The session unfolded in the card: where it runs, the way to its window, the interrupt.</summary>
    public string? ExpandedSessionId
    {
        get => _expandedSessionId;
        set => Set(ref _expandedSessionId, value);
    }

    /// <summary>The session an interrupt is being confirmed for.</summary>
    public string? InterruptPending
    {
        get => _interruptPending;
        set => Set(ref _interruptPending, value);
    }

    /// <summary>One line under the list after an action: sent, or why not. Clears itself.</summary>
    public string? Notice { get; private set; }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _generation++;
        var pass = _generation;
        var found = await _scanner.ScanActivityAsync(cancellationToken).ConfigureAwait(true);
        if (pass != _generation)
        {
            return;
        }
        // Nothing open, nothing to move: the views that read Now are not redrawn for it, and,
        // as Claudy publishes only what changed, a quiet pass raises nothing.
        var changed = false;
        var isQuiet = found.Agents.Count == 0 && found.Sessions.Count == 0;
        if (!isQuiet || Agents.Count > 0 || Sessions.Count > 0)
        {
            Now = _now();
            changed = true;
        }
        AssignColours(found.Agents);
        if (!found.Agents.Equals(Agents))
        {
            Agents = found.Agents;
            changed = true;
        }
        if (!found.Sessions.Equals(Sessions))
        {
            Sessions = found.Sessions;
            changed = true;
        }
        changed |= DropWhatLeft();
        var isBusy = found.Agents.Count > 0 || found.Sessions.Any(session => session.Status == SessionState.Busy || session.IsWaiting);
        var interval = isBusy ? BusyInterval : QuietInterval;
        changed |= interval != Interval;
        Interval = interval;
        if (changed)
        {
            Changed?.Invoke();
        }
        OnPass?.Invoke(found);
    }

    /// <summary>An unfolded row, or a confirmation, goes with the agent or session it was shown for.</summary>
    private bool DropWhatLeft()
    {
        var dropped = false;
        if (_expandedId is { } expanded && !Agents.Any(agent => agent.Id == expanded))
        {
            _expandedId = null;
            dropped = true;
        }
        var listed = Groups.Select(group => group.Session).ToList();
        if (_expandedSessionId is { } session && !listed.Any(listed => listed.Id == session))
        {
            _expandedSessionId = null;
            dropped = true;
        }
        if (_interruptPending is { } pending && !listed.Any(listed => listed.Id == pending && listed.CanBeInterrupted))
        {
            _interruptPending = null;
            dropped = true;
        }
        return dropped;
    }

    /// <summary>
    /// How many are not done: what the badges count. Finished agents stay listed a while, but
    /// they are not running.
    /// </summary>
    public int RunningCount => Agents.Count(agent => !agent.IsFinished);

    /// <summary>
    /// The agent whose small Claudy speaks for the group in the notification area and the island:
    /// one at work, else the oldest still running.
    /// </summary>
    public RunningAgent? Lead =>
        Agents.FirstOrDefault(agent => agent.Status(Now) == AgentStatus.Working) ?? Agents.FirstOrDefault(agent => !agent.IsFinished);

    /// <summary>
    /// The sessions the card lists, each with its agents: the ones someone sits at, and a
    /// program's own only while it runs agents. Waiting first, the longest wait on top, then at
    /// work, then at rest.
    /// </summary>
    public IReadOnlyList<SessionGroup> Groups
    {
        get
        {
            var byId = Agents.GroupBy(agent => agent.SessionId, StringComparer.Ordinal)
                             .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            var listed = Sessions.Where(session => !session.IsBackground || byId.ContainsKey(session.Id)).ToList();
            // Agents whose session file was not read in this pass still show, under their session.
            foreach (var agent in Agents)
            {
                if (!listed.Any(session => session.Id == agent.SessionId))
                {
                    listed.Add(new ClaudeSession
                    {
                        Id = agent.SessionId,
                        Pid = agent.SessionPid,
                        ProcessStartedAt = agent.SessionStartedAt,
                        Name = agent.SessionName,
                        Status = SessionState.Unknown,
                    });
                }
            }
            listed.Sort(Compare);
            return listed.Select(session => new SessionGroup(session, byId.GetValueOrDefault(session.Id) ?? [])).ToList();
        }
    }

    /// <summary>The sessions asking the user something, the longest wait first.</summary>
    public IReadOnlyList<ClaudeSession> Waiting =>
        Groups.Select(group => group.Session).Where(session => session.IsWaiting && !session.IsBackground).ToList();

    private static int Compare(ClaudeSession first, ClaudeSession second)
    {
        var ranks = Rank(first).CompareTo(Rank(second));
        if (ranks != 0)
        {
            return ranks;
        }
        if (first.IsWaiting)
        {
            var since = (first.StatusSince ?? DateTimeOffset.MinValue).CompareTo(second.StatusSince ?? DateTimeOffset.MinValue);
            return since != 0 ? since : string.CompareOrdinal(first.Id, second.Id);
        }
        var names = string.CompareOrdinal(first.Name.ToLowerInvariant(), second.Name.ToLowerInvariant());
        return names != 0 ? names : string.CompareOrdinal(first.Id, second.Id);
    }

    private static int Rank(ClaudeSession session) => session.Status switch
    {
        SessionState.Waiting => 0,
        SessionState.Busy => 1,
        SessionState.Unknown => 2,
        _ => 3,
    };

    /// <summary>Its colour in <see cref="Theme.AgentTints"/>. An agent not seen yet takes the first one.</summary>
    public int ColourIndex(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return _colours.GetValueOrDefault(agent.Id);
    }

    /// <summary>Its Claudy's colour, grey once it falls silent.</summary>
    public Rgba Tint(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Status(Now) switch
        {
            AgentStatus.Idle or AgentStatus.Stalled => Theme.Idle,
            _ => Theme.AgentTints[ColourIndex(agent)],
        };
    }

    /// <summary>
    /// A newcomer takes the least used colour, the first one on a tie, so agents running side by
    /// side differ; one that keeps running never changes colour.
    /// </summary>
    private void AssignColours(ValueList<RunningAgent> found)
    {
        var ids = found.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        _colours = _colours.Where(pair => ids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var agent in found)
        {
            if (_colours.ContainsKey(agent.Id))
            {
                continue;
            }
            var uses = new int[Theme.AgentTints.Count];
            foreach (var index in _colours.Values)
            {
                uses[index]++;
            }
            var least = 0;
            for (var index = 1; index < uses.Length; index++)
            {
                if (uses[index] < uses[least])
                {
                    least = index;
                }
            }
            _colours[agent.Id] = least;
        }
    }

    // MARK: - Words

    /// <summary>How long it has been running, in the largest unit that still reads at a glance.</summary>
    public string Elapsed(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return Duration(Now - agent.StartedAt);
    }

    /// <summary>
    /// What its row says on the right: the file it touches while it works, its silence once that
    /// silence is worth a look, what it wrote once it is done.
    /// </summary>
    public string State(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Status(Now) switch
        {
            AgentStatus.Working => agent.Touching,
            AgentStatus.Idle => "waiting",
            AgentStatus.Stalled => $"silent {Duration(agent.Silence(Now))}",
            _ => agent.EditedFiles.Count == 0 ? "done" : $"done · {Files(agent.EditedFiles.Count)}",
        };
    }

    /// <summary>The hover text of a row: whose agent it is and everything it wrote.</summary>
    public string Details(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var lines = new List<string> { $"{agent.SessionName} · {Elapsed(agent)} · {agent.ActionCount} actions" };
        if (agent.EditedFiles.Count > 0)
        {
            lines.Add("Edited: " + string.Join(", ", agent.EditedFiles));
        }
        return string.Join('\n', lines);
    }

    /// <summary>Its last few tool calls, newest first, as "Edit AgentScanner.cs".</summary>
    public IReadOnlyList<string> LastCalls(RunningAgent agent, int limit = 3)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return agent.Recent.TakeLast(limit).Reverse()
                    .Select(action => action.File is { } file ? $"{action.Tool} {file}" : action.Tool)
                    .ToList();
    }

    /// <summary>Where a session is, on the right of its row.</summary>
    public string State(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Status switch
        {
            SessionState.Busy => "working",
            SessionState.Waiting => "waiting",
            SessionState.Unknown => "open",
            _ => session.StatusSince is { } since ? $"idle {Duration(Now - since)}" : "idle",
        };
    }

    /// <summary>How long it has waited for the user.</summary>
    public string Waited(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Duration(Now - (session.StatusSince ?? Now));
    }

    /// <summary>How long it has been open, when its file says.</summary>
    public string? Age(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.OpenedAt is { } opened ? Duration(Now - opened) : null;
    }

    /// <summary>The line under a waiting session's name: what it waits for, its project, its app.</summary>
    public string WaitingDetail(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.Join(" · ", new[] { session.WaitingReason ?? "Waiting for you", session.FolderName, session.Host?.ShortName }
                                     .OfType<string>());
    }

    /// <summary>An unfolded session's first line: its folder, its app, when it opened, its agents.</summary>
    public string Details(ClaudeSession session, int agents)
    {
        ArgumentNullException.ThrowIfNull(session);
        var parts = new List<string>();
        if (session.Cwd is { } cwd)
        {
            parts.Add(Abbreviated(cwd));
        }
        if (session.Host is { } host)
        {
            parts.Add(host.ShortName);
        }
        if (session.OpenedAt is { } opened)
        {
            parts.Add($"started {UsageFormat.Clock(opened)}");
        }
        parts.Add(agents == 0 ? "no agent" : agents == 1 ? "1 agent" : $"{agents} agents");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// A path from the home folder on, as a terminal shows it: <c>~\Projects\Claudio</c>. Windows
    /// paths are compared without case, and either separator follows the home folder.
    /// </summary>
    public static string Abbreviated(string path, string? home = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        home = home.TrimEnd('/', '\\');
        if (home.Length == 0)
        {
            return path;
        }
        if (path.Equals(home, StringComparison.OrdinalIgnoreCase))
        {
            return "~";
        }
        return path.Length > home.Length && path.StartsWith(home, StringComparison.OrdinalIgnoreCase) && path[home.Length] is '/' or '\\'
            ? "~" + path[home.Length..]
            : path;
    }

    public static string Duration(TimeSpan span)
    {
        var seconds = (long)span.TotalSeconds;
        return seconds switch
        {
            < 60 => "<1 min",
            < 3600 => $"{seconds / 60} min",
            _ => $"{seconds / 3600} h {seconds % 3600 / 60:00}",
        };
    }

    public static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    // MARK: - Actions

    /// <summary>
    /// Folding or unfolding any row drops a pending confirmation: it was asked for the row it
    /// showed in, and must not greet the next one armed.
    /// </summary>
    public void ToggleExpanded(RunningAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _expandedId = _expandedId == agent.Id ? null : agent.Id;
        _interruptPending = null;
        Changed?.Invoke();
    }

    public void ToggleExpanded(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _expandedSessionId = _expandedSessionId == session.Id ? null : session.Id;
        _interruptPending = null;
        Changed?.Invoke();
    }

    /// <summary>Asks before interrupting: Ctrl+C reaches the whole session, every agent of it.</summary>
    public void AskToInterrupt(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        InterruptPending = session.Id;
    }

    public void CancelInterrupt() => InterruptPending = null;

    /// <summary>
    /// Interrupts a Claude Code session with the Ctrl+C its terminal would send: the turn stops and
    /// every agent the session runs with it, and the session stays open, at its prompt or mid-turn
    /// alike (checked by Claudy on real sessions: five signals, five survivors). Agents have no
    /// process of their own, so there is nothing narrower to stop. A command an agent had already
    /// started, a long <c>sleep</c> say, runs to its end on its own. On Windows Ctrl+C goes to the
    /// session's console, which also reaches the shell it was started from, as in the terminal.
    /// </summary>
    public void Interrupt(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _interruptPending = null;
        // Checked again now, not when the list was last read: the pid may belong to someone else.
        // Without the start time its file records, nothing proves the pid is still the session.
        if (session.Pid <= ProcessIdentity.LastSystemPid || session.ProcessStartedAt is not { } startedAt
            || !_actions.IsSession(session.Pid, startedAt))
        {
            Show($"Not sent: {session.Name} has closed.");
            return;
        }
        Show(_actions.Interrupt(session.Pid, startedAt) switch
        {
            SignalResult.Sent => $"Ctrl+C sent to {session.Name}.",
            SignalResult.OtherUser => $"Not sent: {session.Name} runs under another user.",
            SignalResult.NoConsole => $"Not sent: {session.Name} has no console to receive it.",
            _ => $"Not sent: {session.Name} has closed.",
        });
    }

    /// <summary>Brings forward the app the session runs in: its terminal, its editor.</summary>
    public void Focus(ClaudeSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Host is not { } host)
        {
            Show($"Claudio cannot tell which app runs {session.Name}.");
            return;
        }
        if (!_actions.BringForward(host))
        {
            Show($"{host.Name} could not be opened.");
        }
    }

    /// <summary>The session a notification spoke of, if it is still open.</summary>
    public void Focus(string sessionId)
    {
        if (Sessions.FirstOrDefault(session => session.Id == sessionId) is { } session)
        {
            Focus(session);
        }
    }

    private void Show(string text)
    {
        Notice = text;
        Changed?.Invoke();
        _noticeClear?.Cancel();
        _noticeClear?.Dispose();
        _noticeClear = new CancellationTokenSource();
        _ = ClearNoticeAsync(_noticeClear.Token);
    }

    private async Task ClearNoticeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NoticeDuration, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Notice = null;
        Changed?.Invoke();
    }

    /// <summary>Stops the notice's clock; the view model is not used afterwards.</summary>
    public void Dispose()
    {
        _noticeClear?.Cancel();
        _noticeClear?.Dispose();
        _noticeClear = null;
    }

    private void Set(ref string? field, string? value)
    {
        if (field == value)
        {
            return;
        }
        field = value;
        Changed?.Invoke();
    }
}
#pragma warning restore CA1822
