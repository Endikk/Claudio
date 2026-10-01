using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claudio.App;

/// <summary>
/// The widget's preferences, as Claudy keeps them in <c>UserDefaults</c>: a small JSON file at
/// <c>%LOCALAPPDATA%\Claudio\settings.json</c>. A missing or damaged file reads as the defaults.
/// </summary>
internal static class Preferences
{
    private static readonly string File = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claudio", "settings.json");

    private static readonly JsonObject Values = Load();

    public static bool IsMinimal
    {
        get => Bool("isMinimal", false);
        set => Set("isMinimal", value);
    }

    public static bool IsAlwaysOnTop
    {
        get => Bool("alwaysOnTop", true);
        set => Set("alwaysOnTop", value);
    }

    public static bool IsDetailsExpanded
    {
        get => Bool("detailsExpanded", false);
        set => Set("detailsExpanded", value);
    }

    public static string? Placement
    {
        get => Values["placement"]?.GetValue<string>();
        set => Set("placement", value);
    }

    /// <summary>Set by "Sign out", cleared by signing back in: a relaunch keeps Claudio signed out whatever Claude Code does.</summary>
    public static bool IsSignedOut
    {
        get => Bool("signedOut", false);
        set => Set("signedOut", value);
    }

    /// <summary>The version whose bubble was answered: it does not come back for that one.</summary>
    public static string? AnnouncedUpdate
    {
        get => Values["update.announced"]?.GetValue<string>();
        set => Set("update.announced", value);
    }

    /// <summary>
    /// Claude Code's folder when it is not where Claude Code puts it by default, as Claudy's
    /// <c>claudy.configDir</c>: it wins over <c>CLAUDE_CONFIG_DIR</c>, and isolates completely.
    /// </summary>
    public static string? ConfigDirectory => Values["configDir"]?.GetValue<string>();

    private static bool Bool(string key, bool fallback) =>
        Values[key] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : fallback;

    private static void Set<T>(string key, T value)
    {
        Values[key] = value is null ? null : JsonValue.Create(value);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(File)!);
            System.IO.File.WriteAllText(File, Values.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static JsonObject Load()
    {
        try
        {
            return System.IO.File.Exists(File) && JsonNode.Parse(System.IO.File.ReadAllText(File)) is JsonObject values ? values : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
