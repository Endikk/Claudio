using System.Buffers.Text;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Claudio.Core.Services;

/// <summary>How the authorisation code comes back from the browser.</summary>
public enum OAuthMode
{
    /// <summary>A tiny local server on <c>localhost:54545</c> receives the redirect: nothing for the user to do.</summary>
    Loopback,

    /// <summary>The port is taken: the page shows <c>code#state</c>, which the user pastes into Claudio.</summary>
    Manual,
}

/// <summary>Why a sign-in ended without a token.</summary>
public enum OAuthFailure
{
    TimedOut,
    StateMismatch,
    MalformedCode,
    ExchangeFailed,
    Cancelled,
}

/// <summary>A sign-in that did not go through, with the message Claudy shows for it.</summary>
public sealed class OAuthException : Exception
{
    public OAuthException(OAuthFailure failure, int status = 0)
        : base(Describe(failure, status))
    {
        Failure = failure;
        Status = status;
    }

    public OAuthFailure Failure { get; }

    /// <summary>The HTTP status of a refused code exchange; 0 when the network did not answer.</summary>
    public int Status { get; }

    private static string Describe(OAuthFailure failure, int status) => failure switch
    {
        OAuthFailure.TimedOut => "Sign-in timed out. Try again.",
        OAuthFailure.StateMismatch => "Unexpected authorisation response (state). Try again.",
        OAuthFailure.MalformedCode => "Invalid code: paste exactly what the page shows (code#state).",
        OAuthFailure.ExchangeFailed => $"Code exchange refused (HTTP {status}).",
        _ => "Sign-in cancelled.",
    };
}

/// <summary>
/// <i>Authorization code + PKCE</i> sign-in against Claude Code's public client, a port of
/// Claudy's <c>ClaudeOAuth</c>.
/// <para>
/// <b>Fallback path.</b> Claudio reads Claude Code's token first, read-only
/// (<see cref="ClaudeCodeCredentials"/>): that is the source which never goes stale. This sign-in
/// serves machines where that token cannot be read. Claudio then gets <b>its own token</b>, kept in
/// its own store: the session stays independent, and rotating one side's tokens never affects the
/// other.
/// </para>
/// Two ways of capturing the code: <see cref="OAuthMode.Loopback"/>, a single-request server on
/// <c>localhost:54545</c>, and <see cref="OAuthMode.Manual"/>, the fallback when that port is taken.
/// </summary>
public sealed class ClaudeOAuth : IDisposable
{
    public const int LoopbackPort = 54545;

    public static Uri LoopbackRedirectUrl { get; } = new($"http://localhost:{LoopbackPort}/callback");

    private readonly HttpClient _http;
    private readonly Action<Uri> _openBrowser;
    private readonly Action<string> _log;
    private readonly Lock _lock = new();
    private string _verifier = string.Empty;
    private string _state = string.Empty;
    private string _redirectUri = string.Empty;
    private LoopbackServer? _server;

    /// <param name="openBrowser">Opens the authorisation page in the user's browser.</param>
    public ClaudeOAuth(HttpClient http, Action<Uri> openBrowser, Action<string>? log = null)
    {
        _http = http;
        _openBrowser = openBrowser;
        _log = log ?? (_ => { });
    }

    /// <summary>How long the loopback server waits for the browser: five minutes.</summary>
    public TimeSpan LoopbackTimeout { get; init; } = TimeSpan.FromSeconds(300);

    /// <summary>Generates verifier and state, picks the mode, opens the browser.</summary>
    public OAuthMode Begin()
    {
        Uri url;
        OAuthMode mode;
        lock (_lock)
        {
            // A sign-in started again replaces the previous one, which would otherwise hold the port.
            // Its waiter, if any, ends as cancelled and disposes it.
            _server?.Stop();
            _verifier = RandomUrlSafe(64);
            _state = RandomUrlSafe(32);
            _server = LoopbackServer.TryStart(LoopbackPort);
            mode = _server is null ? OAuthMode.Manual : OAuthMode.Loopback;
            _redirectUri = _server is null ? ClaudeAccountClient.ManualRedirectUrl : LoopbackRedirectUrl.AbsoluteUri;
            url = AuthorizeUri(_redirectUri, _verifier, _state);
        }
        try
        {
            _openBrowser(url);
        }
        catch
        {
            Cancel();
            throw;
        }
        return mode;
    }

    /// <summary>
    /// Loopback mode: waits for the browser redirect, then exchanges the code. Throws an
    /// <see cref="OAuthException"/>; <see cref="Cancel"/> ends it with <see cref="OAuthFailure.Cancelled"/>.
    /// </summary>
    public async Task<OAuthCredentials> AwaitLoopbackCodeAsync(CancellationToken cancel = default)
    {
        LoopbackServer? server;
        string state, verifier, redirectUri;
        lock (_lock)
        {
            (server, state, verifier, redirectUri) = (_server, _state, _verifier, _redirectUri);
        }
        if (server is null)
        {
            throw new OAuthException(OAuthFailure.Cancelled);
        }
        try
        {
            var code = await server.WaitForCodeAsync(state, LoopbackTimeout, cancel).ConfigureAwait(false);
            return await ExchangeAsync(code, state, verifier, redirectUri, cancel).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (_server == server)
                {
                    _server = null;
                }
            }
            server.Dispose();
        }
    }

    /// <summary>Manual mode: the page shows <c>code#state</c>, which the user pastes verbatim.</summary>
    public async Task<OAuthCredentials> RedeemManualCodeAsync(string pasted, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(pasted);
        var parts = pasted.Trim().Split('#', 2);
        if (parts[0].Length == 0)
        {
            throw new OAuthException(OAuthFailure.MalformedCode);
        }
        string state, verifier, redirectUri;
        lock (_lock)
        {
            (state, verifier, redirectUri) = (_state, _verifier, _redirectUri);
        }
        var pastedState = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : state;
        return await ExchangeAsync(parts[0], pastedState, verifier, redirectUri, cancel).ConfigureAwait(false);
    }

    /// <summary>Stops the loopback server: the sign-in waiting on it ends as cancelled.</summary>
    public void Cancel()
    {
        LoopbackServer? server;
        lock (_lock)
        {
            server = _server;
            _server = null;
        }
        server?.Stop();
    }

    public void Dispose() => Cancel();

    /// <summary>PKCE's S256: the base64url SHA-256 of the verifier.</summary>
    public static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    private static Uri AuthorizeUri(string redirectUri, string verifier, string state)
    {
        (string Name, string Value)[] items =
        [
            ("code", "true"),
            ("client_id", ClaudeAccountClient.ClientId),
            ("response_type", "code"),
            ("redirect_uri", redirectUri),
            ("scope", string.Join(' ', ClaudeAccountClient.AuthorizeScopes)),
            ("code_challenge", Challenge(verifier)),
            ("code_challenge_method", "S256"),
            ("state", state),
        ];
        var query = string.Join('&', items.Select(item => $"{item.Name}={Uri.EscapeDataString(item.Value)}"));
        return new Uri($"{ClaudeAccountClient.AuthorizeUrl.AbsoluteUri}?{query}");
    }

    private async Task<OAuthCredentials> ExchangeAsync(string code, string state, string verifier, string redirectUri,
                                                       CancellationToken cancel)
    {
        var (body, status) = await ClaudeAccountClient.PostJsonAsync(_http, ClaudeAccountClient.TokenUrl, new JsonObject
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["state"] = state,
            ["client_id"] = ClaudeAccountClient.ClientId,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        }, TimeSpan.FromSeconds(20), cancel).ConfigureAwait(false);
        if (status == 0)
        {
            throw new OAuthException(OAuthFailure.ExchangeFailed, 0);
        }
        var root = status == HttpStatusCode.OK ? OAuthJson.Object(body) : null;
        if (OAuthJson.Text(root?["access_token"]) is not { Length: > 0 } accessToken)
        {
            _log($"OAuth exchange HTTP {(int)status}");
            throw new OAuthException(OAuthFailure.ExchangeFailed, (int)status);
        }

        var refreshToken = OAuthJson.Text(root!["refresh_token"]);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(OAuthJson.Number(root["expires_in"]) ?? 3600);
        var oauth = new JsonObject
        {
            ["accessToken"] = accessToken,
            ["expiresAt"] = expiresAt.ToUnixTimeMilliseconds(),
        };
        if (refreshToken is not null)
        {
            oauth["refreshToken"] = refreshToken;
        }
        if (OAuthJson.Text(root["scope"]) is { } scope)
        {
            oauth["scopes"] = new JsonArray([.. scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(name => (JsonNode?)name)]);
        }

        _log("OAuth sign-in succeeded");
        return new OAuthCredentials(accessToken, refreshToken, expiresAt, new JsonObject { ["claudeAiOauth"] = oauth },
                                    CredentialSource.OwnStore);
    }

    private static string RandomUrlSafe(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>
    /// Single-request HTTP server: receives <c>GET /callback?code=â€¦&amp;state=â€¦</c>, answers with a
    /// confirmation page and returns the code. Stray requests (favicon and friends) are ignored.
    /// A <see cref="TcpListener"/> on 127.0.0.1 rather than <c>HttpListener</c>, which needs a URL
    /// reservation on Windows.
    /// </summary>
    private sealed class LoopbackServer : IDisposable
    {
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();

        private LoopbackServer(TcpListener listener) => _listener = listener;

        /// <summary>
        /// The availability probe and the server in one: binding fails at once when the port is
        /// taken, early enough to choose the manual mode before the browser opens.
        /// </summary>
        public static LoopbackServer? TryStart(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return new LoopbackServer(listener);
            }
            catch (SocketException)
            {
                listener.Dispose();
                return null;
            }
        }

        public void Stop()
        {
            try
            {
                _stop.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            _listener.Stop();
        }

        public void Dispose()
        {
            Stop();
            _stop.Dispose();
        }

        public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken cancel)
        {
            var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, _stop.Token);
            linked.CancelAfter(timeout);
            using var ended = linked.Token.Register(() =>
            {
                if (cancel.IsCancellationRequested)
                {
                    result.TrySetCanceled(cancel);
                }
                else
                {
                    var failure = _stop.IsCancellationRequested ? OAuthFailure.Cancelled : OAuthFailure.TimedOut;
                    result.TrySetException(new OAuthException(failure));
                }
            });
            var accepting = AcceptAsync(expectedState, result, linked.Token);
            try
            {
                return await result.Task.ConfigureAwait(false);
            }
            finally
            {
                _listener.Stop();
                await accepting.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Every connection is served on its own: a browser opens speculative ones that never send
        /// a request, and waiting on one of those would stall the real redirect.
        /// </summary>
        private async Task AcceptAsync(string expectedState, TaskCompletionSource<string> result, CancellationToken cancel)
        {
            while (!result.Task.IsCompleted)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException error)
                {
                    result.TrySetException(cancel.IsCancellationRequested ? new OAuthException(OAuthFailure.Cancelled) : error);
                    return;
                }
                _ = ServeAsync(client, expectedState, result);
            }
        }

        private static async Task ServeAsync(TcpClient client, string expectedState, TaskCompletionSource<string> result)
        {
            using (client)
            using (var deadline = new CancellationTokenSource(RequestTimeout))
            {
                string? code;
                bool ok;
                try
                {
                    var stream = client.GetStream();
                    var request = await ReadHeadAsync(stream, deadline.Token).ConfigureAwait(false);
                    code = QueryValue("code", request);
                    if (code is null)
                    {
                        await RespondAsync(stream, "404 Not Found", string.Empty, deadline.Token).ConfigureAwait(false);
                        return;
                    }
                    ok = QueryValue("state", request) == expectedState;
                    await RespondAsync(stream, "200 OK", ok ? SuccessPage : FailurePage, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
                if (ok)
                {
                    result.TrySetResult(code);
                }
                else
                {
                    result.TrySetException(new OAuthException(OAuthFailure.StateMismatch));
                }
            }
        }

        /// <summary>
        /// The request line and headers, read to their end: closing a socket on unread data resets
        /// the connection, and the browser would then drop the page.
        /// </summary>
        private static async Task<string> ReadHeadAsync(NetworkStream stream, CancellationToken cancel)
        {
            var buffer = new byte[16_384];
            var length = 0;
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), cancel).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                length += read;
                if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                {
                    break;
                }
            }
            return Encoding.UTF8.GetString(buffer, 0, length);
        }

        private static async Task RespondAsync(NetworkStream stream, string status, string body, CancellationToken cancel)
        {
            var content = Encoding.UTF8.GetBytes(body);
            var head = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, cancel).ConfigureAwait(false);
            await stream.WriteAsync(content, cancel).ConfigureAwait(false);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
        }

        /// <summary>One parameter of the request line, <c>GET /callback?â€¦ HTTP/1.1</c>; empty counts as absent.</summary>
        private static string? QueryValue(string name, string request)
        {
            var line = request.Split("\r\n", 2)[0].Split(' ');
            if (line.Length < 2 || line[1].IndexOf('?', StringComparison.Ordinal) is not (var mark and >= 0))
            {
                return null;
            }
            foreach (var pair in line[1][(mark + 1)..].Split('&'))
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);
                var key = equals >= 0 ? pair[..equals] : pair;
                if (Uri.UnescapeDataString(key) == name)
                {
                    var value = equals >= 0 ? Uri.UnescapeDataString(pair[(equals + 1)..]) : string.Empty;
                    return value.Length > 0 ? value : null;
                }
            }
            return null;
        }

        private const string SuccessPage = """
            <html><head><meta charset="utf-8"><title>Claudio</title></head>
            <body style="font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif;background:#141210;color:#eee;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
            <div style="text-align:center"><div style="font-size:44px">âœ³ï¸Ž</div>
            <h2>Claudio is connected</h2><p style="color:#999">You can close this tab.</p></div>
            </body></html>
            """;

        private const string FailurePage = """
            <html><head><meta charset="utf-8"><title>Claudio</title></head>
            <body style="font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif;background:#141210;color:#eee;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
            <div style="text-align:center"><h2>Unexpected response</h2>
            <p style="color:#999">Go back to Claudio and try again.</p></div>
            </body></html>
            """;
    }
}
