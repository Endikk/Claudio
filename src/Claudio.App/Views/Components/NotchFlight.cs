using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// What the island flies from its ears into its open view, and back, as Claudy's
/// <c>NotchFlight</c>: the mascot and the lead percentage. The island draws them itself the whole
/// way; the open view only marks where they land.
/// </summary>
internal static class NotchFlight
{
    /// <summary>1.5 units per sprite pixel once landed. At rest the mascot is drawn at a third of that.</summary>
    public const double MascotCell = 1.5;

    /// <summary>The mascot's height once landed: the sprite is 27 pixels tall.</summary>
    public const double MascotSize = 27 * MascotCell;

    /// <summary>The landed mascot's width: the sprite's own proportions.</summary>
    public static double MascotWidth
    {
        get
        {
            var sprite = Mascot.Shared.Poses["resting"];
            return MascotSize * sprite.Columns / sprite.Rows.Count;
        }
    }
}

/// <summary>
/// The lead percentage as the open island shows it, as Claudy's <c>IslandPercent</c>: a large
/// figure, then a small "%". The right ear shows the same at 13 points.
/// </summary>
internal sealed partial class IslandPercent : ContentControl
{
    /// <summary>The figure's size once landed.</summary>
    public const double LandedSize = 34;

    /// <summary>The figure at rest, in the right ear.</summary>
    public const double RestingSize = 13;

    private readonly TextBlock _text;
    private readonly double _size;

    public IslandPercent(double size = LandedSize)
    {
        _size = size;
        // SF Rounded semibold is heavier than Nunito's: the bold weight carries the same mass.
        _text = Ui.Text(string.Empty, size, Ui.Bold);
        _text.TextTrimming = Microsoft.UI.Xaml.TextTrimming.None;
        Content = _text;
        IsTabStop = false;
    }

    public void Update(UsageWindow window)
    {
        var scale = _size / LandedSize;
        Ui.Figure(_text, UsageFormat.Percent(window), Ui.Primary(window.IsMeasured ? 0.95 : 0.45), window.IsMeasured,
                  16 * scale, Ui.Primary(0.4), Ui.Medium);
    }
}
