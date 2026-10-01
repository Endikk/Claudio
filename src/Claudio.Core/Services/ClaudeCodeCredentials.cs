namespace Claudio.Core.Services;

/// <summary>
/// <b>Read-only</b> access to Claude Code's token, the most dependable source there is: Claude
/// Code renews it itself. Its refresh token is dropped on reading, so Claudio can never rotate it
/// and sign Claude Code out, and its file is never written.
/// </summary>
public static class ClaudeCodeCredentials
{
    /// <summary>Claude Code's current token, or <c>null</c> when the tool is absent or signed out.</summary>
    public static OAuthCredentials? Load(ClaudeHome home)
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

    public static OAuthCredentials? Parse(string json) => OAuthCredentials.Parse(json, CredentialSource.ClaudeCode);
}
