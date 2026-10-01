using Claudio.App.Views;
using Claudio.Core.Design;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Claudio.App;

/// <summary>
/// The update bubble's window, as Claudy's <c>UpdateBubblePanel</c>: above the clock, on top of
/// everything, and shown without taking the focus from whatever the user is typing in.
/// </summary>
internal sealed partial class UpdateBubblePanel : FloatingPanel
{
    private readonly UpdateBubble _bubble;

    public UpdateBubblePanel(UpdateChecker updates, Action close)
    {
        _bubble = new UpdateBubble(updates, close);
        Face = _bubble;
        IsDraggable = false;
        Title = "Claudio";
        ThemeFlipped += Update;
    }

    public void Update()
    {
        _bubble.Update();
        FitToCard();
    }

    public void Present()
    {
        if (AppWindow.IsVisible)
        {
            Update();
            return;
        }
        SetActivatable(false);
        Update();
        AppWindow.Show(activateWindow: false);
        SetTopmost(true);
        Anchor = Corner();
    }

    public void Dismiss()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
    }

    /// <summary>The corner of the work area next to the taskbar, where the notification area is.</summary>
    private PointInt32 Corner()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var work = display.WorkArea;
        var margin = (int)Math.Round(Theme.Metric("screenMargin") * Scale);
        return new PointInt32(work.X + work.Width - margin, work.Y + work.Height - margin);
    }
}
