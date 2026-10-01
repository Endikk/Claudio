namespace Claudio.Core.Services;

/// <summary>
/// One process of the snapshot. <see cref="StartedAt"/> turns a reusable PID into a stable
/// identity; it is <see cref="DateTimeOffset.MinValue"/> for the few processes the system will
/// not describe, and such a time is never compared.
/// </summary>
public sealed record RunningProcess(int Pid, int Parent, DateTimeOffset StartedAt, string Arguments);

/// <summary>
/// A snapshot of the process tree, as Claudy's <c>ProcessTable</c>.
///
/// Attribution does not depend on this table: the environment carries that. The tree serves one
/// purpose, knowing which Claude session is still alive, so the reaper never kills it. Windows
/// keeps a dead parent's PID in its children and hands that PID out again, so a "parent" that
/// started after its child is a stranger, and the walk stops there.
/// </summary>
public sealed class ProcessTable(IReadOnlyDictionary<int, RunningProcess> processes)
{
    private const int MaximumDepth = 64;

    /// <summary>The Idle process and System (0 and 4) parent everything and are no one's session.</summary>
    private const int LastSystemPid = 4;

    private static readonly string[] ScriptHosts = ["node", "node.exe", "bun", "bun.exe"];

    public IReadOnlyDictionary<int, RunningProcess> Processes { get; } = processes;

    /// <summary>
    /// Ancestors from the closest parent upward. Depth-capped and cycle-safe: a malformed table
    /// must never spin the scan.
    /// </summary>
    public IReadOnlyList<RunningProcess> Ancestors(int pid)
    {
        var found = new List<RunningProcess>();
        if (!Processes.TryGetValue(pid, out var child))
        {
            return found;
        }
        var seen = new HashSet<int> { pid };
        while (child.Parent > LastSystemPid && found.Count < MaximumDepth && seen.Add(child.Parent)
               && Processes.TryGetValue(child.Parent, out var parent) && IsParent(parent, child))
        {
            found.Add(parent);
            child = parent;
        }
        return found;
    }

    /// <summary>The processes this one started and that are still running, by the same rule as <see cref="Ancestors"/>.</summary>
    public IReadOnlyList<RunningProcess> Children(int pid)
    {
        if (pid <= LastSystemPid || !Processes.TryGetValue(pid, out var parent))
        {
            return [];
        }
        return Processes.Values.Where(process => process.Parent == pid && process.Pid != pid && IsParent(parent, process))
                               .OrderBy(process => process.Pid)
                               .ToList();
    }

    /// <summary>The live Claude session a process belongs to, when there is one.</summary>
    public RunningProcess? ClaudeSessionRoot(int pid) =>
        Ancestors(pid).FirstOrDefault(process => IsClaudeBinary(process.Arguments));

    /// <summary>
    /// Claude Code as Windows runs it: the native <c>claude.exe</c> (installer, VS Code extension
    /// under <c>anthropic.claude-code-*</c>), or Node or Bun running the npm package's
    /// <c>@anthropic-ai\claude-code\cli.js</c>. Only the program is looked at, and for a script
    /// host the script it runs: a shell whose command line merely says "claude" is not one.
    /// Claude Desktop's own <c>claude.exe</c> matches as well, which errs on the safe side: it is
    /// never killed, and what it launched reads as live.
    /// </summary>
    public static bool IsClaudeBinary(string arguments)
    {
        var tokens = Tokens(arguments);
        if (tokens.Count == 0)
        {
            return false;
        }
        if (IsClaudePath(tokens[0]))
        {
            return true;
        }
        if (!ScriptHosts.Contains(FileName(tokens[0]), StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        var script = tokens.Skip(1).FirstOrDefault(token => !token.StartsWith('-'));
        return script is not null && IsClaudePath(script);
    }

    private static bool IsParent(RunningProcess parent, RunningProcess child) =>
        parent.StartedAt == DateTimeOffset.MinValue || child.StartedAt == DateTimeOffset.MinValue
        || parent.StartedAt <= child.StartedAt;

    private static bool IsClaudePath(string path)
    {
        var name = FileName(path);
        return name.Equals("claude", StringComparison.OrdinalIgnoreCase)
            || name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase)
            || path.Contains("anthropic.claude-code", StringComparison.OrdinalIgnoreCase)
            || path.Replace('\\', '/').Contains("@anthropic-ai/claude-code", StringComparison.OrdinalIgnoreCase);
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    /// <summary>
    /// The first few words of a command line, a quoted path kept whole. Enough to find the program
    /// and its script; not a full <c>CommandLineToArgvW</c>.
    /// </summary>
    private static List<string> Tokens(string arguments)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < arguments.Length && tokens.Count < 8)
        {
            while (index < arguments.Length && char.IsWhiteSpace(arguments[index]))
            {
                index++;
            }
            if (index >= arguments.Length)
            {
                break;
            }
            int end;
            if (arguments[index] == '"')
            {
                end = arguments.IndexOf('"', index + 1);
                end = end < 0 ? arguments.Length : end;
                tokens.Add(arguments[(index + 1)..end]);
                index = end + 1;
                continue;
            }
            end = index;
            while (end < arguments.Length && !char.IsWhiteSpace(arguments[end]))
            {
                end++;
            }
            tokens.Add(arguments[index..end].Replace("\"", string.Empty, StringComparison.Ordinal));
            index = end;
        }
        return tokens;
    }
}
