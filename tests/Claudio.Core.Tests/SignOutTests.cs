using System.Net;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// Signing Claudio out must hold even while Claude Code stays signed in: the borrowed token would
/// otherwise take over at the next reading and the button would do nothing.
/// </summary>
public sealed class SignOutTests : IDisposable
{
    private const string Usage = """
        {"five_hour": {"utilization": 42, "resets_at": "2099-01-01T00:00:00Z"},
         "seven_day": {"utilization": 17, "resets_at": "2099-01-05T00:00:00Z"}}
        """;

    private const string Profile = """
        {"account": {"full_name": "Previous Account", "email": "previous@example.com"}}
        """;

    private readonly DateTimeOffset _now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private readonly SignOutFlag _signedOut = SignOutFlag.InMemory();
    private readonly StubbedAnthropic _server = new() { Usage = (HttpStatusCode.OK, Usage), Profile = (HttpStatusCode.OK, Profile) };

    public void Dispose() => _server.Dispose();

    private OAuthCredentials ClaudeCodeToken(string value = "borrowed", double expiresInMinutes = 60) =>
        Tokens.Borrowed(value, _now.AddMinutes(expiresInMinutes));

    /// <summary>The Credential Manager is never reached: Claudio's own store is a fake.</summary>
    private ClaudeAccountClient Client(OAuthCredentials? borrowed, FakeTokenStore? own = null) =>
        new(new HttpClient(_server), () => borrowed, (own ?? new FakeTokenStore()).Store, _signedOut, () => _now);

    // Signing in

    [Fact]
    public void SignInStoresTheTokenThroughTheInjectedStore()
    {
        var own = new FakeTokenStore();
        using var client = Client(null, own);

        client.SignIn(Tokens.Own("own", _now.AddHours(1)));

        Assert.Equal(["own"], own.Persisted.Select(token => token.AccessToken));
    }

    [Fact]
    public async Task SigningInClearsASignOut()
    {
        _signedOut.IsSet = true;
        using var client = Client(null);

        client.SignIn(Tokens.Own("own", _now.AddHours(1)));
        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(client.IsSignedOutByUser);
        Assert.False(_signedOut.IsSet);
        Assert.True(payload.IsSignedIn);
        Assert.Equal("Bearer own", _server.LastAuthorization);
    }

    // Signing out

    [Fact]
    public async Task SignOutErasesClaudiosTokenAndHoldsAcrossARelaunch()
    {
        var own = new FakeTokenStore(Tokens.Own("own", _now.AddHours(1)));
        using (var running = Client(ClaudeCodeToken(), own))
        {
            running.SignOut();
        }
        using var relaunched = Client(ClaudeCodeToken(), own);

        var payload = await relaunched.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, own.Erasures);
        Assert.Null(own.Stored);
        Assert.False(payload.IsSignedIn);
        Assert.True(payload.IsSignedOutByUser);
        Assert.Null(payload.Reading);
        Assert.Null(payload.Profile);
        Assert.Equal(0, _server.UsageRequests);
    }

    [Fact]
    public async Task ASignedOutClientIgnoresClaudeCodesToken()
    {
        _signedOut.IsSet = true;
        using var client = Client(ClaudeCodeToken());

        var payload = await client.FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(AccountPayload.SignedOut, payload);
        Assert.Equal(0, _server.UsageRequests);
    }

    // Signing back in

    [Fact]
    public void SigningBackInReusesClaudeCodesSessionWithoutABrowser()
    {
        _signedOut.IsSet = true;
        using var client = Client(ClaudeCodeToken());

        var resumed = client.ResumeWithClaudeCode();

        Assert.True(resumed);
        Assert.False(client.IsSignedOutByUser);
        Assert.False(_signedOut.IsSet);
    }

    [Fact]
    public void SigningBackInNeedsTheBrowserWhenClaudeCodeIsSignedOut()
    {
        _signedOut.IsSet = true;
        using var client = Client(null);

        var resumed = client.ResumeWithClaudeCode();

        Assert.False(resumed);
        Assert.True(client.IsSignedOutByUser);
    }

    [Fact]
    public void AnExpiredClaudeCodeTokenDoesNotCountAsASession()
    {
        _signedOut.IsSet = true;
        using var client = Client(ClaudeCodeToken(expiresInMinutes: -1));

        Assert.False(client.ResumeWithClaudeCode());
    }

    // A reading still in flight when the session changes

    /// <summary>
    /// Sign out, then straight back in, while a reading waits on the network. What it brings back
    /// belongs to the session that ended: it must not be served, nor cached for the next reading.
    /// </summary>
    [Fact]
    public async Task AReadingInFlightAcrossASessionChangeIsDropped()
    {
        var cancel = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource();
        _server.Hold = release;
        using var client = Client(ClaudeCodeToken("signout-race"));

        var inFlight = client.FetchAsync(cancel);
        await _server.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancel);

        client.SignOut();
        Assert.True(client.ResumeWithClaudeCode());
        _server.Usage = (HttpStatusCode.ServiceUnavailable, "");
        release.SetResult();

        var stale = await inFlight;
        Assert.Null(stale.Reading);
        Assert.Null(stale.Profile);

        var next = await client.FetchAsync(cancel);
        Assert.Null(next.Reading);
        Assert.Null(next.Profile);
    }

    /// <summary>A sign-out landing while a reading is out wins over that reading's answer.</summary>
    [Fact]
    public async Task ASignOutDuringAReadingWinsOverItsAnswer()
    {
        var cancel = TestContext.Current.CancellationToken;
        var release = new TaskCompletionSource();
        _server.Hold = release;
        using var client = Client(ClaudeCodeToken());

        var inFlight = client.FetchAsync(cancel);
        await _server.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancel);
        client.SignOut();
        release.SetResult();

        Assert.Equal(AccountPayload.SignedOut, await inFlight);
    }
}
