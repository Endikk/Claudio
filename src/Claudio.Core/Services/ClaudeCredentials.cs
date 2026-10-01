using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claudio.Core.Services;

/// <summary>Where a token comes from, and therefore what Claudio may do with it.</summary>
public enum CredentialSource
{
    /// <summary>
    /// Claudio's own store, the token its sign-in obtained: owned by the app, so it is refreshed,
    /// persisted and erased here.
    /// </summary>
    OwnStore,

    /// <summary>
    /// Claude Code's token, read but never written back: Claude Code renews it. No refresh token
    /// is retained for this source.
    /// </summary>
    ClaudeCode,
}

/// <summary>
/// An OAuth token together with the store it came from, so it can be written back to exactly the
/// same place. <see cref="Root"/> is the store's document, rewritten as is: only the
/// <c>claudeAiOauth</c> token fields change.
/// </summary>
public sealed record OAuthCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    JsonObject Root,
    CredentialSource Source)
{
    /// <summary>
    /// True when the token belongs to Claude Code: Claudio must neither refresh, persist, nor
    /// delete it.
    /// </summary>
    public bool IsBorrowed => Source == CredentialSource.ClaudeCode;

    /// <summary>
    /// Scopes actually attached to the token. They are sent back unchanged on refresh: a renewed
    /// token without <c>user:profile</c> stops granting access to the quotas.
    /// </summary>
    public IReadOnlyList<string>? Scopes
    {
        get
        {
            if (OAuth?["scopes"] is not JsonArray array)
            {
                return null;
            }
            var scopes = array.Select(OAuthJson.Text).OfType<string>().ToList();
            return scopes.Count > 0 ? scopes : null;
        }
    }

    /// <summary>"pro", "max", "enterprise": what the sign-in recorded.</summary>
    public string? SubscriptionType => OAuthJson.Text(OAuth?["subscriptionType"]);

    /// <summary>Two minutes of margin: a token expiring mid-request would earn an avoidable 401.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry < now.AddMinutes(2);

    private JsonObject? OAuth => Root.TryGetPropertyValue("claudeAiOauth", out var oauth) ? oauth as JsonObject : null;

    /// <summary>
    /// Reads a <c>{"claudeAiOauth": {...}}</c> document. Claude Code's refresh token is dropped,
    /// from the record and from its root alike: Claudio must never be able to spend it, since
    /// rotating it would sign Claude Code out. Nothing else of Claude Code's document is kept.
    /// </summary>
    public static OAuthCredentials? Parse(string json, CredentialSource source)
    {
        if (OAuthJson.Object(json) is not { } root
            || root["claudeAiOauth"] is not JsonObject oauth
            || OAuthJson.Text(oauth["accessToken"]) is not { Length: > 0 } accessToken)
        {
            return null;
        }
        DateTimeOffset? expiresAt = OAuthJson.Number(oauth["expiresAt"]) is { } milliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds)
            : null;

        if (source == CredentialSource.ClaudeCode)
        {
            var kept = oauth.DeepClone().AsObject();
            kept.Remove("refreshToken");
            return new OAuthCredentials(accessToken, null, expiresAt, new JsonObject { ["claudeAiOauth"] = kept }, source);
        }
        return new OAuthCredentials(accessToken, OAuthJson.Text(oauth["refreshToken"]), expiresAt, root, source);
    }

    /// <summary>The tokens stay out of logs and test output: only where they come from is printed.</summary>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Source = ").Append(Source).Append(", ExpiresAt = ").Append(ExpiresAt);
        return true;
    }
}

/// <summary>Lenient reads of the OAuth documents: a field of the wrong kind counts as absent.</summary>
internal static class OAuthJson
{
    public static JsonObject? Object(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue<string>(out var text)
            ? text
            : null;

    public static double? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<double>(out var number)
            ? number
            : null;
}

/// <summary>
/// Claudio's own token store, as the account client uses it: on Windows, the Credential Manager
/// item its sign-in creates. Injected so tests never reach the real one.
/// </summary>
public sealed class OwnTokenStore(Func<OAuthCredentials?> load, Func<OAuthCredentials, bool> persist, Action erase)
{
    /// <summary>No token of Claudio's own, and nothing written or erased.</summary>
    public static OwnTokenStore None { get; } = new(() => null, _ => true, () => { });

    public OAuthCredentials? Load() => load();

    /// <summary>Writes the token back. Rewriting Claude Code's would steal its session, so it is refused outright.</summary>
    public bool Persist(OAuthCredentials credentials) => !credentials.IsBorrowed && persist(credentials);

    /// <summary>Sign-out: removes Claudio's item. Claude Code's stores are never touched.</summary>
    public void Erase() => erase();
}

/// <summary>
/// Set by "Sign out", cleared by signing back in. Persisted by the app, so a relaunch keeps
/// Claudio signed out whatever Claude Code does meanwhile.
/// </summary>
public sealed class SignOutFlag(Func<bool> read, Action<bool> write)
{
    public bool IsSet
    {
        get => read();
        set => write(value);
    }

    /// <summary>A flag kept in memory only, for tests.</summary>
    public static SignOutFlag InMemory(bool isSet = false)
    {
        var state = isSet;
        return new SignOutFlag(() => state, value => state = value);
    }
}
