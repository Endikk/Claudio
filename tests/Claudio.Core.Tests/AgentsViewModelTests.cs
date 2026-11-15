using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;

namespace Claudio.Core.Tests;

public sealed class AgentsViewModelTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RunningAgent Agent(string id, DateTimeOffset lastActivity, DateTimeOffset? startedAt = null, string session = "s1",
                                      int pid = 4242, bool isFinished = false, AgentAction? last = null,
                                      AgentAction[]? recent = null, string[]? edited = null) => new()
                                      {
                                          Id = id,
                                          SessionId = session,
                                          SessionPid = pid,
                                          SessionName = "claire-bb",
                                          Title = "Benchmark",
                                          StartedAt = startedAt ?? T,
                                          LastActivity = lastActivity,
                                          IsFinished = isFinished,
                                          ActionCount = 3,
                                          Recent = recent ?? [],
                                          LastAction = last,
                                          EditedFiles = edited ?? ["a.ts", "b.ts"],
                                      };

    private static ClaudeSession Session(string id = "s1", int pid = 4242, SessionState status = SessionState.Busy,
                                         string? waitingFor = null, DateTimeOffset? since = null, DateTimeOffset? opened = null,
                                         bool background = false, HostApp? host = null, string name = "claire-bb",
                                         string? cwd = null) => new()
                                         {
                                             Id = id,
                                             Pid = pid,
                                             ProcessStartedAt = T.AddHours(-1),
                                             Name = name,
                                             Cwd = cwd,
                                             OpenedAt = opened,
                                             Status = status,
                                             WaitingFor = waitingFor,
                                             StatusSince = since,
                                             IsBackground = background,
                                             Host = host,
                                         };

    private static AgentAction Action(string tool, string? file = null, double secondsAgo = 0) =>
        new(T.AddSeconds(-secondsAgo), AgentActionKinds.Of(tool), tool, file);

    private static AgentsViewModel Model(IAgentScanning scanner, FakeActions? actions = null, Func<DateTimeOffset>? now = null) =>
        new(scanner, actions ?? new FakeActions(), now ?? (() => T));

    private static StubActivity Agents(params RunningAgent[] agents) => new(new ClaudeActivity { Agents = agents });

    [Fact]
    public async Task PublishesScannedAgents()
    {
        RunningAgent[] found = [Agent("a1", T)];
        var model = Model(Agents(found));
        await model.RefreshAsync(Ct);
        Assert.Equal(found, model.Agents);
    }

    /// <summary>The small Claudy in the notification area and the island stands for one at work rather than one at rest.</summary>
    [Fact]
    public async Task LeadIsAnAgentAtWork()
    {
        var model = Model(Agents(Agent("resting", T.AddSeconds(-600)), Agent("working", T)));
        await model.RefreshAsync(Ct);
        Assert.Equal("working", model.Lead?.Id);
    }

    /// <summary>With nothing running, a pass publishes nothing: the card is not redrawn every few seconds.</summary>
    [Fact]
    public async Task QuietPassLeavesTheClockAlone()
    {
        var clock = T;
        var model = Model(Agents(), now: () => clock);
        var before = model.Now;
        clock = T.AddMinutes(1);
        await model.RefreshAsync(Ct);
        Assert.Equal(before, model.Now);
        Assert.Equal(AgentsViewModel.QuietInterval, model.Interval);
        var changes = 0;
        model.Changed += () => changes++;
        await model.RefreshAsync(Ct);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// Each agent has its own colour and keeps it while it runs; a colour freed by an agent that
    /// left goes to the next one launched.
    /// </summary>
    [Fact]
    public async Task EachAgentKeepsItsOwnColour()
    {
        RunningAgent a1 = Agent("a1", T), a2 = Agent("a2", T), a3 = Agent("a3", T);
        var model = Model(new SequenceAgents([a1, a2], [a2, a3]));

        await model.RefreshAsync(Ct);
        int first = model.ColourIndex(a1), second = model.ColourIndex(a2);
        await model.RefreshAsync(Ct);

        Assert.NotEqual(first, second);
        Assert.Equal(second, model.ColourIndex(a2));
        Assert.Equal(first, model.ColourIndex(a3));
    }

    /// <summary>Never the main Claudy's coral, nor the amber that warns.</summary>
    [Fact]
    public void AgentColoursLeaveCoralAndAmberToClaudy()
    {
        foreach (var colour in Theme.AgentTints)
        {
            Assert.NotEqual(Theme.Color(Accent.Coral), colour);
            Assert.NotEqual(Theme.Color(Accent.Amber), colour);
        }
    }

    [Fact]
    public void DetailsNameTheSessionAndEveryEditedFile()
    {
        var model = Model(Agents());
        Assert.Equal("claire-bb · 12 min · 3 actions\nEdited: a.ts, b.ts", model.Details(Agent("a1", T, startedAt: T.AddSeconds(-750))));
    }

    /// <summary>The right of a row: the file while it works, its silence once it stalls, its output once done.</summary>
    [Fact]
    public void StateSaysWhereTheAgentIs()
    {
        var model = Model(Agents());
        Assert.Equal("banc.ts", model.State(Agent("a", T, last: Action("Edit", "banc.ts"))));
        Assert.Equal("waiting", model.State(Agent("a", T.AddSeconds(-90))));
        Assert.Equal("silent 4 min", model.State(Agent("a", T.AddSeconds(-250))));
        Assert.Equal("done · 2 files", model.State(Agent("a", T, isFinished: true)));
        Assert.Equal("done", model.State(Agent("a", T, isFinished: true, edited: [])));
    }

    /// <summary>A silent agent loses its colour, a finished one keeps it.</summary>
    [Fact]
    public async Task SilentAgentTurnsGrey()
    {
        RunningAgent working = Agent("w", T), stalled = Agent("s", T.AddSeconds(-300)), done = Agent("d", T, isFinished: true);
        var model = Model(Agents(working, stalled, done));
        await model.RefreshAsync(Ct);
        Assert.Equal(Theme.Idle, model.Tint(stalled));
        Assert.NotEqual(Theme.Idle, model.Tint(working));
        Assert.NotEqual(Theme.Idle, model.Tint(done));
    }

    [Fact]
    public void LastCallsReadNewestFirst()
    {
        var model = Model(Agents());
        AgentAction[] recent = [Action("Read", "a.ts", 30), Action("Bash", secondsAgo: 20), Action("Edit", "b.ts", 10), Action("Write", "c.ts", 5)];
        Assert.Equal(["Write c.ts", "Edit b.ts", "Bash"], model.LastCalls(Agent("a", T, recent: recent)));
    }

    /// <summary>One row unfolded at a time; an agent that leaves the list folds with it.</summary>
    [Fact]
    public async Task ExpandsOneRowAndFoldsItWhenTheAgentLeaves()
    {
        RunningAgent a = Agent("a", T), b = Agent("b", T);
        var model = Model(new SequenceAgents([a, b], [b]));
        await model.RefreshAsync(Ct);
        model.ToggleExpanded(a);
        Assert.Equal("a", model.ExpandedId);
        model.ToggleExpanded(b);
        Assert.Equal("b", model.ExpandedId);
        model.ToggleExpanded(b);
        Assert.Null(model.ExpandedId);
        model.ToggleExpanded(a);
        await model.RefreshAsync(Ct);
        Assert.Null(model.ExpandedId);
    }

    /// <summary>The interrupt is asked first, then sends Ctrl+C to the session, with the start time its file records.</summary>
    [Fact]
    public async Task InterruptSendsCtrlCToTheSessionAfterConfirmation()
    {
        var actions = new FakeActions();
        var model = Model(new StubActivity(new ClaudeActivity { Sessions = new[] { Session() } }), actions);
        await model.RefreshAsync(Ct);
        var session = model.Groups[0].Session;

        model.AskToInterrupt(session);
        Assert.Equal("s1", model.InterruptPending);
        Assert.Empty(actions.Interrupts);
        model.CancelInterrupt();
        Assert.Null(model.InterruptPending);

        model.AskToInterrupt(session);
        model.Interrupt(session);
        var sent = Assert.Single(actions.Interrupts);
        Assert.Equal((4242, T.AddHours(-1)), sent);
        Assert.Null(model.InterruptPending);
        Assert.Equal("Ctrl+C sent to claire-bb.", model.Notice);
    }

    /// <summary>A refused interrupt names the reason it can: gone, someone else's, or no console to reach.</summary>
    [Fact]
    public void FailedInterruptSaysWhy()
    {
        var gone = Model(Agents(), new FakeActions { Result = SignalResult.Gone });
        gone.Interrupt(Session());
        Assert.Equal("Not sent: claire-bb has closed.", gone.Notice);
        var other = Model(Agents(), new FakeActions { Result = SignalResult.OtherUser });
        other.Interrupt(Session());
        Assert.Equal("Not sent: claire-bb runs under another user.", other.Notice);
        var detached = Model(Agents(), new FakeActions { Result = SignalResult.NoConsole });
        detached.Interrupt(Session());
        Assert.Equal("Not sent: claire-bb has no console to receive it.", detached.Notice);
    }

    /// <summary>
    /// The pid is checked again as Ctrl+C goes out: never the system's pids, never a pid that no
    /// longer belongs to the session, even if the list still shows it.
    /// </summary>
    [Fact]
    public void InterruptNeverSignalsAPidThatIsNotTheSession()
    {
        var actions = new FakeActions { IsLive = false };
        var dead = Model(Agents(), actions);
        dead.Interrupt(Session());
        Assert.Equal("Not sent: claire-bb has closed.", dead.Notice);

        var live = new FakeActions();
        foreach (var pid in new[] { 0, 1, 4, -1 })
        {
            var model = Model(Agents(), live);
            model.Interrupt(Session(pid: pid));
            Assert.Equal("Not sent: claire-bb has closed.", model.Notice);
        }
        Assert.Empty(actions.Interrupts);
        Assert.Empty(live.Interrupts);
    }

    /// <summary>
    /// Without the start time its file records, nothing proves the pid is still the session: no
    /// Ctrl+C, and no button offering one.
    /// </summary>
    [Fact]
    public void InterruptNeedsTheSessionsStartTime()
    {
        var actions = new FakeActions();
        var unknown = Session() with { ProcessStartedAt = null };
        var model = Model(Agents(), actions);
        Assert.False(unknown.CanBeInterrupted);
        model.Interrupt(unknown);
        Assert.Empty(actions.Interrupts);
        Assert.Equal("Not sent: claire-bb has closed.", model.Notice);
    }

    /// <summary>
    /// Unfolding another row, or the session leaving the list, drops the confirmation: it must
    /// never greet the next row armed.
    /// </summary>
    [Fact]
    public async Task PendingInterruptIsDroppedWhenTheRowOrTheSessionChanges()
    {
        RunningAgent a = Agent("a", T), b = Agent("b", T);
        var model = Model(new SequenceAgents([a, b], []));
        await model.RefreshAsync(Ct);
        var session = model.Groups[0].Session;

        model.ToggleExpanded(session);
        model.AskToInterrupt(session);
        model.ToggleExpanded(b);
        Assert.Null(model.InterruptPending);

        model.AskToInterrupt(session);
        await model.RefreshAsync(Ct);
        Assert.Empty(model.Groups);
        Assert.Null(model.InterruptPending);
        Assert.Null(model.ExpandedSessionId);
    }

    /// <summary>Finished agents stay listed but are not counted as running, nor made the lead.</summary>
    [Fact]
    public async Task RunningCountLeavesFinishedAgentsOut()
    {
        RunningAgent done = Agent("d", T, startedAt: T.AddSeconds(-900), isFinished: true), working = Agent("w", T);
        var model = Model(Agents(done, working));
        await model.RefreshAsync(Ct);
        Assert.Equal(2, model.Agents.Count);
        Assert.Equal(1, model.RunningCount);
        Assert.Equal("w", model.Lead?.Id);

        var onlyDone = Model(Agents(done));
        await onlyDone.RefreshAsync(Ct);
        Assert.Equal(0, onlyDone.RunningCount);
        Assert.Null(onlyDone.Lead);
    }

    // MARK: - Sessions

    /// <summary>
    /// Waiting first, the longest wait on top; then at work; then at rest. A program's own session
    /// shows only while it runs agents.
    /// </summary>
    [Fact]
    public async Task GroupsPutWaitingSessionsFirstAndHideBackgroundOnes()
    {
        var activity = new ClaudeActivity
        {
            Sessions = new[]
            {
                Session("idle", status: SessionState.Idle, since: T.AddSeconds(-60), name: "a-idle"),
                Session("busy", status: SessionState.Busy, name: "b-busy"),
                Session("late", status: SessionState.Waiting, waitingFor: "permission prompt", since: T.AddSeconds(-30), name: "c-late"),
                Session("early", status: SessionState.Waiting, since: T.AddSeconds(-300), name: "d-early"),
                Session("sdk", status: SessionState.Busy, background: true, name: "observer"),
                Session("sdk-agents", status: SessionState.Busy, background: true, name: "worker"),
            },
            Agents = new[] { Agent("x", T, session: "sdk-agents") },
        };
        var model = Model(new StubActivity(activity));
        await model.RefreshAsync(Ct);

        Assert.Equal(["early", "late", "busy", "sdk-agents", "idle"], model.Groups.Select(group => group.Session.Id));
        Assert.Equal(["x"], model.Groups.First(group => group.Session.Id == "sdk-agents").Agents.Select(agent => agent.Id));
        Assert.Equal(["early", "late"], model.Waiting.Select(session => session.Id));
        Assert.Equal(AgentsViewModel.BusyInterval, model.Interval);
    }

    /// <summary>The words of a session row: what it does, how long it has waited, what it waits for.</summary>
    [Fact]
    public void SessionWords()
    {
        var model = Model(Agents());
        var now = model.Now;
        Assert.Equal("working", model.State(Session(status: SessionState.Busy)));
        Assert.Equal("idle 12 min", model.State(Session(status: SessionState.Idle, since: now.AddMinutes(-12))));
        Assert.Equal("open", model.State(Session(status: SessionState.Unknown)));
        var waiting = Session(status: SessionState.Waiting, waitingFor: "permission prompt", since: now.AddSeconds(-90),
                              host: new HostApp("Visual Studio Code", 10196), cwd: @"C:\Users\me\EVAN");
        Assert.Equal("1 min", model.Waited(waiting));
        Assert.Equal("Permission prompt · EVAN · VS Code", model.WaitingDetail(waiting));
        Assert.Equal(@"~\EVAN", AgentsViewModel.Abbreviated(@"C:\Users\me\EVAN", @"C:\Users\me"));
        Assert.Equal(@"~\EVAN", AgentsViewModel.Abbreviated(@"c:\users\me\EVAN", @"C:\Users\me"));
        Assert.Equal(@"C:\Users\meme\EVAN", AgentsViewModel.Abbreviated(@"C:\Users\meme\EVAN", @"C:\Users\me"));
        Assert.Equal("~/EVAN", AgentsViewModel.Abbreviated("/Users/me/EVAN", "/Users/me"));
        Assert.Equal("/Users/meme/EVAN", AgentsViewModel.Abbreviated("/Users/meme/EVAN", "/Users/me"));
    }

    /// <summary>"Open" brings the session's app forward, and says so when there is none to bring.</summary>
    [Fact]
    public async Task FocusOpensTheSessionsApp()
    {
        var actions = new FakeActions();
        var host = new HostApp("Terminal", 700);
        var model = Model(new StubActivity(new ClaudeActivity { Sessions = new[] { Session(host: host) } }), actions);
        await model.RefreshAsync(Ct);
        model.Focus("s1");
        Assert.Equal([host], actions.Opened);
        Assert.Null(model.Notice);

        model.Focus(Session(host: null));
        Assert.Equal("Claudio cannot tell which app runs claire-bb.", model.Notice);

        var refused = Model(Agents(), new FakeActions { Opens = false });
        refused.Focus(Session(host: host));
        Assert.Equal("Terminal could not be opened.", refused.Notice);
    }

    /// <summary>A session at its prompt has no turn to stop: no confirmation survives it going idle.</summary>
    [Fact]
    public void IdleSessionCannotBeInterrupted()
    {
        Assert.True(Session(status: SessionState.Busy).CanBeInterrupted);
        Assert.True(Session(status: SessionState.Waiting).CanBeInterrupted);
        Assert.False(Session(status: SessionState.Idle).CanBeInterrupted);
    }

    [Fact]
    public void StatusReadsTheSessionFilesWords()
    {
        Assert.Equal(SessionState.Busy, ClaudeSession.StateOf("busy"));
        Assert.Equal(SessionState.Idle, ClaudeSession.StateOf("idle"));
        Assert.Equal(SessionState.Waiting, ClaudeSession.StateOf("waiting"));
        Assert.Equal(SessionState.Unknown, ClaudeSession.StateOf(null));
        Assert.Equal(SessionState.Busy, ClaudeSession.StateOf("compacting"));
        Assert.True(ClaudeSession.IsBackgroundEntrypoint("sdk-cli"));
        Assert.False(ClaudeSession.IsBackgroundEntrypoint("claude-vscode"));
        Assert.False(ClaudeSession.IsBackgroundEntrypoint(null));
    }

    /// <summary>A pass that finishes after a newer one changes nothing.</summary>
    [Fact]
    public async Task StalePassDoesNotOverwriteANewerOne()
    {
        var slow = new SlowAgents();
        var model = Model(slow);
        var first = model.RefreshAsync(Ct);
        await model.RefreshAsync(Ct);
        Assert.Equal(["new"], model.Agents.Select(agent => agent.Id));
        slow.Release(Agent("old", T));
        await first;
        Assert.Equal(["new"], model.Agents.Select(agent => agent.Id));
    }

    /// <summary>Every change is told: a pass, an unfolded row, a notice.</summary>
    [Fact]
    public async Task EveryChangeIsAnnounced()
    {
        var model = Model(Agents(Agent("a", T)));
        var changes = 0;
        model.Changed += () => changes++;
        await model.RefreshAsync(Ct);
        model.ToggleExpanded(model.Agents[0]);
        model.Focus(Session(host: null));
        Assert.Equal(3, changes);
    }

    private sealed class StubActivity(ClaudeActivity activity) : IAgentScanning
    {
        public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default) => Task.FromResult(activity);
    }

    /// <summary>One list per pass, then the last one again.</summary>
    private sealed class SequenceAgents(params RunningAgent[][] passes) : IAgentScanning
    {
        private readonly Queue<RunningAgent[]> _passes = new(passes);

        public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default)
        {
            var pass = _passes.Count > 1 ? _passes.Dequeue() : _passes.Peek();
            return Task.FromResult(new ClaudeActivity { Agents = pass });
        }
    }

    /// <summary>Two passes: the first waits until released, the second answers at once.</summary>
    private sealed class SlowAgents : IAgentScanning
    {
        private readonly TaskCompletionSource<ClaudeActivity> _first = new();
        private int _calls;

        public void Release(params RunningAgent[] agents) => _first.SetResult(new ClaudeActivity { Agents = agents });

        public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default) =>
            ++_calls == 1 ? _first.Task : Task.FromResult(new ClaudeActivity { Agents = new[] { Agent("new", T) } });
    }

    /// <summary>Records what a view model asks of Windows, and answers as told.</summary>
    private sealed class FakeActions : ISessionActions
    {
        public bool IsLive { get; init; } = true;
        public SignalResult Result { get; init; } = SignalResult.Sent;
        public bool Opens { get; init; } = true;
        public List<(int Pid, DateTimeOffset StartedAt)> Interrupts { get; } = [];
        public List<HostApp> Opened { get; } = [];

        public bool IsSession(int pid, DateTimeOffset? startedAt) => IsLive;

        public SignalResult Interrupt(int pid, DateTimeOffset startedAt)
        {
            Interrupts.Add((pid, startedAt));
            return Result;
        }

        public bool BringForward(HostApp host)
        {
            Opened.Add(host);
            return Opens;
        }
    }
}
