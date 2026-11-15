using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;

namespace Claudio.App;

/// <summary>
/// What the Sessions tab asks of Windows: the kernel's word on a session's pid and the app it runs
/// in (<see cref="IProcessKernel"/>, read by <see cref="ProcessIdentity"/>), Ctrl+C to its console,
/// and its app's window brought forward (<see cref="ISessionActions"/>, used by
/// <see cref="AgentsViewModel"/>). Claudy gets the same from <c>kill</c>, <c>proc_pidinfo</c> and
/// <c>NSWorkspace</c>.
///
/// <code>
/// var sessions = new WindowsSessions();
/// var agents = new AgentsViewModel(AgentScanner.Create(ClaudeHome.Current, sessions.Identity), sessions);
/// </code>
///
/// The process readers may run on any thread (the scanner calls them from the thread pool).
/// <see cref="BringForward"/> belongs on the UI thread, which received the click that asks for it.
/// </summary>
internal sealed unsafe partial class WindowsSessions : IProcessKernel, ISessionActions
{
    /// <summary>
    /// A process has one console at most: attaching to a session's is process-wide state, so two
    /// interrupts never overlap.
    /// </summary>
    private static readonly Lock ConsoleLock = new();

    private static bool _swallowing;

    public WindowsSessions() => Identity = new ProcessIdentity(this);

    public ProcessIdentity Identity { get; }

    // MARK: - IProcessKernel

    public bool IsAlive(int pid) => WindowsProcesses.IsAlive(pid);

    public DateTimeOffset? StartDate(int pid) => WindowsProcesses.StartDate(pid);

    public string? ExecutablePath(int pid) => WindowsProcesses.ExecutablePath(pid);

    public int? Parent(int pid) => WindowsProcesses.Parent(pid);

    /// <summary>
    /// A visible, unowned, top-level window that is not a tool window. A classic console window
    /// counts: Windows reports the program that opened it, not <c>conhost.exe</c>, as its owner.
    /// </summary>
    public bool HasVisibleWindow(int pid)
    {
        var search = FindWindow(pid);
        return search.Found != 0 || search.Cloaked != 0;
    }

    public string? Description(string executablePath)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(executablePath).FileDescription;
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch (Exception error) when (error is FileNotFoundException or ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ctrl+C as the terminal sends it, while the process is held open and checked to be the
    /// session (<see cref="WindowsProcesses.Holding"/>):
    /// <c>FreeConsole; AttachConsole(pid); SetConsoleCtrlHandler(null, TRUE);
    /// GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0); FreeConsole</c>, then Claudio's handling restored.
    /// The event goes to every process on the session's console, its shell included, exactly as the
    /// key would, and to Claudio, attached for that moment. Claudio, a windowed app, has no console
    /// of its own to give up.
    ///
    /// The event reaches Claudio on a thread of its own, possibly after the ignore flag is lifted
    /// again, and the default handler would then end Claudio. A handler that swallows Ctrl+C and
    /// Ctrl+Break is therefore registered once and kept: it is only ever called while Claudio is
    /// attached to a console, and unlike the ignore flag it is not inherited by the programs Claudio
    /// starts.
    /// </summary>
    public SignalResult SendInterrupt(int pid, DateTimeOffset startedAt) =>
        WindowsProcesses.Holding(pid, startedAt, () => CtrlC(pid));

    private static SignalResult CtrlC(int pid)
    {
        lock (ConsoleLock)
        {
            if (!_swallowing)
            {
                _swallowing = SetConsoleCtrlHandler(&Swallow, 1) != 0;
            }
            _ = FreeConsole();
            if (AttachConsole((uint)pid) == 0)
            {
                // No console of its own (ERROR_INVALID_HANDLE), gone (ERROR_INVALID_PARAMETER), or
                // out of reach: an elevated session refuses a process that is not.
                return Marshal.GetLastPInvokeError() switch
                {
                    ErrorInvalidHandle => SignalResult.NoConsole,
                    ErrorInvalidParameter => SignalResult.Gone,
                    _ => SignalResult.OtherUser,
                };
            }
            try
            {
                _ = SetConsoleCtrlHandler(null, 1);
                return GenerateConsoleCtrlEvent(CtrlCEvent, 0) != 0 ? SignalResult.Sent : SignalResult.NoConsole;
            }
            finally
            {
                _ = FreeConsole();
                _ = SetConsoleCtrlHandler(null, 0);
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Swallow(uint type) => type is CtrlCEvent or CtrlBreakEvent ? 1 : 0;

    // MARK: - ISessionActions

    public bool IsSession(int pid, DateTimeOffset? startedAt) => Identity.IsSession(pid, startedAt);

    public SignalResult Interrupt(int pid, DateTimeOffset startedAt) => Identity.Send(pid, startedAt);

    /// <summary>
    /// Brings forward the app's window last in use, as activating a Mac app brings its last window
    /// forward: the topmost of its windows, restored if minimised, then made the foreground one.
    /// When Windows refuses the foreground to Claudio (its card never takes the focus), the call is
    /// made again with Claudio's input joined to the foreground window's thread for that moment.
    /// False when the app has no window left.
    /// </summary>
    public bool BringForward(HostApp host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var search = FindWindow(host.Pid);
        var window = search.Found != 0 ? search.Found : search.Cloaked;
        if (window == 0)
        {
            return false;
        }
        if (IsIconic(window) != 0)
        {
            _ = ShowWindow(window, SwRestore);
        }
        if (SetForegroundWindow(window) != 0)
        {
            return true;
        }
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), null);
        var current = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != current && AttachThreadInput(current, foregroundThread, 1) != 0;
        try
        {
            _ = BringWindowToTop(window);
            _ = SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(current, foregroundThread, 0);
            }
        }
        return true;
    }

    // MARK: - Windows

    private struct WindowSearch
    {
        public uint Pid;

        /// <summary>The first window of the process in z-order, the one used last.</summary>
        public nint Found;

        /// <summary>The first one hidden by the shell (another virtual desktop), used only failing any other.</summary>
        public nint Cloaked;
    }

    private static WindowSearch FindWindow(int pid)
    {
        var search = new WindowSearch { Pid = (uint)pid };
        if (pid > ProcessIdentity.LastSystemPid)
        {
            _ = EnumWindows(&Visit, (nint)(&search));
        }
        return search;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Visit(nint window, nint context)
    {
        var search = (WindowSearch*)context;
        uint owner;
        _ = GetWindowThreadProcessId(window, &owner);
        if (owner != search->Pid || IsWindowVisible(window) == 0 || GetWindow(window, GwOwner) != 0
            || (GetWindowLongPtrW(window, GwlExStyle) & WsExToolWindow) != 0)
        {
            return 1;
        }
        var cloaked = 0;
        if (DwmGetWindowAttribute(window, DwmwaCloaked, &cloaked, sizeof(int)) >= 0 && cloaked != 0)
        {
            if (search->Cloaked == 0)
            {
                search->Cloaked = window;
            }
            return 1;
        }
        search->Found = window;
        return 0;
    }

    private const uint CtrlCEvent = 0;
    private const uint CtrlBreakEvent = 1;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorInvalidParameter = 87;
    private const uint GwOwner = 4;
    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x00000080;
    private const uint DwmwaCloaked = 14;
    private const int SwRestore = 9;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int AttachConsole(uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int FreeConsole();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetConsoleCtrlHandler(delegate* unmanaged[Stdcall]<uint, int> handler, int add);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint context);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, uint* processId);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial int IsIconic(nint window);

    [LibraryImport("user32.dll")]
    private static partial int ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    private static partial int SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial int BringWindowToTop(nint window);

    [LibraryImport("user32.dll")]
    private static partial int AttachThreadInput(uint attach, uint attachTo, int doAttach);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(nint window, uint attribute, void* value, uint size);
}
