using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Presentation;

/// <summary>What the card shows for one reading, worked out away from the UI so it can be tested.</summary>
public sealed record CardSummary(
    string Title,
    string Window,
    string Percent,
    double Fraction,
    bool IsMeasured,
    string Detail,
    string? Badge,
    Accent Accent,
    bool IsDanger,
    bool IsRunning)
{
    /// <summary>The figure shown when there is no measurement: a dash, never an estimate.</summary>
    public const string NoFigure = "—";

    public static CardSummary From(AccountPayload? payload, bool isClaudeInstalled, DateTimeOffset now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!isClaudeInstalled)
        {
            return Empty("Claude Code was not found on this PC.");
        }
        if (payload is null || !payload.IsSignedIn)
        {
            return Empty("Sign in to Claude Code, and Claudio follows.");
        }

        var reading = payload.Reading;
        var badge = reading is null ? QuotaSource.Unavailable.Badge : reading.Source.Badge;
        if (reading?.Spend is { } spend)
        {
            var spent = string.Format(culture, "{0} of {1}", Money(spend.Used, spend.Currency, culture), Money(spend.Limit, spend.Currency, culture));
            return Measured("Spend", "month", spend.Percent, $"{spent} · reset {ResetTime(spend.ResetsAt, now, culture)}",
                            badge, spend.IsLimitReached, isRunning: true);
        }
        if (reading?.Session is { } session)
        {
            var active = session.ResetsAt is { } reset && reset > now;
            var detail = active ? $"reset {ResetTime(session.ResetsAt!.Value, now, culture)} · in {Countdown(session.ResetsAt.Value, now)}" : "inactive";
            return Measured("Session", "5h", session.Percent, detail, badge, session.Percent >= 1, active);
        }
        return new CardSummary("Session", "5h", NoFigure, 0, false, "Quotas unavailable: no figure rather than an estimate.",
                               badge, Accent.Coral, false, false);

        static CardSummary Empty(string detail) =>
            new("Session", "5h", NoFigure, 0, false, detail, null, Accent.Coral, false, false);

        CardSummary Measured(string title, string window, double fraction, string detail, string? sourceBadge, bool full, bool isRunning) =>
            new(title, window, ((int)(fraction * 100)).ToString(culture), fraction, true, detail, sourceBadge,
                Accent.Coral, full || fraction >= Thresholds.Danger, isRunning);
    }

    /// <summary>The gauge's colour band: its own accent, then amber, then danger.</summary>
    public static class Thresholds
    {
        public const double Amber = 0.75;
        public const double Danger = 0.90;
    }

    /// <summary>"14:30" within a day, the date beyond: a monthly cap would otherwise read as a bare hour.</summary>
    public static string ResetTime(DateTimeOffset reset, DateTimeOffset now, CultureInfo culture) =>
        reset - now < TimeSpan.FromDays(1)
            ? reset.ToLocalTime().ToString("HH:mm", culture)
            : reset.ToLocalTime().ToString("d MMM", culture);

    /// <summary>"2 h 14 min", "14 min", "3 d 2 h".</summary>
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
}
