using System.Globalization;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>What became of a Ctrl+C sent to a session, as Claudy's <c>send</c> answers with <c>errno</c>.</summary>
public enum SignalResult
{
    Sent,

    /// <summary>A pid that is never a session: the system's own (0 and 4), or none at all.</summary>
    Refused,

    /// <summary>The process is gone, or its pid now belongs to another process.</summary>
    Gone,

    /// <summary>Another user's process, or an elevated one Claudio may not reach.</summary>
    OtherUser,

    /// <summary>
    /// The process has no console to receive Ctrl+C: Windows delivers it to a console, not to a
    /// pid. Claudy has no such case, a Mac signal reaching any process.
    /// </summary>
    NoConsole,
}

/// <summary>
/// What Windows says about a process: the kernel's record of it, and the window manager's. The
/// App implements it with Win32 (<c>Claudio.App/Services/WindowsSessions.cs</c>); the tests fake it,
/// so no test ever touches a real process. Every reader answers null or false for a process that
/// is gone or not ours rather than throwing.
/// </summary>
public interface IProcessKernel
{
    /// <summary>Whether a process runs under this pid, whoever it belongs to.</summary>
    bool IsAlive(int pid);

    /// <summary>When the process started, from the kernel's own record.</summary>
    DateTimeOffset? StartDate(int pid);

    /// <summary>The path of the program the process runs.</summary>
    string? ExecutablePath(int pid);

    /// <summary>
    /// The pid of the process that started it. Windows keeps a dead parent's pid and hands it out
    /// again, so the answer is only a candidate: <see cref="ProcessIdentity"/> checks start times.
    /// </summary>
    int? Parent(int pid);

    /// <summary>Whether the process owns a visible top-level window, a console window included.</summary>
    bool HasVisibleWindow(int pid);

    /// <summary>The program's own name for itself, its file description: "Visual Studio Code".</summary>
    string? Description(string executablePath);

    /// <summary>
    /// Sends Ctrl+C to the console of <paramref name="pid"/>, as pressing it in the terminal does,
    /// once its start time is checked against <paramref name="startedAt"/> with
    /// <see cref="ProcessIdentity.Matches"/> while the process is held open, so that its pid
    /// cannot pass to another process between the check and the signal.
    /// </summary>
    SignalResult SendInterrupt(int pid, DateTimeOffset startedAt);
}

/// <summary>
/// What the system says about a process Claudio only knows by pid: whether it runs, when it
/// started, which program it is, and the app it runs in. A pid is reused once its process exits,
/// so the pid in a session file says little on its own; the start time the file records tells the
/// two apart. As Claudy's <c>ProcessIdentity</c>, over an <see cref="IProcessKernel"/> rather than
/// Darwin's calls.
/// </summary>
public sealed class ProcessIdentity(IProcessKernel kernel, TimeZoneInfo? zone = null)
{
    /// <summary>The Idle process and System (0 and 4) parent everything and are no one's session.</summary>
    public const int LastSystemPid = 4;

    /// <summary>Start times agree within this: Claude Code on a Mac writes whole seconds.</summary>
    public static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(2);

    /// <summary>Ancestors past this are a loop or a broken table, not a real tree.</summary>
    private const int HostSearchDepth = 16;

    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;

    public bool IsAlive(int pid) => pid > LastSystemPid && kernel.IsAlive(pid);

    /// <summary>When the process started, from the kernel's own record; null when it is gone or not ours.</summary>
    public DateTimeOffset? StartDate(int pid) => pid > LastSystemPid ? kernel.StartDate(pid) : null;

    /// <summary>The path of the program the process runs; null when it is gone or not ours.</summary>
    public string? ExecutablePath(int pid) => pid > LastSystemPid ? kernel.ExecutablePath(pid) : null;

    /// <summary>The process's parent; null when it is gone or not ours.</summary>
    public int? Parent(int pid) => pid > LastSystemPid ? kernel.Parent(pid) : null;

    /// <summary>
    /// The process a session file describes still runs: alive, started when the file says, or,
    /// for a file that says nothing, a Claude Code binary. The system's pids are never a session.
    /// </summary>
    public bool IsSession(int pid, DateTimeOffset? startedAt)
    {
        if (!IsAlive(pid))
        {
            return false;
        }
        if (startedAt is { } recorded)
        {
            return StartDate(pid) is { } actual && Matches(actual, recorded);
        }
        if (ExecutablePath(pid) is not { } path)
        {
            return false;
        }
        // Quoted, or a path with a space in it would be read as a program and its arguments.
        return ProcessTable.IsClaudeBinary($"\"{path}\"")
            || FileName(path).Equals("node.exe", StringComparison.OrdinalIgnoreCase)
            || FileName(path).Equals("node", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The file's start time is read as UTC, which is what Claude Code writes. Read as local time
    /// too, in case another build writes the clock it shows: the hour's shift between the two is
    /// far larger than the tolerance, so a reused pid still never matches both. On Windows the file
    /// holds the kernel's own FILETIME, which matches to the tick.
    /// </summary>
    public bool Matches(DateTimeOffset actual, DateTimeOffset recorded) => Matches(actual, recorded, _zone);

    /// <inheritdoc cref="Matches(DateTimeOffset, DateTimeOffset)"/>
    public static bool Matches(DateTimeOffset actual, DateTimeOffset recorded, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if ((actual - recorded).Duration() <= StartTolerance)
        {
            return true;
        }
        var offset = zone.GetUtcOffset(recorded);
        return (actual - (recorded - offset)).Duration() <= StartTolerance;
    }

    /// <summary>
    /// Sends Ctrl+C to a session's console. The system's pids are refused without asking; the
    /// kernel checks the start time again as the signal goes out.
    /// </summary>
    public SignalResult Send(int pid, DateTimeOffset startedAt) =>
        pid > LastSystemPid ? kernel.SendInterrupt(pid, startedAt) : SignalResult.Refused;

    /// <summary>
    /// The app a process runs in. A Mac names the first ancestor whose binary sits in an
    /// <c>.app</c> bundle; Windows has no bundles, so the process and its ancestors are walked,
    /// closest first, to the first that is either:
    /// <list type="bullet">
    /// <item>a known terminal or editor (<see cref="HostName"/>), taken at its outermost process of
    /// the same program: VS Code runs Claude Code from a utility process of its own, and the window
    /// belongs to the main one above it;</item>
    /// <item>or a program owning a visible window, Explorer aside: a classic console window reports
    /// the program that opened it (the shell, or Claude Code itself) as its owner, so a session in a
    /// console is found through it, even when that console was started from an editor's terminal.
    /// A shell inside a terminal or an editor has a pseudo console, whose window is never visible.</item>
    /// </list>
    /// A session in Windows Terminal through the "default terminal" handoff has neither, its shell
    /// being Explorer's child, and has no app.
    /// </summary>
    public HostApp? HostApp(int pid)
    {
        var lineage = Lineage(pid);
        for (var index = 0; index < lineage.Count; index++)
        {
            var (process, path) = lineage[index];
            if (path is null)
            {
                continue;
            }
            if (index > 0 && HostName(path) is { } name)
            {
                var outermost = index;
                var program = FileName(path);
                while (outermost + 1 < lineage.Count && lineage[outermost + 1].Path is { } next
                       && FileName(next).Equals(program, StringComparison.OrdinalIgnoreCase))
                {
                    outermost++;
                }
                return new HostApp(name, lineage[outermost].Pid);
            }
            if (!FileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && kernel.HasVisibleWindow(process))
            {
                return new HostApp(WindowOwnerName(path, kernel.Description(path)), process);
            }
        }
        return null;
    }

    /// <summary>
    /// The process and its ancestors, closest first. Bounded and cycle-safe; a parent that started
    /// after its child is a stranger that took a dead parent's pid, and the walk stops there.
    /// </summary>
    private List<(int Pid, string? Path)> Lineage(int pid)
    {
        var lineage = new List<(int, string?)>();
        if (pid <= LastSystemPid)
        {
            return lineage;
        }
        lineage.Add((pid, ExecutablePath(pid)));
        var seen = new HashSet<int> { pid };
        var child = pid;
        while (lineage.Count <= HostSearchDepth && Parent(child) is { } parent && parent > LastSystemPid && seen.Add(parent))
        {
            if (StartDate(parent) is { } parentStart && StartDate(child) is { } childStart && parentStart > childStart)
            {
                break;
            }
            lineage.Add((parent, ExecutablePath(parent)));
            child = parent;
        }
        return lineage;
    }

    private static readonly Dictionary<string, string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WindowsTerminal.exe"] = "Terminal",
        ["Code.exe"] = "Visual Studio Code",
        ["Code - Insiders.exe"] = "Visual Studio Code - Insiders",
        ["VSCodium.exe"] = "VSCodium",
        ["Cursor.exe"] = "Cursor",
        ["Windsurf.exe"] = "Windsurf",
        ["Kiro.exe"] = "Kiro",
        ["Zed.exe"] = "Zed",
        ["devenv.exe"] = "Visual Studio",
        ["wezterm-gui.exe"] = "WezTerm",
        ["alacritty.exe"] = "Alacritty",
        ["Hyper.exe"] = "Hyper",
        ["Tabby.exe"] = "Tabby",
        ["warp.exe"] = "Warp",
        ["ConEmu.exe"] = "ConEmu",
        ["ConEmu64.exe"] = "ConEmu",
        ["mintty.exe"] = "Mintty",
        ["idea64.exe"] = "IntelliJ IDEA",
        ["idea.exe"] = "IntelliJ IDEA",
        ["rider64.exe"] = "Rider",
        ["pycharm64.exe"] = "PyCharm",
        ["webstorm64.exe"] = "WebStorm",
        ["goland64.exe"] = "GoLand",
        ["clion64.exe"] = "CLion",
        ["phpstorm64.exe"] = "PhpStorm",
        ["rubymine64.exe"] = "RubyMine",
        ["rustrover64.exe"] = "RustRover",
        ["datagrip64.exe"] = "DataGrip",
        ["studio64.exe"] = "Android Studio",
    };

    private static readonly Dictionary<string, string> Consoles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cmd.exe"] = "Command Prompt",
        ["powershell.exe"] = "Windows PowerShell",
        ["pwsh.exe"] = "PowerShell",
        ["claude.exe"] = "Console",
        ["node.exe"] = "Console",
        ["bun.exe"] = "Console",
    };

    /// <summary>
    /// A terminal or an editor Claude Code runs in, named as its users know it: <c>Code.exe</c> is
    /// "Visual Studio Code", <c>WindowsTerminal.exe</c> "Terminal", as the Mac names Terminal.app.
    /// Null for any other program.
    /// </summary>
    public static string? HostName(string? executablePath) =>
        executablePath is not null && Hosts.TryGetValue(FileName(executablePath), out var name) ? name : null;

    /// <summary>
    /// The name of a program found owning a window: a console after the shell that opened it
    /// ("Command Prompt"), or the console alone when Claude Code opened it; else the program's own
    /// description, else its file name.
    /// </summary>
    public static string WindowOwnerName(string executablePath, string? description)
    {
        ArgumentNullException.ThrowIfNull(executablePath);
        var file = FileName(executablePath);
        if (Hosts.TryGetValue(file, out var host))
        {
            return host;
        }
        if (Consoles.TryGetValue(file, out var console))
        {
            return console;
        }
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description.Trim();
        }
        return file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    /// <summary>
    /// The <c>procStart</c> of a session file. Claude Code on Windows writes the kernel's creation
    /// time as a FILETIME, 100-nanosecond ticks since 1601 in UTC, as a string of digits
    /// ("134392374497722792"). On a Mac it writes it as <c>ctime</c> does, in UTC:
    /// "Mon Nov  9 22:26:03 2026". Both are read.
    /// </summary>
    public static DateTimeOffset? DateFromProcStart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var trimmed = text.Trim();
        if (trimmed.All(char.IsAsciiDigit))
        {
            return long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                   && ticks > 0 && ticks <= DateTime.MaxValue.ToFileTimeUtc()
                ? new DateTimeOffset(DateTime.FromFileTimeUtc(ticks), TimeSpan.Zero)
                : null;
        }
        // The weekday is dropped rather than checked, as a Mac's formatter does not check it.
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 5)
        {
            return null;
        }
        return DateTimeOffset.TryParseExact(string.Join(' ', words[1..]), "MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture,
                                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];
}
