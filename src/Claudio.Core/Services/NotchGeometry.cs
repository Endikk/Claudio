using System.Globalization;

namespace Claudio.Core.Services;

/// <summary>A rectangle on screen, as Claudy's <c>CGRect</c>: x to the right, y downwards, as Windows counts.</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double MidX => X + (Width / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;

    public bool Contains(ScreenRect other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;

    /// <summary>The smallest rectangle holding both; an empty one adds nothing.</summary>
    public ScreenRect Union(ScreenRect other)
    {
        if (IsEmpty)
        {
            return other;
        }
        if (other.IsEmpty)
        {
            return this;
        }
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new ScreenRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }
}

/// <summary>A size, as Claudy's <c>CGSize</c>.</summary>
public readonly record struct ScreenSize(double Width, double Height);

/// <summary>
/// What the system says about the top edge of one screen, as plain values: Claudy's
/// <c>ScreenMetrics</c>. <see cref="SafeAreaTop"/> is the notch's height, and the two widths the
/// part of the top edge left usable on each side of it. A PC's screens report none: Claudio gives
/// the main one a notch of its own (<see cref="NotchGeometry.Current"/>).
/// </summary>
public sealed record ScreenMetrics(ScreenRect Frame, double SafeAreaTop = 0, double? LeftAreaWidth = null, double? RightAreaWidth = null);

/// <summary>The notch of a screen and the screen it sits on, in global screen coordinates, as Claudy's.</summary>
public sealed record NotchGeometry(ScreenRect ScreenFrame, ScreenRect Notch)
{
    /// <summary>
    /// Windows' notch. No PC has one, so the island hangs from the top centre of the main screen
    /// round a notch of this size: the gap between its ears, the height of a window's title bar.
    /// </summary>
    public const string WindowsNotch = "24x32";

    /// <summary>
    /// The notch of <paramref name="screen"/>, null for a screen without one. The notch is what the
    /// two usable areas leave between them; only their widths are used.
    /// </summary>
    public static NotchGeometry? Of(ScreenMetrics screen)
    {
        if (screen.SafeAreaTop <= 0 || screen.LeftAreaWidth is not { } left || screen.RightAreaWidth is not { } right)
        {
            return null;
        }
        var width = screen.Frame.Width - left - right;
        if (width <= 0)
        {
            return null;
        }
        return new NotchGeometry(screen.Frame, new ScreenRect(screen.Frame.X + left, screen.Frame.Y, width, screen.SafeAreaTop));
    }

    /// <summary>The first notched screen of the list.</summary>
    public static NotchGeometry? Find(IEnumerable<ScreenMetrics> screens) =>
        screens.Select(Of).FirstOrDefault(geometry => geometry is not null);

    /// <summary>
    /// Where the island goes on this PC: round <see cref="WindowsNotch"/> at the top of the first
    /// screen, the main one, unless <c>--simulate-notch</c> says otherwise.
    /// </summary>
    public static NotchGeometry? Current(IReadOnlyList<ScreenMetrics> screens, string? simulation = null) =>
        Find(Simulating(simulation ?? WindowsNotch, screens));

    /// <summary>
    /// The screens as a simulation describes them, as Claudy's <c>-ClaudySimulateNotch</c>: "none"
    /// removes every notch; "WIDTHxHEIGHT" puts a notch of that size at the top centre of the first
    /// screen. Nothing, or anything unreadable, leaves the screens as they are.
    /// </summary>
    public static IReadOnlyList<ScreenMetrics> Simulating(string? value, IReadOnlyList<ScreenMetrics> screens)
    {
        if (value is null)
        {
            return screens;
        }
        if (value == "none")
        {
            return screens.Select(screen => new ScreenMetrics(screen.Frame)).ToList();
        }
        var size = value.Split('x', StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : (double?)null)
                        .OfType<double>()
                        .ToList();
        if (size.Count != 2 || screens.Count == 0)
        {
            return screens;
        }
        var first = screens[0];
        if (size[0] <= 0 || size[1] <= 0 || size[0] >= first.Frame.Width || size[1] >= first.Frame.Height)
        {
            return screens;
        }
        var side = (first.Frame.Width - size[0]) / 2;
        return [new ScreenMetrics(first.Frame, size[1], side, side), .. screens.Skip(1)];
    }
}
