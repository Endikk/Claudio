using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>The bridge, the merge, and when a reading waits for the history.</summary>
public sealed class UsageDataSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheBridgeReadsTheStatusLineShape()
    {
        var reading = UsageBridge.Parse("""
            {"rate_limits":{"five_hour":{"used_percentage":42,"resets_at":1790000000},"seven_day":{"used_percentage":7}}}
            """)!;

        Assert.Equal(0.42, reading.Session!.Percent, 6);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_790_000_000), reading.Session.ResetsAt);
        Assert.Equal(0.07, reading.Weekly!.Percent, 6);
        Assert.Equal(QuotaSourceKind.Bridge, reading.Source.Kind);
    }

    [Fact]
    public void TheBridgeReadsTheSdkShape()
    {
        var reading = UsageBridge.Parse("""
            {"rate_limits":{"five_hour":{"utilization":13,"resets_at":"2026-10-01T12:00:00Z"}}}
            """)!;

        Assert.Equal(0.13, reading.Session!.Percent, 6);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), reading.Session.ResetsAt);
        Assert.Null(reading.Weekly);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"rate_limits":{}}""")]
    [InlineData("not json")]
    public void ABridgeWithoutWindowsIsNoReading(string json) => Assert.Null(UsageBridge.Parse(json));

    [Fact]
    public void AnOldBridgeFileIsIgnored()
    {
        var file = Path.Combine(Path.GetTempPath(), $"claudio-bridge-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """{"rate_limits":{"five_hour":{"used_percentage":42}}}""");
        try
        {
            Assert.NotNull(UsageBridge.Read(file, DateTimeOffset.UtcNow));
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-31));
            Assert.Null(UsageBridge.Read(file, DateTimeOffset.UtcNow));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AFreshAccountReadingWinsOverTheBridge()
    {
        var api = new QuotaReading { Session = new QuotaWindow(0.5, Now.AddHours(1)), Source = QuotaSource.Api };
        var stale = api with { Source = QuotaSource.Stale(Now.AddMinutes(-20)) };
        var bridge = new QuotaReading { Session = new QuotaWindow(0.6, Now.AddHours(1)), Source = QuotaSource.Bridge };
        var spend = new QuotaReading { Spend = new SpendReading(1, 2, "USD", false, Now.AddDays(3)), Source = QuotaSource.Stale(Now) };

        Assert.Same(api, UsageBridge.Merge(api, bridge));
        Assert.Same(bridge, UsageBridge.Merge(stale, bridge));
        Assert.Same(bridge, UsageBridge.Merge(null, bridge));
        Assert.Same(spend, UsageBridge.Merge(spend, bridge));
        Assert.Same(stale, UsageBridge.Merge(stale, null));
    }

    [Fact]
    public async Task TheSignInCardDoesNotWaitForTheHistory()
    {
        using var release = new ManualResetEventSlim();
        var source = Source(() =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return [];
        }, new AccountPayload(null, null, IsSignedIn: false));

        var snapshot = await source.FetchAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        release.Set();

        Assert.False(snapshot.IsSignedIn);
    }

    [Fact]
    public async Task ASignedInCardWaitsForItsTokenCounts()
    {
        var source = Source(() => [Entry(1_234)], new AccountPayload(null, null, IsSignedIn: true));

        var snapshot = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.IsSignedIn);
        Assert.Equal(1_234, snapshot.TodayTokens);
    }

    [Fact]
    public async Task ASignedOutCardPicksTheHistoryUpOnceItIsRead()
    {
        var source = Source(() => [Entry(50)], new AccountPayload(null, null, IsSignedIn: false));

        await source.FetchAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        var next = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(50, next.TodayTokens);
    }

    [Fact]
    public async Task SigningOutSilencesTheBridge()
    {
        var bridge = new QuotaReading { Session = new QuotaWindow(0.6, Now.AddHours(1)), Source = QuotaSource.Bridge };
        var source = new LocalUsageDataSource(() => [], _ => Task.FromResult(AccountPayload.SignedOut), () => true,
                                              _ => bridge, () => Account.Empty, () => Now, TimeZoneInfo.Utc);

        var snapshot = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.Session.IsMeasured);
    }

    [Fact]
    public async Task TheProfileNamesTheAccount()
    {
        var payload = new AccountPayload(null, new OAuthProfile("Ada", "ada@example.com", "Max 5×", "Lovelace Ltd"), IsSignedIn: true);
        var source = new LocalUsageDataSource(() => [], _ => Task.FromResult(payload), () => true, _ => null,
                                              () => Account.Empty with { IsAdmin = true }, () => Now, TimeZoneInfo.Utc);

        var account = (await source.FetchAsync(TestContext.Current.CancellationToken)).Account;

        Assert.Equal("Ada", account.Name);
        Assert.Equal("Max 5×", account.Plan);
        Assert.True(account.IsAdmin);
        Assert.Equal("A", account.Initial);
    }

    [Fact]
    public void TheAccountComesFromClaudeJson()
    {
        var account = AccountLoader.Parse("""
            {"oauthAccount":{"displayName":"Ada","emailAddress":"ada@example.com","organizationRateLimitTier":"default_claude_max_5x","organizationName":"Lovelace","organizationRole":"admin"}}
            """, "fallback")!;

        Assert.Equal(new Account("Ada", "ada@example.com", "Max 5×", "Lovelace", true), account);
        Assert.Equal("fallback", AccountLoader.Parse("""{"oauthAccount":{}}""", "fallback")!.Name);
        Assert.Null(AccountLoader.Parse("{}", "fallback"));
    }

    private static LocalUsageDataSource Source(Func<IReadOnlyList<TranscriptEntry>> scan, AccountPayload payload) =>
        new(scan, _ => Task.FromResult(payload), () => true, _ => null, () => Account.Empty, () => Now, TimeZoneInfo.Utc);

    private static TranscriptEntry Entry(long tokens) =>
        new(Now.AddMinutes(-5), "claude-opus-5", tokens, 1, "/p", "s", false, Guid.NewGuid().ToString());
}
