using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class CredentialsTests
{
    [Fact]
    public void ReadsClaudeCodesTokenAndDropsItsRefreshToken()
    {
        var token = ClaudeCodeCredentials.Parse("""
            {"claudeAiOauth": {"accessToken": "sk-ant-oat01-x", "refreshToken": "never-read",
                               "expiresAt": 1790157600000, "subscriptionType": "max"}}
            """);

        Assert.NotNull(token);
        Assert.Equal("sk-ant-oat01-x", token.AccessToken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790157600000), token.ExpiresAt);
        Assert.Equal("max", token.SubscriptionType);
        Assert.DoesNotContain("never-read", token.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"claudeAiOauth": {"accessToken": ""}}""")]
    [InlineData("not json")]
    public void NoTokenIsNull(string json) => Assert.Null(ClaudeCodeCredentials.Parse(json));

    [Fact]
    public void ExpiryHasTwoMinutesOfMargin()
    {
        var now = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.True(new OAuthToken("t", now.AddMinutes(1), null).IsExpired(now));
        Assert.False(new OAuthToken("t", now.AddMinutes(3), null).IsExpired(now));
        Assert.False(new OAuthToken("t", null, null).IsExpired(now));
    }

    [Fact]
    public void ARedirectIsolatesTheConfiguration()
    {
        var plain = new ClaudeHome(Path.Combine("C:", "Users", "ada"));
        var custom = new ClaudeHome(Path.Combine("C:", "Users", "ada"), Path.Combine("D:", "claude-work"));

        Assert.Equal(Path.Combine("C:", "Users", "ada", ".claude", ".credentials.json"), plain.CredentialsFile);
        Assert.Equal(Path.Combine("C:", "Users", "ada", ".claude.json"), plain.ConfigFile);
        Assert.Equal(Path.Combine("D:", "claude-work", ".credentials.json"), custom.CredentialsFile);
        Assert.Equal(Path.Combine("D:", "claude-work", ".claude.json"), custom.ConfigFile);
    }
}
