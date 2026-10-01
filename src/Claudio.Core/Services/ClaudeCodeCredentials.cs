using System.Text.Json;

namespace Claudio.Core.Services;

/// <summary>An OAuth access token and when it expires.</summary>
public sealed record OAuthToken(string AccessToken, DateTimeOffset? ExpiresAt, string? SubscriptionType)
{
    /// <summary>Two minutes of margin: a token expiring mid-request would earn an avoidable 401.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry < now.AddMinutes(2);
}

/// <summary>
/// <b>Read-only</b> access to Claude Code's token, the most dependable source there is: Claude
/// Code renews it itself. Its refresh token is never read, so Claudio can never rotate it and
/// sign Claude Code out.
/// </summary>
public static class ClaudeCodeCredentials
{
    public static OAuthToken? Load(ClaudeHome home)
    {
        try
        {
            return File.Exists(home.CredentialsFile) ? Parse(File.ReadAllText(home.CredentialsFile)) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static OAuthToken? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || !oauth.TryGetProperty("accessToken", out var token)
                || token.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(token.GetString()))
            {
                return null;
            }
            DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out var expiry) && expiry.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)expiry.GetDouble())
                : null;
            var subscription = oauth.TryGetProperty("subscriptionType", out var type) && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
            return new OAuthToken(token.GetString()!, expiresAt, subscription);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
