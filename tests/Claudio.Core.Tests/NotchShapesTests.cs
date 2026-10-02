using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// Every shape, not only the one Claudio is developed on, as Claudy's NotchShapesTests: notches of
/// other widths and heights, screens of the sizes Windows offers, and a screen placed anywhere
/// around another. The sizes are a sweep, not measurements: what is checked holds for any of them.
/// </summary>
public sealed class NotchShapesTests
{
    private static readonly double[] ScreenWidths = [1024, 1280, 1366, 1440, 1536, 1707, 1920, 2560, 3440];
    private static readonly double[] NotchWidths = [24, 150, 185, 230];
    private static readonly double[] NotchHeights = [24, 32, 37, 44];

    /// <summary>The open island: its view plus the shadow's margin, and a tall content.</summary>
    private static readonly ScreenSize OpenSize = new(440 + (2 * 28), 220);

    private sealed record Case(ScreenRect Frame, ScreenSize Notch, double Skew);

    /// <summary>Origins of the screen: main, left of another, right of it and lower, above it.</summary>
    private static IEnumerable<Case> Cases() =>
        from width in ScreenWidths
        let height = Math.Round(width * 0.5625)
        from origin in new[] { (0.0, 0.0), (-width, 0.0), (2560.0, 300.0), (0.0, -height) }
        from notchWidth in NotchWidths
        from notchHeight in NotchHeights
        from skew in new[] { 0, 1.5 }
        select new Case(new ScreenRect(origin.Item1, origin.Item2, width, height), new ScreenSize(notchWidth, notchHeight), skew);

    private static NotchGeometry Geometry(Case item)
    {
        var side = (item.Frame.Width - item.Notch.Width) / 2;
        return NotchGeometry.Of(new ScreenMetrics(item.Frame, item.Notch.Height, side + item.Skew, side - item.Skew))!;
    }

    [Fact]
    public void TheNotchIsFoundWhereverTheScreenIs()
    {
        foreach (var item in Cases())
        {
            var notch = Geometry(item).Notch;
            Assert.Equal(item.Notch, new ScreenSize(notch.Width, notch.Height));
            Assert.Equal(item.Frame.Y, notch.Y);
            Assert.Equal(item.Frame.MidX + item.Skew, notch.MidX, 3);
        }
    }

    [Fact]
    public void AtRestTheEarsHugTheNotchAndStayOnScreen()
    {
        foreach (var item in Cases())
        {
            var geometry = Geometry(item);
            var resting = new NotchLayout(geometry).RestingFrame;
            Assert.Equal(item.Frame.Y, resting.Y);
            Assert.Equal(item.Notch.Height, resting.Height);
            Assert.Equal(geometry.Notch.MidX, resting.MidX, 3);
            Assert.Equal(item.Notch.Width + (2 * (NotchLayout.EarWidth + NotchLayout.Shoulder)), resting.Width);
            Assert.True(item.Frame.Contains(resting), $"{item}");
        }
    }

    [Fact]
    public void OpenTheIslandStaysOnScreenAndGrowsFromTheEars()
    {
        foreach (var item in Cases())
        {
            var layout = new NotchLayout(Geometry(item));
            var open = layout.Frame(OpenSize);
            Assert.Equal(item.Frame.Y, open.Y);
            Assert.True(item.Frame.Contains(open), $"{item}");
            Assert.Equal(open, NotchLayout.Step(layout.RestingFrame, open).Now);
            Assert.True(layout.OpenWidth(440, 28) >= layout.RestingFrame.Width, $"{item}");
        }
    }

    [Fact]
    public void AScreenWithoutNotchNeverGetsAnIsland()
    {
        foreach (var width in ScreenWidths)
        {
            var flat = new ScreenMetrics(new ScreenRect(0, 0, width, Math.Round(width * 0.5625)));
            Assert.Null(NotchGeometry.Of(flat));
            Assert.Null(NotchGeometry.Find([flat, flat]));
        }
    }
}
