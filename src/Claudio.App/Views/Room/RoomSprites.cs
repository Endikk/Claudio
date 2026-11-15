using System.Globalization;
using System.Numerics;
using Claudio.Core.Design;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;

namespace Claudio.App;

/// <summary>
/// What the room draws with letters: the symbols of its screens and the words of its signs, each
/// made into a drawing once (<see cref="RoomStamp"/>). Claudy's <c>RoomSprites.swift</c> holds the
/// sprite and the symbols' names, which live in <c>Claudio.Core.Design.RoomSprites</c> for the
/// room's view model; this holds what only Windows draws: the Segoe Fluent Icons glyph standing
/// for each SF Symbol, and the rounded face.
/// </summary>
internal static class RoomSprites
{
    /// <summary>The face of the room's words: Nunito, which Claudio ships, standing for SF Pro Rounded.</summary>
    public static string Face { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Nunito.ttf") + "#Nunito";

    private const string Icons = "Segoe Fluent Icons";

    /// <summary>Nunito's cap height, as a share of its size: the middle of a line is the middle of its capitals.</summary>
    private const double CapHeight = 0.705;

    /// <summary>
    /// The Segoe Fluent Icons glyph for each of Claudy's SF Symbols. Where Segoe has no such symbol
    /// the nearest one stands in: a wrench for the hammer, a flask for the test tubes, a disc for
    /// the database, a puzzle piece for the plug, a pointing hand for the raised one.
    /// </summary>
    private static readonly Dictionary<string, int> Glyphs = new(StringComparer.Ordinal)
    {
        ["chevron.left.forwardslash.chevron.right"] = 0xE943,
        ["doc.text.fill"] = 0xE729,
        ["checklist"] = 0xE9D5,
        ["paintbrush.pointed.fill"] = 0xE771,
        ["magnifyingglass"] = 0xE721,
        ["eye.fill"] = 0xE890,
        ["globe"] = 0xE774,
        ["terminal.fill"] = 0xE756,
        ["testtube.2"] = 0xF196,
        ["hammer.fill"] = 0xE90F,
        ["speedometer"] = 0xEC4A,
        ["shippingbox.fill"] = 0xE7B8,
        ["arrow.triangle.branch"] = 0xF003,
        ["cylinder.split.1x2.fill"] = 0xEE94,
        ["cloud.fill"] = 0xE753,
        ["paperplane.fill"] = 0xE725,
        ["lock.shield.fill"] = 0xEA18,
        ["person.2.fill"] = 0xE716,
        ["book.closed.fill"] = 0xE82D,
        ["powerplug.fill"] = 0xEA86,
        ["ellipsis"] = 0xE712,
        ["hand.raised.fill"] = 0xE815,
        ["pause.fill"] = 0xE769,
        ["checkmark"] = 0xE73E,
    };

    /// <summary>The glyph standing for an SF Symbol: the ellipsis for one it does not know.</summary>
    public static string Glyph(string name) => char.ConvertFromUtf32(Glyphs.TryGetValue(name, out var glyph) ? glyph : 0xE712);

    /// <summary>A symbol of <paramref name="size"/> pixels, placed by the middle of its ink.</summary>
    public static RoomStamp Symbol(ICanvasResourceCreator creator, string name, double size, uint color)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = Icons,
            FontSize = (float)size,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        using var layout = new CanvasTextLayout(creator, Glyph(name), format, 0, 0);
        var list = new CanvasCommandList(creator);
        using (var session = list.CreateDrawingSession())
        {
            session.DrawTextLayout(layout, Vector2.Zero, RoomPainter.Hex(color));
        }
        return new RoomStamp(list, layout.DrawBounds);
    }

    /// <summary>
    /// A line of text in the rounded face, of <paramref name="size"/> pixels, on one line however
    /// long: its box's middle is the middle of its capitals.
    /// </summary>
    public static RoomStamp Label(ICanvasResourceCreator creator, string text, double size, FontWeight weight, uint color)
    {
        using var format = Format(size, weight, CanvasWordWrapping.NoWrap);
        using var layout = new CanvasTextLayout(creator, text, format, 0, 0);
        var list = new CanvasCommandList(creator);
        using (var session = list.CreateDrawingSession())
        {
            session.DrawTextLayout(layout, Vector2.Zero, RoomPainter.Hex(color));
        }
        var bounds = layout.LayoutBounds;
        var middle = Middle(layout, size);
        return new RoomStamp(list, new Windows.Foundation.Rect(bounds.X, middle - (bounds.Height / 2), bounds.Width, bounds.Height));
    }

    public static CanvasTextFormat Format(double size, FontWeight weight, CanvasWordWrapping wrapping) => new()
    {
        FontFamily = Face,
        FontSize = (float)size,
        FontWeight = weight,
        WordWrapping = wrapping,
    };

    /// <summary>
    /// Where a block of text has its middle, from the top of its layout: halfway between the top
    /// of the first line's capitals and the last line's baseline. SwiftUI centres SF's line box,
    /// whose middle falls there too.
    /// </summary>
    public static double Middle(CanvasTextLayout layout, double size)
    {
        var lines = layout.LineMetrics;
        if (lines.Length == 0)
        {
            return layout.LayoutBounds.Height / 2;
        }
        double top = 0, lastBaseline = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            lastBaseline = top + lines[index].Baseline;
            top += lines[index].Height;
        }
        var capTop = lines[0].Baseline - (CapHeight * size);
        return (capTop + lastBaseline) / 2;
    }

    /// <summary>The weight's number, for a tag.</summary>
    public static string Tag(FontWeight weight) => weight.Weight.ToString(CultureInfo.InvariantCulture);
}
