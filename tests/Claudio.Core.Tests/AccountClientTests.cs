using System.Net;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>The account client against a stubbed Anthropic: no test ever reaches the network.</summary>
public sealed class AccountClientTests
{
    private const string Usage = """
        {"five_hour": {"utilization": 42.0, "resets_at": "2026-10-01T14:00:00.000000+00:00"},
         "seven_day": {"utilization": 17.0, "resets_at": "2026-10-05T00:00:00.000000+00:00"}}
        """;

    private const string Profile = """
        {"account": {"full_name": "Ada Lovelace", "email": "ada@example.com"},
         "organization": {"name": "Analytical", "rate_limit_tier": "default_claude_max_5x"}}
        """;

    private DateTimeOffset _now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadsTheQuotasAndTheProfile()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.OK, Usage), Profile = (HttpStatusCode.OK, Profile) };
        using var client = Client(server, Token("a"));

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.True(payload.IsSignedIn);
        Assert.Equal(QuotaSourceKind.Api, payload.Reading!.Source.Kind);
        Assert.Equal(0.42, payload.Reading.Session!.Percent, 4);
        Assert.Equal(new OAuthProfile("Ada Lovelace", "ada@example.com", "Max 5×", "Analytical"), payload.Profile);
        Assert.Equal("Bearer a", server.LastAuthorization);
        Assert.Equal("oauth-2025-04-20", server.LastBeta);
    }

    [Fact]
    public async Task ServesTheCacheForAMinute()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.OK, Usage) };
        using var client = Client(server, Token("a"));

        await client.FetchAsync(TestContext.Current.CancellationToken);
        _now = _now.AddSeconds(30);
        await client.FetchAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, server.UsageRequests);

        _now = _now.AddSeconds(31);
        await client.FetchAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, server.UsageRequests);
    }

    [Fact]
    public async Task AFailureKeepsTheLastReadingMarkedStaleAndBacksOff()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.OK, Usage) };
        using var client = Client(server, Token("a"));
        await client.FetchAsync(TestContext.Current.CancellationToken);

        server.Usage = (HttpStatusCode.TooManyRequests, "{}");
        _now = _now.AddMinutes(2);
        var stale = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(QuotaSourceKind.Stale, stale.Reading!.Source.Kind);
        Assert.Equal(0.42, stale.Reading.Session!.Percent, 4);

        // A rate limit waits five minutes: one minute later, no request is sent.
        _now = _now.AddMinutes(1);
        await client.FetchAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, server.UsageRequests);

        Assert.True(client.ResetBackoff());
        await client.FetchAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, server.UsageRequests);
    }

    [Fact]
    public async Task AWindowPastItsResetIsDroppedFromAStaleReading()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.OK, Usage) };
        using var client = Client(server, Token("a"));
        await client.FetchAsync(TestContext.Current.CancellationToken);

        server.Usage = (HttpStatusCode.InternalServerError, "");
        _now = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);
        var stale = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Null(stale.Reading!.Session);
        Assert.Equal(0.17, stale.Reading.Weekly!.Percent, 4);
    }

    [Fact]
    public async Task On401ItRereadsClaudeCodesToken()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.Unauthorized, "{}") };
        var tokens = new Queue<OAuthCredentials?>([Token("old"), Token("new")]);
        using var client = new ClaudeAccountClient(new HttpClient(server), () => tokens.Count > 0 ? tokens.Dequeue() : Token("new"),
                                                   OwnTokenStore.None, SignOutFlag.InMemory(), () => _now);
        server.OnUsage = authorization => server.Usage = authorization == "Bearer new" ? (HttpStatusCode.OK, Usage) : server.Usage;

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0.42, payload.Reading!.Session!.Percent, 4);
        Assert.Equal("Bearer new", server.LastAuthorization);
    }

    [Fact]
    public async Task WithoutATokenNothingIsSentAndNobodyIsSignedIn()
    {
        var server = new StubbedAnthropic();
        using var client = Client(server, null);

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(payload.IsSignedIn);
        Assert.Null(payload.Reading);
        Assert.Equal(0, server.UsageRequests);
    }

    /// <summary>Holding the refresh key repeats it: each repeat must not lift a backoff the server asked for.</summary>
    [Fact]
    public void ARefreshLiftsTheBackoffOncePerMinute()
    {
        using var client = Client(new StubbedAnthropic(), null);

        var first = client.ResetBackoff();
        _now += ClaudeAccountClient.ManualRetrySpacing - TimeSpan.FromSeconds(1);
        var repeated = client.ResetBackoff();
        _now += TimeSpan.FromSeconds(1);
        var aMinuteLater = client.ResetBackoff();

        Assert.True(first);
        Assert.False(repeated);
        Assert.True(aMinuteLater);
    }

    [Fact]
    public async Task On401ABorrowedTokenIsNeverRefreshed()
    {
        var server = new StubbedAnthropic { Usage = (HttpStatusCode.Unauthorized, "{}") };
        using var client = Client(server, Token("a"));

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Null(payload.Reading);
        Assert.True(payload.IsSignedIn);
        Assert.Empty(server.TokenRequests);
        Assert.Equal(1, server.UsageRequests);
    }

    private ClaudeAccountClient Client(StubbedAnthropic server, OAuthCredentials? token) =>
        new(new HttpClient(server), () => token, OwnTokenStore.None, SignOutFlag.InMemory(), () => _now);

    private static OAuthCredentials Token(string value) => Tokens.Borrowed(value);
}
