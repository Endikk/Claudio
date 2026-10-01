namespace Claudio.Core.Services;

/// <summary>
/// Where Claude Code keeps its data for the current user. On Windows: <c>%USERPROFILE%\.claude</c>
/// and <c>%USERPROFILE%\.claude.json</c>, or the folder <c>CLAUDE_CONFIG_DIR</c> names. A Windows
/// app inherits the user's environment variables, so the redirect applies however Claudio starts.
/// </summary>
public sealed class ClaudeHome
{
    public ClaudeHome(string home, string? customConfigDirectory = null)
    {
        Home = home;
        CustomConfigDirectory = string.IsNullOrWhiteSpace(customConfigDirectory) ? null : customConfigDirectory;
    }

    public static ClaudeHome Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"));

    public string Home { get; }

    /// <summary>Set when <c>CLAUDE_CONFIG_DIR</c> redirects the configuration.</summary>
    public string? CustomConfigDirectory { get; }

    public string ConfigDirectory => CustomConfigDirectory ?? Path.Combine(Home, ".claude");

    /// <summary>Claude Code's token, which it writes here on Windows rather than in a vault.</summary>
    public string CredentialsFile => Path.Combine(ConfigDirectory, ".credentials.json");

    /// <summary>
    /// <c>.claude.json</c> sits in a custom directory when there is one, at the home root
    /// otherwise. Neither falls back to the other: a redirect isolates completely.
    /// </summary>
    public string ConfigFile => CustomConfigDirectory is null
        ? Path.Combine(Home, ".claude.json")
        : Path.Combine(CustomConfigDirectory, ".claude.json");

    public string ProjectsDirectory => Path.Combine(ConfigDirectory, "projects");

    /// <summary>True as soon as any trace of Claude Code exists; without one, demo mode.</summary>
    public bool IsInstalled => File.Exists(ConfigFile) || Directory.Exists(ProjectsDirectory);
}
