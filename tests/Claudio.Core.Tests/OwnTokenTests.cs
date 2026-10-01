using System.Net;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// Claudio's own token, the one its sign-in obtains: used when Claude Code's cannot be read,
/// refreshed here, and declared dead on <c>invalid_grant</c>. Claude Code's is never refreshed.
/// </summary>
public sealed class OwnTokenTests : IDisposable
{
    private const string Usage = """
        {"five_hour": {"utilization": 42.0, "resets_at": "2026-10-01T14:00:00.000000+00:00"},
         "seven_day": {"utilization": 17.0, "resets_at": "2026-10-05T00:00:00.000000+00:00"}}
        """;

    private DateTimeOffset _now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly StubbedAnthropic _server = new() { Usage = (HttpStatusCode.OK, Usage) };

    public void Dispose() => _server.Dispose();

    private ClaudeAccountClient Client(OAuthCredentials? borrowed, FakeTokenStore own, SignOutFlag? signedOut = null) =>
        new(new HttpClient(_server), () => borrowed, own.Store, signedOut ?? SignOutFlag.InMemory(), () => _now);

    [Fact]
    public async Task ClaudiosOwnTokenServesWhenClaudeCodeHasNone()
    {
        using var client = Client(null, new FakeTokenStore(Tokens.Own("own", _now.AddHours(1))));

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.True(payload.IsSignedIn);
        Assert.Equal(0.42, payload.Reading!.Session!.Percent, 4);
        Assert.Equal("Bearer own", _server.LastAuthorization);
    }

    [Fact]
    public async Task ClaudeCodesTokenComesFirst()
    {
        using var client = Client(Tokens.Borrowed("claude-code", _now.AddHours(1)), new FakeTokenStore(Tokens.Own("own", _now.AddHours(1))));

        await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Bearer claude-code", _server.LastAuthorization);
    }

    [Fact]
    public async Task AnExpiredOwnTokenIsRefreshedWithItsOwnScopesAndStored()
    {
        _server.Token = (HttpStatusCode.OK, """{"access_token": "own-2", "refresh_token": "refresh-2", "expires_in": 28800}""");
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddMinutes(1), "refresh-1", "user:profile", "user:inference"));
        using var client = Client(null, own);

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(_server.TokenRequests);
        Assert.Equal("refresh_token", (string?)request["grant_type"]);
        Assert.Equal("refresh-1", (string?)request["refresh_token"]);
        Assert.Equal(ClaudeAccountClient.ClientId, (string?)request["client_id"]);
        Assert.Equal("user:profile user:inference", (string?)request["scope"]);

        var stored = Assert.Single(own.Persisted);
        Assert.Equal("own-2", stored.AccessToken);
        Assert.Equal("refresh-2", stored.RefreshToken);
        Assert.Equal(_now.AddHours(8), stored.ExpiresAt);
        var oauth = stored.Root["claudeAiOauth"]!;
        Assert.Equal("own-2", (string?)oauth["accessToken"]);
        Assert.Equal("refresh-2", (string?)oauth["refreshToken"]);
        Assert.Equal(_now.AddHours(8).ToUnixTimeMilliseconds(), (long?)oauth["expiresAt"]);
        Assert.Equal(["user:profile", "user:inference"], stored.Scopes!);

        Assert.Equal("Bearer own-2", _server.LastAuthorization);
        Assert.NotNull(payload.Reading);
    }

    [Fact]
    public async Task WithoutScopesOfItsOwnTheRefreshAsksForClaudeCodes()
    {
        _server.Token = (HttpStatusCode.OK, """{"access_token": "own-2"}""");
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddMinutes(1), "refresh-1"));
        using var client = Client(null, own);

        await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(string.Join(' ', ClaudeAccountClient.Scopes), (string?)Assert.Single(_server.TokenRequests)["scope"]);
        // No new refresh token in the answer: the one held stays.
        Assert.Equal("refresh-1", Assert.Single(own.Persisted).RefreshToken);
        Assert.Equal(_now.AddHours(1), own.Stored!.ExpiresAt);
    }

    /// <summary>Another pass refreshed the store already: spending the refresh token again would rotate it for nothing.</summary>
    [Fact]
    public async Task ATokenAlreadyRenewedInTheStoreIsNotRefreshedAgain()
    {
        var expired = Tokens.Own("own-1", _now.AddMinutes(1));
        var renewed = Tokens.Own("own-2", _now.AddHours(1));
        var loads = new Queue<OAuthCredentials>([expired, renewed]);
        var store = new OwnTokenStore(() => loads.Count > 0 ? loads.Dequeue() : renewed, _ => true, () => { });
        using var client = new ClaudeAccountClient(new HttpClient(_server), () => null, store, SignOutFlag.InMemory(), () => _now);

        await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_server.TokenRequests);
        Assert.Equal("Bearer own-2", _server.LastAuthorization);
    }

    [Fact]
    public async Task On401TheOwnTokenIsRefreshedOnce()
    {
        _server.Token = (HttpStatusCode.OK, """{"access_token": "own-2", "expires_in": 3600}""");
        _server.Usage = (HttpStatusCode.Unauthorized, "{}");
        _server.OnUsage = authorization => _server.Usage = authorization == "Bearer own-2" ? (HttpStatusCode.OK, Usage) : _server.Usage;
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddHours(1)));
        using var client = Client(null, own);

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Single(_server.TokenRequests);
        Assert.Equal(2, _server.UsageRequests);
        Assert.Equal(0.42, payload.Reading!.Session!.Percent, 4);
        Assert.Equal("own-2", own.Stored!.AccessToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error": "invalid_grant", "error_description": "Refresh token not found or invalid"}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"error": {"type": "invalid_grant", "message": "revoked"}}""")]
    public async Task InvalidGrantDropsTheOwnTokenForClaudeCodes(HttpStatusCode status, string answer)
    {
        _server.Token = (status, answer);
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddMinutes(1)));
        // Expired too: only the server decides, so it is still tried rather than declared signed out.
        using var client = Client(Tokens.Borrowed("claude-code", _now.AddMinutes(-5)), own);

        var first = await client.FetchAsync(TestContext.Current.CancellationToken);
        _now = _now.AddMinutes(2);
        await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(first.Reading);
        Assert.Equal("Bearer claude-code", _server.LastAuthorization);
        Assert.Single(_server.TokenRequests);
        Assert.Empty(own.Persisted);
    }

    [Fact]
    public async Task AnotherRefusalKeepsTheOwnTokenForTheNextAttempt()
    {
        _server.Token = (HttpStatusCode.InternalServerError, "");
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddMinutes(1)));
        using var client = Client(null, own);

        await client.FetchAsync(TestContext.Current.CancellationToken);
        _now = _now.AddMinutes(2);
        Assert.True(client.ResetBackoff());
        await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _server.TokenRequests.Count);
    }

    /// <summary>
    /// A refresh still out when the user signs out: its token belongs to the session that ended
    /// and must not land back in the store the sign-out just emptied.
    /// </summary>
    [Fact]
    public async Task ARefreshLandingAfterASignOutIsDropped()
    {
        var cancel = TestContext.Current.CancellationToken;
        _server.Token = (HttpStatusCode.OK, """{"access_token": "own-2", "refresh_token": "refresh-2", "expires_in": 3600}""");
        var release = new TaskCompletionSource();
        _server.Hold = release;
        _server.HoldPath = "/v1/oauth/token";
        var own = new FakeTokenStore(Tokens.Own("own-1", _now.AddMinutes(1)));
        using var client = Client(null, own);

        var inFlight = client.FetchAsync(cancel);
        await _server.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        client.SignOut();
        release.SetResult();

        Assert.Equal(AccountPayload.SignedOut, await inFlight);
        Assert.Empty(own.Persisted);
        Assert.Null(own.Stored);
        Assert.Equal(0, _server.UsageRequests);
    }
}
