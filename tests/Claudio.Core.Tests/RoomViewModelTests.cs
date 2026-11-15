using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;

namespace Claudio.Core.Tests;

/// <summary>The agent room: one room per project, agents seated by their work, walking when it changes.</summary>
public sealed class RoomViewModelTests
{
    private DateTimeOffset _now = DateTimeOffset.FromUnixTimeSeconds(1_793_468_000);
    private readonly MemoryStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ClaudeSession Session(string id, string cwd, SessionState status = SessionState.Busy, string? waitingFor = null) => new()
    {
        Id = id,
        Pid = 4242,
        ProcessStartedAt = _now,
        Name = id,
        Cwd = cwd,
        OpenedAt = _now,
        Status = status,
        WaitingFor = waitingFor,
        StatusSince = _now,
        LastPrompt = "Add the room",
    };

    private RunningAgent Agent(string id, string session, (string Tool, string? Command)[] calls, bool isFinished = false)
    {
        var actions = calls.Select(call =>
        {
            var reading = AgentWorks.Read(call.Tool, call.Command);
            return new AgentAction(_now, AgentActionKinds.Of(call.Tool), call.Tool, null, reading.Work, reading.Detail);
        }).ToList();
        return new RunningAgent
        {
            Id = id,
            SessionId = session,
            SessionPid = 4242,
            SessionStartedAt = _now,
            SessionName = session,
            Title = $"Agent {id}",
            StartedAt = _now,
            LastActivity = _now,
            IsFinished = isFinished,
            ActionCount = actions.Count,
            Recent = new(actions),
            LastAction = actions.LastOrDefault(),
        };
    }

    private static ClaudeActivity Activity(ClaudeSession[] sessions, RunningAgent[]? agents = null) =>
        new() { Sessions = new(sessions), Agents = new(agents ?? []) };

    private static readonly (string, string?)[] Read = [("Read", null)];

    private (RoomViewModel Room, AgentsViewModel Agents) Room(params ClaudeActivity[] passes)
    {
        var agents = new AgentsViewModel(new SequenceActivity(passes), new NoActions(), () => _now);
        var room = new RoomViewModel(agents, _store, () => _now, () => false);
        return (room, agents);
    }

    private static RoomScene SceneOf(RoomViewModel room, DateTimeOffset date, double? quota = null) => room.Scene(room.Selected, date, quota);

    // Rooms put away

    private ClaudeActivity ThreeRooms => Activity(
        [Session("a", "/me/Claudy"), Session("b", "/me/scratch-1"), Session("c", "/me/Api")],
        [Agent("x", "b", Read)]);

    [Fact]
    public async Task ARoomPutAwayIsNoLongerListed()
    {
        var (room, agents) = Room(ThreeRooms);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.Hide("scratch-1");
        Assert.Equal(["Claudy", "Api"], room.Projects.Select(project => project.Id));
        Assert.Equal(["scratch-1"], room.Hidden);
    }

    [Fact]
    public async Task PuttingAwayTheRoomOnScreenShowsTheNextOne()
    {
        var (room, agents) = Room(ThreeRooms);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.Enter("scratch-1");
        room.Hide("scratch-1");
        Assert.Equal("Claudy", room.Selected?.Id);
    }

    [Fact]
    public async Task ARoomPutAwayStaysAwayAfterARelaunch()
    {
        var (first, agents) = Room(ThreeRooms);
        await agents.RefreshAsync(Ct);
        first.Sync();
        first.Hide("scratch-1");
        var (second, others) = Room(ThreeRooms);
        await others.RefreshAsync(Ct);
        second.Sync();
        Assert.Equal(["Claudy", "Api"], second.Projects.Select(project => project.Id));
    }

    [Fact]
    public async Task ARoomPutAwayComesBackWhenAskedFor()
    {
        var (room, agents) = Room(ThreeRooms);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.Hide("scratch-1");
        room.Restore("scratch-1");
        Assert.Equal(["Claudy", "scratch-1", "Api"], room.Projects.Select(project => project.Id));
        Assert.Empty(room.Hidden);
    }

    [Fact]
    public async Task ARoomPutAwayIsForgottenOnceItsSessionsClose()
    {
        var later = Activity([Session("a", "/me/Claudy")]);
        var (room, agents) = Room(ThreeRooms, later, ThreeRooms);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.Hide("scratch-1");
        await agents.RefreshAsync(Ct);
        room.Sync();
        Assert.Empty(room.Hidden);
        await agents.RefreshAsync(Ct);
        room.Sync();
        Assert.Equal(["Claudy", "scratch-1", "Api"], room.Projects.Select(project => project.Id));
    }

    [Fact]
    public async Task ABannerAboutAnAgentBringsItsRoomBack()
    {
        var (room, agents) = Room(ThreeRooms);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.Hide("scratch-1");
        room.Show("x");
        Assert.Equal("scratch-1", room.Selected?.Id);
        Assert.Equal("x", room.SelectedAgentId);
    }

    [Fact]
    public async Task SessionsOfOneFolderShareARoom()
    {
        var activity = Activity(
            [Session("a", "/me/Claudy"), Session("b", "/me/Claudy"), Session("c", "/me/Api")],
            [Agent("x", "a", Read), Agent("y", "b", Read)]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.Sync();
        Assert.Equal(new HashSet<string> { "Claudy", "Api" }, room.Projects.Select(project => project.Id).ToHashSet());
        Assert.Equal(["x", "y"], room.Projects.First(project => project.Id == "Claudy").Agents.Select(agent => agent.Id));
    }

    [Fact]
    public async Task AnAgentSitsAtADeskWithItsWorkOnItsScreen()
    {
        var activity = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", [("Bash", "swift test")])]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.Sync();
        var scene = SceneOf(room, _now, 0.4);
        var desk = scene.Desks[RoomViewModel.DeskIndex(new RoomSeat(1, 0))];
        Assert.Equal(RoomGlyph.Symbol(AgentWork.Tests), desk?.Glyph);
        Assert.Equal(RoomOccupantState.Working, desk?.State);
        Assert.Equal("Commands · 1/6", scene.Zones[1].Title);
        Assert.Equal(RoomChiefState.Working, scene.Chief.State);
        Assert.Equal(RoomGlyph.Delegating, scene.Chief.Glyph);
        Assert.Equal(0.4, scene.Chief.Quota);
    }

    [Fact]
    public async Task AnAgentWhoseWorkChangesWalksToTheOtherArea()
    {
        var reading = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read)]);
        var writing = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", [("Edit", null), ("Edit", null)])]);
        var (room, agents) = Room(reading, writing);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        Assert.NotNull(SceneOf(room, _now).Desks[RoomViewModel.DeskIndex(new RoomSeat(2, 0))]);

        _now = _now.AddSeconds(30);
        await agents.RefreshAsync(Ct);
        room.Sync();
        var onTheWay = SceneOf(room, _now.AddSeconds(0.5));
        Assert.Equal(["x"], onTheWay.Walkers.Select(walker => walker.Id));
        Assert.All(onTheWay.Desks, desk => Assert.Null(desk));

        var arrived = SceneOf(room, _now.AddSeconds(60));
        Assert.Empty(arrived.Walkers);
        Assert.Equal("x", arrived.Desks[RoomViewModel.DeskIndex(new RoomSeat(0, 0))]?.Id);
    }

    [Fact]
    public async Task NobodyWalksInWhenTheRoomOpensOnAgentsAlreadyAtWork()
    {
        var activity = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read)]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        Assert.Empty(SceneOf(room, _now).Walkers);
    }

    [Fact]
    public async Task ANewAgentComesInByTheDoor()
    {
        var one = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read)]);
        var two = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read), Agent("y", "a", Read)]);
        var (room, agents) = Room(one, two);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        await agents.RefreshAsync(Ct);
        room.Sync();
        var scene = SceneOf(room, _now);
        Assert.Equal(["y"], scene.Walkers.Select(walker => walker.Id));
        Assert.Equal(RoomGeometry.Door, scene.Walkers[0].Position);
    }

    [Fact]
    public async Task AnAgentThatJustCrossedTheRoomStaysPutForAWhile()
    {
        var reading = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read)]);
        var writing = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", [("Edit", null)])]);
        var (room, agents) = Room(reading, writing, reading);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        _now = _now.AddSeconds(30);
        await agents.RefreshAsync(Ct);
        room.Sync();
        _now = _now.AddSeconds(5);
        await agents.RefreshAsync(Ct);
        room.Sync();
        var scene = SceneOf(room, _now.AddSeconds(60));
        Assert.Equal("x", scene.Desks[RoomViewModel.DeskIndex(new RoomSeat(0, 0))]?.Id);
    }

    [Fact]
    public async Task AnAgentThatIsDoneGoesToTheSofa()
    {
        var working = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read)]);
        var done = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read, isFinished: true)]);
        var (room, agents) = Room(working, done);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        await agents.RefreshAsync(Ct);
        room.Sync();

        var onTheWay = SceneOf(room, _now.AddSeconds(0.5));
        Assert.Equal(["x"], onTheWay.Walkers.Select(walker => walker.Id));
        Assert.Equal(RoomGlyph.Done, onTheWay.Walkers[0].Glyph);
        Assert.All(onTheWay.Desks, desk => Assert.Null(desk));

        var seated = SceneOf(room, _now.AddSeconds(60));
        Assert.Equal(["x"], seated.Loungers.Select(lounger => lounger.Id));
        Assert.Equal([0], seated.Loungers.Select(lounger => lounger.Slot));
        Assert.Empty(seated.Walkers);
        Assert.Equal(0, seated.Zones[2].Count);
    }

    [Fact]
    public async Task AnAgentAlreadyDoneWhenTheRoomOpensSitsOnTheSofaAtOnce()
    {
        var done = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read, isFinished: true)]);
        var (room, agents) = Room(done);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        var scene = SceneOf(room, _now);
        Assert.Empty(scene.Walkers);
        Assert.Equal([0], scene.Loungers.Select(lounger => lounger.Slot));
    }

    [Fact]
    public async Task AnAgentThatLeavesTheListGetsUpAndGoesOutByTheDoor()
    {
        var done = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", Read, isFinished: true)]);
        var gone = Activity([Session("a", "/me/Claudy")]);
        var (room, agents) = Room(done, gone);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        await agents.RefreshAsync(Ct);
        room.Sync();

        var leaving = SceneOf(room, _now.AddSeconds(0.5));
        Assert.Equal(["x"], leaving.Walkers.Select(walker => walker.Id));
        Assert.Empty(leaving.Loungers);
        Assert.Empty(SceneOf(room, _now.AddSeconds(120)).Walkers);
    }

    [Fact]
    public async Task WithTheSofaFullAnAgentThatIsDoneGoesStraightOut()
    {
        string[] ids = ["a", "b", "c", "d", "e"];
        var working = Activity([Session("s", "/me/Claudy")], [.. ids.Select(id => Agent(id, "s", Read))]);
        var done = Activity([Session("s", "/me/Claudy")], [.. ids.Select(id => Agent(id, "s", Read, isFinished: true))]);
        var (room, agents) = Room(working, done);
        await agents.RefreshAsync(Ct);
        room.IsVisible = true;
        await agents.RefreshAsync(Ct);
        room.Sync();
        var later = SceneOf(room, _now.AddSeconds(120));
        Assert.Equal(RoomGeometry.SofaSeats, later.Loungers.Count);
        Assert.Empty(later.Walkers);
        Assert.All(later.Desks, desk => Assert.Null(desk));
    }

    [Fact]
    public async Task ClaudysScreenShowsWhatTheSessionItselfDoes()
    {
        var reading = AgentWorks.Read("Bash", "swift test");
        var lead = Session("a", "/me/Claudy") with
        {
            LastAction = new AgentAction(_now, AgentActionKind.Run, "Bash", null, reading.Work, reading.Detail),
        };
        var (room, agents) = Room(Activity([lead]));
        await agents.RefreshAsync(Ct);
        room.Sync();
        var chief = SceneOf(room, _now).Chief;
        Assert.Equal(RoomGlyph.Symbol(AgentWork.Tests), chief.Glyph);
        Assert.Equal("a · tests · swift test", chief.Help);

        var stale = SceneOf(room, _now.AddSeconds(600)).Chief;
        Assert.Equal(RoomGlyph.Thinking, stale.Glyph);
    }

    [Fact]
    public async Task AClickOnADeskChoosesItsAgentAndASecondOneLetsGo()
    {
        var activity = Activity([Session("a", "/me/Claudy")], [Agent("x", "a", [("Bash", "swift test")])]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.ToggleAgent("x");
        Assert.Equal("x", room.SelectedAgent?.Id);
        Assert.Equal(RoomViewModel.DeskIndex(new RoomSeat(1, 0)), SceneOf(room, _now).SelectedDesk);
        Assert.Equal(RoomGeometry.Desk(new RoomSeat(1, 0)), room.Place("x"));
        room.ToggleAgent("x");
        Assert.Null(room.SelectedAgent);
    }

    [Fact]
    public async Task ABannerAboutAnAgentLeadsToItsDeskInItsProject()
    {
        var activity = Activity(
            [Session("a", "/me/Claudy"), Session("b", "/me/Api")],
            [Agent("x", "a", Read), Agent("y", "b", [("Bash", "swift test")])]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.Sync();
        room.IsOverview = true;
        room.Show("y");
        Assert.Equal("Api", room.Selected?.Id);
        Assert.Equal("y", room.SelectedAgent?.Id);
        Assert.False(room.IsOverview);

        room.Enter("Claudy");
        Assert.Equal("Claudy", room.Selected?.Id);
        Assert.Null(room.SelectedAgent);
    }

    [Fact]
    public async Task AWaitingSessionShowsOnClaudysScreen()
    {
        var activity = Activity([Session("a", "/me/Claudy", SessionState.Waiting, "permission prompt")]);
        var (room, agents) = Room(activity);
        await agents.RefreshAsync(Ct);
        room.Sync();
        var chief = SceneOf(room, _now).Chief;
        Assert.Equal(RoomChiefState.Waiting, chief.State);
        Assert.Equal(RoomGlyph.Waiting, chief.Glyph);
    }

    [Fact]
    public void AnEmptyRoomHasNobodyAtTheCentreDesk()
    {
        var (room, _) = Room(ClaudeActivity.Empty);
        Assert.Equal(RoomChiefState.Away, room.Scene(null, _now, null).Chief.State);
    }

    [Fact]
    public void TheSummarySaysWhatNeedsTheUserFirst()
    {
        var waiting = Session("a", "/me/Claudy", SessionState.Waiting, "permission prompt");
        var project = new RoomViewModel.Project("Claudy", new([waiting]), new([Agent("x", "a", Read)]));
        var lines = RoomSummaryText.Sentences(new RoomTally(project, _now), waiting).Select(line => line.Text);
        Assert.Equal(["Waiting for your answer: permission prompt", "1 agent is working"], lines);
    }

    /// <summary>One activity per pass, then the last one again.</summary>
    private sealed class SequenceActivity(params ClaudeActivity[] passes) : IAgentScanning
    {
        private readonly Queue<ClaudeActivity> _passes = new(passes);

        public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_passes.Count > 1 ? _passes.Dequeue() : _passes.Peek());
    }

    /// <summary>What Claudy's tests keep in a <c>UserDefaults</c> suite of their own: shared by two rooms, as by two launches.</summary>
    private sealed class MemoryStore : IRoomStore
    {
        public IReadOnlyList<string> HiddenRooms { get; set; } = [];
        public string WeatherChoice { get; set; } = RoomWeathers.AsItComes;
    }

    private sealed class NoActions : ISessionActions
    {
        public bool IsSession(int pid, DateTimeOffset? startedAt) => true;

        public SignalResult Interrupt(int pid, DateTimeOffset startedAt) => SignalResult.Sent;

        public bool BringForward(HostApp host) => false;
    }
}
