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

/// <summary>What the Ports tab shows: a scan under way, its result, or why there is none.</summary>
public abstract record PortScanState
{
    private PortScanState()
    {
    }

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
