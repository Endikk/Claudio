using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Reads the account's quotas from Anthropic's OAuth endpoints, the ones behind claude.ai ▸ Usage
/// and Claude Code's <c>/usage</c>. A port of Claudy's <c>ClaudeAccountClient</c>: percentages come
/// from here alone, never from an estimate. Tokens are tried in order of dependability: Claude
/// Code's, borrowed read-only, which it renews itself and Claudio therefore never refreshes; then
/// Claudio's own, from its sign-in, for machines where that one cannot be read. A failure resets
/// nothing: the last reading is served again, marked stale, and attempts space out by the nature
/// of the fault.
/// </summary>
public sealed class ClaudeAccountClient : IDisposable
{
    /// <summary>Claude Code's public OAuth client, the one the token was issued to.</summary>
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>
    /// Scopes Claude Code 2.1 carries through a refresh. <c>user:profile</c> is essential: without
    /// it the API returns no quota at all ("missing profile scope").
    /// </summary>
    public static IReadOnlyList<string> Scopes { get; } =
        ["user:profile", "user:inference", "user:sessions:claude_code", "user:mcp_servers", "user:file_upload"];

    /// <summary>
    /// Scopes requested at authorisation: Claude Code adds <c>org:create_api_key</c>. Asking for
    /// the exact same set keeps Anthropic from treating Claudio's request differently.
    /// </summary>
    public static IReadOnlyList<string> AuthorizeScopes { get; } = ["org:create_api_key", .. Scopes];

    /// <summary>
    /// Claude Code 2.1's canonical entry point. It currently redirects to
    /// <c>claude.ai/oauth/authorize</c>; going through it follows the redirect Anthropic maintains.
    /// </summary>
    public static Uri AuthorizeUrl { get; } = new("https://claude.com/cai/oauth/authorize");

    /// <summary>Claude Code 2.1's production token host.</summary>
    public static Uri TokenUrl { get; } = new("https://platform.claude.com/v1/oauth/token");

    /// <summary>Where the sign-in page shows <c>code#state</c> when the loopback port is taken.</summary>
    public const string ManualRedirectUrl = "https://platform.claude.com/oauth/code/callback";

    private static readonly Uri UsageUrl = new("https://api.anthropic.com/api/oauth/usage");
    private static readonly Uri ProfileUrl = new("https://api.anthropic.com/api/oauth/profile");

    /// <summary>claude.ai updates by the minute: polling faster gains nothing and invites 429s.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    /// <summary>The profile only moves when a subscription does.</summary>
    private static readonly TimeSpan ProfileTtl = TimeSpan.FromHours(6);
    /// <summary>Rate limit or refused token: patience is the only useful answer.</summary>
    private static readonly TimeSpan[] BackoffSteps = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];
    /// <summary>Network or server fault: it can clear any second.</summary>
    private static readonly TimeSpan[] TransientSteps = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

    /// <summary>
    /// A manual retry lifts the server's backoff once a minute at most: holding a refresh key
    /// repeats it, and each repeat would otherwise send a request. Same span as the cache.
    /// </summary>
    public static TimeSpan ManualRetrySpacing => CacheTtl;

    private readonly HttpClient _http;
    private readonly Func<OAuthCredentials?> _borrowedToken;
    private readonly OwnTokenStore _ownToken;
    private readonly SignOutFlag _signedOut;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _log;
    /// <summary>One reading at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>
    /// Guards the state below. It is never held across a request: signing in or out while a
    /// reading waits on the network takes effect at once, as on Claudy's reentrant actor.
    /// </summary>
    private readonly Lock _lock = new();

    private OAuthCredentials? _credentials;
    private QuotaReading? _lastReading;
    private OAuthProfile? _lastProfile;
    private DateTimeOffset? _profileFetchedAt;
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastManualRetry;
    private int _failureCount;
    /// <summary>
    /// Set by a manual retry: the next reading skips the cache. <c>_lastSuccess</c> stays, since it
    /// is what lets a failed retry fall back to the last reading instead of "—".
    /// </summary>
    private bool _skipsCache;
    /// <summary>
    /// Claudio's own token declared dead (<c>invalid_grant</c>): it is no longer attempted, and the
    /// borrowed token takes over until an explicit new sign-in.
    /// </summary>
    private bool _ownTokenIsDead;
    /// <summary>
    /// Bumped by every sign-in and sign-out. A reading or a token refresh can still be waiting on
    /// the network when the session changes: whatever it brings back then belongs to the previous
    /// session and is dropped, never written back. Without this, a refresh landing after a
    /// sign-out would put a live token back in the store.
    /// </summary>
    private int _generation;

    /// <param name="borrowedToken">Claude Code's token, read-only.</param>
    /// <param name="ownToken">Claudio's own store, the one its sign-in fills.</param>
    /// <param name="signedOut">Persisted by the app: the user signed Claudio out on purpose.</param>
    public ClaudeAccountClient(HttpClient http, Func<OAuthCredentials?> borrowedToken, OwnTokenStore ownToken,
                               SignOutFlag signedOut, Func<DateTimeOffset>? now = null, Action<string>? log = null)
    {
        _http = http;
        _borrowedToken = borrowedToken;
        _ownToken = ownToken;
        _signedOut = signedOut;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// True after "Sign out": no token is read, not even Claude Code's, until the user signs back
    /// in. Without it the borrowed token would take over at the next reading and the sign-out
    /// would undo itself.
    /// </summary>
    public bool IsSignedOutByUser => _signedOut.IsSet;

    /// <summary>Current reading: from the cache, from the network, or the last known one.</summary>
    public async Task<AccountPayload> FetchAsync(CancellationToken cancel = default)
    {
        if (IsSignedOutByUser)
        {
            return AccountPayload.SignedOut;
        }
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var payload = await ReadAsync(cancel).ConfigureAwait(false);
            // A sign-out landing while a request was in flight wins over that request's answer.
            return IsSignedOutByUser ? AccountPayload.SignedOut : payload;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Immediate retry requested by the user: a click on "refresh" must attempt something, even in
    /// the middle of an hour-long backoff. False when the last one is under a minute old.
    /// </summary>
    public bool ResetBackoff()
    {
        var now = _now();
        lock (_lock)
        {
            if (_lastManualRetry is { } last && now - last < ManualRetrySpacing)
            {
                return false;
            }
            _lastManualRetry = now;
            _failureCount = 0;
            _nextAttempt = DateTimeOffset.MinValue;
            _skipsCache = true;
            return true;
        }
    }

    /// <summary>Stores the tokens obtained through Claudio's own sign-in in its own store.</summary>
    public void SignIn(OAuthCredentials credentials)
    {
        lock (_lock)
        {
            _signedOut.IsSet = false;
            StartSession(credentials);
            if (!_ownToken.Persist(credentials))
            {
                _log("signed in, but the token could not be stored");
            }
        }
    }

    /// <summary>
    /// Signing back in while Claude Code holds a live session: borrowing it again is enough, no
    /// browser needed. False when Claude Code has none, and the browser sign-in has to run.
    /// </summary>
    public bool ResumeWithClaudeCode()
    {
        if (_borrowedToken() is not { } borrowed || borrowed.IsExpired(_now()))
        {
            return false;
        }
        lock (_lock)
        {
            _signedOut.IsSet = false;
            StartSession(borrowed);
        }
        _log("signed back in: borrowing Claude Code's token");
        return true;
    }

    /// <summary>
    /// Erases Claudio's own token and stops borrowing Claude Code's until the user signs back in.
    /// Claude Code's stores are never touched: the CLI stays signed in.
    /// </summary>
    public void SignOut()
    {
        lock (_lock)
        {
            _signedOut.IsSet = true;
            _ownToken.Erase();
            StartSession(null);
        }
        _log("signed out: Claudio token removed, Claude Code's no longer read");
    }

    /// <summary>
    /// A new session keeps nothing of the previous one: no reading, no profile, since the account
    /// may differ, and no schedule. Work still in flight for the old one is dropped on arrival.
    /// Called under the lock.
    /// </summary>
    private void StartSession(OAuthCredentials? credentials)
    {
        _generation++;
        _credentials = credentials;
        _lastReading = null;
        _lastProfile = null;
        _profileFetchedAt = null;
        _ownTokenIsDead = false;
        _lastSuccess = null;
        _failureCount = 0;
        _nextAttempt = DateTimeOffset.MinValue;
    }

    private async Task<AccountPayload> ReadAsync(CancellationToken cancel)
    {
        var now = _now();
        int started;
        lock (_lock)
        {
            started = _generation;
            if (!_skipsCache && _lastSuccess is { } success && now - success < CacheTtl && _lastReading is not null)
            {
                return new AccountPayload(_lastReading, _lastProfile, IsSignedIn: true);
            }
            if (now < _nextAttempt)
            {
                return StalePayload(now);
            }
            _skipsCache = false;
        }

        await ResolveCredentialsAsync(started, cancel).ConfigureAwait(false);

        // An unreadable token must not wipe a valid reading: onboarding returns only if there
        // never was one.
        if (Current(started) is not { AccessToken.Length: > 0 } credentials)
        {
            return Stale(now);
        }

        var (body, status, retryAfter) = await GetAsync(UsageUrl, credentials.AccessToken, cancel).ConfigureAwait(false);
        if (Current(started) is null)
        {
            return Stale(now);
        }
        if (status == HttpStatusCode.Unauthorized
            && await RecoverFromUnauthorizedAsync(started, cancel).ConfigureAwait(false)
            && Current(started) is { } recovered)
        {
            credentials = recovered;
            (body, status, retryAfter) = await GetAsync(UsageUrl, credentials.AccessToken, cancel).ConfigureAwait(false);
        }

        var reading = status == HttpStatusCode.OK && body is not null ? UsageParser.Parse(body, now) : null;
        if (reading is null)
        {
            var reason = status == HttpStatusCode.OK ? "usage HTTP 200 without any quota" : $"usage HTTP {(int)status}";
            lock (_lock)
            {
                return started == _generation ? RecordFailure(reason, status, retryAfter, now) : StalePayload(now);
            }
        }

        bool profileIsStale;
        lock (_lock)
        {
            profileIsStale = _profileFetchedAt is not { } fetched || now - fetched > ProfileTtl;
        }
        OAuthProfile? profile = null;
        if (profileIsStale)
        {
            var (profileBody, profileStatus, _) = await GetAsync(ProfileUrl, credentials.AccessToken, cancel).ConfigureAwait(false);
            if (profileStatus == HttpStatusCode.OK && profileBody is not null)
            {
                profile = ParseProfile(profileBody, credentials.SubscriptionType);
            }
        }

        lock (_lock)
        {
            if (started != _generation)
            {
                return StalePayload(now);
            }
            if (profile is not null)
            {
                _lastProfile = profile;
                _profileFetchedAt = now;
            }
            _lastReading = reading;
            _lastSuccess = now;
            _failureCount = 0;
            _nextAttempt = DateTimeOffset.MinValue;
            return new AccountPayload(reading, _lastProfile, IsSignedIn: true);
        }
    }

    /// <summary>The token in use, or <c>null</c> once the session that started the work has ended.</summary>
    private OAuthCredentials? Current(int started)
    {
        lock (_lock)
        {
            return started == _generation ? _credentials : null;
        }
    }

    /// <summary>Takes a token for the session that asked for it, unless that session has ended.</summary>
    private bool Adopt(int started, OAuthCredentials credentials)
    {
        lock (_lock)
        {
            if (started != _generation)
            {
                return false;
            }
            _credentials = credentials;
            return true;
        }
    }

    private AccountPayload Stale(DateTimeOffset now)
    {
        lock (_lock)
        {
            return StalePayload(now);
        }
    }

    /// <summary>
    /// Picks the token to use, borrowed first. A still-valid borrowed token is kept as is: re-reading
    /// Claude Code's file every three minutes gains nothing. Expired, it is still tried last: only
    /// the server decides, and better that than announcing "signed out" on the strength of a local
    /// date.
    /// </summary>
    private async Task ResolveCredentialsAsync(int started, CancellationToken cancel)
    {
        var now = _now();
        bool ownTokenIsDead;
        lock (_lock)
        {
            if (_credentials is { IsBorrowed: true } current && !current.IsExpired(now))
            {
                return;
            }
            ownTokenIsDead = _ownTokenIsDead;
        }

        var borrowed = _borrowedToken();
        if (borrowed is not null && !borrowed.IsExpired(now))
        {
            Adopt(started, borrowed);
            return;
        }

        if (!ownTokenIsDead && _ownToken.Load() is { } own)
        {
            if (!Adopt(started, own) || !own.IsExpired(now)
                || await RefreshOwnTokenAsync(force: false, started, cancel).ConfigureAwait(false))
            {
                return;
            }
        }

        if (borrowed is not null)
        {
            Adopt(started, borrowed);
        }
    }

    /// <summary>
    /// Response to a 401. On a borrowed token there is nothing to refresh: Claude Code may have
    /// written a new one meanwhile, so a re-read is enough, and it is all Claudio allows itself.
    /// </summary>
    private async Task<bool> RecoverFromUnauthorizedAsync(int started, CancellationToken cancel)
    {
        if (Current(started) is not { } current)
        {
            return false;
        }
        if (current.IsBorrowed)
        {
            if (_borrowedToken() is not { } fresh || fresh.AccessToken == current.AccessToken)
            {
                _log("usage HTTP 401 on borrowed token: Claude Code must sign in again");
                return false;
            }
            return Adopt(started, fresh);
        }
        _log("usage HTTP 401: forcing refresh");
        return await RefreshOwnTokenAsync(force: true, started, cancel).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes <b>Claudio's own</b> token. The store is re-read first: a previous pass may have
    /// done it already, in which case spending the refresh token again would be both useless and
    /// destructive (rotation).
    /// </summary>
    private async Task<bool> RefreshOwnTokenAsync(bool force, int started, CancellationToken cancel)
    {
        if (_ownToken.Load() is { } stored)
        {
            lock (_lock)
            {
                if (started != _generation)
                {
                    return false;
                }
                var tokenChanged = stored.AccessToken != _credentials?.AccessToken;
                _credentials = stored;
                if (tokenChanged || (!force && !stored.IsExpired(_now())))
                {
                    return true;
                }
            }
        }

        var current = Current(started);
        if (current is null || current.IsBorrowed || string.IsNullOrEmpty(current.RefreshToken))
        {
            _log("refresh impossible: no refresh token");
            return false;
        }
        var (body, status) = await PostJsonAsync(_http, TokenUrl, new JsonObject
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = current.RefreshToken,
            ["client_id"] = ClientId,
            ["scope"] = string.Join(' ', current.Scopes ?? Scopes),
        }, TimeSpan.FromSeconds(15), cancel).ConfigureAwait(false);
        if (status == 0)
        {
            _log("refresh: network unavailable");
            return false;
        }

        // Held until the new token is stored: a sign-out cannot slip in between the check and the
        // write, and erase a store this would then fill again.
        lock (_lock)
        {
            if (started != _generation)
            {
                _log("refresh answer dropped: the session changed meanwhile");
                return false;
            }
            var answer = status == HttpStatusCode.OK ? OAuthJson.Object(body) : null;
            if (OAuthJson.Text(answer?["access_token"]) is not { Length: > 0 } accessToken)
            {
                if (IsInvalidGrant(status, body))
                {
                    _ownTokenIsDead = true;
                    _log("refresh invalid_grant: Claudio token dropped, borrowing from Claude Code");
                }
                else
                {
                    _log($"refresh HTTP {(int)status}");
                }
                return false;
            }

            var refreshToken = OAuthJson.Text(answer!["refresh_token"]) is { Length: > 0 } rotated ? rotated : current.RefreshToken;
            var expiresAt = _now().AddSeconds(OAuthJson.Number(answer["expires_in"]) ?? 3600);
            var root = current.Root.DeepClone().AsObject();
            if (root["claudeAiOauth"] is not JsonObject oauth)
            {
                oauth = [];
                root["claudeAiOauth"] = oauth;
            }
            oauth["accessToken"] = accessToken;
            oauth["refreshToken"] = refreshToken;
            oauth["expiresAt"] = expiresAt.ToUnixTimeMilliseconds();
            var updated = current with { AccessToken = accessToken, RefreshToken = refreshToken, ExpiresAt = expiresAt, Root = root };

            _log(_ownToken.Persist(updated) ? "refresh OK, store rewritten" : "refresh OK but persistence FAILED: check the Credential Manager");
            _credentials = updated;
            return true;
        }
    }

    /// <summary>
    /// <c>invalid_grant</c> means the refresh token was revoked or already rotated: it will not come
    /// back, so it is declared dead once and for all rather than retried in a loop.
    /// </summary>
    private static bool IsInvalidGrant(HttpStatusCode status, string? body)
    {
        if (status is not (HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) || OAuthJson.Object(body) is not { } root)
        {
            return false;
        }
        return root["error"] switch
        {
            JsonObject error => OAuthJson.Text(error["type"]) == "invalid_grant",
            var error => OAuthJson.Text(error) == "invalid_grant",
        };
    }

    /// <summary>
    /// The last reading, stripped of whatever stopped being true: a window past its reset has
    /// reopened at zero since, so its old percentage would be wrong rather than merely old. Called
    /// under the lock.
    /// </summary>
    private AccountPayload StalePayload(DateTimeOffset now)
    {
        if (_lastReading is not { } last || _lastSuccess is not { } success)
        {
            return new AccountPayload(null, _lastProfile, IsSignedIn: _credentials is not null);
        }
        QuotaWindow? StillValid(QuotaWindow? window) => window?.ResetsAt is { } reset && reset > now ? window : null;
        var stale = new QuotaReading
        {
            Session = StillValid(last.Session),
            Weekly = StillValid(last.Weekly),
            Scoped = StillValid(last.Scoped),
            Spend = last.Spend is { } spend && spend.ResetsAt > now ? spend : null,
            Source = QuotaSource.Stale(success),
        };
        return new AccountPayload(stale.IsEmpty ? null : stale, _lastProfile,
                                  IsSignedIn: _credentials is not null || _lastProfile is not null);
    }

    /// <summary>Spaces attempts by the nature of the fault. Called under the lock.</summary>
    private AccountPayload RecordFailure(string reason, HttpStatusCode status, TimeSpan? retryAfter, DateTimeOffset now)
    {
        _failureCount++;
        var transient = status == 0 || (int)status >= 500;
        var steps = transient ? TransientSteps : BackoffSteps;
        var step = steps[Math.Min(_failureCount - 1, steps.Length - 1)];
        var delay = retryAfter is { } wait && wait > step ? wait : step;
        _nextAttempt = now + delay;
        _log($"{reason}: failure #{_failureCount}, next attempt in {(int)delay.TotalSeconds}s");
        return StalePayload(now);
    }

    /// <summary>Authenticated GET. Without <c>anthropic-beta: oauth-2025-04-20</c> the API refuses OAuth tokens.</summary>
    private async Task<(string? Body, HttpStatusCode Status, TimeSpan? RetryAfter)> GetAsync(Uri url, string token, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.Add("anthropic-version", "2023-06-01");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return (body, response.StatusCode, response.Headers.RetryAfter?.Delta);
        }
        catch (HttpRequestException)
        {
            return (null, 0, null);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return (null, 0, null);
        }
    }

    /// <summary>A JSON POST to the token endpoint. Status 0: the network did not answer.</summary>
    internal static async Task<(string? Body, HttpStatusCode Status)> PostJsonAsync(HttpClient http, Uri url, JsonObject payload,
                                                                                     TimeSpan timeout, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            deadline.CancelAfter(timeout);
            using var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return (body, response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return (null, 0);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return (null, 0);
        }
    }

    /// <summary>The token's own subscription is the last candidate: the organisation's tier says more.</summary>
    public static OAuthProfile? ParseProfile(string json, string? subscription)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            static string? Text(JsonElement element, string name) =>
                element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            var name = Text(account, "full_name") ?? Text(account, "display_name") ?? string.Empty;
            if (name.Length == 0)
            {
                return null;
            }
            var organization = root.TryGetProperty("organization", out var org) ? org : default;
            var plan = PlanLabel.From([
                Text(organization, "rate_limit_tier"),
                Text(organization, "seat_tier"),
                Text(organization, "organization_type"),
                subscription,
            ]);
            return new OAuthProfile(name, Text(account, "email") ?? string.Empty, plan, Text(organization, "name") ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
