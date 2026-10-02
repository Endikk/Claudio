using Claudio.Core.Design;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The island's window hangs from the top of the screen, centred on the notch, and never leaves
/// the screen, as in Claudy's NotchLayoutTests. It grows at once and shrinks late, so the animated
/// shape is never cut.
/// </summary>
public sealed class NotchLayoutTests
{
    private static NotchGeometry Geometry(ScreenRect? frame = null, double left = 771.5, double right = 771.5) =>
        NotchGeometry.Of(new ScreenMetrics(frame ?? new ScreenRect(0, 0, 1728, 1117), 32, left, right))!;

    [Fact]
    public void AtRestTheEarsFlankTheNotch()
    {
        var layout = new NotchLayout(Geometry());

        // The notch, an ear and a shoulder on each side.
        Assert.Equal(new ScreenSize(185 + (2 * 48) + (2 * 8), 32), layout.RestingSize);
        Assert.Equal(new ScreenRect(715.5, 0, 297, 32), layout.RestingFrame);
    }

    [Fact]
    public void OpenTheShapeHangsFromTheTopCentredOnTheNotch()
    {
        var layout = new NotchLayout(Geometry());

        // The notch's centre is x = 864.
        Assert.Equal(new ScreenRect(719, 0, 290, 400), layout.Frame(new ScreenSize(290, 400)));
    }

    [Fact]
    public void TheFrameNeverLeavesTheScreen()
    {
        var small = new ScreenRect(0, 0, 400, 300);
        var nearLeft = new NotchLayout(Geometry(small, left: 10, right: 200));
        var nearRight = new NotchLayout(Geometry(small, left: 200, right: 10));
        var tall = new ScreenSize(290, 400);

        Assert.Equal(new ScreenRect(0, 0, 290, 300), nearLeft.Frame(tall));
        Assert.Equal(new ScreenRect(110, 0, 290, 300), nearRight.Frame(tall));
    }

    [Fact]
    public void OnASecondScreenTheFramesUseGlobalCoordinates()
    {
        var layout = new NotchLayout(Geometry(new ScreenRect(-1728, 200, 1728, 1117)));

        Assert.Equal(new ScreenRect(-1012.5, 200, 297, 32), layout.RestingFrame);
    }

    [Fact]
    public void GrowingTakesTheLargerFrameAtOnce()
    {
        var layout = new NotchLayout(Geometry());
        var open = layout.Frame(new ScreenSize(512, 220));

        var (now, settle) = NotchLayout.Step(layout.RestingFrame, open);

        Assert.Equal(open, now);
        Assert.Equal(open, settle);
    }

    [Fact]
    public void ShrinkingWaitsForTheShapeToClose()
    {
        var layout = new NotchLayout(Geometry());
        var open = layout.Frame(new ScreenSize(512, 220));

        var (now, settle) = NotchLayout.Step(open, layout.RestingFrame);

        Assert.Equal(open, now);
        Assert.Equal(layout.RestingFrame, settle);
    }

    [Fact]
    public void AWindowWithoutFrameYetTakesTheTargetAsIs()
    {
        var layout = new NotchLayout(Geometry());

        var (now, settle) = NotchLayout.Step(default, layout.RestingFrame);

        Assert.Equal(layout.RestingFrame, now);
        Assert.Equal(layout.RestingFrame, settle);
    }

    /// <summary>Known before the island opens, so the window widens while the island is still at rest.</summary>
    [Fact]
    public void TheOpenWidthCoversTheContentAndItsShadow()
    {
        var layout = new NotchLayout(Geometry());

        // The content, a shoulder on each side, then the shadow's margin.
        Assert.Equal(440 + (2 * 8) + (2 * 28), layout.OpenWidth(440, 28));
        Assert.Equal(297 + (2 * 28), layout.OpenWidth(200, 28));
    }

    /// <summary>The island flares into the screen's top edge the way a notch does, instead of meeting it square.</summary>
    [Fact]
    public void TheShapeFlaresIntoTheTopEdge()
    {
        var rect = new ScreenRect(0, 0, 300, 100);
        bool Holds(double x, double y) => NotchShape.Contains(rect, 20, 8, x, y);

        Assert.True(Holds(6, 1), "glass along the top edge, left");
        Assert.False(Holds(1, 6), "nothing under the left shoulder");
        Assert.True(Holds(294, 1), "glass along the top edge, right");
        Assert.False(Holds(299, 6), "nothing under the right shoulder");
        Assert.True(Holds(150, 50), "the body");
        Assert.False(Holds(3, 50), "the body starts past the shoulder");
    }

    [Fact]
    public void TheBottomCornersAreRounded()
    {
        var rect = new ScreenRect(0, 0, 300, 100);

        Assert.False(NotchShape.Contains(rect, 20, 8, 10, 98), "outside the bottom-left curve");
        Assert.True(NotchShape.Contains(rect, 20, 8, 30, 98), "the bottom edge past the curve");
        Assert.False(NotchShape.Contains(rect, 20, 8, 290, 98), "outside the bottom-right curve");
        Assert.True(NotchShape.Contains(rect, 20, 8, 286, 80), "inside the bottom-right curve");
    }
}
