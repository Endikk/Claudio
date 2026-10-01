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
}
