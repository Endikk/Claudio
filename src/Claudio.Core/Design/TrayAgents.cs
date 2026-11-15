namespace Claudio.Core.Design;

/// <summary>
/// What Claudy adds after its figure in the menu bar, while agents run or a session waits: an
/// amber raised hand, then a small Claudy in the colour of the agent at work and how many there
/// are. The notification area holds square icons only, so each gets an icon of its own beside
/// Claudio's figure, drawn to the pixel as the mascot is.
/// </summary>
public static class TrayAgents
{
    /// <summary>SF Symbols' <c>hand.raised.fill</c> as pixel art: four fingers up, the thumb aside.</summary>
    private static readonly string[] Hand =
    [
        "...#.#...",
        ".#.#.#...",
        ".#.#.#.#.",
        ".#.#.#.#.",
        ".#######.",
        "########.",
        ".#######.",
        "..######.",
        "..#####..",
        "...####..",
    ];

    /// <summary>The raised hand in <paramref name="ink"/>, centred in a <paramref name="size"/> square, whole pixels per cell.</summary>
    public static Rgba[,] PaintHand(Rgba ink, int size)
    {
        var square = new Rgba[size, size];
        int height = Hand.Length, width = Hand[0].Length;
        var scale = Math.Max(1, Math.Min(size / width, size / height));
        var left = (size - (width * scale)) / 2;
        var top = (size - (height * scale)) / 2;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                if (Hand[row][column] == '#')
                {
                    Fill(square, left + (column * scale), top + (row * scale), scale, ink);
                }
            }
        }
        return square;
    }

    /// <summary>
    /// The lead agent's Claudy at rest in its tint, and the count in the bottom-right corner on a
    /// dark plate so it reads over the sprite; nine at most, a 16-pixel icon having no room for two digits.
    /// </summary>
    public static Rgba[,] PaintAgents(int count, Rgba tint, int size, Mascot mascot, DesignTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(mascot);
        var resting = mascot.Poses["resting"];
        var image = TrayIconImage.Fit(TrayIconImage.Paint(TrayIconImage.ForIcon(resting, size), tint, mascot, tokens), size);
        var text = count > 9 ? "9" : Math.Max(count, 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var digits = TrayFigure.Paint(text, tint, 7);
        var scale = Math.Max(1, size / 16);
        var plate = (7 * scale) + 2;
        var left = size - plate;
        var top = size - plate;
        var dark = new Rgba(0x1C, 0x17, 0x14);
        for (var y = 0; y < plate; y++)
        {
            for (var x = 0; x < plate; x++)
            {
                image[top + y, left + x] = dark;
            }
        }
        for (var y = 0; y < 7; y++)
        {
            for (var x = 0; x < 7; x++)
            {
                if (digits[y, x].A > 0)
                {
                    Fill(image, left + 1 + (x * scale), top + 1 + (y * scale), scale, tint);
                }
            }
        }
        return image;
    }

    private static void Fill(Rgba[,] square, int x, int y, int side, Rgba ink)
    {
        var size = square.GetLength(0);
        for (var dy = 0; dy < side; dy++)
        {
            for (var dx = 0; dx < side; dx++)
            {
                if (x + dx >= 0 && x + dx < size && y + dy >= 0 && y + dy < size)
                {
                    square[y + dy, x + dx] = ink;
                }
            }
        }
    }
}
