using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The CSV export: one line per local hour, project and model over the last seven days, in a form
/// a spreadsheet opens without surprises whatever the folder names hold.
/// </summary>
public sealed class UsageExportTests
{
    private const string Header = "hour,project,model,input_tokens,output_tokens,cache_read_tokens,cache_write_tokens,list_price_usd";

    /// <summary>11:30 in Paris, which is UTC+1 in November.</summary>
    private static readonly DateTimeOffset Now = Date("2026-11-10T10:30:00Z");

    private static readonly TimeZoneInfo Paris = Zone("Europe/Paris");

    // Lines

    [Fact]
    public void ResponsesOfOneHourProjectAndModelShareALine()
    {
        TranscriptEntry[] entries =
        [
            Entry("2026-11-10T08:05:00Z", input: 100, output: 10, cacheRead: 1_000, cacheWrite: 50, weight: 1.25),
            Entry("2026-11-10T08:55:00Z", input: 20, output: 5, cacheRead: 300, cacheWrite: 7, weight: 0.5),
            Entry("2026-11-10T08:30:00Z", model: "claude-sonnet-5-5", input: 1, output: 2, cacheRead: 3, cacheWrite: 4, weight: 0.01),
            Entry("2026-11-10T09:10:00Z", input: 9, output: 8, cacheRead: 7, cacheWrite: 6, weight: 0.2),
        ];

        Assert.Equal(
            Header + "\n"
            + "2026-11-10T09:00,Claudy,claude-opus-5-5,120,15,1300,57,1.7500\n"
            + "2026-11-10T09:00,Claudy,claude-sonnet-5-5,1,2,3,4,0.0100\n"
            + "2026-11-10T10:00,Claudy,claude-opus-5-5,9,8,7,6,0.2000\n",
            UsageExport.Csv(entries, Now, Paris));
    }

    [Fact]
    public void HourIsReadInTheGivenTimeZone()
    {
        TranscriptEntry[] entries = [Entry("2026-11-10T08:05:00Z", output: 1)];

        Assert.Equal(["2026-11-10T09:00"], UsageExport.Rows(entries, Now, Paris).Select(row => row.Hour));
        Assert.Equal(["2026-11-10T17:00"], UsageExport.Rows(entries, Now, Zone("Asia/Tokyo")).Select(row => row.Hour));
        // 08:05 UTC is 13:50 at UTC+5:45.
        Assert.Equal(["2026-11-10T13:00"], UsageExport.Rows(entries, Now, Zone("Asia/Kathmandu")).Select(row => row.Hour));
    }

    /// <summary>
    /// A user whose culture counts years in the Buddhist era still gets 2026: a script reading the
    /// file must not depend on the settings of the PC it came from. Numbers keep their point.
    /// </summary>
    [Fact]
    public void TheFileIgnoresTheUsersCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var rows = UsageExport.Rows([Entry("2026-11-10T08:05:00Z", output: 1)], Now, Paris);
            Assert.Equal(["2026-11-10T09:00"], rows.Select(row => row.Hour));
            Assert.Equal("claudio-usage-2026-11-10.csv", UsageExport.FileName(Now, Paris));

            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Assert.EndsWith(",1.5000\n", UsageExport.Csv([Entry("2026-11-10T08:05:00Z", output: 1, weight: 1.5)], Now, Paris), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void OnlyTheLastSevenDaysAreExported()
    {
        var weekAgo = Now.AddDays(-7);
        TranscriptEntry[] entries = [Entry(weekAgo.AddSeconds(-1), output: 1), Entry(weekAgo, output: 2), Entry(Now.AddMinutes(-1), output: 3)];

        // A response seven days old to the second is kept.
        Assert.Equal([2L, 3L], UsageExport.Rows(entries, Now, Paris).Select(row => row.OutputTokens));
    }

    [Fact]
    public void LinesAreSortedByHourThenProjectThenModel()
    {
        TranscriptEntry[] entries =
        [
            Entry("2026-11-10T09:10:00Z", project: @"C:\w\EVAN", model: "claude-opus-5-5", output: 1),
            Entry("2026-11-10T08:10:00Z", project: @"C:\w\EVAN", model: "claude-sonnet-5-5", output: 1),
            Entry("2026-11-10T08:20:00Z", project: @"C:\w\Claudy", model: "claude-sonnet-5-5", output: 1),
            Entry("2026-11-10T08:30:00Z", project: @"C:\w\EVAN", model: "claude-haiku-5", output: 1),
            Entry("2026-11-10T08:40:00Z", project: @"C:\w\Claudy", model: "claude-opus-5-5", output: 1),
            Entry("2026-11-09T23:40:00Z", project: @"C:\w\Zed", model: "claude-opus-5-5", output: 1),
        ];

        Assert.Equal(
            [
                "2026-11-10T00:00 Zed claude-opus-5-5",
                "2026-11-10T09:00 Claudy claude-opus-5-5",
                "2026-11-10T09:00 Claudy claude-sonnet-5-5",
                "2026-11-10T09:00 EVAN claude-haiku-5",
                "2026-11-10T09:00 EVAN claude-sonnet-5-5",
                "2026-11-10T10:00 EVAN claude-opus-5-5",
            ],
            UsageExport.Rows(entries, Now, Paris).Select(row => $"{row.Hour} {row.Project} {row.Model}"));
    }

    // Project names

    /// <summary>
    /// Two folders named alike are told apart even when they never share an hour: the names hold
    /// across the whole file, so a pivot table does not merge them.
    /// </summary>
    [Fact]
    public void CollidingFolderNamesArePrefixedByTheirParent()
    {
        TranscriptEntry[] entries =
        [
            Entry("2026-11-10T08:10:00Z", project: @"C:\Users\me\work\api", output: 1),
            Entry("2026-11-09T08:10:00Z", project: @"C:\Users\me\personal\api", output: 1),
            Entry("2026-11-10T08:20:00Z", project: @"C:\Users\me\Claudy", output: 1),
        ];

        Assert.Equal(["personal/api", "Claudy", "work/api"], UsageExport.Rows(entries, Now, Paris).Select(row => row.Project));
    }

    /// <summary>A project the export leaves out, older than the week, does not rename the ones it keeps.</summary>
    [Fact]
    public void OnlyExportedProjectsCountAsCollisions()
    {
        TranscriptEntry[] entries =
        [
            Entry("2026-11-10T08:10:00Z", project: @"C:\Users\me\work\api", output: 1),
            Entry("2026-11-01T08:10:00Z", project: @"C:\Users\me\personal\api", output: 1),
        ];

        Assert.Equal(["api"], UsageExport.Rows(entries, Now, Paris).Select(row => row.Project));
    }

    // Escaping

    [Theory]
    [InlineData("Claudy", "Claudy")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("carriage\rreturn", "\"carriage\rreturn\"")]
    [InlineData("windows\r\nline", "\"windows\r\nline\"")]
    public void FieldsThatWouldBreakALineAreQuoted(string value, string field) => Assert.Equal(field, UsageExport.Escape(value));

    /// <summary>Folder names are the user's: one named <c>=HYPERLINK(...)</c> must open as text, not run.</summary>
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+33", "'+33")]
    [InlineData("-rf", "'-rf")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tindent", "'\tindent")]
    [InlineData("\rback", "\"'\rback\"")]
    [InlineData("\r\nback", "\"'\r\nback\"")]
    [InlineData("=1,2", "\"'=1,2\"")]
    [InlineData("a=b", "a=b")]
    public void FieldsThatASpreadsheetWouldRunAsAFormulaAreDefused(string value, string field) =>
        Assert.Equal(field, UsageExport.Escape(value));

    [Fact]
    public void ProjectNamesGoThroughTheEscaping()
    {
        var csv = UsageExport.Csv([Entry("2026-11-10T08:10:00Z", project: "/w/=HYPERLINK(\"x\",\"y\")", output: 1)], Now, Paris);

        Assert.Equal("2026-11-10T09:00,\"'=HYPERLINK(\"\"x\"\",\"\"y\"\")\",claude-opus-5-5,0,1,0,0,0.0000",
                     csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]);
    }

    // Edges

    /// <summary>A response with no folder leaves its project cell empty, not the card's dash.</summary>
    [Fact]
    public void ResponseWithoutAFolderHasAnEmptyProject() =>
        Assert.Contains("\n2026-11-10T09:00,,claude-opus-5-5,1,0,0,0,0.0000\n",
                        UsageExport.Csv([Entry("2026-11-10T08:05:00Z", project: "", input: 1)], Now, Paris), StringComparison.Ordinal);

    [Fact]
    public void EmptyExportIsTheHeaderAlone()
    {
        TranscriptEntry[] stale = [Entry(Now.AddDays(-8), output: 1)];

        Assert.Equal(Header + "\n", UsageExport.Csv([], Now, Paris));
        Assert.Equal(Header + "\n", UsageExport.Csv(stale, Now, Paris));
        Assert.Empty(UsageExport.Rows(stale, Now, Paris));
    }

    [Fact]
    public void PriceKeepsFourDecimalsWithAPoint()
    {
        TranscriptEntry[] entries =
        [
            Entry("2026-11-10T08:10:00Z", output: 1, weight: 0.1),
            Entry("2026-11-10T08:20:00Z", output: 1, weight: 0.2),
            Entry("2026-11-10T08:10:00Z", model: "claude-haiku-5", output: 1, weight: 12.345_67),
        ];

        var prices = UsageExport.Csv(entries, Now, Paris).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                                .Select(line => line.Split(',')[^1]);
        Assert.Equal(["12.3457", "0.3000"], prices);
    }

    [Fact]
    public void FileNameCarriesTheLocalDate()
    {
        var lateEvening = Date("2026-11-10T23:30:00Z");

        Assert.Equal("claudio-usage-2026-11-10.csv", UsageExport.FileName(lateEvening, TimeZoneInfo.Utc));
        Assert.Equal("claudio-usage-2026-11-11.csv", UsageExport.FileName(lateEvening, Paris));
    }

    // Sources

    [Fact]
    public async Task LocalSourceExportsItsHistory()
    {
        var source = Source(() => [Entry(Now, output: 42)], isInstalled: true);

        Assert.Equal([42L], (await source.RecentEntriesAsync()).Select(entry => entry.OutputTokens));
    }

    /// <summary>An export clicked during a refresh joins the pass under way instead of reading the transcripts a second time.</summary>
    [Fact]
    public async Task ExportJoinsAPassUnderWay()
    {
        using var release = new ManualResetEventSlim();
        var passes = 0;
        var source = Source(() =>
        {
            Interlocked.Increment(ref passes);
            release.Wait(TimeSpan.FromSeconds(5));
            return [Entry(Now, output: 1)];
        }, isInstalled: true);

        var first = source.RecentEntriesAsync();
        for (var attempt = 0; attempt < 40 && Volatile.Read(ref passes) == 0; attempt++)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        var second = source.RecentEntriesAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var counted = Volatile.Read(ref passes);
        release.Set();
        await first;
        await second;

        Assert.Equal(1, counted);
    }

    [Fact]
    public async Task LocalSourceWithoutClaudeCodeSaysSo() =>
        // No Claude Code must surface, not read as an idle week.
        await Assert.ThrowsAsync<ClaudeNotInstalledException>(() => Source(() => [Entry(Now, output: 1)], isInstalled: false).RecentEntriesAsync());

    /// <summary>
    /// Without Claude Code the card shows the demo set, but the export must never write it out: a
    /// file of invented figures would pass for real ones.
    /// </summary>
    [Fact]
    public async Task AdaptiveSourceExportsNothingWithoutClaudeCode()
    {
        var adaptive = new AdaptiveUsageDataSource(Source(() => [Entry(Now, output: 1)], isInstalled: false),
                                                   new DemoUsageDataSource(() => Account.Empty, () => Now, Paris, delay: TimeSpan.Zero));

        Assert.Empty(await adaptive.RecentEntriesAsync());
    }

    [Fact]
    public async Task AdaptiveSourceExportsTheLocalHistory()
    {
        var adaptive = new AdaptiveUsageDataSource(Source(() => [Entry(Now, output: 7)], isInstalled: true),
                                                   new DemoUsageDataSource(() => Account.Empty, () => Now, Paris, delay: TimeSpan.Zero));

        Assert.Equal([7L], (await adaptive.RecentEntriesAsync()).Select(entry => entry.OutputTokens));
    }

    // Fixtures

    private static LocalUsageDataSource Source(Func<IReadOnlyList<TranscriptEntry>> scan, bool isInstalled) =>
        new(scan, _ => Task.FromResult(new AccountPayload(null, null, IsSignedIn: true)), () => isInstalled, _ => null,
            () => Account.Empty, () => Now, Paris);

    private static TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);

    private static DateTimeOffset Date(string stamp) => DateTimeOffset.Parse(stamp, CultureInfo.InvariantCulture);

    private static TranscriptEntry Entry(string stamp, string project = @"C:\Users\me\Claudy", string model = "claude-opus-5-5",
                                         long input = 0, long output = 0, long cacheRead = 0, long cacheWrite = 0, double weight = 0) =>
        Entry(Date(stamp), project, model, input, output, cacheRead, cacheWrite, weight);

    private static TranscriptEntry Entry(DateTimeOffset date, string project = @"C:\Users\me\Claudy", string model = "claude-opus-5-5",
                                         long input = 0, long output = 0, long cacheRead = 0, long cacheWrite = 0, double weight = 0) =>
        new(date, model, input + output + cacheRead + cacheWrite, weight, project, "s", false, Guid.NewGuid().ToString())
        {
            Project = project,
            InputTokens = input,
            OutputTokens = output,
            CacheReadTokens = cacheRead,
            CacheWriteTokens = cacheWrite,
        };
}
