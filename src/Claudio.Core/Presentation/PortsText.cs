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
