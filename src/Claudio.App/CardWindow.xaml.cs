using Claudio.Core.Design;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Claudio.App;

/// <summary>
/// The floating card: no frame, always on top, out of the taskbar, settled in the bottom-right
/// corner of the work area, above the clock.
/// </summary>
public sealed partial class CardWindow : Window
{
    public CardWindow()
    {
        InitializeComponent();

        var tokens = DesignTokens.Shared;
        var width = tokens.Dimension("dimension.fullWidth");
        var margin = tokens.Dimension("dimension.screenMargin");

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
        var size = new Windows.Graphics.SizeInt32((int)(width * scale), (int)(160 * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + area.Width - size.Width - (int)(margin * scale),
            area.Y + area.Height - size.Height - (int)(margin * scale),
            size.Width,
            size.Height));

        Mascot.Tint = tokens.Color("color.accent.coral");
        Mascot.IsTyping = true;
    }
}
