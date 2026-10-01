using System.Text.Json.Nodes;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>Claudio's own store, in memory: a test never reaches the Credential Manager.</summary>
internal sealed class FakeTokenStore
{
    public FakeTokenStore(OAuthCredentials? stored = null)
    {
        Stored = stored;
        Store = new OwnTokenStore(() => Stored, Write, () =>
        {
            Stored = null;
            Erasures++;
        });
    }

    public OAuthCredentials? Stored { get; set; }
    public List<OAuthCredentials> Persisted { get; } = [];
    public int Erasures { get; private set; }
    public OwnTokenStore Store { get; }

    private bool Write(OAuthCredentials credentials)
    {
        Stored = credentials;
        Persisted.Add(credentials);
        return true;
    }
}

/// <summary>Tokens as each source yields them.</summary>
internal static class Tokens
{
    /// <summary>Claude Code's, as <see cref="ClaudeCodeCredentials"/> reads it: no refresh token.</summary>
    public static OAuthCredentials Borrowed(string value, DateTimeOffset? expiresAt = null, string? subscription = null)
    {
        var oauth = new JsonObject { ["accessToken"] = value };
        if (subscription is not null)
        {
            oauth["subscriptionType"] = subscription;
        }
        return new(value, null, expiresAt, new JsonObject { ["claudeAiOauth"] = oauth }, CredentialSource.ClaudeCode);
    }

    /// <summary>Claudio's own, as its sign-in stores it.</summary>
    public static OAuthCredentials Own(string value, DateTimeOffset? expiresAt, string? refreshToken = "refresh", params string[] scopes)
    {
        var oauth = new JsonObject { ["accessToken"] = value };
        if (refreshToken is not null)
        {
            oauth["refreshToken"] = refreshToken;
        }
        if (expiresAt is { } expiry)
        {
            oauth["expiresAt"] = expiry.ToUnixTimeMilliseconds();
        }
        if (scopes.Length > 0)
        {
            oauth["scopes"] = new JsonArray([.. scopes.Select(scope => (JsonNode?)scope)]);
        }
        return new(value, refreshToken, expiresAt, new JsonObject { ["claudeAiOauth"] = oauth }, CredentialSource.OwnStore);
    }
}
