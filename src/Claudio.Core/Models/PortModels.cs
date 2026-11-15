namespace Claudio.Core.Models;

/// <summary>One TCP socket in the LISTEN state, as the system's TCP table reports it.</summary>
public sealed record Listener(int Pid, string Command, int Port, string Address);

public enum PortAttribution
{
    /// <summary>A Claude session that is still running owns this port.</summary>
    Live,
    /// <summary>Claude launched it, and the session that did is gone. The reason the Ports tab exists.</summary>
    Orphan,
}

/// <summary>
/// A port Claude Code holds open. <see cref="Id"/> joins the PID and the port, and
/// <see cref="StartedAt"/> is what tells this process from a later one that reuses its PID.
/// </summary>
public sealed record ListeningPort(
    string Id,
    int Pid,
    int Port,
    string Command,
    string? ProjectName,
    DateTimeOffset StartedAt,
    PortAttribution Attribution,
    int? SessionRootPid);

/// <summary>
/// An MCP server Claude Code launched over stdio and left running when its session ended, as
/// Claudy's <c>McpOrphan</c>. It holds no port, so the TCP table never sees it: its environment and
/// command line give it away.
/// </summary>
/// <param name="Pid">The top of the orphaned tree: <c>cmd.exe /c npx …</c>, not the <c>node</c> it spawned.</param>
/// <param name="ResidentBytes">
/// The working set of the top process and everything under it, in bytes: what a kill gives back.
/// </param>
public sealed record McpOrphan(
    string Id,
    int Pid,
    DateTimeOffset StartedAt,
    string Name,
    string? ProjectName,
    ulong ResidentBytes);

/// <summary>What the Ports tab shows: a scan under way, its result, or why there is none.</summary>
public abstract record PortScanState
{
    private PortScanState()
    {
    }

    /// <summary>
    /// The processes this scan lists by port, which the MCP scan skips so no process is listed
    /// twice. None when the scan could not run: the MCP scan does not need it.
    /// </summary>
    public IReadOnlySet<int> ListedPids => this is Ready ready ? ready.Ports.Select(port => port.Pid).ToHashSet() : new HashSet<int>();

    /// <summary>The first scan has not answered yet.</summary>
    public sealed record Scanning : PortScanState
    {
        public static Scanning Instance { get; } = new();
    }

    /// <summary>
    /// <see cref="IsDegraded"/> marks a scan that could not read process environments and fell
    /// back to the process tree: live sessions still show, orphans cannot. Two readings with the
    /// same ports are equal, so a view can tell a new scan from the same one.
    /// </summary>
    public sealed record Ready(IReadOnlyList<ListeningPort> Ports, bool IsDegraded) : PortScanState
    {
        public bool Equals(Ready? other) =>
            other is not null && IsDegraded == other.IsDegraded && Ports.SequenceEqual(other.Ports);

        public override int GetHashCode() => HashCode.Combine(IsDegraded, Ports.Count);
    }

    /// <summary>The scan could not run at all; <see cref="Reason"/> is shown to the user.</summary>
    public sealed record Unavailable(string Reason) : PortScanState;
}
