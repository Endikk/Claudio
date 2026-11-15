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

    [Fact]
    public void KillsByPidWithTheSameGuardrails()
    {
        var signals = new FakeSignals(OwnPid);
        Assert.Equal(KillRefusal.ProtectedProcess,
                     Reaper(signals).Kill(OwnPid, Epoch, Table(Process(OwnPid, arguments: "Claudio.exe"))));
        Assert.Empty(signals.Terminated);
    }

    // MCP servers left behind

    private static McpOrphan Server(int pid, double seconds = 0) =>
        new($"mcp-{pid}", pid, Epoch.AddSeconds(seconds), "chrome-devtools-mcp", null, 0);

    /// <summary>
    /// <c>cmd.exe /c npx</c> above npx's <c>node</c> above the server's <c>node</c>: the tree an MCP
    /// server runs as on Windows.
    /// </summary>
    private static RunningProcess[] ServerTree(int root = 600, int parent = 1) =>
    [
        Process(root, parent, @"C:\WINDOWS\system32\cmd.exe /d /s /c ""npx -y chrome-devtools-mcp@latest"""),
        Process(root + 1, root, @"""C:\Program Files\nodejs\node.exe"" ""C:\Program Files\nodejs\node_modules\npm\bin\npx-cli.js"" -y chrome-devtools-mcp@latest", 1),
        Process(root + 2, root + 1, @"""C:\Program Files\nodejs\node.exe"" C:\Users\x\AppData\Local\npm-cache\_npx\1a2b\node_modules\chrome-devtools-mcp\build\src\index.js", 2),
    ];

    /// <summary>
    /// The whole tree goes, deepest first and the server last, and nothing outside it: the dead
    /// session left other processes behind, which share nothing with the row the user clicked.
    /// </summary>
    [Fact]
    public void TerminatesTheServersTreeAndNothingElse()
    {
        var signals = new FakeSignals(600, 601, 602, 700);
        var result = Reaper(signals).Kill(Server(600), Table([.. ServerTree(), Process(700, 300, "node.exe other.js")]));
        Assert.Null(result);
        Assert.Equal([602, 601, 600], signals.Terminated);
        Assert.Contains(700, signals.Alive);
    }

    [Fact]
    public void EscalatesTheWholeTree()
    {
        var signals = new FakeSignals(600, 601, 602) { Toughness = 2 };
        Assert.Null(Reaper(signals).Kill(Server(600), Table(ServerTree())));
        Assert.Equal([602, 601, 600, 602, 601, 600], signals.Terminated);
    }

    /// <summary>A process of the tree that already exited is not terminated: its PID may be someone else's by now.</summary>
    [Fact]
    public void SkipsAMemberAlreadyGone()
    {
        var signals = new FakeSignals(600, 602);
        Assert.Null(Reaper(signals).Kill(Server(600), Table(ServerTree())));
        Assert.Equal([602, 600], signals.Terminated);
    }

    [Fact]
    public void ReportsATreeThatSurvives()
    {
        var signals = new FakeSignals(600, 601, 602) { Toughness = int.MaxValue };
        Assert.Equal(KillRefusal.SurvivedKill, Reaper(signals).Kill(Server(600), Table(ServerTree())));
    }

    [Fact]
    public void ReportsAServerTheSystemRefuses()
    {
        var signals = new FakeSignals(600, 601, 602) { Refuses = true };
        Assert.Equal(KillRefusal.SystemRefused, Reaper(signals).Kill(Server(600), Table(ServerTree())));
    }

    /// <summary>Between the scan and the click, the server may have come under a live session.</summary>
    [Fact]
    public void RefusesAServerThatHasALiveClaudeAncestorAgain()
    {
        var signals = new FakeSignals(600, 8859);
        var result = Reaper(signals).Kill(Server(600), Table(
            [.. ServerTree(parent: 8859), Process(8859, 1, @"C:\Users\x\.local\bin\claude.exe", -60)]));
        Assert.Equal(KillRefusal.LiveClaudeSession, result);
        Assert.Empty(signals.Terminated);
    }

    [Fact]
    public void RefusesAServerWhoseStartTimeChanged()
    {
        var signals = new FakeSignals(600);
        Assert.Equal(KillRefusal.IdentityChanged, Reaper(signals).Kill(Server(600, seconds: -60), Table(ServerTree())));
        Assert.Empty(signals.Terminated);
    }

    [Fact]
    public void RefusesAServerAboveClaudio()
    {
        var signals = new FakeSignals(600, OwnPid);
        var result = Reaper(signals).Kill(Server(600), Table([.. ServerTree(), Process(OwnPid, 602, "Claudio.exe", 5)]));
        Assert.Equal(KillRefusal.ProtectedProcess, result);
        Assert.Empty(signals.Terminated);
    }

    /// <summary>A server that drives Claude Code holds a live session in its tree: killing the tree would kill that session.</summary>
    [Fact]
    public void RefusesATreeThatHoldsALiveClaudeSession()
    {
        var signals = new FakeSignals(600, 601, 602, 603);
        var result = Reaper(signals).Kill(Server(600), Table(
            [.. ServerTree(), Process(603, 602, @"C:\Users\x\.local\bin\claude.exe -p", 3)]));
        Assert.Equal(KillRefusal.LiveClaudeSession, result);
        Assert.Empty(signals.Terminated);
    }
}
