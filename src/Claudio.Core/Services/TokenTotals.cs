using System.Globalization;

namespace Claudio.Core.Services;

/// <summary>
/// What this machine consumed, from its transcripts: today and the last seven days. A local count,
/// never mixed with the account's percentage: Anthropic's quota is not a token tally.
/// </summary>
public sealed record TokenTotals(long Today, long Week, IReadOnlyList<long> Days)
{
    public static TokenTotals Empty { get; } = new(0, 0, [0, 0, 0, 0, 0, 0, 0]);

    /// <summary>Days run midnight to midnight in <paramref name="zone"/>; the last one is today.</summary>
    public static TokenTotals From(IEnumerable<TranscriptEntry> entries, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var days = new long[7];
        long week = 0;
        foreach (var entry in entries)
        {
            if (entry.Date < now.AddDays(-7))
            {
                continue;
            }
            week += entry.Tokens;
            var day = (today - TimeZoneInfo.ConvertTime(entry.Date, zone).Date).Days;
            if (day is >= 0 and < 7)
            {
                days[6 - day] += entry.Tokens;
            }
        }
        return new TokenTotals(days[6], week, days);
    }

    /// <summary>"3.14 B", "2.34 M", "640 k": a busy week of cache reads passes a billion.</summary>
    public static string Format(long count, CultureInfo culture)
    {
        static string Number(double value, int digits, CultureInfo culture) =>
            value.ToString("0." + new string('#', digits), culture);
        return count switch
        {
            >= 1_000_000_000 => $"{Number(count / 1e9, 2, culture)} B",
            >= 1_000_000 => $"{Number(count / 1e6, 2, culture)} M",
            >= 10_000 => $"{Number(count / 1e3, 0, culture)} k",
            >= 1_000 => $"{Number(count / 1e3, 1, culture)} k",
            _ => count.ToString(culture),
        };
    }
}
