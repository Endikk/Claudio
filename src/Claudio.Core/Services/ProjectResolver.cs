namespace Claudio.Core.Services;

/// <summary>
/// Maps a working directory to the project it belongs to, as Claudy's <c>ProjectResolver</c> does.
/// Claude Code records the directory the model was <i>in</i>, which moves with every <c>cd</c>: one
/// session in a monorepo shows up as <c>App</c>, <c>bridge</c>, <c>src</c>… The project is the
/// repository instead: the nearest ancestor holding <c>.git</c>, with worktrees folded back into
/// their main checkout.
/// </summary>
public sealed class ProjectResolver
{
    /// <summary>
    /// Stops the upward walk: a dotfiles repository at the home root would otherwise swallow every
    /// folder that is not itself a repository.
    /// </summary>
    private readonly string _home;
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);

    public ProjectResolver(string home) => _home = Normalize(home);

    /// <summary>
    /// Repository root holding <paramref name="cwd"/>, or null outside any repository: a scratch
    /// directory, a deleted temporary folder, a plain folder never put under Git, or a path this
    /// machine cannot reach (a WSL <c>/home/…</c> seen from Windows).
    /// </summary>
    public string? RepositoryRoot(string cwd)
    {
        if (_cache.TryGetValue(cwd, out var cached))
        {
            return cached;
        }
        string? found = null;
        if (cwd.Length > 0 && Path.IsPathFullyQualified(cwd))
        {
            var path = Normalize(cwd);
            while (!IsCeiling(path))
            {
                var marker = Path.Combine(path, ".git");
                if (Directory.Exists(marker))
                {
                    found = path;
                    break;
                }
                if (File.Exists(marker))
                {
                    found = MainCheckout(marker) ?? path;
                    break;
                }
                path = Path.GetDirectoryName(path) ?? string.Empty;
            }
        }
        _cache[cwd] = found;
        return found;
    }

    private bool IsCeiling(string path) =>
        path.Length == 0 || Path.GetPathRoot(path) == path || string.Equals(path, _home, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A linked worktree's <c>.git</c> is a file reading <c>gitdir: &lt;main&gt;/.git/worktrees/&lt;name&gt;</c>.
    /// Its work belongs to the main project. A submodule's points into <c>.git/modules/</c> instead
    /// and stays a project of its own.
    /// </summary>
    private static string? MainCheckout(string marker)
    {
        string contents;
        try
        {
            contents = File.ReadAllText(marker);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        var line = contents.Split('\n').FirstOrDefault(text => text.StartsWith("gitdir:", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }
        // Git for Windows writes forward slashes: "gitdir: G:/work/app/.git/worktrees/feature".
        var gitdir = line["gitdir:".Length..].Trim().Replace('\\', '/');
        var index = gitdir.IndexOf("/.git/worktrees/", StringComparison.Ordinal);
        if (index <= 0)
        {
            return null;
        }
        var root = gitdir[..index].Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathFullyQualified(root) ? Normalize(root) : null;
    }

    /// <summary>
    /// One spelling per folder: Claude Code records <c>g:\work</c> and <c>G:\work</c> alike, and
    /// Windows takes them for the same place.
    /// </summary>
    public static string Normalize(string path)
    {
        if (path.Length == 0)
        {
            return path;
        }
        var full = Path.GetFullPath(path);
        if (full.Length >= 2 && full[1] == ':' && char.IsAsciiLetterLower(full[0]))
        {
            full = char.ToUpperInvariant(full[0]) + full[1..];
        }
        return Path.GetPathRoot(full) == full ? full : Path.TrimEndingDirectorySeparator(full);
    }
}
