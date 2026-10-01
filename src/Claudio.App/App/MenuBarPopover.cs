using Claudio.App.Views;
using Claudio.Core.Design;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// The short card above the clock, Claudy's menu bar popover: it opens from the notification-area
/// icon while Claudio lives there, and closes as soon as something else takes the focus.
/// </summary>
internal sealed partial class MenuBarPopover : FloatingPanel
{
    private readonly MenuBarView _view;
    private DateTimeOffset _hiddenAt = DateTimeOffset.MinValue;

    public MenuBarPopover(UsageViewModel model, UpdateChecker updates, Action showWidget)
    {
        _view = new MenuBarView(model, updates, showWidget);
        Face = _view;
        IsDraggable = false;
        Title = "Claudio";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
        }
        ThemeFlipped += Update;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
            {
                _hiddenAt = DateTimeOffset.UtcNow;
                AppWindow.Hide();
            }
        };
    }

    public void Update()
    {
        _view.Update();
        FitToCard();
    }

    /// <summary>
    /// Opens above the clock, or closes when open. A click on the icon first takes the focus from
    /// the flyout, which hides it: a second opening right after that click would be the same click.
    /// </summary>
    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
            return;
        }
        if (DateTimeOffset.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(300))
        {
            return;
        }
        Update();
        Activate();
        SetTopmost(true);
        Anchor = Corner();
        _view.Replay();
    }

    public void HideFlyout()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
    }

    /// <summary>Where the notification area is: the corner of the work area next to the taskbar.</summary>
    private PointInt32 Corner()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var outer = display.OuterBounds;
        var margin = (int)Math.Round(Theme.Metric("screenMargin") * Scale);
        // The taskbar sits where the work area stops short of the screen; the tray is at its end.
        var right = work.X + work.Width - margin;
        var bottom = work.Y + work.Height - margin;
        if (work.Y > outer.Y && work.Height < outer.Height)
        {
            bottom = work.Y + margin;
        }
        return new PointInt32(right, bottom);
    }
}
