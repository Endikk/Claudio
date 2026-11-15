using Claudio.Core.Models;

namespace Claudio.Core.Tests;

public sealed class AgentModelsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_793_468_000);

    private static AgentAction Action(string tool, string? file = null, double secondsAgo = 0) =>
        new(Now.AddSeconds(-secondsAgo), AgentActionKinds.Of(tool), tool, file);

    private static RunningAgent Agent(AgentAction? last, AgentAction[]? recent = null, double lastActivity = 0, bool isFinished = false) => new()
    {
        Id = "a1", SessionId = "s1", SessionPid = 4242, SessionName = "claire-bb", Title = "Benchmark",
        StartedAt = Now.AddSeconds(-600), LastActivity = Now.AddSeconds(-lastActivity), IsFinished = isFinished,
        ActionCount = recent?.Length ?? 0, Recent = recent ?? [], LastAction = last,
    };

    [Fact]
    public void ToolNamesSortIntoKinds()
    {
        Assert.Equal(AgentActionKind.Read, AgentActionKinds.Of("Read"));
        Assert.Equal(AgentActionKind.Read, AgentActionKinds.Of("Grep"));
        Assert.Equal(AgentActionKind.Edit, AgentActionKinds.Of("Write"));
        Assert.Equal(AgentActionKind.Edit, AgentActionKinds.Of("Edit"));
        Assert.Equal(AgentActionKind.Run, AgentActionKinds.Of("Bash"));
        Assert.Equal(AgentActionKind.Web, AgentActionKinds.Of("WebFetch"));
        Assert.Equal(AgentActionKind.Web, AgentActionKinds.Of("mcp__plugin_ecc_chrome-devtools__click"));
        Assert.Equal(AgentActionKind.Other, AgentActionKinds.Of("Agent"));
    }

    /// <summary>The file when the last call had one, otherwise the kind of work in one word.</summary>
    [Fact]
    public void TouchingNamesTheFileOrTheKindOfWork()
    {
        Assert.Equal("banc.ts", Agent(Action("Write", "banc.ts")).Touching);
        Assert.True(Agent(Action("Write", "banc.ts")).IsWriting);
        Assert.False(Agent(Action("Read", "a.ts")).IsWriting);
        Assert.Equal("terminal", Agent(Action("Bash")).Touching);
        Assert.Equal("web", Agent(Action("WebSearch")).Touching);
        Assert.Equal("search", Agent(Action("Grep")).Touching);
        Assert.Equal("starting", Agent(null).Touching);
    }

    /// <summary>A minute of silence is a pause; three minutes are worth a look; finished is finished.</summary>
    [Theory]
    [InlineData(10, false, AgentStatus.Working)]
    [InlineData(59, false, AgentStatus.Working)]
    [InlineData(60, false, AgentStatus.Idle)]
    [InlineData(90, false, AgentStatus.Idle)]
    [InlineData(179, false, AgentStatus.Idle)]
    [InlineData(180, false, AgentStatus.Stalled)]
    [InlineData(200, false, AgentStatus.Stalled)]
    [InlineData(10, true, AgentStatus.Done)]
    [InlineData(200, true, AgentStatus.Done)]
    public void StatusFollowsTheLastActivity(double silence, bool finished, AgentStatus status) =>
        Assert.Equal(status, Agent(null, lastActivity: silence, isFinished: finished).Status(Now));

    [Fact]
    public void SilenceCountsFromTheLastWrite()
    {
        Assert.Equal(200, Agent(null, lastActivity: 200).Silence(Now).TotalSeconds, 3);
        Assert.Equal(TimeSpan.Zero, Agent(null, lastActivity: -5).Silence(Now));
    }

    /// <summary>Ten minutes in forty slots of fifteen seconds, the newest last; anything older is left out.</summary>
    [Fact]
    public void TrackCountsEditsApartFromTheRest()
    {
        AgentAction[] recent = [Action("Write", "a.ts", 5), Action("Read", "b.ts", 7), Action("Bash", secondsAgo: 20), Action("Edit", "c.ts", 620)];
        var track = Agent(null, recent).Track(Now);

        Assert.Equal(RunningAgent.TrackSlots, track.Count);
        Assert.Equal(new AgentTrackSlot(1, 1), track[^1]);
        Assert.Equal(new AgentTrackSlot(0, 1), track[^2]);
        Assert.Equal(0, track.SkipLast(2).Sum(slot => slot.Edits + slot.Others));
    }

    /// <summary>Two readings of the same agent are equal, so a view model can tell a new pass from the same one.</summary>
    [Fact]
    public void TheSameReadingIsEqual()
    {
        AgentAction[] recent = [Action("Write", "a.ts", 5)];
        Assert.Equal(Agent(recent[0], [.. recent]), Agent(recent[0], [.. recent]));
        Assert.NotEqual(Agent(recent[0], [.. recent]), Agent(recent[0], []));
    }

    [Fact]
    public void ASessionSaysWhatItWaitsFor()
    {
        var session = new ClaudeSession { Id = "s", Pid = 7, Name = "claudio-3c", Cwd = @"g:\Projet - Programation\Claudio\", Status = SessionState.Waiting, WaitingFor = "permission prompt" };
        Assert.Equal("Permission prompt", session.WaitingReason);
        Assert.Equal("Claudio", session.FolderName);
        Assert.True(session.IsWaiting);
        Assert.False(session.CanBeInterrupted);
        Assert.True((session with { ProcessStartedAt = Now }).CanBeInterrupted);
        Assert.False((session with { ProcessStartedAt = Now, Status = SessionState.Idle }).CanBeInterrupted);
        Assert.Equal(SessionState.Busy, ClaudeSession.StateOf("compacting"));
        Assert.Equal(SessionState.Unknown, ClaudeSession.StateOf(null));
        Assert.True(ClaudeSession.IsBackgroundEntrypoint("sdk-ts"));
        Assert.False(ClaudeSession.IsBackgroundEntrypoint("claude-vscode"));
    }
}
