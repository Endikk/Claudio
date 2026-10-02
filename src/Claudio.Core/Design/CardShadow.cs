using System.Globalization;
using System.Text.Json;

namespace Claudio.Core.Design;

/// <summary>One drop shadow layer, in device-independent pixels.</summary>
public sealed record ShadowLayer(Rgba Color, double OffsetX, double OffsetY, double Blur);

/// <summary>
/// The card's drop shadow, as Claudy's <c>CardShadow</c> draws it: computed from the card's outline
/// rather than from its content, so it looks the same whatever the wallpaper, and painted only in
/// the ring around the outline, never under the card. Rendered once per size into an image the
/// window lays under the card.
/// </summary>
public static class CardShadow
{
    /// <summary>The two layers of <c>shadow.card</c>: a soft drop, and a tight contact shadow that keeps the edge drawn on white.</summary>
    public static IReadOnlyList<ShadowLayer> Layers { get; } = Load();

    /// <summary>
    /// The transparent margin round the card where the shadow lives: three blur radii past the
    /// offset edge, or the window edge cuts the shadow and shows as a grey frame.
    /// </summary>
    public static double Inset { get; } = DesignTokens.Shared.Dimension("dimension.shadowInset");

    /// <summary>
    /// The shadow of a rounded card of <paramref name="cardWidth"/> × <paramref name="cardHeight"/>
    /// placed <see cref="Inset"/> from every edge, as premultiplied BGRA rows, <paramref name="scale"/>
    /// device pixels per unit. The card's own area is left fully transparent.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Render(double cardWidth, double cardHeight, double corner, double scale,
                                                               IReadOnlyList<ShadowLayer>? layers = null) =>
        Render(cardWidth, cardHeight, corner, scale, layers ?? Layers, Inset, hollow: true);

    /// <summary>
    /// The open island's shadow, as Claudy's <c>OutlineShadow(outline: NotchShape)</c>: the island
    /// hangs from the screen's top edge, so its shadow only has sides and a bottom. The image is
    /// <see cref="Inset"/> wider than the island on each side and taller below it; its top row is
    /// the screen's edge, and the island's own area is left clear.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Island(double width, double height, double corner, double scale)
    {
        // A card reaching above the screen by its corner radius, its rounded top cut off with the
        // margin above it: what is left meets the top edge square, as the island does.
        var (pixels, imageWidth, imageHeight) = Render(width, height + corner, corner, scale, Layers, Inset, hollow: true);
        var cut = (int)Math.Round((Inset + corner) * scale);
        var rows = Math.Max(imageHeight - cut, 0);
        var kept = new byte[imageWidth * rows * 4];
        Array.Copy(pixels, imageWidth * cut * 4, kept, 0, kept.Length);
        return (kept, imageWidth, rows);
    }

    /// <summary>
    /// Any SwiftUI <c>.shadow</c> of a rounded shape: the glow under a gauge, the avatar's, the
    /// sign-in button's. The shape sits <paramref name="margin"/> from every edge of the image;
    /// <paramref name="hollow"/> leaves the shape's own area clear, for a shadow under glass.
    /// </summary>
    public static (byte[] Pixels, int Width, int Height) Render(double shapeWidth, double shapeHeight, double corner, double scale,
                                                               IReadOnlyList<ShadowLayer> layers, double margin, bool hollow)
    {
        var width = (int)Math.Ceiling((shapeWidth + (2 * margin)) * scale);
        var height = (int)Math.Ceiling((shapeHeight + (2 * margin)) * scale);
        var shape = Coverage(width, height, margin * scale, margin * scale, shapeWidth * scale, shapeHeight * scale, corner * scale);

        var alpha = new double[width * height];
        var colour = new double[width * height * 3];
        foreach (var layer in layers)
        {
            var shifted = Shift(shape, width, height, (int)Math.Round(layer.OffsetX * scale), (int)Math.Round(layer.OffsetY * scale));
            Blur(shifted, width, height, layer.Blur * scale);
            var strength = layer.Color.A / 255.0;
            for (var index = 0; index < alpha.Length; index++)
            {
                var top = shifted[index] * strength;
                var below = alpha[index] * (1 - top);
                var total = top + below;
                if (total > 0)
                {
                    colour[index * 3] = ((layer.Color.B * top) + (colour[index * 3] * below)) / total;
                    colour[(index * 3) + 1] = ((layer.Color.G * top) + (colour[(index * 3) + 1] * below)) / total;
                    colour[(index * 3) + 2] = ((layer.Color.R * top) + (colour[(index * 3) + 2] * below)) / total;
                }
                alpha[index] = total;
            }
        }

        var pixels = new byte[width * height * 4];
        for (var index = 0; index < alpha.Length; index++)
        {
            // Nothing sits under what the outline holds, glass included.
            var value = Math.Clamp(hollow ? alpha[index] * (1 - shape[index]) : alpha[index], 0, 1);
            // Premultiplied, as the image the window shows it in expects.
            pixels[index * 4] = (byte)Math.Round(colour[index * 3] * value);
            pixels[(index * 4) + 1] = (byte)Math.Round(colour[(index * 3) + 1] * value);
            pixels[(index * 4) + 2] = (byte)Math.Round(colour[(index * 3) + 2] * value);
            pixels[(index * 4) + 3] = (byte)Math.Round(value * 255);
        }
        return (pixels, width, height);
    }

    /// <summary>Anti-aliased coverage of a rounded rectangle, 0…1 per pixel.</summary>
    private static double[] Coverage(int width, int height, double left, double top, double cardWidth, double cardHeight, double radius)
    {
        var coverage = new double[width * height];
        radius = Math.Min(radius, Math.Min(cardWidth, cardHeight) / 2);
        double centreX = left + (cardWidth / 2), centreY = top + (cardHeight / 2);
        double halfX = (cardWidth / 2) - radius, halfY = (cardHeight / 2) - radius;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Signed distance to the rounded rectangle, negative inside.
                var qx = Math.Abs(x + 0.5 - centreX) - halfX;
                var qy = Math.Abs(y + 0.5 - centreY) - halfY;
                var outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
                var distance = outside + Math.Min(Math.Max(qx, qy), 0) - radius;
                coverage[(y * width) + x] = Math.Clamp(0.5 - distance, 0, 1);
            }
        }
        return coverage;
    }

    private static double[] Shift(double[] source, int width, int height, int dx, int dy)
    {
        var shifted = new double[source.Length];
        for (var y = 0; y < height; y++)
        {
            var fromY = y - dy;
            if (fromY < 0 || fromY >= height)
            {
                continue;
            }
            for (var x = 0; x < width; x++)
            {
                var fromX = x - dx;
                if (fromX >= 0 && fromX < width)
                {
                    shifted[(y * width) + x] = source[(fromY * width) + fromX];
                }
            }
        }
        return shifted;
    }

    /// <summary>A Gaussian of the given deviation, as three box blurs each way: indistinguishable at this size, and linear.</summary>
    private static void Blur(double[] image, int width, int height, double sigma)
    {
        if (sigma < 0.5)
        {
            return;
        }
        foreach (var box in BoxSizes(sigma))
        {
            var radius = (box - 1) / 2;
            BoxPass(image, width, height, radius, horizontal: true);
            BoxPass(image, width, height, radius, horizontal: false);
        }
    }

    private static int[] BoxSizes(double sigma)
    {
        const int Passes = 3;
        var ideal = Math.Sqrt((12 * sigma * sigma / Passes) + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0)
        {
            lower--;
        }
        var upper = lower + 2;
        var mIdeal = ((12 * sigma * sigma) - (Passes * lower * lower) - (4 * Passes * lower) - (3 * Passes)) / ((-4 * lower) - 4);
        var m = (int)Math.Round(mIdeal);
        return Enumerable.Range(0, Passes).Select(index => index < m ? lower : upper).ToArray();
    }

    private static void BoxPass(double[] image, int width, int height, int radius, bool horizontal)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var line = new double[length];
        var span = (2 * radius) + 1;
        for (var l = 0; l < lines; l++)
        {
            for (var i = 0; i < length; i++)
            {
                line[i] = image[horizontal ? (l * width) + i : (i * width) + l];
            }
            double sum = 0;
            for (var i = -radius; i <= radius; i++)
            {
                sum += i >= 0 && i < length ? line[i] : 0;
            }
            for (var i = 0; i < length; i++)
            {
                image[horizontal ? (l * width) + i : (i * width) + l] = sum / span;
                var leaving = i - radius;
                var entering = i + radius + 1;
                sum += (entering < length ? line[entering] : 0) - (leaving >= 0 ? line[leaving] : 0);
            }
        }
    }

    private static List<ShadowLayer> Load()
    {
        using var document = DesignData.Open("Design/claudy.tokens.json");
        static double Pixels(JsonElement layer, string name) =>
            double.Parse(layer.GetProperty(name).GetString()!.Replace("px", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);

        return document.RootElement.GetProperty("shadow").GetProperty("card").GetProperty("$value").EnumerateArray()
                       .Select(layer => new ShadowLayer(Rgba.Parse(layer.GetProperty("color").GetString()!),
                                                        Pixels(layer, "offsetX"), Pixels(layer, "offsetY"), Pixels(layer, "blur")))
                       .ToList();
    }
}
