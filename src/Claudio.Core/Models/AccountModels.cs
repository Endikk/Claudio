namespace Claudio.Core.Models;

/// <summary>Account identity as Anthropic's OAuth API reports it.</summary>
public sealed record OAuthProfile(string Name, string Email, string Plan, string Organization);

/// <summary>
/// What one reading of the account yields. <see cref="IsSignedIn"/> is true when a token is
/// available, borrowed or Claudio's own. <see cref="IsSignedOutByUser"/> means the user signed
/// Claudio out: nothing is to be shown, not even what Claude Code itself still holds.
/// </summary>
public sealed record AccountPayload(QuotaReading? Reading, OAuthProfile? Profile, bool IsSignedIn, bool IsSignedOutByUser = false)
{
    public static AccountPayload SignedOut { get; } = new(null, null, IsSignedIn: false, IsSignedOutByUser: true);
}
