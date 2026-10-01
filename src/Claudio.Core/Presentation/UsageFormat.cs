using System.Globalization;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Presentation;

/// <summary>A window's distance from its expected pace, ready to display.</summary>
public sealed record Pace(string Text, Rgba Color);

/// <summary>
/// How the card writes figures, dates and the pace: the static helpers of Claudy's
/// <c>UsageViewModel</c>, kept apart from the UI so they can be tested. The text is English, as
/// Claudy's is; numbers and clock times follow the system's culture.
/// </summary>
public static class UsageFormat
{
    /// <summary>The figure shown when there is no measurement: a dash, never an estimate.</summary>
    public const string NoFigure = "—";

    /// <summary>
    /// "3.14 B", "2.34 M", "640 k". The billions step is not decorative: counting cache reads, a
    /// busy week routinely passes a billion tokens.
    /// </summary>
    public static string Tokens(long count, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string Number(double value, int digits) => value.ToString("0." + new string('#', digits), culture);
        return count switch
        {
            >= 1_000_000_000 => $"{Number(count / 1e9, 2)} B",
            >= 1_000_000 => $"{Number(count / 1e6, 2)} M",
            >= 10_000 => $"{Number(count / 1e3, 0)} k",
            >= 1_000 => $"{Number(count / 1e3, 1)} k",
            _ => count.ToString(culture),
        };
    }

    /// <summary>"42" for a measured gauge, a dash otherwise.</summary>
    public static string Percent(UsageWindow window, CultureInfo? culture = null) =>
        window.IsMeasured ? ((int)(window.Percent * 100)).ToString(culture ?? CultureInfo.CurrentCulture) : NoFigure;

    /// <summary>"2 h 14 min", "14 min", "3 d 2 h": time left before a window resets.</summary>
    public static string Countdown(DateTimeOffset reset, DateTimeOffset now)
    {
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "any moment";
        }
        var hours = (int)remaining.TotalHours;
        if (hours >= 24)
        {
            return $"{hours / 24} d {hours % 24} h";
        }
        return hours > 0 ? $"{hours} h {remaining.Minutes} min" : $"{remaining.Minutes} min";
    }

    /// <summary>"14:30", in local time.</summary>
    public static string Clock(DateTimeOffset date, CultureInfo? culture = null) =>
        date.ToLocalTime().ToString("HH:mm", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// When a gauge resets: the time within a day, the date beyond. A monthly cap resetting on the
    /// 1st would otherwise read as a bare "02:00".
    /// </summary>
    public static string ResetTime(DateTimeOffset reset, DateTimeOffset now, CultureInfo? culture = null) =>
        reset - now < TimeSpan.FromDays(1)
            ? Clock(reset, culture)
            : reset.ToLocalTime().ToString("d MMM", culture ?? CultureInfo.CurrentCulture);

    /// <summary>"$46.31 of $500.00", each amount in the currency the account reports.</summary>
    public static string Spent(SpendReading spend, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return $"{Money(spend.Used, spend.Currency, culture)} of {Money(spend.Limit, spend.Currency, culture)}";
    }

    private static string Money(double amount, string currency, CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = currency switch
        {
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            _ => currency + " ",
        };
        return amount.ToString("C2", format);
    }

    /// <summary>
    /// A window's distance from its expected pace, or null when no window is running. Below four
    /// points it is "on pace": the noise of a single request is not an advance. Past twenty ahead,
    /// the label turns red.
    /// </summary>
    public static Pace? PaceOf(UsageWindow window, DateTimeOffset now)
    {
        if (!window.IsActive(now))
        {
            return null;
        }
        var delta = window.PaceDelta(now);
        var points = PacePoints(delta);
        if (points < 4)
        {
            return new Pace("on pace", Theme.Color(Accent.Sage));
        }
        return delta > 0
            ? new Pace($"{points} pts ahead of pace", delta > 0.20 ? Theme.Danger : Theme.Color(Accent.Amber))
            : new Pace($"{points} pts behind pace", Theme.Color(Accent.Sage));
    }

    /// <summary>The pace gap in whole percentage points.</summary>
    public static int PacePoints(double delta) => (int)Math.Round(Math.Abs(delta) * 100, MidpointRounding.AwayFromZero);

    /// <summary>"+15" or "−14": the columns reduce the pace to a sign; nothing below four points.</summary>
    public static string? PaceSign(UsageWindow window, DateTimeOffset now)
    {
        if (!window.IsActive(now) || Math.Abs(window.PaceDelta(now)) < 0.04)
        {
            return null;
        }
        var points = PacePoints(window.PaceDelta(now));
        return window.PaceDelta(now) > 0 ? $"+{points}" : $"−{points}";
    }

    /// <summary>
    /// Day initial for the chart axis: S M T W T F S. An explicit table, so the axis stays in the
    /// app's language whatever the system's.
    /// </summary>
    public static string DayInitial(DateTimeOffset date) =>
        "SMTWTFS"[(int)date.ToLocalTime().DayOfWeek].ToString();

    /// <summary>"Tue 16", or "Today": the hovered day in a chart header.</summary>
    public static string DayName(DateTimeOffset date, DateTimeOffset now)
    {
        var local = date.ToLocalTime();
        if (local.Date == now.ToLocalTime().Date)
        {
            return "Today";
        }
        string[] names = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
        return $"{names[(int)local.DayOfWeek]} {local.Day.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// A reading's age in one short phrase: "4 min", "2 h", "3 d". On stale data the age is what
    /// lets the reader judge: a clock time alone never says it.
    /// </summary>
    public static string Age(DateTimeOffset since, DateTimeOffset now)
    {
        var seconds = Math.Max((now - since).TotalSeconds, 0);
        return seconds switch
        {
            < 60 => "less than a minute",
            < 3600 => $"{(int)(seconds / 60)} min",
            < 86_400 => $"{(int)(seconds / 3600)} h",
            _ => $"{(int)(seconds / 86_400)} d",
        };
    }

    /// <summary>
    /// What each origin badge means, in one sentence: on hover the reader must be able to tell
    /// whether the number comes from their account or from somewhere else.
    /// </summary>
    public static string SourceExplanation(QuotaSource source, DateTimeOffset now) => source.Kind switch
    {
        QuotaSourceKind.Api => "Account quotas, identical to claude.ai ▸ Usage.",
        QuotaSourceKind.Bridge => "Counters relayed by Claude Code's status line: same values, no request.",
        QuotaSourceKind.Stale => $"Account momentarily unreachable. Reading from {Age(source.Since ?? now, now)} ago.",
        QuotaSourceKind.Demo => "Claude Code was not found on this machine: sample data.",
        _ => "Quotas unavailable: no figure is shown rather than an estimate.",
    };

    /// <summary>"1 session", "3 sessions".</summary>
    public static string Sessions(int count) => count > 1 ? $"{count} sessions" : $"{count} session";
}
