using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Presentation;

/// <summary>What the Ports tab writes, worked out away from the UI so it can be tested, as Claudy's <c>PortsViewModel</c>.</summary>
public static class PortsText
{
    /// <summary>Age in the largest unit that still reads at a glance: "12 s", "4 min", "2 h", "3 d".</summary>
    public static string Age(DateTimeOffset since, DateTimeOffset now)
    {
        var seconds = (long)(now - since).TotalSeconds;
        return seconds switch
        {
            < 60 => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(seconds, 0)} s"),
            < 3600 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min"),
            < 86400 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} h"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{seconds / 86400} d"),
        };
    }

    /// <summary>The row's second line: "Surikat · 4 min", or the age alone without a project.</summary>
    public static string Subtitle(ListeningPort port, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(port);
        var age = Age(port.StartedAt, now);
        return port.ProjectName is { } project ? $"{project} · {age}" : age;
    }

    /// <summary>An MCP server's second line, as a port's: "GoodFood · 3 h".</summary>
    public static string Subtitle(McpOrphan server, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(server);
        var age = Age(server.StartedAt, now);
        return server.ProjectName is { } project ? $"{project} · {age}" : age;
    }

    /// <summary>Everything Claude left behind, ports and servers alike: the number on the tab's badge.</summary>
    public static int OrphanCount(IReadOnlyCollection<ListeningPort> orphans, IReadOnlyCollection<McpOrphan> servers)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        ArgumentNullException.ThrowIfNull(servers);
        return orphans.Count + servers.Count;
    }

    /// <summary>
    /// What the bulk kill is about to take down, as the confirmation names it: ports by number,
    /// servers by name.
    /// </summary>
    public static string BulkKillTargets(IEnumerable<ListeningPort> orphans, IEnumerable<McpOrphan> servers)
    {
        ArgumentNullException.ThrowIfNull(orphans);
        ArgumentNullException.ThrowIfNull(servers);
        return string.Join(", ", orphans.Select(port => port.Port.ToString(CultureInfo.InvariantCulture))
                                        .Concat(servers.Select(server => server.Name)));
    }

    /// <summary>What every server listed holds, in bytes.</summary>
    public static ulong McpMemory(IEnumerable<McpOrphan> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        return servers.Aggregate(0UL, (sum, server) => sum + server.ResidentBytes);
    }

    private const ulong BytesPerMegabyte = 1UL << 20;
    private const ulong MegabytesPerGigabyte = 1UL << 10;

    /// <summary>
    /// Memory in binary units, as Task Manager counts it: whole megabytes, then gigabytes with one
    /// decimal once the megabytes no longer read at a glance. "412 MB", "1.1 GB".
    /// </summary>
    public static string Memory(ulong bytes)
    {
        var megabytes = (bytes + (BytesPerMegabyte / 2)) / BytesPerMegabyte;
        if (megabytes < MegabytesPerGigabyte)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{megabytes} MB");
        }
        var gigabytes = (double)bytes / (BytesPerMegabyte * MegabytesPerGigabyte);
        // printf's rounding, half to even, as Claudy's %.1f: 1280 MB reads "1.2 GB" in both.
        return Math.Round(gigabytes, 1, MidpointRounding.ToEven).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>The kill button's accessible name on a server's row.</summary>
    public static string KillLabel(McpOrphan server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return $"Kill the MCP server {server.Name} and its processes";
    }

    /// <summary>Shown inline on the row whose kill failed.</summary>
    public static string Explain(KillRefusal refusal) => refusal switch
    {
        KillRefusal.IdentityChanged => "the process changed, nothing was killed",
        KillRefusal.ProtectedProcess => "protected process",
        KillRefusal.LiveClaudeSession => "live Claude session",
        KillRefusal.SystemRefused => "refused by the system",
        KillRefusal.SurvivedKill => "still alive after the kill",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
    };
}
