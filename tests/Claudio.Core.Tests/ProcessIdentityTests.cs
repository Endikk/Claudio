using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The system's word on a pid, from a fake: Claudy checks it on its own test process, which on
/// Windows would say nothing of the rules, and Core's tests also run on Linux.
/// </summary>
public sealed class ProcessIdentityTests
{
    private const int Me = 4242;
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 8, 30, 0, TimeSpan.Zero);

    private static (ProcessIdentity Identity, FakeKernel Kernel) Make(TimeZoneInfo? zone = null)
    {
        var kernel = new FakeKernel();
        kernel.Add(Me, 1000, Start, @"C:\Program Files\dotnet\dotnet.exe");
        return (new ProcessIdentity(kernel, zone ?? TimeZoneInfo.Utc), kernel);
    }

    /// <summary>Claude Code on a Mac writes <c>procStart</c> as <c>ctime</c> does, in UTC, with a padded day.</summary>
    [Fact]
    public void ProcStartIsReadAsUtc()
    {
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 22, 26, 3, TimeSpan.Zero), ProcessIdentity.DateFromProcStart("Mon Nov  9 22:26:03 2026"));
        Assert.Null(ProcessIdentity.DateFromProcStart("yesterday"));
        Assert.Null(ProcessIdentity.DateFromProcStart(""));
    }

    /// <summary>
    /// On Windows it writes the kernel's creation time as a FILETIME: this one, from a real session
    /// file, lands a fraction of a second before the file's <c>startedAt</c>.
    /// </summary>
    [Fact]
    public void ProcStartFromWindowsIsAFileTime()
    {
        var date = ProcessIdentity.DateFromProcStart("134392374497722792");
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(17_947_638_497_722_792), date);
        var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(1794763850644);
        Assert.InRange(startedAt - date!.Value, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Null(ProcessIdentity.DateFromProcStart("99999999999999999999999"));
    }

    [Fact]
    public void KnowsWhenThisProcessStarted()
    {
        var (identity, kernel) = Make();
        Assert.Equal(Start, identity.StartDate(Me));
        Assert.Null(identity.StartDate(0));
        Assert.Null(identity.StartDate(4));
        Assert.DoesNotContain(0, kernel.Asked);
        Assert.DoesNotContain(4, kernel.Asked);
    }

    /// <summary>
    /// The process is the session when it started when the file says; hours off, it is another
    /// process that took the pid. The Idle process and System never are.
    /// </summary>
    [Fact]
    public void SessionMatchesOnStartTime()
    {
        var (identity, _) = Make();
        Assert.True(identity.IsSession(Me, Start));
        Assert.True(identity.IsSession(Me, Start.AddSeconds(1)));
        Assert.False(identity.IsSession(Me, Start.AddHours(-3)));
        Assert.False(identity.IsSession(0, Start));
        Assert.False(identity.IsSession(4, null));
        Assert.False(identity.IsSession(-1, null));
        Assert.False(identity.IsSession(5151, Start));
    }

    /// <summary>A start time written as the local clock still matches, an hour's shift away from UTC.</summary>
    [Fact]
    public void AStartTimeWrittenInLocalTimeStillMatches()
    {
        var paris = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");
        var (identity, _) = Make(paris);
        var shown = new DateTimeOffset(Start.UtcDateTime.AddHours(1), TimeSpan.Zero);
        Assert.True(identity.IsSession(Me, shown));
        Assert.False(identity.IsSession(Me, Start.AddHours(-1)));
    }

    /// <summary>Without a recorded start, only a Claude Code binary passes: the test host is not one.</summary>
    [Fact]
    public void WithoutAStartTimeOnlyAClaudeBinaryPasses()
    {
        var (identity, kernel) = Make();
        Assert.False(identity.IsSession(Me, null));
        kernel.Add(5000, 1000, Start, @"C:\Users\Ada Lovelace\.local\bin\claude.exe");
        kernel.Add(5001, 1000, Start, @"C:\Program Files\nodejs\node.exe");
        Assert.True(identity.IsSession(5000, null));
        Assert.True(identity.IsSession(5001, null));
    }

    [Fact]
    public void SendRefusesSystemPidsAndNamesAGoneProcess()
    {
        var (identity, kernel) = Make();
        Assert.Equal(SignalResult.Refused, identity.Send(0, Start));
        Assert.Equal(SignalResult.Refused, identity.Send(4, Start));
        Assert.Empty(kernel.Interrupts);
        Assert.Equal(SignalResult.Sent, identity.Send(Me, Start));
        Assert.Equal(SignalResult.Gone, identity.Send(2_147_000_000, Start));
        Assert.Equal([(Me, Start), (2_147_000_000, Start)], kernel.Interrupts);
    }

    // MARK: - The app a session runs in

    /// <summary>
    /// Claude Code in VS Code runs under a utility process of Code; the window is the main one's,
    /// the outermost Code above it. A real tree from this PC.
    /// </summary>
    [Fact]
    public void HostAppIsTheOutermostProcessOfAKnownEditor()
    {
        var kernel = new FakeKernel();
        kernel.Add(11252, 900, Start.AddHours(-2), @"C:\WINDOWS\Explorer.EXE", window: true);
        kernel.Add(10196, 11252, Start.AddHours(-1), @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", window: true);
        kernel.Add(30152, 10196, Start.AddMinutes(-1), @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe");
        kernel.Add(30872, 30152, Start, @"c:\Users\x\.vscode\extensions\anthropic.claude-code-2.1.325-win32-x64\resources\native-binary\claude.exe");
        Assert.Equal(new HostApp("Visual Studio Code", 10196), new ProcessIdentity(kernel).HostApp(30872));
    }

    [Fact]
    public void WindowsTerminalIsTerminal()
    {
        var kernel = new FakeKernel();
        kernel.Add(700, 11252, Start.AddHours(-1), @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.23\WindowsTerminal.exe", window: true);
        kernel.Add(800, 700, Start.AddMinutes(-5), @"C:\Program Files\PowerShell\7\pwsh.exe");
        kernel.Add(900, 800, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Equal(new HostApp("Terminal", 700), new ProcessIdentity(kernel).HostApp(900));
    }

    /// <summary>
    /// A classic console window reports the shell that opened it as its owner: the session is
    /// opened through it, named as the Start menu names it. Explorer, which owns the taskbar, is
    /// never the answer.
    /// </summary>
    [Fact]
    public void ASessionInAConsoleWindowIsFoundThroughItsShell()
    {
        var kernel = new FakeKernel();
        kernel.Add(11252, 900, Start.AddHours(-2), @"C:\WINDOWS\Explorer.EXE", window: true);
        kernel.Add(800, 11252, Start.AddMinutes(-5), @"C:\WINDOWS\system32\cmd.exe", window: true);
        kernel.Add(900, 800, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Equal(new HostApp("Command Prompt", 800), new ProcessIdentity(kernel).HostApp(900));

        var orphan = new FakeKernel();
        orphan.Add(11252, 900, Start.AddHours(-2), @"C:\WINDOWS\Explorer.EXE", window: true);
        orphan.Add(900, 11252, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Null(new ProcessIdentity(orphan).HostApp(900));
    }

    /// <summary>
    /// A console window opened from an editor's terminal (<c>start cmd</c>) holds the session: the
    /// closest window wins over the editor further up.
    /// </summary>
    [Fact]
    public void AConsoleOpenedFromAnEditorIsTheConsole()
    {
        var kernel = new FakeKernel();
        kernel.Add(10196, 11252, Start.AddHours(-1), @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", window: true);
        kernel.Add(600, 10196, Start.AddMinutes(-9), @"C:\Program Files\PowerShell\7\pwsh.exe");
        kernel.Add(700, 600, Start.AddMinutes(-8), @"C:\WINDOWS\system32\conhost.exe");
        kernel.Add(800, 700, Start.AddMinutes(-8), @"C:\WINDOWS\system32\cmd.exe", window: true);
        kernel.Add(900, 800, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Equal(new HostApp("Command Prompt", 800), new ProcessIdentity(kernel).HostApp(900));
    }

    /// <summary>An unknown program owning a window is named by its own description.</summary>
    [Fact]
    public void AnUnknownWindowOwnerIsNamedByItsDescription()
    {
        var kernel = new FakeKernel();
        kernel.Add(600, 11252, Start.AddHours(-1), @"C:\Tools\fancyterm.exe", window: true, description: "Fancy Term");
        kernel.Add(900, 600, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Equal(new HostApp("Fancy Term", 600), new ProcessIdentity(kernel).HostApp(900));
        Assert.Equal("fancyterm", ProcessIdentity.WindowOwnerName(@"C:\Tools\fancyterm.exe", null));
    }

    /// <summary>Windows hands a dead parent's pid out again: a "parent" that started after its child is a stranger.</summary>
    [Fact]
    public void HostSearchStopsAtAParentThatStartedAfterItsChild()
    {
        var kernel = new FakeKernel();
        kernel.Add(700, 11252, Start.AddHours(1), @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", window: true);
        kernel.Add(900, 700, Start, @"C:\Users\x\.local\bin\claude.exe");
        Assert.Null(new ProcessIdentity(kernel).HostApp(900));
    }

    /// <summary>A malformed tree must not hang the scan: a loop ends, a long chain is cut.</summary>
    [Fact]
    public void HostSearchIsBoundedAndCycleSafe()
    {
        var loop = new FakeKernel();
        loop.Add(10, 11, Start, @"C:\a.exe");
        loop.Add(11, 10, Start, @"C:\b.exe");
        Assert.Null(new ProcessIdentity(loop).HostApp(10));

        var chain = new FakeKernel();
        for (var pid = 100; pid < 140; pid++)
        {
            chain.Add(pid, pid + 1, Start, @"C:\sh.exe");
        }
        chain.Add(140, 11252, Start, @"C:\Program Files\WindowsApps\WindowsTerminal.exe", window: true);
        Assert.Null(new ProcessIdentity(chain).HostApp(100));
        Assert.True(chain.Asked.Max() < 140);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.23.1\WindowsTerminal.exe", "Terminal")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", "Visual Studio Code")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code Insiders\Code - Insiders.exe", "Visual Studio Code - Insiders")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\cursor\Cursor.exe", "Cursor")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Windsurf\Windsurf.exe", "Windsurf")]
    [InlineData(@"C:\Program Files\WezTerm\wezterm-gui.exe", "WezTerm")]
    [InlineData(@"C:\Program Files\Alacritty\ALACRITTY.EXE", "Alacritty")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Hyper\Hyper.exe", "Hyper")]
    [InlineData(@"C:\Program Files\Tabby\Tabby.exe", "Tabby")]
    [InlineData(@"C:\Program Files\JetBrains\IntelliJ IDEA 2026.2\bin\idea64.exe", "IntelliJ IDEA")]
    [InlineData(@"C:\Program Files\JetBrains\JetBrains Rider 2026.2\bin\rider64.exe", "Rider")]
    [InlineData(@"C:\Program Files\JetBrains\PyCharm 2026.2\bin\pycharm64.exe", "PyCharm")]
    [InlineData(@"C:\Program Files\JetBrains\WebStorm 2026.2\bin\webstorm64.exe", "WebStorm")]
    public void KnownHostsAreNamedAsTheirUsersKnowThem(string path, string name) => Assert.Equal(name, ProcessIdentity.HostName(path));

    [Theory]
    [InlineData(@"C:\WINDOWS\system32\cmd.exe")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\WINDOWS\Explorer.EXE")]
    [InlineData(@"C:\Users\x\.local\bin\claude.exe")]
    [InlineData(null)]
    public void OtherProgramsAreNoHost(string? path) => Assert.Null(ProcessIdentity.HostName(path));
}

/// <summary>A process table and window list for <see cref="ProcessIdentity"/>, recording what it is asked.</summary>
internal sealed class FakeKernel : IProcessKernel
{
    private readonly Dictionary<int, (int Parent, DateTimeOffset Start, string Path, bool Window, string? Description)> _processes = [];

    public List<int> Asked { get; } = [];

    public List<(int Pid, DateTimeOffset StartedAt)> Interrupts { get; } = [];

    public void Add(int pid, int parent, DateTimeOffset start, string path, bool window = false, string? description = null) =>
        _processes[pid] = (parent, start, path, window, description);

    public bool IsAlive(int pid)
    {
        Asked.Add(pid);
        return _processes.ContainsKey(pid);
    }

    public DateTimeOffset? StartDate(int pid)
    {
        Asked.Add(pid);
        return _processes.TryGetValue(pid, out var process) ? process.Start : null;
    }

    public string? ExecutablePath(int pid)
    {
        Asked.Add(pid);
        return _processes.TryGetValue(pid, out var process) ? process.Path : null;
    }

    public int? Parent(int pid)
    {
        Asked.Add(pid);
        return _processes.TryGetValue(pid, out var process) ? process.Parent : null;
    }

    public bool HasVisibleWindow(int pid) => _processes.TryGetValue(pid, out var process) && process.Window;

    public string? Description(string executablePath) =>
        _processes.Values.FirstOrDefault(process => process.Path == executablePath).Description;

    public SignalResult SendInterrupt(int pid, DateTimeOffset startedAt)
    {
        Interrupts.Add((pid, startedAt));
        return _processes.ContainsKey(pid) ? SignalResult.Sent : SignalResult.Gone;
    }
}
