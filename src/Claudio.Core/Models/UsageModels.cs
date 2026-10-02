using Claudio.Core.Services;

namespace Claudio.Core.Models;

/// <summary>One quota window as the card renders it: session 5h, weekly, per-model, or the month's spend.</summary>
public sealed record UsageWindow
{
    /// <summary>Displayed title: "Session", "Weekly", or the model name.</summary>
    public required string Title { get; init; }

    /// <summary>Window qualifier: "5h", "7d", "month".</summary>
    public required string Window { get; init; }

    /// <summary>
    /// 0…1 as the account reports it. With no measurement this is 0 and <see cref="IsMeasured"/>
    /// is false, so the UI shows "—" rather than that zero.
    /// </summary>
    public double Percent { get; init; }

    /// <summary>
    /// Tokens recorded on this machine during the window. A local count, unrelated to the
    /// percentage: Anthropic's quota is not a token tally.
    /// </summary>
    public long TokensUsed { get; init; }

    /// <summary>Start of the window, which is what sets the expected pace.</summary>
    public DateTimeOffset WindowStart { get; init; }

    public DateTimeOffset ResetDate { get; init; }

    public Accent Accent { get; init; }

    /// <summary>True when <see cref="Percent"/> comes from a real account quota.</summary>
    public bool IsMeasured { get; init; } = true;

    /// <summary>
    /// Money behind the percentage, on a monthly spend cap only. It replaces the token line: a
    /// month of tokens is not something the local transcripts can count.
    /// </summary>
    public SpendReading? Amount { get; init; }

    /// <summary>
    /// End of this machine's current activity block, when the transcripts show one. Kept apart
    /// from <see cref="ResetDate"/> so it can animate the mascot without ever turning into a countdown.
    /// </summary>
    public DateTimeOffset? LocalActivityEnd { get; init; }

    /// <summary>
    /// False when no window is running: there is nothing to count down, and showing a reset time
    /// already in the past would be a lie.
    /// </summary>
    public bool IsActive(DateTimeOffset now) => IsMeasured && ResetDate > now;

    /// <summary>
    /// True while a window runs on the account or Claude Code works on this machine. This is what
    /// animates the mascot: <see cref="IsActive"/> alone froze it whenever the account had no
    /// window to report, unreadable quota or Claude Code billing another account alike.
    /// </summary>
    public bool IsRunning(DateTimeOffset now) => ResetDate > now || LocalActivityEnd > now;

    /// <summary>
    /// Share of the window already elapsed, 0…1. This is where the pace marker sits: halfway
    /// through a window, steady consumption would read 50 %.
    /// </summary>
    public double Elapsed(DateTimeOffset now)
    {
        var duration = (ResetDate - WindowStart).TotalSeconds;
        return duration <= 0 ? 0 : Math.Clamp((now - WindowStart).TotalSeconds / duration, 0, 1);
    }

    /// <summary>Distance from the expected pace, as a fraction. Positive means ahead of the clock.</summary>
    public double PaceDelta(DateTimeOffset now) => Percent - Elapsed(now);
}

/// <summary>One day of the history feeding the sparkline.</summary>
public sealed record TokenSample(DateTimeOffset Date, long Tokens);

/// <summary>Seven-day split by model.</summary>
public sealed record ModelUsage(string Id, string Name, long Tokens, double Share, Accent Accent);

/// <summary>Seven-day split by project.</summary>
public sealed record ProjectUsage(string Id, string Name, long Tokens, double Share);

/// <summary>The account whose quotas these are.</summary>
public sealed record Account(string Name, string Email, string Plan, string Organization, bool IsAdmin)
{
    public static Account Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false);

    public string Initial
    {
        get
        {
            var name = Name.Trim();
            return name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
        }
    }
}

/// <summary>
/// Everything the views render at one instant. A single structure, so the UI can never show a
/// half-updated state.
/// </summary>
public sealed record UsageSnapshot
{
    public required UsageWindow Session { get; init; }
    public required UsageWindow Weekly { get; init; }

    /// <summary>The weekly window scoped to one model ("Sonnet", "Opus"…), as Claudy's <c>sonnet</c>.</summary>
    public required UsageWindow Scoped { get; init; }

    /// <summary>
    /// Monthly spend cap of a usage-billed plan (Enterprise), null on plans metered in windows.
    /// Such a plan has no session or weekly quota, so this gauge leads the card instead.
    /// </summary>
    public UsageWindow? Spend { get; init; }

    /// <summary>
    /// The gauge the card leads with. <see cref="Session"/> still drives the mascot either way: it
    /// is placed from local activity when the account has no 5-hour window.
    /// </summary>
    public UsageWindow Primary => Spend ?? Session;

    /// <summary>Seven points, oldest first; the last one is today and therefore partial.</summary>
    public IReadOnlyList<TokenSample> History { get; init; } = [];
    public IReadOnlyList<ModelUsage> Models { get; init; } = [];
    public IReadOnlyList<ProjectUsage> Projects { get; init; } = [];

    public Account Account { get; init; } = Account.Empty;
    public string ActiveModel { get; init; } = string.Empty;
    public long TodayTokens { get; init; }
    public long WeekTokens { get; init; }
    public int SessionCount { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Where the displayed percentages come from. The UI announces it: this is what separates an
    /// account reading from a last known state, and both from an absence of measurement.
    /// </summary>
    public QuotaSource QuotaSource { get; init; } = QuotaSource.Unavailable;

    /// <summary>
    /// True when a Claude token is available, so the gauges show real quotas. False sends the card
    /// to onboarding, never to estimated quotas.
    /// </summary>
    public bool IsSignedIn { get; init; }

    /// <summary>Claudy's sample set, shown while Claude Code is absent and nobody is signed in: never a reading.</summary>
    public bool IsDemo => QuotaSource.Kind == QuotaSourceKind.Demo;

    private IEnumerable<UsageWindow> Gauges =>
        Spend is null ? [Session, Weekly, Scoped] : [Session, Weekly, Scoped, Spend];

    /// <summary>
    /// 0 below 95 % on every gauge, 1 at 100 %. Above 0 the card's hairline reddens: a signal you
    /// catch without reading a number. An unmeasured gauge never triggers it.
    /// </summary>
    public double Strain(double threshold = 0.95)
    {
        var peak = Gauges.Where(gauge => gauge.IsMeasured).Select(gauge => gauge.Percent).DefaultIfEmpty(0).Max();
        return Math.Clamp((peak - threshold) / (1 - threshold), 0, 1);
    }

    /// <summary>
    /// A quota that blocks work is full: the 5h session, the weekly limit, or the monthly spend cap.
    /// The per-model window is left out, since another model still answers. Unmeasured windows
    /// never count.
    /// </summary>
    public bool IsOverloaded =>
        Spend?.Amount?.IsLimitReached == true
        || (Spend is null ? [Session, Weekly] : new[] { Session, Weekly, Spend })
            .Any(gauge => gauge.IsMeasured && gauge.Percent >= 1);

    /// <summary>State before the first reading: never on screen for long, but it spares every view a null.</summary>
    public static UsageSnapshot Placeholder(DateTimeOffset now) => new()
    {
        Session = new UsageWindow { Title = "Session", Window = "5h", WindowStart = now, ResetDate = now, Accent = Accent.Coral, IsMeasured = false },
        Weekly = new UsageWindow { Title = "Weekly", Window = "7d", WindowStart = now, ResetDate = now, Accent = Accent.Amber, IsMeasured = false },
        Scoped = new UsageWindow { Title = "Per model", Window = "7d", WindowStart = now, ResetDate = now, Accent = Accent.Violet, IsMeasured = false },
        UpdatedAt = now,
    };
}
