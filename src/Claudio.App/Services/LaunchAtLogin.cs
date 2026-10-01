using Microsoft.Win32;

namespace Claudio.App;

/// <summary>
/// Launch at sign-in, Claudy's <c>LaunchAtLogin</c>: a value under the user's own <c>Run</c> key, no
/// admin rights needed. It names the launcher installers keep at a fixed place (Velopack's and
/// Scoop's <c>Claudio.exe</c> above <c>current\</c>), so an update never breaks it.
/// </summary>
internal static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Claudio";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    /// <summary>Returns the state actually reached, which may differ from the one requested.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(Name, $"\"{Launcher()}\"");
            }
            else
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
        return IsEnabled;
    }

    /// <summary>The stable launcher when Claudio was installed, the running executable otherwise.</summary>
    private static string Launcher()
    {
        var running = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Claudio.exe");
        var folder = Path.GetDirectoryName(running);
        if (folder is not null && string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase))
        {
            var stub = Path.Combine(Path.GetDirectoryName(folder)!, "Claudio.exe");
            if (File.Exists(stub))
            {
                return stub;
            }
        }
        return running;
    }
}
