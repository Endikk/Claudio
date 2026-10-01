namespace Claudio.Core.Models;

/// <summary>Account identity as Anthropic's OAuth API reports it.</summary>
public sealed record OAuthProfile(string Name, string Email, string Plan, string Organization);

/// <summary>What one reading of the account yields.</summary>
public sealed record AccountPayload(QuotaReading? Reading, OAuthProfile? Profile, bool IsSignedIn);
