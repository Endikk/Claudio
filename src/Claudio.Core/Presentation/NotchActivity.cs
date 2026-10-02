using System.Globalization;
using Claudio.Core.Models;

namespace Claudio.Core.Presentation;

/// <summary>
/// What the open island says, worked out from a reading, as Claudy's <c>NotchActivity</c>: the
/// lead quota in large, the others in small, and when the lead one resets.
/// </summary>
public sealed record NotchActivity
{
    public NotchActivity(UsageSnapshot snapshot, DateTimeOffset now, CultureInfo? culture = null)
    {
        var lead = snapshot.Primary;
        Lead = lead;
        Others = snapshot.Spend is null ? [snapshot.Weekly, snapshot.Scoped] : [];
        ResetLine = lead.IsActive(now)
            ? $"reset {UsageFormat.ResetTime(lead.ResetDate, now, culture)} · in {UsageFormat.Countdown(lead.ResetDate, now)}"
            : lead.IsMeasured ? "inactive" : "unavailable";
        SpentLine = lead.Amount is { } amount ? UsageFormat.Spent(amount, culture) : null;
    }

    public UsageWindow Lead { get; }

    /// <summary>Weekly then per model, as on the card. None on a plan billed on usage, which has neither.</summary>
    public IReadOnlyList<UsageWindow> Others { get; }

    /// <summary>"reset 14:30 · in 4 h 5 min", or why there is no countdown: never one to a reset already past.</summary>
    public string ResetLine { get; }

    /// <summary>"$46.31 of $500.00" when the lead is a spend cap.</summary>
    public string? SpentLine { get; }
}
