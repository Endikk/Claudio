using System.Text.Json.Nodes;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class CredentialsTests
{
    [Fact]
    public void ReadsClaudeCodesTokenAndDropsItsRefreshToken()
    {
        var token = ClaudeCodeCredentials.Parse("""
            {"claudeAiOauth": {"accessToken": "sk-ant-oat01-x", "refreshToken": "never-read",
                               "expiresAt": 1790157600000, "subscriptionType": "max",
                               "scopes": ["user:inference", "user:profile"]},
             "mcpOAuth": {"server": {"accessToken": "not-ours"}}}
            """);

        Assert.NotNull(token);
        Assert.Equal("sk-ant-oat01-x", token.AccessToken);
        Assert.Null(token.RefreshToken);
        Assert.Equal(CredentialSource.ClaudeCode, token.Source);
        Assert.True(token.IsBorrowed);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790157600000), token.ExpiresAt);
        Assert.Equal("max", token.SubscriptionType);
        Assert.Equal(["user:inference", "user:profile"], token.Scopes);
        // Kept nowhere: neither in the record nor in the document it carries.
        Assert.DoesNotContain("never-read", token.Root.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-ours", token.Root.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudiosOwnStoreKeepsItsRefreshTokenAndItsDocument()
    {
        var token = OAuthCredentials.Parse("""
            {"claudeAiOauth": {"accessToken": "own", "refreshToken": "refresh", "expiresAt": 1790157600000}}
            """, CredentialSource.OwnStore);

        Assert.NotNull(token);
        Assert.Equal("refresh", token.RefreshToken);
        Assert.False(token.IsBorrowed);
        Assert.Null(token.Scopes);
        Assert.Null(token.SubscriptionType);
        Assert.Equal("refresh", token.Root["claudeAiOauth"]!["refreshToken"]!.GetValue<string>());
    }

    [Fact]
    public void TokensNeverShowInTheirTextForm()
    {
        var token = Tokens.Own("sk-ant-oat01-secret", null, "sk-ant-ort01-secret");

        Assert.DoesNotContain("secret", token.ToString(), StringComparison.Ordinal);
        Assert.Contains("OwnStore", token.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"claudeAiOauth": "x"}""")]
    [InlineData("""{"claudeAiOauth": {"accessToken": ""}}""")]
    [InlineData("""{"claudeAiOauth": {"accessToken": 42}}""")]
    [InlineData("not json")]
    [InlineData("")]
    public void NoTokenIsNull(string json) => Assert.Null(ClaudeCodeCredentials.Parse(json));

    [Fact]
    public void ExpiryHasTwoMinutesOfMargin()
    {
        var now = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.True(Tokens.Borrowed("t", now.AddMinutes(1)).IsExpired(now));
        Assert.False(Tokens.Borrowed("t", now.AddMinutes(3)).IsExpired(now));
        Assert.False(Tokens.Borrowed("t").IsExpired(now));
    }

    [Fact]
    public void ABorrowedTokenIsNeverPersisted()
    {
        var store = new FakeTokenStore();

        Assert.False(store.Store.Persist(Tokens.Borrowed("claude-code")));
        Assert.True(store.Store.Persist(Tokens.Own("own", null)));
        Assert.Equal(["own"], store.Persisted.Select(token => token.AccessToken));
    }

    [Fact]
    public void TheSignOutFlagReadsWhatWasWritten()
    {
        var persisted = false;
        var flag = new SignOutFlag(() => persisted, value => persisted = value);

        flag.IsSet = true;

        Assert.True(persisted);
        Assert.True(flag.IsSet);
        Assert.False(SignOutFlag.InMemory().IsSet);
    }

    [Fact]
    public void ScopesOfTheWrongKindCountAsAbsent()
    {
        var token = Tokens.Own("own", null) with
        {
            Root = new JsonObject { ["claudeAiOauth"] = new JsonObject { ["scopes"] = new JsonArray() } },
        };

        Assert.Null(token.Scopes);
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
