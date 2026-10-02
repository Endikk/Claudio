using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// What Claudy's panels get from AppKit's style flags, for a WinUI window: no frame, above the
/// others or not, never taking the focus, and clicks only where something shows. Shared by the
/// card's <see cref="FloatingPanel"/> and the island's <see cref="NotchPanel"/>.
/// </summary>
internal static partial class PanelChrome
{
    /// <summary>
    /// Windows 11 draws its own corners and a one-pixel border round any window, a frame around the
    /// glass: both go, the panel draws its own edge.
    /// </summary>
    public static void RemoveFrame(AppWindow window)
    {
        var handle = Win32Interop.GetWindowFromWindowId(window.Id);
        // The presenter leaves a thin dialog frame on a window without title bar: a white line round the glass.
        const int Frame = 0x00800000 | 0x00400000 | 0x00040000;   // WS_BORDER | WS_DLGFRAME | WS_THICKFRAME
        const int EdgeStyles = 0x00000100 | 0x00000001;           // WS_EX_WINDOWEDGE | WS_EX_DLGMODALFRAME
        _ = SetWindowLongW(handle, -16, GetWindowLongW(handle, -16) & ~Frame);
        _ = SetWindowLongW(handle, -20, GetWindowLongW(handle, -20) & ~EdgeStyles);
        _ = SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020); // NOSIZE NOMOVE NOZORDER NOACTIVATE FRAMECHANGED
        var noRounding = 1; // DWMWCP_DONOTROUND
        _ = DwmSetWindowAttribute(handle, 33, ref noRounding, sizeof(int));
        var noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
    }

    /// <summary>
    /// Above every other window, or among them. Set on the window itself: the presenter's flag
    /// lost track of it once the extended style had been rewritten.
    /// </summary>
    public static void SetTopmost(AppWindow window, bool topmost)
    {
        const int Topmost = 0x00000008; // WS_EX_TOPMOST
        var handle = Win32Interop.GetWindowFromWindowId(window.Id);
        if (((GetWindowLongW(handle, -20) & Topmost) != 0) != topmost)
        {
            _ = SetWindowPos(handle, topmost ? -1 : -2, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE NOMOVE NOACTIVATE
        }
    }

    /// <summary>
    /// A window that never takes the focus, as Claudy's non-activating panels: a click leaves the
    /// keyboard where it was. Typing (the sign-in code) needs it back.
    /// </summary>
    public static void SetActivatable(AppWindow window, bool activatable)
    {
        const int NoActivate = 0x08000000; // WS_EX_NOACTIVATE
        var handle = Win32Interop.GetWindowFromWindowId(window.Id);
        var style = GetWindowLongW(handle, -20);
        var wanted = activatable ? style & ~NoActivate : style | NoActivate;
        if (wanted != style)
        {
            _ = SetWindowLongW(handle, -20, wanted);
        }
    }

    /// <summary>Where the window takes clicks, in its own pixels; the rest lets them through to what lies behind.</summary>
    public static void SetReach(AppWindow window, int left, int top, int right, int bottom)
    {
        var region = CreateRectRgn(left, top, right, bottom);
        // The system owns the region from here on.
        _ = SetWindowRgn(Win32Interop.GetWindowFromWindowId(window.Id), region, 1);
    }

    public static PointInt32 Cursor()
    {
        GetCursorPos(out var point);
        return new PointInt32(point.X, point.Y);
    }

    /// <summary>Device pixels per unit on the screen holding <paramref name="point"/>: 1.5 at 150 %.</summary>
    public static double ScaleAt(PointInt32 point)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, 2); // MONITOR_DEFAULTTONEAREST
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    /// <summary>
    /// Something runs full screen in front (a game, a presentation, a video): Claudy's island sits
    /// in the black band a notch leaves; on a PC it would sit on the picture, so it steps aside.
    /// </summary>
    public static bool IsFullScreenAppInFront() =>
        SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4; // BUSY, RUNNING_D3D_FULL_SCREEN, PRESENTATION_MODE

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint window, nint region, int redraw);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint window, int index, int value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
