using System.Runtime.InteropServices;

namespace Claudio.App;

/// <summary>
/// A keyboard shortcut heard from any app, as Claudy's <c>GlobalHotKey</c>: Ctrl+Alt+C shows or
/// hides Claudio, Ctrl+Alt+R opens the agent room.
///
/// Claudy files them with Carbon's hot keys; Windows has <c>RegisterHotKey</c>, which needs no
/// permission either and keeps the key from the app in front. The shortcuts come to a message-only
/// window of the UI thread, whose message loop WinUI already runs, so the action runs on that
/// thread. When another app holds a shortcut, registering fails and the menus say so.
/// </summary>
internal sealed partial class GlobalHotKey : IDisposable
{
    /// <summary>The shortcuts as menus show them.</summary>
    public const string Display = "Ctrl+Alt+C";
    public const string RoomDisplay = "Ctrl+Alt+R";

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmHotKey = 0x0312;
    private static readonly nint MessageOnly = -3;

    private static readonly Dictionary<int, GlobalHotKey> Registered = [];
    private static nint _window;

    private readonly int _id;
    private readonly uint _key;
    private readonly string _display;
    private readonly Action _action;

    /// <param name="key">The virtual key, 'C' or 'R'.</param>
    /// <param name="id">Tells this shortcut apart from Claudio's others.</param>
    public GlobalHotKey(char key, int id, string display, Action action)
    {
        _key = key;
        _id = id;
        _display = display;
        _action = action;
    }

    /// <summary>False when another app holds the shortcut: the menu says so instead of promising it.</summary>
    public bool IsRegistered { get; private set; }

    public void Register()
    {
        if (IsRegistered || !EnsureWindow())
        {
            return;
        }
        IsRegistered = RegisterHotKey(_window, _id, ModControl | ModAlt | ModNoRepeat, _key) != 0;
        if (IsRegistered)
        {
            Registered[_id] = this;
        }
        else
        {
            DiagnosticLog.Append($"hotkey: {_display} not registered ({Marshal.GetLastPInvokeError()}), taken by another app?");
        }
    }

    public void Unregister()
    {
        if (IsRegistered)
        {
            _ = UnregisterHotKey(_window, _id);
            Registered.Remove(_id);
        }
        IsRegistered = false;
    }

    public void Dispose() => Unregister();

    private static unsafe bool EnsureWindow()
    {
        if (_window != 0)
        {
            return true;
        }
        fixed (char* name = "ClaudioHotKeys")
        {
            var type = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                Procedure = (nint)(delegate* unmanaged<nint, uint, nint, nint, nint>)&Procedure,
                Instance = GetModuleHandleW(null),
                ClassName = name,
            };
            RegisterClassExW(&type);
            _window = CreateWindowExW(0, name, name, 0, 0, 0, 0, 0, MessageOnly, 0, type.Instance, 0);
        }
        if (_window == 0)
        {
            DiagnosticLog.Append($"hotkey: no message window ({Marshal.GetLastPInvokeError()})");
        }
        return _window != 0;
    }

    [UnmanagedCallersOnly]
    private static nint Procedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotKey && Registered.TryGetValue((int)wParam, out var hotKey))
        {
            try
            {
                hotKey._action();
            }
#pragma warning disable CA1031 // A shortcut that fails is written down, never thrown into the system's callback.
            catch (Exception error)
#pragma warning restore CA1031
            {
                DiagnosticLog.Append($"hotkey: {error}");
            }
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    private static partial int UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial ushort RegisterClassExW(WindowClass* type);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial nint CreateWindowExW(uint extendedStyle, char* className, char* windowName, uint style,
                                                       int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
