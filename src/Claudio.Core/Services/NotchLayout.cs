namespace Claudio.Core.Services;

/// <summary>
/// Where the island's window goes, as Claudy's <c>NotchLayout</c>: hung from the top edge of the
/// screen, centred on the notch, never past the screen's sides.
/// </summary>
public sealed record NotchLayout(NotchGeometry Geometry)
{
    /// <summary>Width of each ear: the mascot on the left of the notch, the percentage on its right.</summary>
    public const double EarWidth = 48;

    /// <summary>
    /// Width of the curve on each side where the island flares into the screen's top edge, as a
    /// notch does, instead of meeting it square.
    /// </summary>
    public const double Shoulder = 8;

    /// <summary>The notch plus an ear and a shoulder on each side, the notch's height.</summary>
    public ScreenSize RestingSize => new(Geometry.Notch.Width + (2 * (EarWidth + Shoulder)), Geometry.Notch.Height);

    public ScreenRect RestingFrame => Frame(RestingSize);

    /// <summary>
    /// The open island's width: its content and a shoulder on each side, never narrower than the
    /// ears, plus the shadow's margin on both sides.
    /// </summary>
    public double OpenWidth(double content, double margin) => Math.Max(RestingSize.Width, content + (2 * Shoulder)) + (2 * margin);

    /// <summary>The window for a shape of <paramref name="size"/>, open or not.</summary>
    public ScreenRect Frame(ScreenSize size)
    {
        var screen = Geometry.ScreenFrame;
        var width = Math.Min(size.Width, screen.Width);
        var height = Math.Min(size.Height, screen.Height);
        var x = Math.Min(Math.Max(Geometry.Notch.MidX - (width / 2), screen.X), screen.Right - width);
        return new ScreenRect(x, screen.Y, width, height);
    }

    /// <summary>
    /// The frame to take now and the one to settle on once the shape stops moving. Both frames hang
    /// from the same top edge, so their union is the larger one: growing is immediate and the opening
    /// shape is never cut, shrinking waits until the closing shape fits.
    /// </summary>
    public static (ScreenRect Now, ScreenRect Settle) Step(ScreenRect current, ScreenRect target) =>
        (current.IsEmpty ? target : current.Union(target), target);
}
