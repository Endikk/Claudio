using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>
/// A backdrop that paints nothing, so the window is transparent wherever its content is: the card
/// draws its own rounded glass and its own shadow, as Claudy's does, instead of the rectangle and
/// small corners Windows gives a window. DWM only honours a window's alpha once its frame reaches
/// into the client area and blur-behind is on (over an empty region, so nothing is blurred): the
/// recipe WinUIEx's TransparentTintBackdrop uses.
/// </summary>
internal sealed partial class TransparentBackdrop : SystemBackdrop
{
    private const uint EraseBackground = 0x0014;        // WM_ERASEBKGND
    private const uint CompositionChanged = 0x031E;     // WM_DWMCOMPOSITIONCHANGED
    private const uint DisplayChange = 0x007E;          // WM_DISPLAYCHANGE
    private const uint SettingChange = 0x001A;          // WM_SETTINGCHANGE
    private const nint WorkArea = 0x002F;               // SPI_SETWORKAREA
    private static readonly SubclassProcedure Procedure = Subclass;
    private static Windows.UI.Composition.Compositor? _compositor;
    private Windows.UI.Composition.CompositionColorBrush? _brush;
    private nint _window;

    /// <summary>
    /// Screens came or went, changed resolution, or the taskbar moved: the windows find their
    /// place again, as Claudy's card does when screens change.
    /// </summary>
    public static event Action? DisplayChanged;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _window = Win32Interop.GetWindowFromWindowId(xamlRoot.ContentIslandEnvironment.AppWindowId);
        _ = SetWindowSubclass(_window, Procedure, 1, 0);
        ConfigureDwm(_window);
        _brush ??= Compositor().CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        connectedTarget.SystemBackdrop = _brush;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
        if (_window != 0)
        {
            _ = RemoveWindowSubclass(_window, Procedure, 1);
        }
    }

    private static void ConfigureDwm(nint window)
    {
        var margins = default(Margins);
        _ = DwmExtendFrameIntoClientArea(window, ref margins);
        var region = CreateRectRgn(-2, -2, -1, -1);
        var blur = new BlurBehind { Flags = 3, Enable = 1, Region = region };   // DWM_BB_ENABLE | DWM_BB_BLURREGION
        _ = DwmEnableBlurBehindWindow(window, ref blur);
        _ = DeleteObject(region);
    }

    /// <summary>GDI paints black as fully transparent here: the window starts clear rather than white.</summary>
    private static nint Subclass(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == EraseBackground && GetClientRect(window, out var rect))
        {
            _ = FillRect(wParam, ref rect, GetStockObject(4)); // BLACK_BRUSH
            return 1;
        }
        if (message == CompositionChanged)
        {
            ConfigureDwm(window);
        }
        if (message == DisplayChange || (message == SettingChange && wParam == WorkArea))
        {
            DisplayChanged?.Invoke();
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>
    /// The system compositor the backdrop brush must come from. It needs a Windows.System
    /// dispatcher queue on the UI thread, which a WinUI 3 app does not create by itself.
    /// </summary>
    private static Windows.UI.Composition.Compositor Compositor()
    {
        if (_compositor is null)
        {
            if (Windows.System.DispatcherQueue.GetForCurrentThread() is null)
            {
                var options = new DispatcherQueueOptions
                {
                    Size = Marshal.SizeOf<DispatcherQueueOptions>(),
                    ThreadType = 2,     // DQTYPE_THREAD_CURRENT
                    ApartmentType = 2,  // DQTAT_COM_STA
                };
                _ = CreateDispatcherQueueController(options, out _);
            }
            _compositor = new Windows.UI.Composition.Compositor();
        }
        return _compositor;
    }

    private delegate nint SubclassProcedure(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurBehind
    {
        public uint Flags;
        public int Enable;
        public nint Region;
        public int TransitionOnMaximized;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmEnableBlurBehindWindow(nint window, ref BlurBehind blur);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint dc, ref Rect rect, nint brush);

#pragma warning disable SYSLIB1054 // A delegate crosses here: source-generated marshalling does not take one.
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);
#pragma warning restore SYSLIB1054

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
