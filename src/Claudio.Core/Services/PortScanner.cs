using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Lists the TCP ports Claude Code is holding open on this machine, as Claudy's
/// <c>PortScanner</c>. The three sources are injected: the app reads them from Windows, the tests
/// hand them in, and nothing here touches a live process.
///
/// Attribution comes from the process environment, never from the process tree: an orphaned
/// server has no Claude ancestor left, yet it is exactly the one worth killing. The tree only
/// answers a second question, whether the owning session is still alive.
/// </summary>
public sealed class PortScanner(
    Func<IReadOnlyList<Listener>?> listeners,
    Func<ProcessTable?> table,
    Func<int, ProcessEnvironment.Markers?> markers)
{
    /// <summary>
    /// A container runtime publishes ports for everything it hosts, and WSL's relay forwards the
    /// ports of a whole distribution. Killing one would take everything behind it down, so it is
    /// never attributed, whatever its environment.
    /// </summary>
    private static readonly string[] DeniedCommands =
        ["orbstack", "docker", "com.docker.backend", "vpnkit", "wslrelay", "podman", "gvproxy", "rancher-desktop"];

    /// <summary>Listeners first, then the tree: a server started in between is then in the tree.</summary>
    public PortScanState Scan()
    {
        if (listeners() is not { } found)
        {
            return new PortScanState.Unavailable("the TCP table is unreadable");
        }
        if (table() is not { } processes)
        {
            return new PortScanState.Unavailable("the process list is unreadable");
        }
        var (ports, isDegraded) = Attribute(Collapse(found), processes, markers);
        return new PortScanState.Ready(ports.OrderBy(port => port.Port).ThenBy(port => port.Pid).ToList(), isDegraded);
    }

    /// <summary>
    /// A server holds one socket per address family, and Windows reports each: a dev server on
    /// <c>localhost</c> shows on 127.0.0.1 and on ::1. Collapsing on (pid, port) is what keeps
    /// <see cref="ListeningPort.Id"/> unique; the first address seen is kept.
    /// </summary>
    public static IReadOnlyList<Listener> Collapse(IEnumerable<Listener> sockets)
    {
        var seen = new HashSet<(int, int)>();
        return sockets.Where(socket => seen.Add((socket.Pid, socket.Port))).ToList();
    }

    public static bool IsDenied(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return DeniedCommands.Any(denied => command.Contains(denied, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// When an environment cannot be read at all, the scan degrades to what the process tree
    /// alone can prove, a live session, and says so, because that fallback is blind to the
    /// orphans the tab exists for.
    /// </summary>
    public static (IReadOnlyList<ListeningPort> Ports, bool IsDegraded) Attribute(
        IReadOnlyList<Listener> listeners,
        ProcessTable table,
        Func<int, ProcessEnvironment.Markers?> markers)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(markers);

        var isDegraded = false;
        // One process often listens on several ports; its environment is read once.
        var environments = new Dictionary<int, ProcessEnvironment.Markers?>();
        var ports = new List<ListeningPort>();

        foreach (var listener in listeners)
        {
            if (IsDenied(listener.Command) || !table.Processes.TryGetValue(listener.Pid, out var process))
            {
                continue;
            }
            var root = table.ClaudeSessionRoot(listener.Pid);
            if (!environments.TryGetValue(listener.Pid, out var marker))
            {
                marker = markers(listener.Pid);
                environments[listener.Pid] = marker;
            }

            if (marker is null)
            {
                isDegraded = true;
                if (root is not null)
                {
                    ports.Add(Port(listener, process, null, PortAttribution.Live, root.Pid));
                }
                continue;
            }
            if (!marker.IsClaude)
            {
                continue;
            }
            ports.Add(Port(listener, process, ProjectName(marker.ProjectDirectory),
                           root is null ? PortAttribution.Orphan : PortAttribution.Live, root?.Pid));
        }

        return (ports, isDegraded);
    }

    /// <summary>The last component of <c>CLAUDE_PROJECT_DIR</c>, whichever separator it uses.</summary>
    public static string? ProjectName(string? directory)
    {
        var trimmed = directory?.TrimEnd('\\', '/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }
        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }

    private static ListeningPort Port(Listener listener, RunningProcess process, string? project,
                                      PortAttribution attribution, int? sessionRoot) =>
        new($"{listener.Pid}-{listener.Port}", listener.Pid, listener.Port, listener.Command, project,
            process.StartedAt, attribution, sessionRoot);
}
