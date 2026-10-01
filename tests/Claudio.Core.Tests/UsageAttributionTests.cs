using System.Globalization;
using System.Text.Json;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>Which model and which project the week's usage lands on, as Claudy's UsageAttributionTests.</summary>
public sealed class UsageAttributionTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), $"claudio-tests-{Guid.NewGuid():N}");

    public UsageAttributionTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);

    [Theory]
    [InlineData("claude-opus-5", 5)]
    [InlineData("claude-opus-4-1-20250805", 15)]
    [InlineData("claude-sonnet-5", 2)]
    [InlineData("claude-sonnet-4-6", 3)]
    [InlineData("claude-haiku-4-5-20251001", 1)]
    [InlineData("claude-3-5-haiku-20241022", 0.8)]
    [InlineData("claude-fable-5-1", 10)]
    [InlineData("claude-unknown-9", 3)]
    public void InputPriceFollowsFamilyAndGeneration(string model, double price) =>
        Assert.Equal(price, ModelName.InputPrice(model));

    [Fact]
    public void SubdirectoryResolvesToRepositoryRoot()
    {
        var repo = MakeRepository("Jarvis");
        var nested = MakeDirectory(Path.Combine("Jarvis", "App", "Sources"));
        var resolver = new ProjectResolver(_sandbox);

        Assert.Equal(repo, resolver.RepositoryRoot(nested));
        Assert.Equal(repo, resolver.RepositoryRoot(repo));
    }

    [Fact]
    public void WorktreeResolvesToMainCheckout()
    {
        var repo = MakeRepository("Surikat");
        var worktree = MakeDirectory(Path.Combine("Surikat", ".claude", "worktrees", "feature"));
        File.WriteAllText(Path.Combine(worktree, ".git"), $"gitdir: {repo.Replace('\\', '/')}/.git/worktrees/feature\n");

        Assert.Equal(repo, new ProjectResolver(_sandbox).RepositoryRoot(worktree));
    }

    [Fact]
    public void SubmoduleStaysItsOwnProject()
    {
        MakeRepository("Parent");
        var module = MakeDirectory(Path.Combine("Parent", "vendor", "lib"));
        File.WriteAllText(Path.Combine(module, ".git"), "gitdir: ../../.git/modules/lib\n");

        Assert.Equal(module, new ProjectResolver(_sandbox).RepositoryRoot(module));
    }

    [Fact]
    public void FolderOutsideRepositoryHasNoRoot()
    {
        var plain = MakeDirectory("scratchpad");
        var resolver = new ProjectResolver(_sandbox);

        Assert.Null(resolver.RepositoryRoot(plain));
        Assert.Null(resolver.RepositoryRoot(Path.Combine(_sandbox, "deleted", "tmp")));
        Assert.Null(resolver.RepositoryRoot(string.Empty));
        Assert.Null(resolver.RepositoryRoot("relative/path"));
    }

    [Fact]
    public void ADriveLetterInEitherCaseIsOneProject()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drive letters exist on Windows only.");
        var repo = MakeRepository("Claudio");
        var projects = MakeDirectory("projects");
        var session = MakeDirectory(Path.Combine("projects", "-p"));
        var lower = char.ToLowerInvariant(repo[0]) + repo[1..];
        var plain = MakeDirectory("plain");
        WriteLines(Path.Combine(session, "s.jsonl"),
                   Line("a", repo, "s1", "claude-opus-5", 10),
                   Line("b", lower, "s1", "claude-opus-5", 10),
                   Line("c", plain, "s2", "claude-opus-5", 10),
                   Line("d", char.ToLowerInvariant(plain[0]) + plain[1..], "s2", "claude-opus-5", 10));

        var entries = new TranscriptScanner(() => [projects], resolver: new ProjectResolver(_sandbox)).Scan();

        Assert.Equal(2, entries.Select(entry => entry.Project).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ScannerReadsSubagentTranscriptsAndGroupsByRepository()
    {
        var repo = MakeRepository("Jarvis");
        var app = MakeDirectory(Path.Combine("Jarvis", "App"));
        var scratch = MakeDirectory("scratchpad");
        var projects = MakeDirectory("projects");
        var session = MakeDirectory(Path.Combine("projects", "-Jarvis"));

        WriteLines(Path.Combine(session, "s1.jsonl"),
                   Line("m1", repo, "s1", "claude-opus-5", 100),
                   Line("m1", repo, "s1", "claude-opus-5", 100),
                   Line("m2", scratch, "s1", "claude-opus-5", 10));
        var subagents = MakeDirectory(Path.Combine("projects", "-Jarvis", "s1", "subagents", "workflows", "wf_1"));
        WriteLines(Path.Combine(subagents, "agent-a.jsonl"),
                   Line("m3", app, "s1", "claude-haiku-4-5-20251001", 50, sidechain: true));

        var entries = new TranscriptScanner(() => [projects], resolver: new ProjectResolver(_sandbox)).Scan();

        Assert.Equal(3, entries.Count);
        Assert.Equal([repo], entries.Select(entry => entry.Project).Distinct());
        Assert.Contains(entries, entry => entry.IsSidechain && entry.Model.Contains("haiku", StringComparison.Ordinal));
    }

    [Fact]
    public void WeightPricesCacheReadsAtATenth()
    {
        var projects = MakeDirectory("projects");
        var session = MakeDirectory(Path.Combine("projects", "-p"));
        WriteLines(Path.Combine(session, "s.jsonl"),
                   Line("a", _sandbox, "s", "claude-opus-5", 0, cacheRead: 1_000_000),
                   Line("b", _sandbox, "s", "claude-opus-5", 1_000_000));

        var entries = new TranscriptScanner(() => [projects], resolver: new ProjectResolver(_sandbox)).Scan();

        Assert.Equal(0.5, entries.Single(entry => entry.DedupKey!.StartsWith('a')).Weight, 9);
        Assert.Equal(25, entries.Single(entry => entry.DedupKey!.StartsWith('b')).Weight, 9);
    }

    [Fact]
    public void SplitsRankByWeightAndMergeSnapshots()
    {
        var now = DateTimeOffset.UtcNow;
        TranscriptEntry[] entries =
        [
            Entry("claude-haiku-4-5-20251001", 900, 1, "/w/observer", now),
            Entry("claude-haiku-4-5", 100, 1, "/w/observer", now),
            Entry("claude-opus-5", 100, 8, "/w/Surikat", now),
        ];

        var snapshot = UsageAggregator.Snapshot(entries, Account.Empty, null, now, TimeZoneInfo.Utc);

        Assert.Equal(["Opus 5", "Haiku 4.5"], snapshot.Models.Select(model => model.Name));
        Assert.Equal(0.8, snapshot.Models[0].Share, 9);
        Assert.Equal(1000, snapshot.Models[1].Tokens);
        Assert.Equal(["Surikat", "observer"], snapshot.Projects.Select(project => project.Name));
        Assert.Equal("Opus 5", snapshot.ActiveModel);
    }

    [Fact]
    public void LocalActivityKeepsTheSessionRunningWithoutAQuota()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = UsageAggregator.Snapshot([Entry("claude-opus-5", 100, 1, "/w/Claudio", now)], Account.Empty, null, now, TimeZoneInfo.Utc);

        Assert.False(snapshot.Session.IsMeasured);
        Assert.False(snapshot.Session.IsActive(now));
        Assert.True(snapshot.Session.IsRunning(now));
    }

    [Fact]
    public void LocalActivityKeepsTheSessionRunningWhenTheAccountHasNoWindow()
    {
        var now = DateTimeOffset.UtcNow;
        // Claude Code bills another account (an API key), so the one Claudio reads has no window.
        var reading = new QuotaReading { Session = new QuotaWindow(0, null), Source = QuotaSource.Api };

        var snapshot = UsageAggregator.Snapshot([Entry("claude-opus-5", 100, 1, "/w/Claudio", now)], Account.Empty, reading, now, TimeZoneInfo.Utc);

        Assert.True(snapshot.Session.IsMeasured);
        Assert.False(snapshot.Session.IsActive(now));
        Assert.True(snapshot.Session.IsRunning(now));
    }

    [Fact]
    public void AccountWindowKeepsTheSessionRunningWithoutLocalActivity()
    {
        var now = DateTimeOffset.UtcNow;
        var reading = new QuotaReading { Session = new QuotaWindow(0.4, now.AddHours(1)), Source = QuotaSource.Api };

        var snapshot = UsageAggregator.Snapshot([], Account.Empty, reading, now, TimeZoneInfo.Utc);

        Assert.True(snapshot.Session.IsActive(now));
        Assert.True(snapshot.Session.IsRunning(now));
        Assert.Equal(0.8, snapshot.Session.Elapsed(now), 6);
    }

    [Fact]
    public void NoActivityAndNoQuotaLeavesTheSessionIdle()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(UsageAggregator.Snapshot([], Account.Empty, null, now, TimeZoneInfo.Utc).Session.IsRunning(now));
    }

    [Fact]
    public void HomonymProjectsShowTheirParent()
    {
        var names = UsageAggregator.DisplayNames(["/work/api", "/personal/api", "/work/site", @"G:\code\tool"]);

        Assert.Equal("work/api", names["/work/api"]);
        Assert.Equal("personal/api", names["/personal/api"]);
        Assert.Equal("site", names["/work/site"]);
        Assert.Equal("tool", names[@"G:\code\tool"]);
    }

    [Fact]
    public void HistoryHasSevenDaysTodayLast()
    {
        var now = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        TranscriptEntry[] entries =
        [
            Entry("claude-opus-5", 3, 1, "/p", now.AddDays(-6)),
            Entry("claude-opus-5", 20, 1, "/p", now.AddDays(-1)),
            Entry("claude-opus-5", 100, 1, "/p", now),
            Entry("claude-opus-5", 999, 1, "/p", now.AddDays(-8)),
        ];

        var snapshot = UsageAggregator.Snapshot(entries, Account.Empty, null, now, TimeZoneInfo.Utc);

        Assert.Equal([3L, 0, 0, 0, 0, 20, 100], snapshot.History.Select(sample => sample.Tokens));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), snapshot.History[^1].Date);
        Assert.Equal(100, snapshot.TodayTokens);
        Assert.Equal(123, snapshot.WeekTokens);
    }

    [Fact]
    public void SessionsCountTodaysUserSessionsOnly()
    {
        var now = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        TranscriptEntry[] entries =
        [
            Entry("claude-opus-5", 1, 1, "/p", now) with { SessionId = "a" },
            Entry("claude-opus-5", 1, 1, "/p", now) with { SessionId = "b" },
            Entry("claude-opus-5", 1, 1, "/p", now) with { SessionId = "c", IsSidechain = true },
            Entry("claude-opus-5", 1, 1, "/p", now.AddDays(-2)) with { SessionId = "d" },
        ];

        Assert.Equal(2, UsageAggregator.Snapshot(entries, Account.Empty, null, now, TimeZoneInfo.Utc).SessionCount);
    }

    [Fact]
    public void PastNinetyFivePercentTheCardStrains()
    {
        var now = DateTimeOffset.UtcNow;
        UsageSnapshot At(double percent) => UsageAggregator.Snapshot([], Account.Empty,
            new QuotaReading { Session = new QuotaWindow(percent, now.AddHours(1)), Source = QuotaSource.Api }, now, TimeZoneInfo.Utc);

        Assert.Equal(0, At(0.94).Strain());
        Assert.Equal(0.6, At(0.98).Strain(), 6);
        Assert.False(At(0.98).IsOverloaded);
        Assert.True(At(1).IsOverloaded);
    }

    [Fact]
    public void AMonthlyCapLeadsAPlanBilledOnUsage()
    {
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var spend = new SpendReading(46.31, 500, "USD", false, SpendReading.PeriodEnd(now));
        var snapshot = UsageAggregator.Snapshot([], Account.Empty, new QuotaReading { Spend = spend, Source = QuotaSource.Api }, now, TimeZoneInfo.Utc);

        Assert.Same(snapshot.Spend, snapshot.Primary);
        Assert.Equal("Spend", snapshot.Primary.Title);
        Assert.Equal(0.0926, snapshot.Primary.Percent, 4);
        Assert.True(snapshot.Primary.IsActive(now));
        Assert.Equal(0.5, snapshot.Primary.Elapsed(now), 1);
    }

    [Fact]
    public void ACapReachedIsAnOverload()
    {
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var spend = new SpendReading(500, 500, "USD", true, SpendReading.PeriodEnd(now));
        Assert.True(UsageAggregator.Snapshot([], Account.Empty, new QuotaReading { Spend = spend, Source = QuotaSource.Api }, now, TimeZoneInfo.Utc).IsOverloaded);
    }

    [Fact]
    public void WithoutAMeasurementNoGaugeIsMeasured()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = UsageAggregator.Snapshot([], Account.Empty, null, now, TimeZoneInfo.Utc);

        Assert.False(snapshot.Session.IsMeasured);
        Assert.False(snapshot.Weekly.IsMeasured);
        Assert.False(snapshot.Scoped.IsMeasured);
        Assert.Null(snapshot.Spend);
        Assert.Equal(0, snapshot.Strain());
        Assert.False(snapshot.IsOverloaded);
    }

    private static TranscriptEntry Entry(string model, long tokens, double weight, string project, DateTimeOffset now) =>
        new(now.AddMinutes(-60), model, tokens, weight, project, "s", false, Guid.NewGuid().ToString()) { Project = project };

    private string MakeDirectory(string relative)
    {
        var path = Path.Combine(_sandbox, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private string MakeRepository(string relative)
    {
        var path = MakeDirectory(relative);
        Directory.CreateDirectory(Path.Combine(path, ".git"));
        return path;
    }

    private static void WriteLines(string file, params string[] lines) => File.WriteAllText(file, string.Join('\n', lines) + "\n");

    private static string Line(string id, string cwd, string session, string model, long output, long cacheRead = 0, bool sidechain = false)
    {
        var stamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "assistant",
            ["timestamp"] = stamp,
            ["cwd"] = cwd,
            ["sessionId"] = session,
            ["isSidechain"] = sidechain,
            ["requestId"] = $"req-{id}",
            ["message"] = new Dictionary<string, object>
            {
                ["id"] = id,
                ["model"] = model,
                ["usage"] = new Dictionary<string, long>
                {
                    ["input_tokens"] = 0,
                    ["output_tokens"] = output,
                    ["cache_creation_input_tokens"] = 0,
                    ["cache_read_input_tokens"] = cacheRead,
                },
            },
        });
    }
}
