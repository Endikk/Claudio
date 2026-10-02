using Claudio.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// The island's window, as Claudy's <c>NotchPanel</c>: borderless and transparent, never taking the
/// focus but for the sign-in code, above every other window, out of Alt+Tab. It reports the pointer
/// entering and leaving the whole window, and asks for Claudy's menu on a right click.
/// </summary>
internal sealed partial class NotchPanel : Window
{
    private readonly Grid _root = new() { Background = new SolidColorBrush(Colors.Transparent) };

    public NotchPanel(UIElement face)
    {
        _root.Children.Add(face);
        Content = _root;
        Title = "Claudio";
        SystemBackdrop = new TransparentBackdrop();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Claudio.ico"));
        PanelChrome.RemoveFrame(AppWindow);
        PanelChrome.SetActivatable(AppWindow, false);

        _root.PointerEntered += (_, _) => PointerCrossed?.Invoke(true);
        _root.PointerExited += (_, _) => PointerCrossed?.Invoke(false);
        _root.ContextRequested += (_, args) =>
        {
            args.Handled = true;
            MenuRequested?.Invoke(_root, args.TryGetPosition(_root, out var point) ? point : new Point(_root.ActualWidth / 2, 16));
        };
    }

    /// <summary>The pointer entered (true) or left (false) the window.</summary>
    public event Action<bool>? PointerCrossed;

    /// <summary>A right click, at that point of the given element.</summary>
    public event Action<FrameworkElement, Point>? MenuRequested;

    public bool IsShown => AppWindow.IsVisible;

    /// <summary>The window on screen, in pixels.</summary>
    public RectInt32 Frame => new(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);

    /// <summary>Moves the window, and takes clicks over all of it: the ears at rest, the island and its shadow open.</summary>
    public void SetFrame(RectInt32 frame)
    {
        AppWindow.MoveAndResize(frame);
        PanelChrome.SetReach(AppWindow, 0, 0, frame.Width, frame.Height);
    }

    /// <summary>Shown without taking the focus, over every other window.</summary>
    public void ShowPanel()
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show(activateWindow: false);
        }
        PanelChrome.SetTopmost(AppWindow, true);
    }

    public void HidePanel()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
    }

    /// <summary>The pasted sign-in code is the one thing typed into the island: only then does it take the keyboard.</summary>
    public void SetTyping(bool typing)
    {
        PanelChrome.SetActivatable(AppWindow, typing);
        if (typing)
        {
            Activate();
        }
    }

    /// <summary>The pointer is over the window, whatever was last reported.</summary>
    public bool HoldsCursor()
    {
        var cursor = PanelChrome.Cursor();
        var frame = Frame;
        return AppWindow.IsVisible && new ScreenRect(frame.X, frame.Y, frame.Width, frame.Height).Contains(cursor.X, cursor.Y);
    }
}
