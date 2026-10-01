using System.Net;
using System.Text.Json;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Reads the account's quotas from Anthropic's OAuth endpoints, the ones behind claude.ai ▸ Usage
/// and Claude Code's <c>/usage</c>. A port of Claudy's <c>ClaudeAccountClient</c>: percentages come
/// from here alone, never from an estimate. A failure resets nothing: the last reading is served
/// again, marked stale, and attempts space out by the nature of the fault.
/// </summary>
public sealed class ClaudeAccountClient : IDisposable
{
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

    private readonly HttpClient _http;
    private readonly Func<OAuthToken?> _borrowedToken;
    private readonly Func<DateTimeOffset> _now;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private OAuthToken? _token;
    private QuotaReading? _lastReading;
    private OAuthProfile? _lastProfile;
    private DateTimeOffset? _profileFetchedAt;
    private DateTimeOffset? _lastSuccess;
    private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
    private int _failureCount;
    private bool _skipsCache;

    public ClaudeAccountClient(HttpClient http, Func<OAuthToken?> borrowedToken,
                               Func<DateTimeOffset>? now = null, Action<string>? log = null)
    {
        _http = http;
        _borrowedToken = borrowedToken;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? (_ => { });
    }

    /// <summary>Current reading: from the cache, from the network, or the last known one.</summary>
    public async Task<AccountPayload> FetchAsync(CancellationToken cancel = default)
    {
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancel).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>A refresh the user asked for attempts something, even mid-backoff.</summary>
    public void ResetBackoff()
    {
        _failureCount = 0;
        _nextAttempt = DateTimeOffset.MinValue;
        _skipsCache = true;
    }

    private async Task<AccountPayload> ReadAsync(CancellationToken cancel)
    {
        var now = _now();
        if (!_skipsCache && _lastSuccess is { } success && now - success < CacheTtl && _lastReading is not null)
        {
            return new AccountPayload(_lastReading, _lastProfile, IsSignedIn: true);
        }
        if (now < _nextAttempt)
        {
            return StalePayload(now);
        }
        _skipsCache = false;

        // A valid borrowed token is kept: re-reading the file every three minutes gains nothing.
        if (_token is null || _token.IsExpired(now))
        {
            _token = _borrowedToken() ?? _token;
        }
        if (_token is null)
        {
            return StalePayload(now);
        }

        var (body, status, retryAfter) = await GetAsync(UsageUrl, _token.AccessToken, cancel).ConfigureAwait(false);
        if (status == HttpStatusCode.Unauthorized && _borrowedToken() is { } fresh && fresh.AccessToken != _token.AccessToken)
        {
            // Claude Code may have just written a new token: a re-read is all Claudio allows itself.
            _token = fresh;
            (body, status, retryAfter) = await GetAsync(UsageUrl, _token.AccessToken, cancel).ConfigureAwait(false);
        }

        var reading = status == HttpStatusCode.OK && body is not null ? UsageParser.Parse(body, now) : null;
        if (reading is null)
        {
            var reason = status == HttpStatusCode.OK ? "usage HTTP 200 without any quota" : $"usage HTTP {(int)status}";
            return RecordFailure(reason, status, retryAfter, now);
        }

        if (_profileFetchedAt is not { } fetched || now - fetched > ProfileTtl)
        {
            var (profileBody, profileStatus, _) = await GetAsync(ProfileUrl, _token.AccessToken, cancel).ConfigureAwait(false);
            if (profileStatus == HttpStatusCode.OK && profileBody is not null
                && ParseProfile(profileBody, _token.SubscriptionType) is { } profile)
            {
                _lastProfile = profile;
                _profileFetchedAt = now;
            }
        }

        _lastReading = reading;
        _lastSuccess = now;
        _failureCount = 0;
        _nextAttempt = DateTimeOffset.MinValue;
        return new AccountPayload(reading, _lastProfile, IsSignedIn: true);
    }

    /// <summary>
    /// The last reading, stripped of whatever stopped being true: a window past its reset has
    /// reopened at zero since, so its old percentage would be wrong rather than merely old.
    /// </summary>
    private AccountPayload StalePayload(DateTimeOffset now)
    {
        if (_lastReading is not { } last || _lastSuccess is not { } success)
        {
            return new AccountPayload(null, _lastProfile, IsSignedIn: _token is not null);
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
                                  IsSignedIn: _token is not null || _lastProfile is not null);
    }

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
