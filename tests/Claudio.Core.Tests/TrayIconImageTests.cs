using Claudio.Core.Design;

namespace Claudio.Core.Tests;

public sealed class TrayIconImageTests
{
    private readonly Mascot _mascot = Mascot.Shared;
    private readonly DesignTokens _tokens = DesignTokens.Shared;

    private Rgba[,] Resting() =>
        TrayIconImage.Paint(_mascot.Poses["resting"], _tokens.Color("color.accent.coral"), _mascot, _tokens);

    [Fact]
    public void PaintsOnePixelPerCell()
    {
        var image = Resting();
        Assert.Equal(27, image.GetLength(0));
        Assert.Equal(29, image.GetLength(1));
        Assert.Equal(0, image[0, 0].A);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(64)]
    public void FitsEveryNotificationAreaSize(int size)
    {
        var square = TrayIconImage.Fit(Resting(), size);

        Assert.Equal(size, square.GetLength(0));
        Assert.Equal(size, square.GetLength(1));
        var opaque = Enumerable.Range(0, size * size).Count(index => square[index / size, index % size].A > 0);
        Assert.InRange(opaque, size * size / 5, size * size);
    }

    [Fact]
    public void At32PixelsEveryCellIsAWholePixel()
    {
        var image = Resting();
        var square = TrayIconImage.Fit(image, 32);
        // 29 × 27 centred in 32 × 32: one pixel per cell, offset by (1, 2).
        Assert.Equal(image[13, 14], square[15, 15]);
    }

    [Fact]
    public void TheResourceHasTheLayoutWindowsReads()
    {
        var bytes = TrayIconImage.ToIconResource(TrayIconImage.Fit(Resting(), 16));

        Assert.Equal(40, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(16, BitConverter.ToInt32(bytes, 4));
        Assert.Equal(32, BitConverter.ToInt32(bytes, 8));
        Assert.Equal(32, bytes[14]);
        Assert.Equal(40 + 16 * 16 * 4 + 4 * 16, bytes.Length);
    }

    [Fact]
    public void A16PixelIconUsesTheHalfSizeMascot()
    {
        var half = TrayIconImage.ForIcon(_mascot.Poses["resting"], 16);
        Assert.Equal(15, half.Columns);
        Assert.Equal(13, half.Rows.Count);
        // Both eyes survive, one cell each.
        Assert.Equal(2, half.Rows.Sum(row => row.Count(ink => ink == 'E')));
    }

    [Fact]
    public void A32PixelIconKeepsTheWholeMascot() =>
        Assert.Same(_mascot.Poses["resting"], TrayIconImage.ForIcon(_mascot.Poses["resting"], 32));

    [Fact]
    public void TheHalfSizeIconIsCrisp()
    {
        // Fitted at one pixel per cell, every pixel is a palette ink or empty: nothing is averaged.
        var tint = _tokens.Color("color.accent.coral");
        var half = TrayIconImage.ForIcon(_mascot.Poses["resting"], 16);
        var inks = half.Rows.SelectMany(row => row).Distinct().Where(ink => ink != '.')
                       .Select(ink => _mascot.Paint(ink, tint, _tokens)!.Value).ToHashSet();
        var square = TrayIconImage.Fit(TrayIconImage.Paint(half, tint, _mascot, _tokens), 16);
        foreach (var pixel in square)
        {
            Assert.True(pixel.A == 0 || inks.Contains(pixel), $"blended pixel {pixel}");
        }
    }

    [Theory]
    [InlineData(0.42, "42%")]
    [InlineData(0.999, "99%")]
    [InlineData(1.0, "100")]
    [InlineData(null, "—")]
    public void TheFigureReadsLikeClaudysMenuBar(double? fraction, string expected) =>
        Assert.Equal(expected, TrayFigure.Text(fraction));

    [Theory]
    [InlineData("42%", 16)]
    [InlineData("100", 16)]
    [InlineData("42%", 32)]
    public void TheFigureFitsTheIconCrisply(string text, int size)
    {
        var ink = new Rgba(255, 255, 255);
        var square = TrayFigure.Paint(text, ink, size);
        var inked = 0;
        foreach (var pixel in square)
        {
            Assert.True(pixel == ink || pixel.A == 0);
            inked += pixel == ink ? 1 : 0;
        }
        Assert.True(inked > 10);
        // Centred with room to spare: the last column stays clear, nothing is cut at the edge.
        Assert.All(Enumerable.Range(0, size), row => Assert.Equal(0, square[row, size - 1].A));
    }

    [Fact]
    public void TheUpdateDotSitsInTheTopRightCorner()
    {
        var coral = _tokens.Color("color.accent.coral");
        var dotted = TrayIconImage.WithUpdateDot(new Rgba[16, 16], coral);
        Assert.Equal(coral, dotted[1, 14]);
        Assert.Equal(0, dotted[15, 0].A);
        Assert.Equal(0, dotted[8, 8].A);
    }

    [Fact]
    public void TheStillWaveHasItsArmUp() => Assert.Equal("04-wave-up", _mascot.WaveStill.Name);
}
