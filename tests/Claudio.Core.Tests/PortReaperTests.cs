using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>The guardrails against a fake: no test ever touches a real process.</summary>
public sealed class PortReaperTests
{
    private const int OwnPid = 99;
    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 19, 30, 0, TimeSpan.Zero);

    private sealed class FakeSignals(params int[] alive) : IProcessSignals
    {
        private readonly Dictionary<int, int> _hits = [];

        public HashSet<int> Alive { get; } = [.. alive];
        public List<int> Terminated { get; } = [];
        /// <summary>How many terminations a process takes before it goes.</summary>
        public int Toughness { get; init; } = 1;
        public bool Refuses { get; init; }

        public bool Terminate(int pid)
        {
            Terminated.Add(pid);
            if (Refuses)
            {
                return false;
            }
            _hits[pid] = _hits.GetValueOrDefault(pid) + 1;
            if (_hits[pid] >= Toughness)
            {
                Alive.Remove(pid);
            }
            return true;
        }

        public bool IsRunning(int pid) => Alive.Contains(pid);
    }

    private static ListeningPort Port(int pid, PortAttribution attribution = PortAttribution.Orphan, int? session = null) =>
        new($"{pid}-4000", pid, 4000, "python.exe", null, Epoch, attribution, session);

    private static ProcessTable Table(params RunningProcess[] processes) =>
        new(processes.ToDictionary(process => process.Pid));

    private static PortReaper Reaper(FakeSignals signals) => new(signals, OwnPid, TimeSpan.Zero);

    private static RunningProcess Process(int pid, int parent = 1, string arguments = "python.exe", double seconds = 0) =>
        new(pid, parent, Epoch.AddSeconds(seconds), arguments);

    [Fact]
    public void TerminatesTheProcess()
    {
        var signals = new FakeSignals(500);
        Assert.Null(Reaper(signals).Kill(Port(500), Table(Process(500))));
        Assert.Equal([500], signals.Terminated);
    }

    /// <summary>A dev server is a tree: its children go first, deepest first, the port's process last.</summary>
    [Fact]
    public void TerminatesDescendantsFirst()
    {
        var signals = new FakeSignals(500, 501, 502, 503);
        var result = Reaper(signals).Kill(Port(500), Table(
            Process(500), Process(501, 500, "node.exe", 1), Process(502, 501, "esbuild.exe", 2), Process(503, 500, "node.exe", 3)));
        Assert.Null(result);
        Assert.Equal([502, 501, 503, 500], signals.Terminated);
    }

    /// <summary>A Claude session started from the server keeps its subtree: none of it is touched.</summary>
    [Fact]
    public void SparesAProtectedDescendantAndItsSubtree()
    {
        var signals = new FakeSignals(500, 501, 502);
        Reaper(signals).Kill(Port(500), Table(
            Process(500), Process(501, 500, @"C:\Users\x\.local\bin\claude.exe", 1), Process(502, 501, "node.exe", 2)));
        Assert.Equal([500], signals.Terminated);
    }

    /// <summary>A child whose parent PID was reissued to the server is not the server's.</summary>
    [Fact]
    public void SparesAStaleChild()
    {
        var signals = new FakeSignals(500, 501);
        Reaper(signals).Kill(Port(500), Table(Process(500), Process(501, 500, "node.exe", -60)));
        Assert.Equal([500], signals.Terminated);
    }

    [Fact]
    public void EscalatesWhenTheFirstTerminationDoesNotTake()
    {
        var signals = new FakeSignals(500) { Toughness = 2 };
        Assert.Null(Reaper(signals).Kill(Port(500), Table(Process(500))));
        Assert.Equal([500, 500], signals.Terminated);
    }

    [Fact]
    public void ReportsAProcessThatSurvives()
    {
        var signals = new FakeSignals(500) { Toughness = int.MaxValue };
        Assert.Equal(KillRefusal.SurvivedKill, Reaper(signals).Kill(Port(500), Table(Process(500))));
        Assert.Equal([500, 500], signals.Terminated);
    }

    [Fact]
    public void ReportsASystemRefusal()
    {
        var signals = new FakeSignals(500) { Refuses = true };
        Assert.Equal(KillRefusal.SystemRefused, Reaper(signals).Kill(Port(500), Table(Process(500))));
    }

    /// <summary>A process that exited on its own before the termination landed is closed, not refused.</summary>
    [Fact]
    public void AProcessAlreadyGoneIsNotARefusal()
    {
        var signals = new FakeSignals() { Refuses = true };
        Assert.Null(Reaper(signals).Kill(Port(500), Table(Process(500))));
    }

    /// <summary>
    /// Between the render and the click, the process may have died and its PID been reissued to
    /// something else. The start time is what tells the two apart.
    /// </summary>
    [Fact]
    public void RefusesWhenStartTimeNoLongerMatches()
    {
        var signals = new FakeSignals(500);
        Assert.Equal(KillRefusal.IdentityChanged, Reaper(signals).Kill(Port(500), Table(Process(500, seconds: 60))));
        Assert.Empty(signals.Terminated);
    }

    [Fact]
    public void RefusesWhenTheProcessIsGone()
    {
        var signals = new FakeSignals();
        Assert.Equal(KillRefusal.IdentityChanged, Reaper(signals).Kill(Port(500), Table()));
        Assert.Empty(signals.Terminated);
    }

    [Fact]
    public void RefusesToKillItself()
    {
        var signals = new FakeSignals(OwnPid);
        Assert.Equal(KillRefusal.ProtectedProcess, Reaper(signals).Kill(Port(OwnPid), Table(Process(OwnPid, arguments: "Claudio.exe"))));
        Assert.Empty(signals.Terminated);
    }

    [Fact]
    public void RefusesToKillItsOwnAncestor()
    {
        var signals = new FakeSignals(42);
        var result = Reaper(signals).Kill(Port(42), Table(Process(OwnPid, 42, "Claudio.exe", 5), Process(42, 1, "launcher.exe")));
        Assert.Equal(KillRefusal.ProtectedProcess, result);
        Assert.Empty(signals.Terminated);
    }

    /// <summary>Killing the port would be one thing; killing the Claude session that owns it is another.</summary>
    [Fact]
    public void RefusesToKillALiveClaudeSessionRoot()
    {
        var signals = new FakeSignals(8859);
        var result = Reaper(signals).Kill(Port(8859, PortAttribution.Live, 8859),
                                          Table(Process(8859, arguments: @"C:\Users\x\.local\bin\claude.exe")));
        Assert.Equal(KillRefusal.LiveClaudeSession, result);
        Assert.Empty(signals.Terminated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void RefusesTheSystem(int pid) =>
        Assert.Equal(KillRefusal.ProtectedProcess, Reaper(new FakeSignals(pid)).Kill(Port(pid), Table(Process(pid, 0, "System"))));
}
