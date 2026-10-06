using Claudio.Core.Services;

namespace Claudio.App;

/// <summary>
/// The widget's preferences, as Claudy keeps them in <c>UserDefaults</c>: a small JSON file at
/// <c>%LOCALAPPDATA%\Claudio\settings.json</c>. A missing or damaged file reads as the defaults, and is never overwritten whole: see <see cref="SettingsStore"/>.
/// </summary>
internal static class Preferences
{
    private static readonly SettingsStore Store = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claudio", "settings.json"),
        DiagnosticLog.Append);

    public static bool IsMinimal
    {
        get => Store.Bool("isMinimal", false);
        set => Store.Set("isMinimal", value);
    }

    public static bool IsAlwaysOnTop
    {
        get => Store.Bool("alwaysOnTop", true);
        set => Store.Set("alwaysOnTop", value);
    }

    public static bool IsDetailsExpanded
    {
        get => Store.Bool("detailsExpanded", false);
        set => Store.Set("detailsExpanded", value);
    }

    public static string? Placement
    {
        get => Store.Text("placement");
        set => Store.Set("placement", value);
    }

    /// <summary>Set by "Sign out", cleared by signing back in: a relaunch keeps Claudio signed out whatever Claude Code does.</summary>
    public static bool IsSignedOut
    {
        get => Store.Bool("signedOut", false);
        set => Store.Set("signedOut", value);
    }

    /// <summary>The version whose bubble was answered: it does not come back for that one.</summary>
    public static string? AnnouncedUpdate
    {
        get => Store.Text("update.announced");
        set => Store.Set("update.announced", value);
    }

    /// <summary>
    /// Claude Code's folder when it is not where Claude Code puts it by default, as Claudy's
    /// <c>claudy.configDir</c>: it wins over <c>CLAUDE_CONFIG_DIR</c>, and isolates completely.
    /// </summary>
    public static string? ConfigDirectory => Store.Text("configDir");
}
