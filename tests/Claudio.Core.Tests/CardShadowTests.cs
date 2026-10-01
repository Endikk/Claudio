using Claudio.Core.Design;

namespace Claudio.Core.Tests;

/// <summary>The card's shadow, as Claudy's CardShadowTests: inside the window, under nothing, visible below.</summary>
public sealed class CardShadowTests
{
    public static TheoryData<double, double, double> Cards() => new()
    {
        { 340, 520, 1 },
        { 340, 520, 1.5 },
        { 252, 46, 2 },
    };

    [Fact]
    public void TheTokensCarryTwoLayers()
    {
        Assert.Equal(2, CardShadow.Layers.Count);
        Assert.Equal(new ShadowLayer(new Rgba(0, 0, 0, 0x4c), 0, 4, 8), CardShadow.Layers[0]);
        Assert.Equal(28, CardShadow.Inset);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void TheShadowFadesOutBeforeTheWindowEdge(double width, double height, double scale)
    {
        var (pixels, w, h) = CardShadow.Render(width, height, 20, scale);
        var border = Enumerable.Range(0, w).SelectMany(x => new[] { Alpha(pixels, w, x, 0), Alpha(pixels, w, x, h - 1) })
                               .Concat(Enumerable.Range(0, h).SelectMany(y => new[] { Alpha(pixels, w, 0, y), Alpha(pixels, w, w - 1, y) }));
        Assert.True(border.Max() <= 1);
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void TheShadowShowsBelowTheCard(double width, double height, double scale)
    {
        var (pixels, w, _) = CardShadow.Render(width, height, 20, scale);
        var justBelow = Alpha(pixels, w, w / 2, (int)((CardShadow.Inset + height + 2) * scale));
        Assert.True(justBelow > 20, $"alpha {justBelow}");
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void NothingIsPaintedUnderTheCard(double width, double height, double scale)
    {
        var (pixels, w, h) = CardShadow.Render(width, height, 20, scale);
        Assert.Equal(0, Alpha(pixels, w, w / 2, h / 2));
    }

    private static byte Alpha(byte[] pixels, int width, int x, int y) => pixels[(((y * width) + x) * 4) + 3];
}
