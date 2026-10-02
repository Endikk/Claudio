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
    private RectInt32? _card;
    private PointInt32? _target;

    public UpdateBubblePanel(UpdateChecker updates, Action close)
    {
        _bubble = new UpdateBubble(updates, close);
        Face = _bubble;
        IsDraggable = false;
        Title = "Claudio";
        ThemeFlipped += Update;
        // Its height is known once it is laid out: then it picks its side of the card.
        Placed += Settle;
    }

    public void Update()
    {
        _bubble.Update();
        FitToCard();
    }

    /// <summary>
    /// Shows the bubble next to <paramref name="card"/>, the floating card's bounds on screen, or
    /// above the clock when none is given; moves it there when it is already up.
    /// </summary>
    public void Present(RectInt32? card = null)
    {
        _card = card;
        if (!AppWindow.IsVisible)
        {
            SetActivatable(false);
            Update();
            AppWindow.Show(activateWindow: false);
            SetTopmost(true);
        }
        else
        {
            Update();
        }
        Settle();
    }

    public void Dismiss()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
    }

    /// <summary>
    /// Anchors the bubble where it belongs. Once per target: the panel may clamp the anchor into
    /// the screen, and asking again for the unclamped one would go round forever.
    /// </summary>
    private void Settle()
    {
        var target = Target();
        if (_target != target)
        {
            _target = target;
            Anchor = target;
        }
    }

    /// <summary>
    /// Where the bubble's bottom-right corner goes: a margin above the card, right-aligned with it,
    /// or below it when the screen has no room above; above the clock without a card.
    /// </summary>
    private PointInt32 Target()
    {
        if (_card is not { } card)
        {
            return Corner();
        }
        var area = DisplayArea.GetFromPoint(new PointInt32(card.X, card.Y), DisplayAreaFallback.Nearest).WorkArea;
        var margin = (int)Math.Round(Theme.Metric("screenMargin") * Scale);
        var right = card.X + card.Width;
        var above = card.Y - margin;
        return above - VisualHeight >= area.Y + margin
            ? new PointInt32(right, above)
            : new PointInt32(right, card.Y + card.Height + margin + VisualHeight);
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
