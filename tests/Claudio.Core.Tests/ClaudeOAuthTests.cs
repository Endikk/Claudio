using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Web;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The sign-in flow without a browser and without Anthropic: the test plays the browser, against
/// the real loopback server, and the token endpoint is a stub.
/// </summary>
public sealed class ClaudeOAuthTests : IDisposable
{
    private const string Granted = """
        {"access_token": "sk-ant-oat01-own", "refresh_token": "sk-ant-ort01-own", "expires_in": 3600,
         "scope": "user:profile user:inference"}
        """;

    private readonly StubbedAnthropic _server = new() { Token = (HttpStatusCode.OK, Granted) };

    public void Dispose() => _server.Dispose();
    private readonly List<Uri> _opened = [];

    private ClaudeOAuth Flow(TimeSpan? timeout = null) =>
        new(new HttpClient(_server), _opened.Add) { LoopbackTimeout = timeout ?? TimeSpan.FromSeconds(10) };

    private NameValueCollection AuthorizeQuery => HttpUtility.ParseQueryString(Assert.Single(_opened).Query);

    /// <summary>RFC 7636, appendix B.</summary>
    [Fact]
    public void TheChallengeIsS256() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", ClaudeOAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void TheAuthorizeUrlCarriesClaudeCodesParameters()
    {
        using var flow = Flow();

        var mode = flow.Begin();

        var url = Assert.Single(_opened);
        Assert.Equal("https://claude.com/cai/oauth/authorize", url.GetLeftPart(UriPartial.Path));
        var query = AuthorizeQuery;
        Assert.Equal("true", query["code"]);
        Assert.Equal("9d1c250a-e61b-44d9-88ed-5944d1962f5e", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("org:create_api_key user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(43, query["code_challenge"]!.Length);
        Assert.Equal(43, query["state"]!.Length);
        Assert.Equal(mode == OAuthMode.Loopback ? "http://localhost:54545/callback" : "https://platform.claude.com/oauth/code/callback",
                     query["redirect_uri"]);
    }

    [Fact]
    public void EachSignInHasItsOwnStateAndVerifier()
    {
        using var flow = Flow();

        flow.Begin();
        flow.Begin();

        Assert.Equal(2, _opened.Count);
        var (first, second) = (HttpUtility.ParseQueryString(_opened[0].Query), HttpUtility.ParseQueryString(_opened[1].Query));
        Assert.NotEqual(first["state"], second["state"]);
        Assert.NotEqual(first["code_challenge"], second["code_challenge"]);
    }

    [Fact]
    public void ATakenPortFallsBackToThePastedCode()
    {
        using var taken = Occupy();
        using var flow = Flow();

        Assert.Equal(OAuthMode.Manual, flow.Begin());
        Assert.Equal("https://platform.claude.com/oauth/code/callback", AuthorizeQuery["redirect_uri"]);
    }

    [Fact]
    public async Task APastedCodeIsExchangedWithItsStateAndTheVerifier()
    {
        using var taken = Occupy();
        using var flow = Flow();
        flow.Begin();
        var before = DateTimeOffset.UtcNow;

        var credentials = await flow.RedeemManualCodeAsync("  pasted-code#pasted-state \n", TestContext.Current.CancellationToken);

        var request = Assert.Single(_server.TokenRequests);
        Assert.Equal("authorization_code", (string?)request["grant_type"]);
        Assert.Equal("pasted-code", (string?)request["code"]);
        Assert.Equal("pasted-state", (string?)request["state"]);
        Assert.Equal(ClaudeAccountClient.ClientId, (string?)request["client_id"]);
        Assert.Equal("https://platform.claude.com/oauth/code/callback", (string?)request["redirect_uri"]);
        Assert.Equal(AuthorizeQuery["code_challenge"], ClaudeOAuth.Challenge((string)request["code_verifier"]!));

        Assert.Equal(CredentialSource.OwnStore, credentials.Source);
        Assert.Equal("sk-ant-oat01-own", credentials.AccessToken);
        Assert.Equal("sk-ant-ort01-own", credentials.RefreshToken);
        Assert.InRange(credentials.ExpiresAt!.Value, before.AddSeconds(3599), DateTimeOffset.UtcNow.AddSeconds(3601));
        Assert.Equal(["user:profile", "user:inference"], credentials.Scopes!);
        var oauth = credentials.Root["claudeAiOauth"]!;
        Assert.Equal("sk-ant-oat01-own", (string?)oauth["accessToken"]);
        Assert.Equal("sk-ant-ort01-own", (string?)oauth["refreshToken"]);
        Assert.Equal(credentials.ExpiresAt.Value.ToUnixTimeMilliseconds(), (long?)oauth["expiresAt"]);
    }

    [Fact]
    public async Task ACodeWithoutItsStateIsSentWithTheFlowsOwn()
    {
        using var taken = Occupy();
        using var flow = Flow();
        flow.Begin();

        await flow.RedeemManualCodeAsync("pasted-code", TestContext.Current.CancellationToken);

        Assert.Equal(AuthorizeQuery["state"], (string?)Assert.Single(_server.TokenRequests)["state"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#state")]
    public async Task AMalformedCodeIsRefusedWithoutARequest(string pasted)
    {
        using var flow = Flow();

        var error = await Assert.ThrowsAsync<OAuthException>(() => flow.RedeemManualCodeAsync(pasted, TestContext.Current.CancellationToken));

        Assert.Equal(OAuthFailure.MalformedCode, error.Failure);
        Assert.Equal("Invalid code: paste exactly what the page shows (code#state).", error.Message);
        Assert.Empty(_server.TokenRequests);
    }

    [Fact]
    public async Task ARefusedExchangeSaysWhichStatus()
    {
        _server.Token = (HttpStatusCode.BadRequest, """{"error": "invalid_grant"}""");
        using var flow = Flow();

        var error = await Assert.ThrowsAsync<OAuthException>(() => flow.RedeemManualCodeAsync("code#state", TestContext.Current.CancellationToken));

        Assert.Equal(OAuthFailure.ExchangeFailed, error.Failure);
        Assert.Equal(400, error.Status);
        Assert.Equal("Code exchange refused (HTTP 400).", error.Message);
    }

    [Fact]
    public async Task AnAnswerWithoutATokenIsARefusal()
    {
        _server.Token = (HttpStatusCode.OK, "{}");
        using var flow = Flow();

        var error = await Assert.ThrowsAsync<OAuthException>(() => flow.RedeemManualCodeAsync("code#state", TestContext.Current.CancellationToken));

        Assert.Equal("Code exchange refused (HTTP 200).", error.Message);
    }

    // The loopback server, reached by the test as the browser would reach it

    [Fact]
    public async Task TheLoopbackReceivesTheRedirectAndIgnoresStrayRequests()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var flow = Flow();
        SkipUnlessLoopback(flow.Begin());
        var signIn = flow.AwaitLoopbackCodeAsync(cancel);

        // A speculative connection that never sends anything must not hold the real redirect up.
        using var idle = new TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, ClaudeOAuth.LoopbackPort, cancel);
        using var browser = new HttpClient();
        var favicon = await browser.GetAsync(Callback("/favicon.ico"), cancel);
        var page = await browser.GetAsync(Callback($"/callback?code=loopback-code&state={Uri.EscapeDataString(AuthorizeQuery["state"]!)}"), cancel);
        var credentials = await signIn.WaitAsync(TimeSpan.FromSeconds(10), cancel);

        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Claudio is connected", await page.Content.ReadAsStringAsync(cancel), StringComparison.Ordinal);
        var request = Assert.Single(_server.TokenRequests);
        Assert.Equal("loopback-code", (string?)request["code"]);
        Assert.Equal("http://localhost:54545/callback", (string?)request["redirect_uri"]);
        Assert.Equal(AuthorizeQuery["code_challenge"], ClaudeOAuth.Challenge((string)request["code_verifier"]!));
        Assert.Equal("sk-ant-oat01-own", credentials.AccessToken);
        Assert.Equal(CredentialSource.OwnStore, credentials.Source);
    }

    [Fact]
    public async Task AnUnexpectedStateEndsTheSignIn()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var flow = Flow();
        SkipUnlessLoopback(flow.Begin());
        var signIn = flow.AwaitLoopbackCodeAsync(cancel);

        using var browser = new HttpClient();
        var page = await browser.GetAsync(Callback("/callback?code=loopback-code&state=forged"), cancel);
        var error = await Assert.ThrowsAsync<OAuthException>(() => signIn.WaitAsync(TimeSpan.FromSeconds(10), cancel));

        Assert.Contains("Go back to Claudio", await page.Content.ReadAsStringAsync(cancel), StringComparison.Ordinal);
        Assert.Equal(OAuthFailure.StateMismatch, error.Failure);
        Assert.Equal("Unexpected authorisation response (state). Try again.", error.Message);
        Assert.Empty(_server.TokenRequests);
    }

    [Fact]
    public async Task TheLoopbackGivesUpAfterItsTimeout()
    {
        using var flow = Flow(TimeSpan.FromMilliseconds(200));
        SkipUnlessLoopback(flow.Begin());

        var error = await Assert.ThrowsAsync<OAuthException>(() => flow.AwaitLoopbackCodeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(OAuthFailure.TimedOut, error.Failure);
        Assert.Equal("Sign-in timed out. Try again.", error.Message);
    }

    [Fact]
    public async Task CancellingEndsTheWaitAndFreesThePort()
    {
        var cancel = TestContext.Current.CancellationToken;
        using var flow = Flow();
        SkipUnlessLoopback(flow.Begin());
        var signIn = flow.AwaitLoopbackCodeAsync(cancel);

        flow.Cancel();
        var error = await Assert.ThrowsAsync<OAuthException>(() => signIn.WaitAsync(TimeSpan.FromSeconds(10), cancel));

        Assert.Equal(OAuthFailure.Cancelled, error.Failure);
        Assert.Equal("Sign-in cancelled.", error.Message);
        Assert.Equal(OAuthMode.Loopback, flow.Begin());
        flow.Cancel();
    }

    [Fact]
    public async Task WaitingWithoutALoopbackIsACancelledSignIn()
    {
        using var taken = Occupy();
        using var flow = Flow();
        flow.Begin();

        var error = await Assert.ThrowsAsync<OAuthException>(() => flow.AwaitLoopbackCodeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(OAuthFailure.Cancelled, error.Failure);
    }

    private static Uri Callback(string pathAndQuery) => new($"http://127.0.0.1:{ClaudeOAuth.LoopbackPort}{pathAndQuery}");

    private static void SkipUnlessLoopback(OAuthMode mode) =>
        Assert.SkipUnless(mode == OAuthMode.Loopback, $"port {ClaudeOAuth.LoopbackPort} is taken on this machine");

    /// <summary>Holds the loopback port, as another program would. Already held: it stays taken all the same.</summary>
    private static TcpListener? Occupy()
    {
        var listener = new TcpListener(IPAddress.Loopback, ClaudeOAuth.LoopbackPort);
        try
        {
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            listener.Dispose();
            return null;
        }
    }
}
