namespace Claudio.Core.Design;

/// <summary>
/// The lead percentage next to the clock, as Claudy writes "42%" beside its mascot in the menu bar.
/// The notification area only holds square icons, so the figure gets an icon of its own, drawn in a
/// pixel font so it stays as sharp as the mascot: "42%", "100" (the sign has no room left at 100),
/// or "—" when there is no measurement.
/// </summary>
public static class TrayFigure
{
    private const int GlyphHeight = 7;

    /// <summary>Four columns by seven rows per digit, the sign five columns wide.</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = [".##.", "#..#", "#..#", "#..#", "#..#", "#..#", ".##."],
        ['1'] = ["..#.", ".##.", "..#.", "..#.", "..#.", "..#.", ".###"],
        ['2'] = [".##.", "#..#", "...#", "..#.", ".#..", "#...", "####"],
        ['3'] = ["###.", "...#", "...#", ".##.", "...#", "...#", "###."],
        ['4'] = ["..#.", ".##.", "#.#.", "#.#.", "####", "..#.", "..#."],
        ['5'] = ["####", "#...", "###.", "...#", "...#", "#..#", ".##."],
        ['6'] = [".##.", "#...", "#...", "###.", "#..#", "#..#", ".##."],
        ['7'] = ["####", "...#", "..#.", "..#.", ".#..", ".#..", ".#.."],
        ['8'] = [".##.", "#..#", "#..#", ".##.", "#..#", "#..#", ".##."],
        ['9'] = [".##.", "#..#", "#..#", ".###", "...#", "...#", ".##."],
        ['%'] = ["##...", "##..#", "...#.", "..#..", ".#...", "#..##", "...##"],
        ['—'] = ["....", "....", "....", "####", "....", "....", "...."],
    };

    /// <summary>What the icon reads for a measured fraction, or for no measurement.</summary>
    public static string Text(double? fraction)
    {
        if (fraction is not { } value)
        {
            return "—";
        }
        var percent = (int)(Math.Clamp(value, 0, 1) * 100);
        return percent >= 100 ? "100" : $"{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
    }

    /// <summary>
    /// The text drawn into a <paramref name="size"/> square in <paramref name="ink"/>, centred, at
    /// the largest whole number of pixels per font pixel that fits.
    /// </summary>
    public static Rgba[,] Paint(string text, Rgba ink, int size)
    {
        var glyphs = text.Select(character => Glyphs.TryGetValue(character, out var glyph) ? glyph : Glyphs['—']).ToList();
        var width = glyphs.Sum(glyph => glyph[0].Length) + Math.Max(glyphs.Count - 1, 0);
        var scale = Math.Max(1, Math.Min(size / width, size / GlyphHeight));
        var square = new Rgba[size, size];
        var left = (size - (width * scale)) / 2;
        var top = (size - (GlyphHeight * scale)) / 2;
        foreach (var glyph in glyphs)
        {
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var column = 0; column < glyph[row].Length; column++)
                {
                    if (glyph[row][column] != '#')
                    {
                        continue;
                    }
                    for (var dy = 0; dy < scale; dy++)
                    {
                        for (var dx = 0; dx < scale; dx++)
                        {
                            var x = left + (column * scale) + dx;
                            var y = top + (row * scale) + dy;
                            if (x >= 0 && x < size && y >= 0 && y < size)
                            {
                                square[y, x] = ink;
                            }
                        }
                    }
                }
            }
            left += (glyph[0].Length + 1) * scale;
        }
        return square;
    }
}
