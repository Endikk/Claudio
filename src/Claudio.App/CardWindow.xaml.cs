using Claudio.Core.Design;
using Claudio.Core.Presentation;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>
/// The floating card: no frame, always on top, out of the taskbar, settled in the bottom-right
/// corner of the work area, above the clock.
/// </summary>
public sealed partial class CardWindow : Window
{
    private readonly DesignTokens _tokens = DesignTokens.Shared;

    public CardWindow()
    {
        InitializeComponent();

        var width = _tokens.Dimension("dimension.fullWidth");
        var margin = _tokens.Dimension("dimension.screenMargin");

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Claudio.ico"));

        var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
        var size = new Windows.Graphics.SizeInt32((int)(width * scale), (int)(170 * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + area.Width - size.Width - (int)(margin * scale),
            area.Y + area.Height - size.Height - (int)(margin * scale),
            size.Width,
            size.Height));

        Mascot.Tint = _tokens.Color("color.accent.coral");
    }

    /// <summary>Shows one reading: the lead quota, its colour band, its reset, and its origin.</summary>
    public void Show(CardSummary card)
    {
        Caption.Text = $"{card.Title} · {card.Window}".ToUpperInvariant();
        Percent.Text = card.Percent;
        Percent.Opacity = card.IsMeasured ? 0.95 : 0.45;
        PercentSign.Visibility = card.IsMeasured ? Visibility.Visible : Visibility.Collapsed;
        Detail.Text = card.Detail;

        var tint = Tint(card);
        Mascot.Tint = tint;
        Mascot.IsTyping = card.IsRunning;

        if (card.Badge is { } badge)
        {
            var amber = _tokens.Color("color.accent.amber");
            Badge.Text = badge;
            Badge.Foreground = Brush(amber);
            BadgePill.Background = Brush(amber with { A = 40 });
            BadgePill.Visibility = Visibility.Visible;
        }
        else
        {
            BadgePill.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>The gauge's own accent, amber from 75 %, danger from 90 %: Claudy's bands.</summary>
    private Rgba Tint(CardSummary card)
    {
        if (card.IsDanger)
        {
            return _tokens.Color("color.danger");
        }
        if (card.IsMeasured && card.Fraction >= CardSummary.Thresholds.Amber)
        {
            return _tokens.Color("color.accent.amber");
        }
        return _tokens.Color($"color.accent.{card.Accent.ToString().ToLowerInvariant()}");
    }

    private static SolidColorBrush Brush(Rgba colour) =>
        new(Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B));
}
