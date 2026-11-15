using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// What the MCP scan asks the system about one process, behind delegates so the orphan rule can be
/// tested without any live process, as Claudy's <c>ProcessProbe</c>. The app reads them from Win32
/// in <c>WindowsProcesses.Probe</c>.
/// </summary>
/// <param name="IsOwnedByUser">The process runs as the current user.</param>
/// <param name="ExecutablePath">The full path of its image; null when it cannot be read.</param>
/// <param name="Markers">The Claude markers of its environment; null when it cannot be read.</param>
/// <param name="ResidentBytes">Its working set, in bytes; null when it cannot be read.</param>
public sealed record ProcessProbe(
    Func<int, bool> IsOwnedByUser,
    Func<int, string?> ExecutablePath,
    Func<int, ProcessEnvironment.Markers?> Markers,
    Func<int, ulong?> ResidentBytes);

/// <summary>Injected into the view model so its state machine can be tested without touching the machine.</summary>
public interface IMcpScanning
{
    /// <summary><paramref name="portPids"/> are the processes the ports list already shows, never listed twice.</summary>
    IReadOnlyList<McpOrphan> Scan(IReadOnlySet<int> portPids);
}

/// <summary>
/// Finds the MCP servers Claude Code left running, as Claudy's <c>McpScanner</c>.
///
/// Claude Code talks to an MCP server over its stdin and stdout, so the server opens no port and
/// the ports scan never sees it. When the session exits badly, the server keeps running with no
/// parent, and keeps its memory, often hundreds of megabytes, until the PC restarts. As with
/// ports, attribution comes from the environment; the tree only says whether a session still owns
/// it.
///
/// The open sessions are handed in (<c>liveSessionIds</c>): the ids of the Claude Code sessions
/// running now, as the sessions scan reads them from <c>&lt;config&gt;/sessions/</c>. An empty set
/// means no session is known to be live: the ancestry check still protects every server a live
/// session spawned, and only a session that outlived its link to its servers goes unrecognised.
/// </summary>
public sealed class McpScanner(
    Func<ProcessTable?> loadTable,
    ProcessProbe probe,
    Func<IReadOnlySet<string>> liveSessionIds) : IMcpScanning
{
    /// <summary>
    /// What marks a path component as an MCP server's: <c>chrome-devtools-mcp</c>,
    /// <c>mcp_server_git</c>, and the official <c>@modelcontextprotocol</c> scope, which spells
    /// the protocol out.
    /// </summary>
    private static readonly string[] ServerSignatures = ["mcp", "modelcontextprotocol"];

    /// <summary>
    /// Dropped from a server's name. Windows adds its own to Claudy's scripts: npm's shims
    /// (<c>chrome-devtools-mcp.cmd</c>, <c>.ps1</c>) and a server's own program (<c>.exe</c>).
    /// </summary>
    private static readonly string[] ScriptExtensions = ["js", "cjs", "mjs", "py", "cmd", "bat", "ps1", "exe"];

    /// <summary>The Idle process and System (0 and 4) are no one's server.</summary>
    private const int LastSystemPid = 4;
    private const int MaximumDepth = 64;

    public IReadOnlyList<McpOrphan> Scan(IReadOnlySet<int> portPids)
    {
        ArgumentNullException.ThrowIfNull(portPids);
        return loadTable() is { } table ? Orphans(table, portPids, probe, liveSessionIds) : [];
    }

    /// <summary>
    /// A server is an orphan when it looks like one, belongs to the user, runs outside any
    /// installed app, was launched by Claude Code, has no live Claude Code above it, and its
    /// session, when it names one, is not live.
    ///
    /// Its parent must also be gone, which is what happens to a server whose Claude Code died. On
    /// a Mac launchd adopts such a process; Windows keeps the dead parent's PID in it, and may
    /// hand that PID to a later process, so "gone" is the rule <see cref="ProcessTable"/> walks the
    /// tree by: no process holds the parent PID, or the one that does started after the server.
    /// That makes the row the top of the orphaned tree, the rest of the tree being part of it, and
    /// it spares the servers a live daemon still manages: the claude-mem worker runs its own
    /// <c>chroma-mcp</c> and hands it its Claude markers.
    ///
    /// Everything is injected so the rule can be tested without a live process. The checks run
    /// cheapest first, and the live sessions are only read once something needs them.
    /// </summary>
    public static IReadOnlyList<McpOrphan> Orphans(
        ProcessTable table,
        IReadOnlySet<int> portPids,
        ProcessProbe probe,
        Func<IReadOnlySet<string>> liveSessionIds)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(portPids);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(liveSessionIds);

        var attributed = new List<(RunningProcess Process, ProcessEnvironment.Markers Markers)>();
        foreach (var process in table.Processes.Values.OrderBy(process => process.Pid))
        {
            if (IsShortlisted(process, table, portPids, probe) && probe.Markers(process.Pid) is { IsClaude: true } markers)
            {
                attributed.Add((process, markers));
            }
        }
        if (attributed.Count == 0)
        {
            return [];
        }

        var live = liveSessionIds();
        return attributed
            .Where(server => server.Markers.SessionId is not { } session || !live.Contains(session))
            .Select(server => Orphan(server.Process, server.Markers, table, probe))
            // The heaviest first: that is the one worth killing.
            .OrderByDescending(orphan => orphan.ResidentBytes)
            .ThenBy(orphan => orphan.Pid)
            .ToList();
    }

    /// <summary>
    /// The half of the rule the table and one cheap call can answer. Only what passes has its
    /// environment read: that means opening the process and copying its block out of its memory.
    /// </summary>
    private static bool IsShortlisted(RunningProcess process, ProcessTable table, IReadOnlySet<int> portPids, ProcessProbe probe) =>
        process.Pid > LastSystemPid
        && IsParentGone(process, table)
        && !portPids.Contains(process.Pid)
        && LooksLikeMcpServer(process.Arguments)
        && table.ClaudeSessionRoot(process.Pid) is null
        && probe.IsOwnedByUser(process.Pid)
        && probe.ExecutablePath(process.Pid) is { } executable
        // An app ships and supervises its own helpers: their lifetime is the app's business.
        && !IsInstalledAppBinary(executable);

    /// <summary>
    /// Whatever started the process is gone. <see cref="ProcessTable.Ancestors"/> stops at the
    /// first parent that is not one, so a process with no ancestor has lost its parent.
    /// </summary>
    private static bool IsParentGone(RunningProcess process, ProcessTable table) => table.Ancestors(process.Pid).Count == 0;

    /// <summary>
    /// A binary an installed app ships for itself, Windows' counterpart of a Mac's <c>.app</c>
    /// bundle: an MSIX package (<c>\WindowsApps\</c>), a per-user Squirrel install
    /// (<c>%LOCALAPPDATA%\&lt;App&gt;\app-1.2.3\</c>, as Electron apps and Claude Desktop use), or
    /// an Electron app's own resources (<c>\resources\app\</c>, <c>app.asar.unpacked</c>).
    ///
    /// <c>Program Files</c> is not one of them: Node (<c>C:\Program Files\nodejs\node.exe</c>) and
    /// Python live there, and they run most MCP servers, which a Mac runs from Homebrew outside any
    /// bundle. A Store Python runs from <c>\WindowsApps\</c> and goes unlisted: leaving a server
    /// running is the safe side.
    /// </summary>
    public static bool IsInstalledAppBinary(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var parts = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var part = parts[index];
            if (part.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)
                || part.Equals("app.asar.unpacked", StringComparison.OrdinalIgnoreCase)
                || (part.StartsWith("app-", StringComparison.OrdinalIgnoreCase) && part.Length > 4 && char.IsAsciiDigit(part[4]))
                || (part.Equals("resources", StringComparison.OrdinalIgnoreCase) && index + 1 < parts.Length - 1
                    && parts[index + 1].Equals("app", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }

    private static McpOrphan Orphan(RunningProcess top, ProcessEnvironment.Markers markers, ProcessTable table, ProcessProbe probe)
    {
        var tree = Descendants(top.Pid, table).Prepend(top);
        var resident = tree.Aggregate(0UL, (sum, process) => sum + (probe.ResidentBytes(process.Pid) ?? 0));
        return new McpOrphan(
            $"mcp-{top.Pid}",
            top.Pid,
            top.StartedAt,
            DisplayName(top.Arguments),
            PortScanner.ProjectName(markers.ProjectDirectory),
            resident);
    }

    /// <summary>
    /// Every process below <paramref name="pid"/>, closest first, by the parent rule of
    /// <see cref="ProcessTable.Children"/>. The <c>seen</c> set bounds the walk: a malformed table
    /// with a loop in it still ends, each process being visited once.
    /// </summary>
    private static List<RunningProcess> Descendants(int pid, ProcessTable table)
    {
        var found = new List<RunningProcess>();
        var seen = new HashSet<int> { pid };
        var level = new List<int> { pid };
        for (var depth = 0; level.Count > 0 && depth < MaximumDepth; depth++)
        {
            var next = level.SelectMany(table.Children).Where(child => seen.Add(child.Pid)).OrderBy(child => child.Pid).ToList();
            found.AddRange(next);
            level = next.Select(child => child.Pid).ToList();
        }
        return found;
    }

    /// <summary>
    /// Some argument names an MCP server. Flags do not count: <c>--mcp-config</c> configures a
    /// client. Claude Code itself is excluded, <c>claude mcp serve</c> included: the reaper never
    /// kills it.
    /// </summary>
    public static bool LooksLikeMcpServer(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return !ProcessTable.IsClaudeBinary(arguments) && McpPackage(arguments) is not null;
    }

    /// <summary>
    /// The server's name, from the first argument path component that names it, without its
    /// version or script extension: <c>cmd.exe /c npx chrome-devtools-mcp@latest</c> and
    /// <c>node …\chrome-devtools-mcp\build\src\index.js</c> both give <c>chrome-devtools-mcp</c>.
    /// A scoped package keeps its scope, which alone names nothing and without which
    /// <c>@playwright/mcp</c> would read <c>mcp</c>. Falls back to the executable's file name.
    /// </summary>
    public static string DisplayName(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return McpPackage(arguments) ?? ExecutableName(arguments);
    }

    /// <summary>
    /// Windows quotes a path with spaces (<c>"C:\Program Files\nodejs\node.exe"</c>), and a
    /// <c>cmd /c</c> line quotes the whole command: words are cut at white space and stripped of
    /// their quotes, which a path's components never need, and both separators are read.
    /// </summary>
    private static string? McpPackage(string arguments)
    {
        foreach (var word in arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = word.Trim('"');
            if (token.Length == 0 || token.StartsWith('-'))
            {
                continue;
            }
            var components = token.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < components.Length; index++)
            {
                if (SignedName(components[index]) is { } name)
                {
                    return Package(name, index, components);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Within a component, the run of characters a package or module name is made of that carries
    /// a signature. It cuts the name out of what surrounds it: the pin of <c>chroma-mcp==0.2.6</c>,
    /// or the quotes and calls of an inline <c>node -e</c> script.
    /// </summary>
    private static string? SignedName(string component) =>
        NameRuns(component).FirstOrDefault(run =>
            ServerSignatures.Any(signature => run.Contains(signature, StringComparison.OrdinalIgnoreCase)));

    private static List<string> NameRuns(string component)
    {
        var runs = new List<string>();
        var start = -1;
        for (var index = 0; index <= component.Length; index++)
        {
            var isName = index < component.Length && IsNameCharacter(component[index]);
            if (isName && start < 0)
            {
                start = index;
            }
            else if (!isName && start >= 0)
            {
                runs.Add(component[start..index]);
                start = -1;
            }
        }
        return runs;
    }

    private static bool IsNameCharacter(char character) =>
        char.IsLetterOrDigit(character) || character is '@' or '.' or '_' or '-';

    private static string Package(string name, int index, string[] components)
    {
        if (name.StartsWith('@') && index + 1 < components.Length && NameRuns(components[index + 1]) is [var package, ..])
        {
            return $"{name}/{BareName(package)}";
        }
        if (index > 0 && components[index - 1].StartsWith('@'))
        {
            return $"{components[index - 1]}/{BareName(name)}";
        }
        return BareName(name);
    }

    /// <summary>
    /// <c>chrome-devtools-mcp@latest</c> gives <c>chrome-devtools-mcp</c>, <c>mcp-server.cjs</c>
    /// gives <c>mcp-server</c>. A leading <c>@</c> is a scope, not a version.
    /// </summary>
    private static string BareName(string component)
    {
        var at = component.LastIndexOf('@');
        var unversioned = at > 0 ? component[..at] : component;
        var dot = unversioned.LastIndexOf('.');
        return dot > 0 && ScriptExtensions.Contains(unversioned[(dot + 1)..], StringComparer.OrdinalIgnoreCase)
            ? unversioned[..dot]
            : unversioned;
    }

    /// <summary>The program's file name, a quoted path kept whole.</summary>
    private static string ExecutableName(string arguments)
    {
        var text = arguments.TrimStart();
        string program;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            program = end < 0 ? text[1..] : text[1..end];
        }
        else
        {
            var end = text.IndexOfAny([' ', '\t']);
            program = end < 0 ? text : text[..end];
        }
        var trimmed = program.TrimEnd('/', '\\');
        return trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
    }
}
