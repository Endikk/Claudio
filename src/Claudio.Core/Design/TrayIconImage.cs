namespace Claudio.Core.Design;

/// <summary>
/// Turns a mascot frame into the image the notification area shows. The icon there is square and
/// small (16 pixels at 100 %, 32 at 200 %): the frame is painted at one pixel per cell, then fitted
/// into the square, whole pixels per cell when they fit, averaged down when they do not.
/// </summary>
public static class TrayIconImage
{
    /// <summary>The frame painted with the given tint, one pixel per cell, transparent where empty.</summary>
    public static Rgba[,] Paint(SpriteFrame frame, Rgba tint, Mascot mascot, DesignTokens tokens)
    {
        var pixels = new Rgba[frame.Rows.Count, frame.Columns];
        var cache = new Dictionary<char, Rgba?>();
        for (var row = 0; row < frame.Rows.Count; row++)
        {
            for (var column = 0; column < frame.Columns; column++)
            {
                var ink = frame.Rows[row][column];
                if (!cache.TryGetValue(ink, out var colour))
                {
                    colour = mascot.Paint(ink, tint, tokens);
                    cache[ink] = colour;
                }
                pixels[row, column] = colour ?? default;
            }
        }
        return pixels;
    }

    /// <summary>
    /// The frame to paint into a <paramref name="size"/>-pixel icon. The mascot is 29 cells wide:
    /// from 29 pixels up it is drawn whole, one or more pixels per cell. Below, at the 16 and 24
    /// pixels of the notification area, averaging it down blurs it into a smudge; it is redrawn at
    /// half size instead, one pixel per block of four cells, so every pixel stays a pixel-art ink.
    /// </summary>
    public static SpriteFrame ForIcon(SpriteFrame frame, int size) =>
        size >= Math.Max(frame.Columns, frame.Rows.Count) ? frame : Halve(frame);

    /// <summary>
    /// Half the frame, one cell per block of four: the eye wins any block it is in, a block with
    /// fewer than two inked cells stays empty, otherwise the ink most of the block holds, the
    /// outline yielding to the body on a tie. Blocks start on the second row, which keeps each eye
    /// in a single cell.
    /// </summary>
    public static SpriteFrame Halve(SpriteFrame frame)
    {
        var rows = new List<string>();
        var height = frame.Rows.Count;
        var width = frame.Columns;
        for (var y = 1; y < height; y += 2)
        {
            var line = new char[(width + 1) / 2];
            for (var x = 0; x < width; x += 2)
            {
                var inks = new List<char>(4);
                for (var dy = 0; dy < 2; dy++)
                {
                    for (var dx = 0; dx < 2; dx++)
                    {
                        if (y + dy < height && x + dx < width && frame.Rows[y + dy][x + dx] != '.')
                        {
                            inks.Add(frame.Rows[y + dy][x + dx]);
                        }
                    }
                }
                line[x / 2] = inks.Contains('E') ? 'E'
                    : inks.Count < 2 ? '.'
                    : inks.GroupBy(ink => ink).OrderByDescending(group => group.Count()).ThenBy(group => group.Key == 'o' ? 1 : 0)
                          .ThenBy(group => group.Key).First().Key;
            }
            rows.Add(new string(line));
        }
        return frame with { Name = frame.Name + "-half", Rows = rows };
    }

    /// <summary>The image centred in a <paramref name="size"/> square, its proportions kept.</summary>
    public static Rgba[,] Fit(Rgba[,] image, int size)
    {
        int height = image.GetLength(0), width = image.GetLength(1);
        var square = new Rgba[size, size];
        var scale = Math.Min((double)size / width, (double)size / height);

        if (scale >= 1)
        {
            var cell = (int)Math.Floor(scale);
            int left = (size - width * cell) / 2, top = (size - height * cell) / 2;
            for (var y = 0; y < height * cell; y++)
            {
                for (var x = 0; x < width * cell; x++)
                {
                    square[top + y, left + x] = image[y / cell, x / cell];
                }
            }
            return square;
        }

        // Smaller than the frame: each target pixel averages the cells it covers, alpha-weighted
        // so the transparent surroundings do not darken the edges.
        double outWidth = width * scale, outHeight = height * scale;
        double offsetX = (size - outWidth) / 2, offsetY = (size - outHeight) / 2;
        const int Samples = 4;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var sourceX = (x + (sx + 0.5) / Samples - offsetX) / scale;
                        var sourceY = (y + (sy + 0.5) / Samples - offsetY) / scale;
                        if (sourceX < 0 || sourceY < 0 || sourceX >= width || sourceY >= height)
                        {
                            continue;
                        }
                        var pixel = image[(int)sourceY, (int)sourceX];
                        var alpha = pixel.A / 255.0;
                        r += pixel.R * alpha;
                        g += pixel.G * alpha;
                        b += pixel.B * alpha;
                        a += alpha;
                    }
                }
                if (a > 0)
                {
                    square[y, x] = new Rgba((byte)Math.Round(r / a), (byte)Math.Round(g / a), (byte)Math.Round(b / a),
                                            (byte)Math.Round(a / (Samples * Samples) * 255));
                }
            }
        }
        return square;
    }

    /// <summary>
    /// The icon with Claudy's coral update dot in its top-right corner, where the sprite is empty:
    /// a new version is out. A quarter of the icon wide, whole pixels, no anti-aliasing to blur it.
    /// </summary>
    public static Rgba[,] WithUpdateDot(Rgba[,] square, Rgba colour)
    {
        var size = square.GetLength(0);
        var diameter = Math.Max(3, size / 4);
        var dotted = (Rgba[,])square.Clone();
        var radius = diameter / 2.0;
        for (var y = 0; y < diameter; y++)
        {
            for (var x = 0; x < diameter; x++)
            {
                var dx = x + 0.5 - radius;
                var dy = y + 0.5 - radius;
                if ((dx * dx) + (dy * dy) <= (radius * radius) + 0.25)
                {
                    dotted[y, size - diameter + x] = colour;
                }
            }
        }
        return dotted;
    }

    /// <summary>
    /// The image as an icon resource, the bytes <c>CreateIconFromResourceEx</c> reads: a
    /// BITMAPINFOHEADER, 32-bit BGRA rows bottom-up, then an all-clear AND mask (alpha decides).
    /// </summary>
    public static byte[] ToIconResource(Rgba[,] square)
    {
        int height = square.GetLength(0), width = square.GetLength(1);
        var maskRow = (width + 31) / 32 * 4;
        var colourBytes = width * height * 4;
        var bytes = new byte[40 + colourBytes + maskRow * height];

        void Write(int offset, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
        Write(0, 40);
        Write(4, width);
        Write(8, height * 2);            // colour and mask stacked
        bytes[12] = 1;                   // planes
        bytes[14] = 32;                  // bits per pixel
        Write(20, colourBytes + maskRow * height);

        var position = 40;
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = square[y, x];
                bytes[position++] = pixel.B;
                bytes[position++] = pixel.G;
                bytes[position++] = pixel.R;
                bytes[position++] = pixel.A;
            }
        }
        return bytes;
    }
}
