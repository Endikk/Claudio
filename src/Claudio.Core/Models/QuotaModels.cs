namespace Claudio.Core.Models;

/// <summary>
/// One quota window as the account actually reports it. <see cref="Percent"/> is Anthropic's own
/// value, never a local estimate.
/// </summary>
public sealed record QuotaWindow
{
    public QuotaWindow(double percent, DateTimeOffset? resetsAt, string? label = null)
    {
        Percent = Math.Clamp(percent, 0, 1);
        ResetsAt = resetsAt;
        Label = label;
    }

    /// <summary>0…1.</summary>
    public double Percent { get; }

    /// <summary>Reset instant announced by Anthropic; null when the window is idle.</summary>
    public DateTimeOffset? ResetsAt { get; }

    /// <summary>Model name of a per-model window ("Sonnet"), null otherwise.</summary>
    public string? Label { get; }
}

/// <summary>Where the displayed percentages come from. The UI always states it.</summary>
public enum QuotaSourceKind
{
    /// <summary><c>GET /api/oauth/usage</c>: the values behind claude.ai ▸ Usage.</summary>
    Api,
    /// <summary>Rate-limit headers relayed by Claude Code's status line.</summary>
    Bridge,
    /// <summary>Last known reading, the account being momentarily unreachable.</summary>
    Stale,
    /// <summary>No measurement: gauges stay empty rather than showing an estimate.</summary>
    Unavailable,
    /// <summary>Claude Code is not installed: the sample set is shown, labelled as such.</summary>
    Demo,
}

public sealed record QuotaSource(QuotaSourceKind Kind, DateTimeOffset? Since = null)
{
    public static QuotaSource Api { get; } = new(QuotaSourceKind.Api);
    public static QuotaSource Bridge { get; } = new(QuotaSourceKind.Bridge);
    public static QuotaSource Unavailable { get; } = new(QuotaSourceKind.Unavailable);
    public static QuotaSource Demo { get; } = new(QuotaSourceKind.Demo);
    public static QuotaSource Stale(DateTimeOffset since) => new(QuotaSourceKind.Stale, since);

    public bool IsMeasured => Kind != QuotaSourceKind.Unavailable;

    /// <summary>Short badge for the header, null for the reference source.</summary>
    public string? Badge => Kind switch
    {
        QuotaSourceKind.Bridge => "relay",
        QuotaSourceKind.Stale => "⟳",
        QuotaSourceKind.Unavailable => "offline",
        QuotaSourceKind.Demo => "demo",
        _ => null,
    };
}

/// <summary>
/// Monthly spend cap of a usage-billed plan (Enterprise). Such a plan has no 5-hour or weekly
/// window: this cap is its only quota.
/// </summary>
public sealed record SpendReading(
    double Used,
    double Limit,
    string Currency,
    bool IsLimitReached,
    DateTimeOffset ResetsAt)
{
    /// <summary>0…1, from the amounts rather than the rounded percentage the API also sends.</summary>
    public double Percent => Limit > 0 ? Math.Clamp(Used / Limit, 0, 1) : 0;

    public DateTimeOffset StartsAt => ResetsAt.AddMonths(-1);

    /// <summary>Caps reset at 00:00 UTC on the first of each month; the API does not send it.</summary>
    public static DateTimeOffset PeriodEnd(DateTimeOffset after)
    {
        var utc = after.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
    }
}

/// <summary>Full reading of the account's quotas at one instant.</summary>
public sealed record QuotaReading
{
    public QuotaWindow? Session { get; init; }
    public QuotaWindow? Weekly { get; init; }
    /// <summary>Weekly window scoped to one model.</summary>
    public QuotaWindow? Scoped { get; init; }
    /// <summary>Monthly spend cap, set only on a plan that reports no window.</summary>
    public SpendReading? Spend { get; init; }
    public required QuotaSource Source { get; init; }

    public bool IsEmpty => Session is null && Weekly is null && Scoped is null && Spend is null;
}
