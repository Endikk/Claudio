using Claudio.Core.Design;

namespace Claudio.Core.Tests;

/// <summary>
/// The open island casts the card's shadow into a transparent margin on its sides and below it, as
/// in Claudy's NotchShadowTests. Its top is the screen's edge: nothing to fade there. Whatever
/// reaches the other edges of the window is cut straight, which shows as a grey box.
/// </summary>
public sealed class NotchShadowTests
{
    private const double Width = 290;
    private const double Height = 400;
    private const double Radius = 20;

    public static TheoryData<double> Scales() => [1, 1.5, 2];

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheImageHangsFromTheTopEdge(double scale)
    {
        var (_, width, height) = CardShadow.Island(Width, Height, Radius, scale);

        Assert.Equal((int)Math.Ceiling((Width + (2 * CardShadow.Inset)) * scale), width);
        Assert.Equal(Height + CardShadow.Inset, height / scale, 0);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheShadowFadesOutBeforeTheSideAndBottomEdges(double scale)
    {
        var (pixels, width, height) = CardShadow.Island(Width, Height, Radius, scale);
        var edges = Enumerable.Range(0, width).Select(x => Alpha(pixels, width, x, height - 1))
                              .Concat(Enumerable.Range(0, height).SelectMany(y => new[] { Alpha(pixels, width, 0, y), Alpha(pixels, width, width - 1, y) }));

        Assert.True(edges.Max() <= 1);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheShadowShowsBelowTheIsland(double scale)
    {
        var (pixels, width, _) = CardShadow.Island(Width, Height, Radius, scale);

        var justBelow = Alpha(pixels, width, width / 2, (int)((Height + 2) * scale));
        Assert.True(justBelow > 20, $"alpha {justBelow}");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void TheShadowShowsBesideTheIsland(double scale)
    {
        var (pixels, width, _) = CardShadow.Island(Width, Height, Radius, scale);

        var justLeft = Alpha(pixels, width, (int)((CardShadow.Inset - 2) * scale), (int)(Height / 2 * scale));
        Assert.True(justLeft > 10, $"alpha {justLeft}");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void NothingIsPaintedUnderTheIsland(double scale)
    {
        var (pixels, width, _) = CardShadow.Island(Width, Height, Radius, scale);

        Assert.Equal(0, Alpha(pixels, width, width / 2, (int)(Height / 2 * scale)));
        Assert.Equal(0, Alpha(pixels, width, width / 2, 0));
    }

    private static byte Alpha(byte[] pixels, int width, int x, int y) => pixels[(((y * width) + x) * 4) + 3];
}
