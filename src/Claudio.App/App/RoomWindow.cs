using Claudio.Core.Design;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// The agent room's window, as Claudy's <c>RoomPanel</c> and <c>RoomWindowController</c>: the
/// card's borderless glass, draggable from anywhere like the card, but an ordinary window otherwise.
/// It does not float over the others, it takes the keyboard, it is listed in Alt+Tab, and Escape
/// closes it. It opens in the middle of the screen the pointer is on. Made at launch, as every
/// window of Claudio is, and only shown when the room is opened; a closed room holds no view, so it
/// draws nothing and walks nobody.
/// </summary>
internal sealed partial class RoomWindow : FloatingPanel
{
    private bool _isCentring;

    public RoomWindow()
    {
        Title = "Claudio · Agents";
        AppWindow.IsShownInSwitchers = true;
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            args.Handled = true;
            CloseRequested?.Invoke();
        };
        Card.KeyboardAccelerators.Add(escape);
        Card.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        // Alt+F4 closes the room as its own button does: the window stays, for the next opening.
        AppWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            CloseRequested?.Invoke();
        };
        Card.SizeChanged += (_, _) => Centre();
    }

    /// <summary>Escape, Alt+F4: the room asks to be closed, as its close button does.</summary>
    public event Action? CloseRequested;

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>Shows the room, or brings it forward when it is open already.</summary>
    public void Open(UIElement view)
    {
        if (IsOpen)
        {
            Activate();
            WindowsSessions.Raise(WinRT.Interop.WindowNative.GetWindowHandle(this));
            return;
        }
        Face = view;
        SetActivatable(true);
        SetTopmost(false);
        _isCentring = true;
        PlaceRoughly();
#if DEBUG
        // Filming takes the keyboard from nobody.
        if (App.IsFilming)
        {
            AppWindow.Show(activateWindow: false);
            Centre();
            return;
        }
#endif
        AppWindow.Show(activateWindow: true);
        Activate();
        // Opened from a shortcut or a menu while another app is in front: Windows keeps a new
        // window behind it unless it is brought forward, and a room nobody sees is no room.
        WindowsSessions.Raise(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Centre();
    }

    /// <summary>Hides the window and lets the view go: the canvas unloads and stops drawing.</summary>
    public void Dismiss()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
        Face = null;
    }

    /// <summary>The screen the pointer is on: the room opens there.</summary>
    private static DisplayArea PointerDisplay() => DisplayArea.GetFromPoint(PanelChrome.Cursor(), DisplayAreaFallback.Primary);

    /// <summary>
    /// Before the first layout the card has no size yet: the window is put where a card of the
    /// room's usual size would be centred, so that it never shows elsewhere first.
    /// </summary>
    private void PlaceRoughly()
    {
        var area = PointerDisplay().WorkArea;
        var scale = PanelChrome.ScaleAt(new PointInt32(area.X + (area.Width / 2), area.Y + (area.Height / 2)));
        var padding = Theme.Metric("panelPadding");
        var gap = Theme.Metric("gap");
        var inset = CardShadow.Inset;
        var width = (RoomCanvas.Size.Width + (2 * padding) + (2 * inset)) * scale;
        var height = (RoomCanvas.Size.Height + 44 + 36 + (2 * gap) + (2 * padding) + (2 * inset)) * scale;
        AppWindow.MoveAndResize(new RectInt32(area.X + (int)((area.Width - width) / 2), area.Y + (int)((area.Height - height) / 2),
                                              (int)Math.Ceiling(width), (int)Math.Ceiling(height)));
    }

    /// <summary>Once laid out, the card's own size puts it in the middle of the work area, its anchor its bottom-right corner.</summary>
    private void Centre()
    {
        if (!_isCentring || VisualWidth <= 0 || VisualHeight <= 0)
        {
            return;
        }
        _isCentring = false;
        var area = PointerDisplay().WorkArea;
        var (width, height) = (VisualWidth, VisualHeight);
        var left = area.X + ((area.Width - width) / 2);
        var top = area.Y + ((area.Height - height) / 2);
        Anchor = new PointInt32(left + width, top + height);
    }
}
