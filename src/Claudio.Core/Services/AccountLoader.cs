using System.Text.Json;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Reads the signed-in account from <c>.claude.json</c> (its <c>oauthAccount</c> block), as Claudy's
/// <c>AccountLoader</c> does. Nothing is hardcoded: on an unknown machine the card shows <i>that</i>
/// machine's account, falling back to the Windows user name.
/// </summary>
public sealed class AccountLoader(ClaudeHome home, Func<string>? systemName = null)
{
    private readonly Func<string> _systemName = systemName ?? (() => Environment.UserName);

    /// <summary><c>.claude.json</c> routinely reaches several megabytes: re-read only when it changes.</summary>
    private (DateTime Modified, Account Account)? _cache;

    public Account Load()
    {
        DateTime modified;
        try
        {
            if (!File.Exists(home.ConfigFile))
            {
                return Fallback();
            }
            modified = File.GetLastWriteTimeUtc(home.ConfigFile);
            if (_cache is { } cache && cache.Modified == modified)
            {
                return cache.Account;
            }
            var account = Parse(File.ReadAllText(home.ConfigFile), _systemName());
            if (account is null)
            {
                return Fallback();
            }
            _cache = (modified, account);
            return account;
        }
        catch (IOException)
        {
            return Fallback();
        }
        catch (UnauthorizedAccessException)
        {
            return Fallback();
        }
    }

    public Account Fallback() => new(_systemName(), string.Empty, string.Empty, string.Empty, false);

    public static Account? Parse(string json, string systemName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("oauthAccount", out var oauth) || oauth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            string Text(string name) =>
                oauth.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()!.Trim()
                    : string.Empty;

            var name = Text("displayName");
            return new Account(
                name.Length == 0 ? systemName : name,
                Text("emailAddress"),
                PlanLabel.From([Text("organizationRateLimitTier"), Text("userRateLimitTier"), Text("seatTier"), Text("organizationType")]),
                Text("organizationName"),
                Text("organizationRole").Contains("admin", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
