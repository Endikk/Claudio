using System.Runtime.InteropServices;

namespace Claudio.App;

/// <summary>
/// Asks Windows whether the notification area will take an icon right now, before the icon library
/// is asked to create one. Creating it runs on that library's own thread, and when Windows says no
/// (at sign-in before Explorer is done, in a session without a notification area) it throws there,
/// where nothing can catch it and the process ends. The probe adds a hidden icon, the way the
/// library will, and takes it away again: nothing is ever shown.
/// </summary>
internal static partial class NotificationAreaProbe
{
    private const uint Add = 0, Delete = 2, SetVersion = 4;                  // NIM_ADD, NIM_DELETE, NIM_SETVERSION
    private const uint Message = 0x1, IconFlag = 0x2, State = 0x8;           // NIF_MESSAGE, NIF_ICON, NIF_STATE
    private const uint Hidden = 0x1;                                         // NIS_HIDDEN
    private const int StaticClass = 0, Application = 32512;                  // IDI_APPLICATION

    /// <summary>
    /// True when the taskbar is there and the notification area accepted a hidden icon. A probe
    /// that itself breaks says yes: the icon is then tried as it always was, never blocked for good.
    /// </summary>
    public static bool Accepts()
    {
        try
        {
            return Ask();
        }
#pragma warning disable CA1031 // The probe is a precaution; its failure must not become one of its own.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"tray: the notification-area probe failed ({error.GetType().Name}): {error.Message}");
            return true;
        }
    }

    private static bool Ask()
    {
        if (FindWindowW("Shell_TrayWnd", null) == 0)
        {
            return false;
        }
        var window = CreateWindowExW(0, "STATIC", "ClaudioProbe", 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (window == 0)
        {
            return false;
        }
        try
        {
            var data = new NotifyIconData
            {
                Size = (uint)Marshal.SizeOf<NotifyIconData>(),
                Window = window,
                Id = 0x434C,
                Flags = Message | IconFlag | State,
                Icon = LoadIconW(0, Application),
                State = Hidden,
                StateMask = Hidden,
                Tip = string.Empty,
                Info = string.Empty,
                InfoTitle = string.Empty,
            };
            if (!ShellNotifyIconW(Add, ref data))
            {
                return false;
            }
            data.Version = 4; // NOTIFYICON_VERSION_4, which the library asks for
            var accepted = ShellNotifyIconW(SetVersion, ref data);
            _ = ShellNotifyIconW(Delete, ref data);
            return accepted;
        }
        finally
        {
            _ = DestroyWindow(window);
        }
    }

#pragma warning disable CA1815, SYSLIB1054 // A marshalled struct, passed by reference to one call.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public uint InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIconW(uint message, ref NotifyIconData data);
#pragma warning restore CA1815, SYSLIB1054

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string className, string? windowName);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style,
                                                int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint LoadIconW(nint instance, int name);
}
