using System.Diagnostics;
using System.Text;

namespace Claudio.App;

/// <summary>
/// Claude Code run inside WSL keeps its transcripts in the distribution's own file system. Only
/// running distributions are read: opening <c>\\wsl.localhost</c> would otherwise start a stopped
/// one, a whole virtual machine, for nothing. One whose Claude Code is in use is running anyway.
/// </summary>
internal static class WslSources
{
    public static IReadOnlyList<string> ProjectsDirectories()
    {
        var folders = new List<string>();
        foreach (var distribution in RunningDistributions())
        {
            var root = $@"\\wsl.localhost\{distribution}";
            try
            {
                var homes = Directory.Exists($@"{root}\home") ? Directory.GetDirectories($@"{root}\home") : [];
                foreach (var home in homes.Append($@"{root}\root"))
                {
                    var projects = Path.Combine(home, ".claude", "projects");
                    if (Directory.Exists(projects))
                    {
                        folders.Add(projects);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return folders;
    }

    /// <summary><c>wsl.exe --list --running --quiet</c>, which answers in UTF-16.</summary>
    private static List<string> RunningDistributions()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("wsl.exe", "--list --running --quiet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Unicode,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return [];
            }
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000) || process.ExitCode != 0)
            {
                return [];
            }
            return output.Result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(name => name.Length > 0 && !name.Contains('\0', StringComparison.Ordinal))
                                .ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }
}
