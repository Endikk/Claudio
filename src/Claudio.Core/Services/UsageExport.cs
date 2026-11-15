using System.Globalization;
using System.Text;

namespace Claudio.Core.Services;

/// <summary>
/// The last seven days of usage as a CSV file: one line per local hour, project and model, for a
/// spreadsheet or a script, as Claudy's <c>UsageExport</c>. Built from the local transcripts alone.
///
/// The file must read the same on every PC: dates are Gregorian whatever the user's calendar,
/// numbers take a point whatever their culture, and a folder name never runs as a formula. Claudy
/// writes the very same columns.
/// </summary>
public static class UsageExport
{
    public const string Header = "hour,project,model,input_tokens,output_tokens,cache_read_tokens,cache_write_tokens,list_price_usd";

    /// <summary>
    /// How far back the export reaches: the week the card shows. The scanner keeps nine days in
    /// memory, so the whole window is there.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The card's name for a response with no folder.</summary>
    private const string NoFolder = "—";

    /// <summary>One line of the file.</summary>
    /// <param name="Hour">Local hour, <c>2026-11-10T09:00</c>.</param>
    /// <param name="Project">The project's display name, as the card shows it.</param>
    /// <param name="Model">The raw model id.</param>
    /// <param name="ListPrice">What the tokens weigh in dollars at list price: the sum of <see cref="TranscriptEntry.Weight"/>.</param>
    public sealed record Row(
        string Hour,
        string Project,
        string Model,
        long InputTokens,
        long OutputTokens,
        long CacheReadTokens,
        long CacheWriteTokens,
        double ListPrice);

    /// <summary>The file for <paramref name="entries"/>; <paramref name="zone"/> is the time zone the hours are read in.</summary>
    public static string Csv(IEnumerable<TranscriptEntry> entries, DateTimeOffset now, TimeZoneInfo zone) =>
        Csv(Rows(entries, now, zone));

    /// <summary>Header first, one line per row, each ending in <c>\n</c>.</summary>
    public static string Csv(IEnumerable<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var file = new StringBuilder(Header).Append('\n');
        foreach (var row in rows)
        {
            string[] fields =
            [
                row.Hour, row.Project, row.Model,
                row.InputTokens.ToString(CultureInfo.InvariantCulture),
                row.OutputTokens.ToString(CultureInfo.InvariantCulture),
                row.CacheReadTokens.ToString(CultureInfo.InvariantCulture),
                row.CacheWriteTokens.ToString(CultureInfo.InvariantCulture),
                // The invariant culture: the separator is a point even in France.
                // printf's rounding, half to even, as Claudy's %.4f.
                Math.Round(row.ListPrice, 4, MidpointRounding.ToEven).ToString("F4", CultureInfo.InvariantCulture),
            ];
            file.AppendJoin(',', fields.Select(Escape)).Append('\n');
        }
        return file.ToString();
    }

    /// <summary>
    /// The responses of the last seven days, summed per local hour, project and model, sorted in
    /// that order. A project's name is settled over every project the export holds, so
    /// <c>work/api</c> and <c>personal/api</c> keep their prefix even in an hour where only one of
    /// them worked.
    /// </summary>
    public static IReadOnlyList<Row> Rows(IEnumerable<TranscriptEntry> entries, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(zone);
        var cutoff = now - Window;
        var recent = entries.Where(entry => entry.Date >= cutoff).ToList();
        var names = UsageAggregator.DisplayNames(recent.Select(entry => entry.Project).Distinct(StringComparer.Ordinal).ToList());

        // Grouped by the hour as written, not by the instant: the hour repeated when clocks go back
        // in autumn stays one line, as a reader of the file would expect.
        return recent
            .GroupBy(entry => (Hour: Hour(entry.Date, zone), Path: entry.Project, entry.Model))
            .Select(group => (group.Key.Path, Row: new Row(
                group.Key.Hour,
                ProjectName(names[group.Key.Path]),
                group.Key.Model,
                group.Sum(entry => entry.InputTokens),
                group.Sum(entry => entry.OutputTokens),
                group.Sum(entry => entry.CacheReadTokens),
                group.Sum(entry => entry.CacheWriteTokens),
                group.Sum(entry => entry.Weight))))
            // The path settles a tie between two projects whose names still match after the parent
            // prefix, so the file does not change from one export to the next.
            .OrderBy(line => line.Row.Hour, StringComparer.Ordinal)
            .ThenBy(line => line.Row.Project, StringComparer.Ordinal)
            .ThenBy(line => line.Row.Model, StringComparer.Ordinal)
            .ThenBy(line => line.Path, StringComparer.Ordinal)
            .Select(line => line.Row)
            .ToList();
    }

    /// <summary><c>claudio-usage-2026-11-10.csv</c>, dated in <paramref name="zone"/>.</summary>
    public static string FileName(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return $"claudio-usage-{TimeZoneInfo.ConvertTime(now, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv";
    }

    /// <summary>
    /// A value as a CSV field. One holding a comma, a double quote or a line break is quoted, its
    /// quotes doubled. One starting with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a
    /// carriage return takes a leading <c>'</c>, so a spreadsheet shows it as text instead of
    /// running it as a formula: folder names are the user's, and a cloned repository chooses its
    /// own.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var guarded = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return guarded.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? guarded
            : "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>The Gregorian hour whatever the user's calendar: the invariant culture's.</summary>
    private static string Hour(DateTimeOffset date, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(date, zone).ToString("yyyy-MM-dd'T'HH':00'", CultureInfo.InvariantCulture);

    /// <summary>The card names a response with no folder with a dash; a file reads better with the cell left empty.</summary>
    private static string ProjectName(string name) => name == NoFolder ? string.Empty : name;
}
