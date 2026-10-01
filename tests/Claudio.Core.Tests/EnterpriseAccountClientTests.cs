using System.Net;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The account client end to end on an Enterprise account: requests go through a stub, the token
/// is injected, and no store or network is ever touched.
/// </summary>
public sealed class EnterpriseAccountClientTests : IDisposable
{
    private const string EnterpriseUsage = """
        {
          "five_hour": null, "seven_day": null,
          "extra_usage": {"is_enabled": true, "monthly_limit": 50000, "used_credits": 4631,
                          "utilization": 9.262, "currency": "USD", "decimal_places": 2,
                          "spend_limit_reached": false},
          "spend": {"used": {"amount_minor": 4631, "currency": "USD", "exponent": 2},
                    "limit": {"amount_minor": 50000, "currency": "USD", "exponent": 2},
                    "percent": 9, "severity": "normal", "enabled": true}
        }
        """;

    private const string EnterpriseProfile = """
        {"account": {"full_name": "Ada Lovelace", "email": "ada@example.com"},
         "organization": {"name": "Acme", "rate_limit_tier": "default_claude_zero"}}
        """;

    private readonly DateTimeOffset _now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly StubbedAnthropic _server = new() { Usage = (HttpStatusCode.OK, EnterpriseUsage), Profile = (HttpStatusCode.OK, EnterpriseProfile) };

    public void Dispose() => _server.Dispose();

    private ClaudeAccountClient Client()
    {
        var token = Tokens.Borrowed("enterprise", _now.AddHours(1), subscription: "enterprise");
        return new(new HttpClient(_server), () => token, OwnTokenStore.None, SignOutFlag.InMemory(), () => _now);
    }

    [Fact]
    public async Task AnEnterpriseAnswerIsAReadingNotAFailure()
    {
        using var client = Client();

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        var spend = payload.Reading?.Spend;
        Assert.NotNull(spend);
        Assert.Equal(QuotaSourceKind.Api, payload.Reading!.Source.Kind);
        Assert.Equal(46.31, spend.Used, 4);
        Assert.Equal(500, spend.Limit, 4);
        Assert.True(payload.IsSignedIn);
    }

    /// <summary>The profile only says "default_claude_zero"; the token's <c>subscriptionType</c> names the plan.</summary>
    [Fact]
    public async Task ThePlanReadsEnterpriseFromTheTokenWhenTheTierHidesIt()
    {
        using var client = Client();

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Enterprise", payload.Profile?.Plan);
    }

    [Fact]
    public async Task SpendSurvivesAFailedRefreshAsAStaleReading()
    {
        using var client = Client();
        await client.FetchAsync(TestContext.Current.CancellationToken);

        _server.Usage = (HttpStatusCode.ServiceUnavailable, "");
        client.ResetBackoff();
        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(QuotaSourceKind.Stale, payload.Reading?.Source.Kind);
        Assert.Equal(46.31, payload.Reading!.Spend!.Used, 4);
    }

    /// <summary>Same path on a Max account: a failing manual refresh must not wipe the gauges either.</summary>
    [Fact]
    public async Task WindowsSurviveAFailedManualRefresh()
    {
        _server.Usage = (HttpStatusCode.OK, """
            {"five_hour": {"utilization": 40.0, "resets_at": "2099-01-01T00:00:00.000000+00:00"},
             "seven_day": {"utilization": 10.0, "resets_at": "2099-01-05T00:00:00.000000+00:00"}}
            """);
        using var client = Client();
        await client.FetchAsync(TestContext.Current.CancellationToken);

        _server.Usage = (HttpStatusCode.ServiceUnavailable, "");
        client.ResetBackoff();
        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0.40, payload.Reading!.Session!.Percent, 4);
    }

    /// <summary>
    /// A 200 carrying no quota at all (no window, no cap) is still no measurement: it backs off
    /// like any failure instead of hammering the endpoint every refresh.
    /// </summary>
    [Fact]
    public async Task AnAnswerWithoutAnyQuotaBacksOff()
    {
        _server.Usage = (HttpStatusCode.OK, """{"five_hour": null, "seven_day": null}""");
        using var client = Client();

        var first = await client.FetchAsync(TestContext.Current.CancellationToken);
        var second = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Null(first.Reading);
        Assert.Null(second.Reading);
        Assert.Equal(1, _server.UsageRequests);
    }
}
