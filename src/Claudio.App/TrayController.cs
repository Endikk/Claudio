using System.Runtime.InteropServices;
using Claudio.Core.Design;
using Claudio.Core.Presentation;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// Claudio in the notification area, next to the clock: the mascot, typing while a session runs
/// and tinted by the gauge's band, the session figure in the tooltip. Left click shows or hides
/// the card; right click opens a native Windows menu.
/// </summary>
internal sealed partial class TrayController : IDisposable
{
    private readonly TrayIconWithContextMenu _tray = new("Claudio.Tray");
    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _animation;
    private readonly Dictionary<string, nint> _icons = [];
    private readonly Mascot _mascot = Mascot.Shared;
    private readonly DesignTokens _tokens = DesignTokens.Shared;
    private Rgba _tint;
    private bool _isTyping;
    private int _tick;

    public TrayController(DispatcherQueue ui, Action toggleCard, Func<Task> refresh, Action quit)
    {
        _ui = ui;
        _tint = _tokens.Color("color.accent.coral");
        _animation = ui.CreateTimer();
        _animation.Interval = TimeSpan.FromMilliseconds(_mascot.TypingLoop[0].Milliseconds);
        _animation.IsRepeating = true;
        _animation.Tick += (_, _) => Advance();

        // The tray lives on its own thread: every action is handed back to the UI thread.
        _tray.ContextMenu = new PopupMenu
        {
            Items =
            {
                new PopupMenuItem("Refresh", (_, _) => _ui.TryEnqueue(() => _ = refresh())),
                new PopupMenuItem("Show or hide the card", (_, _) => _ui.TryEnqueue(() => toggleCard())),
                new PopupMenuSeparator(),
                new PopupMenuItem("Quit Claudio", (_, _) => _ui.TryEnqueue(() => quit())),
            },
        };
        _tray.MessageWindow.MouseEventReceived += (_, args) =>
        {
            if (args.MouseEvent == MouseEvent.IconLeftMouseUp)
            {
                _ui.TryEnqueue(() => toggleCard());
            }
        };
        _tray.ToolTip = "Claudio";
        _tray.Icon = Icon(_mascot.Poses["resting"]);
        _tray.Create();
    }

    /// <summary>Follows a new reading: the tint of its band, typing or resting, the tooltip.</summary>
    public void Show(CardSummary card, Rgba tint)
    {
        if (tint != _tint)
        {
            foreach (var handle in _icons.Values)
            {
                DestroyIcon(handle);
            }
            _icons.Clear();
            _tint = tint;
        }

        var tooltip = card.IsMeasured ? $"Claudio · {card.Title} {card.Percent} % · {card.Detail}" : $"Claudio · {card.Detail}";
        _tray.UpdateToolTip(tooltip.Length > 127 ? tooltip[..127] : tooltip);

        _isTyping = card.IsRunning;
        if (_isTyping)
        {
            _animation.Start();
        }
        else
        {
            _animation.Stop();
            _tray.UpdateIcon(Icon(_mascot.Poses["resting"]));
        }
    }

    private void Advance()
    {
        _tick++;
        _tray.UpdateIcon(Icon(_mascot.TypingLoop[_tick % _mascot.TypingLoop.Count]));
    }

    /// <summary>One icon per pose and tint, made once: the loop only swaps handles.</summary>
    private nint Icon(SpriteFrame frame)
    {
        if (_icons.TryGetValue(frame.Name, out var cached))
        {
            return cached;
        }
        var size = Math.Max(16, GetSystemMetrics(SmallIconWidth));
        var bytes = TrayIconImage.ToIconResource(TrayIconImage.Fit(TrayIconImage.Paint(frame, _tint, _mascot, _tokens), size));
        var handle = CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconVersion, size, size, 0);
        _icons[frame.Name] = handle;
        return handle;
    }

    public void Dispose()
    {
        _animation.Stop();
        _tray.Dispose();
        foreach (var handle in _icons.Values)
        {
            DestroyIcon(handle);
        }
        _icons.Clear();
    }

    private const int SmallIconWidth = 49; // SM_CXSMICON
    private const uint IconVersion = 0x00030000;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool isIcon,
                                                         uint version, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);
}
