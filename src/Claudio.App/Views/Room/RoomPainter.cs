using System.Numerics;
using System.Runtime.CompilerServices;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Claudio.App;

/// <summary>
/// A drawing kept once and stamped on every frame (a line of text, a symbol), with the box it is
/// placed by: SwiftUI places an image by its bounds, this by <see cref="Box"/>, relative to where
/// the drawing was made.
/// </summary>
internal sealed record RoomStamp(ICanvasImage Image, Windows.Foundation.Rect Box);

/// <summary>
/// Draws the agent room in its own units, 480 by 270: one unit is <see cref="Unit"/> device
/// independent pixels. Claudy is pixel art, one unit a pixel; the furniture uses half units for its
/// detail. Claudy's <c>RoomPainter</c> on SwiftUI's <c>GraphicsContext</c>, here on a Win2D
/// <see cref="CanvasDrawingSession"/>, call for call.
/// </summary>
internal sealed partial class RoomPainter
{
    private readonly CanvasDrawingSession _context;

    /// <summary>Where the painter's origin lies on its session, in pixels: what <see cref="Lowered"/> moves.</summary>
    private readonly Vector2 _offset;

    public RoomPainter(CanvasDrawingSession context, double unit, IReadOnlyDictionary<string, RoomStamp>? stamps = null,
                       Vector2 offset = default)
    {
        _context = context;
        Unit = unit;
        Stamps = stamps ?? new Dictionary<string, RoomStamp>();
        _offset = offset;
    }

    public double Unit { get; }

    /// <summary>The symbols and the lines of text the canvas made into drawings once, by tag.</summary>
    public IReadOnlyDictionary<string, RoomStamp> Stamps { get; }

    public CanvasDrawingSession Context => _context;

    public const uint Ink = 0x1F1A17;
    public const uint Bubble = 0xF1E6D2;
    public const uint Floor = 0x3A2C24;

    private static readonly DesignTokens Tokens = DesignTokens.Shared;
    private static readonly Color Outline = Paint(Tokens.Color("color.pixel.outline"));
    private static readonly Color Shade = Paint(Tokens.Color("color.pixel.shade"));

    public static Color Hex(uint hex, double opacity = 1) =>
        Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), (byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    public static Color Paint(Rgba colour) => Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    /// <summary>SwiftUI's <c>Color.white.opacity(…)</c>.</summary>
    public static Color White(double opacity) => Hex(0xFFFFFF, opacity);

    /// <summary>SwiftUI's <c>Color.black.opacity(…)</c>.</summary>
    public static Color Black(double opacity) => Hex(0x000000, opacity);

    // Units to the session's pixels.

    public float Px(double x) => (float)(x * Unit) + _offset.X;

    public float Py(double y) => (float)(y * Unit) + _offset.Y;

    public float Length(double units) => (float)(units * Unit);

    public Vector2 Point(double x, double y) => new(Px(x), Py(y));

    public void Fill(uint hex, double x, double y, double width, double height, double opacity = 1) =>
        Fill(Hex(hex, opacity), x, y, width, height);

    public void Fill(Color color, double x, double y, double width, double height) =>
        _context.FillRectangle(Px(x), Py(y), Length(width), Length(height), color);

    /// <summary>The same painter, drawing <paramref name="drop"/> units lower.</summary>
    public RoomPainter Lowered(double drop) => new(_context, Unit, Stamps, _offset + new Vector2(0, Length(drop)));

    /// <summary>The same painter, on another session: a layer drawn apart, then composed.</summary>
    public RoomPainter On(CanvasDrawingSession context) => new(context, Unit, Stamps, _offset);

    /// <summary>A shadow on the floor, feathered: three layers, each half a unit inside the last.</summary>
    public void Shadow(double x, double y, double width, double height, double opacity = 0.22)
    {
        var layer = Black(opacity / 3);
        Fill(layer, x - 0.5, y - 0.5, width + 1, height + 1);
        Fill(layer, x, y, width, height);
        Fill(layer, x + Math.Min(0.5, width / 4), y + Math.Min(0.5, height / 4), Math.Max(width - 1, width / 2), Math.Max(height - 1, height / 2));
    }

    // Claudy

    /// <summary>The cells of a frame, one geometry per ink, made once per device and scale.</summary>
    private static readonly ConditionalWeakTable<CanvasDevice, Dictionary<(IReadOnlyList<string> Rows, double Unit), List<(char Ink, CanvasGeometry Shape)>>> Shapes = [];

    private List<(char Ink, CanvasGeometry Shape)> SpriteShapes(IReadOnlyList<string> rows)
    {
        var cache = Shapes.GetValue(_context.Device, _ => []);
        if (cache.TryGetValue((rows, Unit), out var shapes))
        {
            return shapes;
        }
        var cell = Length(1);
        var builders = new Dictionary<char, CanvasPathBuilder>();
        for (var row = 0; row < rows.Count; row++)
        {
            var line = rows[row];
            for (var column = 0; column < line.Length; column++)
            {
                var ink = line[column];
                if (ink == '.')
                {
                    continue;
                }
                // A run of one ink is one rectangle: the shape has no seam inside it.
                var end = column;
                while (end + 1 < line.Length && line[end + 1] == ink)
                {
                    end++;
                }
                if (!builders.TryGetValue(ink, out var builder))
                {
                    builder = new CanvasPathBuilder(_context.Device);
                    builder.SetFilledRegionDetermination(CanvasFilledRegionDetermination.Winding);
                    builders[ink] = builder;
                }
                float left = column * cell, right = (end + 1) * cell, top = row * cell, bottom = (row + 1) * cell;
                builder.BeginFigure(left, top);
                builder.AddLine(right, top);
                builder.AddLine(right, bottom);
                builder.AddLine(left, bottom);
                builder.EndFigure(CanvasFigureLoop.Closed);
                column = end;
            }
        }
        shapes = [.. builders.Select(pair => (pair.Key, CanvasGeometry.CreatePath(pair.Value)))];
        foreach (var builder in builders.Values)
        {
            builder.Dispose();
        }
        cache[(rows, Unit)] = shapes;
        return shapes;
    }

    /// <summary>A frame of rows of inks, one unit a cell, each ink painted as the app's sprite paints it.</summary>
    public void Sprite(IReadOnlyList<string> rows, double x, double y, Rgba tint)
    {
        var place = Point(x, y);
        foreach (var (ink, shape) in SpriteShapes(rows))
        {
            foreach (var layer in Mascot.Shared.Layers(ink, tint, Tokens))
            {
                _context.FillGeometry(shape, place, Paint(layer));
            }
        }
    }

    /// <summary>A standing or walking Claudy, placed by the middle of its feet.</summary>
    public void Claudy(RoomSprite.Pose pose, RoomPoint feet, Rgba tint) =>
        Sprite(RoomSprite.Frame(pose), feet.X - 8, feet.Y - RoomSprite.Rows, tint);

    /// <summary>
    /// Two arms from a seated Claudy's body down to its keyboard, alternating when it types.
    /// <paramref name="x"/> is the left edge of the body, <paramref name="y"/> the top of the arms.
    /// </summary>
    public void Arms(double x, double y, Rgba tint, bool isTyping, int beat)
    {
        double left = isTyping ? (beat % 2 == 0 ? 5 : 6) : 5;
        double right = isTyping ? (beat % 2 == 0 ? 6 : 5) : 5;
        var body = Paint(tint);
        foreach (var (offset, length) in new[] { (2.0, left), (10.0, right) })
        {
            Fill(body, x + offset, y, 4, length + 1);
            Fill(Outline, x + offset, y, 4, length + 1);
        }
        Fill(body, x + 3, y, 2, left);
        Fill(body, x + 11, y, 2, right);
        Fill(Shade, x + 11, y, 2, right);
    }

    // Screens

    /// <summary>
    /// A symbol centred on a point of the scene. <paramref name="points"/> is its size at the
    /// room's usual scale. The canvas hands over the symbols it made into drawings once; one it does
    /// not have is laid out here, on every frame.
    /// </summary>
    public void Symbol(string name, RoomPoint centre, double points, uint color)
    {
        var place = Point(centre.X, centre.Y);
        if (Stamps.TryGetValue(RoomGlyph.Tag(name, color, points), out var stamp))
        {
            Stamp(stamp, place, isCentred: true);
            return;
        }
        var made = RoomSprites.Symbol(_context, name, points * Unit / 2, color);
        Stamp(made, place, isCentred: true);
        made.Image.Dispose();
    }

    /// <summary>Draws a stamp by the middle of its box, or by the middle of its left edge.</summary>
    public void Stamp(RoomStamp stamp, Vector2 place, bool isCentred)
    {
        var box = stamp.Box;
        var anchorX = isCentred ? box.X + (box.Width / 2) : box.X;
        var anchorY = box.Y + (box.Height / 2);
        _context.DrawImage(stamp.Image, new Vector2(place.X - (float)anchorX, place.Y - (float)anchorY));
    }

    /// <summary>The work a walking Claudy goes to, in a bubble over its head.</summary>
    public void BubbleOver(string name, double x, double y)
    {
        Fill(Ink, x + 0.5, y - 0.5, 10, 12);
        Fill(Ink, x - 0.5, y + 0.5, 12, 10);
        Fill(Bubble, x + 1, y, 9, 11);
        Fill(Bubble, x, y + 1, 11, 9);
        Fill(Bubble, x + 4, y + 11, 3, 1);
        Fill(Bubble, x + 5, y + 12, 1, 1);
        Symbol(name, new RoomPoint(x + 5.5, y + 5.5), 12, Ink);
    }

    /// <summary>A monitor facing the viewer, 15 by 13 plus its stand, a glyph on its screen.</summary>
    public void Monitor(double x, double y, uint screen, string? glyph, uint color = 0xC8E4FF)
    {
        Fill(0x1C1F24, x + 6.5, y + 13, 2, 2.5);
        Fill(0x2B2F36, x + 4, y + 15, 7, 1);
        Fill(0x3A3F48, x + 4, y + 15, 7, 0.5);
        Fill(0x23262C, x + 0.5, y, 14, 13);
        Fill(0x23262C, x, y + 0.5, 15, 12);
        Fill(0x111316, x + 0.5, y + 0.5, 14, 12);
        Fill(screen, x + 1, y + 1, 13, 10.5);
        Fill(White(0.07), x + 1, y + 1, 13, 3);
        Fill(0x2B2F36, x + 0.5, y + 11.5, 14, 1);
        Fill(glyph is null ? 0x4A505Bu : 0x4CC38Au, x + 12.5, y + 11.75, 1, 0.5);
        if (glyph is not null)
        {
            Symbol(glyph, new RoomPoint(x + 7.5, y + 6.25), 13, color);
        }
    }

    /// <summary>
    /// What a desk's screen shows for its occupant: the colour of its glass, its symbol and the
    /// symbol's colour. <c>null</c> for an empty desk.
    /// </summary>
    private static (uint Screen, string? Name, uint Color)? Look(RoomOccupant? occupant, int step)
    {
        if (occupant is null)
        {
            return null;
        }
        var beat = step + occupant.Phase;
        (uint Screen, string? Name, uint Color) look = occupant.State switch
        {
            RoomOccupantState.Working => (0x1E2B36, occupant.Glyph, 0xC8E4FF),
            RoomOccupantState.Idle => (0x1E2B36, occupant.Glyph, 0x5E7C93),
            RoomOccupantState.Waiting => (beat % 4 >= 2 ? 0xC98A2Cu : 0xF2A93Bu, RoomGlyph.Waiting, Ink),
            RoomOccupantState.Stalled => (0x3A1E1B, RoomGlyph.Stalled, 0xF0604D),
            _ => (0x1F3A2C, RoomGlyph.Done, 0x4CC38A),
        };
        if (occupant.IsFlashing)
        {
            look.Screen = 0xC8E4FF;
        }
        return look;
    }

    /// <summary>The whole monitor of a desk, with what it shows.</summary>
    public void Screen(RoomPoint desk, RoomOccupant? occupant, int step)
    {
        if (Look(occupant, step) is not { } look)
        {
            Monitor(desk.X + 1, desk.Y + 1, 0x16181C, null);
            return;
        }
        Monitor(desk.X + 1, desk.Y + 1, look.Screen, look.Name, look.Color);
    }

    /// <summary>
    /// The glass of a desk's monitor alone, over a monitor already drawn: for the desks drawn from
    /// their kept image, and to light the screens again after the night dimmed the room.
    /// </summary>
    public void ScreenFace(RoomPoint desk, RoomOccupant? occupant, int step)
    {
        if (Look(occupant, step) is not { } look)
        {
            return;
        }
        double x = desk.X + 1, y = desk.Y + 1;
        Fill(look.Screen, x + 1, y + 1, 13, 10.5);
        Fill(White(0.07), x + 1, y + 1, 13, 3);
        Fill(0x4CC38A, x + 12.5, y + 11.75, 1, 0.5);
        if (look.Name is { } name)
        {
            Symbol(name, new RoomPoint(x + 7.5, y + 6.25), 13, look.Color);
        }
    }

    // Desks

    /// <summary>
    /// The furniture of a desk, which never changes, in two layers kept as images: the chair,
    /// behind whoever sits there, and the desk itself with its dark monitor and its keyboard.
    /// </summary>
    public sealed record DeskArt(CanvasBitmap Chair, CanvasBitmap Desk)
    {
        /// <summary>Units of margin round the desk in both images, for its shadow.</summary>
        public const double Margin = 2;

        public static RoomSize Size { get; } = new(RoomGeometry.DeskSize.Width + (Margin * 2), RoomGeometry.DeskSize.Height + (Margin * 2));
    }

    /// <summary>The chair of a desk and the shadow under the desk: what is behind its occupant.</summary>
    public void DeskChair(RoomPoint origin)
    {
        double x = origin.X, y = origin.Y;
        Shadow(x + 1, y + 29, 34, 2);
        Fill(0x2A2320, x + 19, y + 2.5, 12, 14);
        Fill(0x2A2320, x + 18.5, y + 3, 13, 13);
        Fill(0x3A302B, x + 19.5, y + 3.5, 11, 12);
        Fill(0x453A34, x + 19.5, y + 3.5, 11, 0.5);
        Fill(0x2A2320, x + 17, y + 9.5, 1.5, 6);
        Fill(0x2A2320, x + 31.5, y + 9.5, 1.5, 6);
        Fill(0x3A302B, x + 17, y + 9.5, 1.5, 0.5);
        Fill(0x3A302B, x + 31.5, y + 9.5, 1.5, 0.5);
    }

    /// <summary>The desk in front of its occupant: its top, its front, its monitor switched off and its keyboard.</summary>
    public void DeskBody(RoomPoint origin)
    {
        double x = origin.X, y = origin.Y;
        Top(x, y + 15, 36, 8, [(3, 3, 9), (14, 7, 12)]);
        Front(x, y + 23, 36, 6, [12, 24], [4.5, 16.5, 28.5]);
        Monitor(x + 1, y + 1, 0x16181C, null);
        Keyboard(x + 18, y + 18, 8, isLit: false);
    }

    /// <summary>
    /// One desk seen from the front and a little above, 36 by 31: the monitor at the back left of
    /// the top, Claudy behind it on the right, its arms on the keyboard. With <paramref name="art"/>
    /// the furniture comes from its kept images and only what lives is drawn.
    /// </summary>
    public void Desk(RoomPoint origin, RoomOccupant? occupant, int step, DeskArt? art = null)
    {
        double x = origin.X, y = origin.Y;
        var beat = step + (occupant?.Phase ?? 0);
        var corner = Point(x - DeskArt.Margin, y - DeskArt.Margin);
        if (art is not null)
        {
            _context.DrawImage(art.Chair, corner);
        }
        else
        {
            DeskChair(origin);
        }
        if (occupant is not null)
        {
            var pose = beat % 23 == 0 || occupant.State == RoomOccupantState.Idle ? RoomSprite.Pose.SitBlink : RoomSprite.Pose.Sit;
            Sprite(RoomSprite.Frame(pose), x + 17, y + 4, occupant.Tint);
        }
        if (art is not null)
        {
            _context.DrawImage(art.Desk, corner);
        }
        else
        {
            DeskBody(origin);
        }
        if (occupant is null)
        {
            return;
        }
        // A lit screen throws its light on the desk.
        if (occupant.State != RoomOccupantState.Stalled)
        {
            Fill(0xC8E4FF, x + 2, y + 16.5, 13, 1.5, opacity: 0.16);
            Fill(0xC8E4FF, x + 3, y + 18, 11, 1, opacity: 0.08);
        }
        ScreenFace(origin, occupant, step);
        Fill(0x9A9CA6, x + 18.5, y + 18.5, 13, 2.5);
        for (var key = 0; key < 8; key++)
        {
            Fill(0x3A3F48, x + 19.5 + (key * 1.5), y + 18.5, 0.5, 2.5);
        }
        Arms(x + 17, y + 12, occupant.Tint, occupant.State == RoomOccupantState.Working, beat);
        if (occupant.State == RoomOccupantState.Idle)
        {
            Doze(x + 30, y + 2, step + occupant.Phase);
        }
        switch (occupant.Phase % 3)
        {
            case 0:
                Fill(Bubble, x + 33, y + 19, 2, 2.5);
                Fill(0x8C6248, x + 33.5, y + 19.5, 1, 0.5);
                break;
            case 1:
                Fill(0xEDE3D3, x + 32.5, y + 19.5, 3, 2);
                Fill(0xB5A898, x + 33, y + 20, 2, 0.5);
                break;
            default:
                Fill(0x9B4E45, x + 32.5, y + 19.5, 3, 2.5);
                Fill(0x6FAE8F, x + 32, y + 17, 4, 3);
                Fill(0x8FC7A8, x + 33.5, y + 16.5, 1.5, 1.5);
                break;
        }
    }

    private static readonly int[][] Zee = [[1, 1, 1], [0, 1, 0], [1, 1, 1]];

    /// <summary>
    /// Three small z's rising from a Claudy that waits, eyes closed: it dozes until its command or
    /// its parent answers.
    /// </summary>
    public void Doze(double x, double y, int step)
    {
        for (var letter = 0; letter < 3; letter++)
        {
            double rise = (step + (letter * 3)) % 9;
            double left = x + (letter * 2.5) + (rise * 0.3), top = y - (letter * 2.5) - (rise * 0.5);
            var size = 0.5 + (letter * 0.25);
            var color = Hex(Bubble, 0.85 - (rise * 0.08));
            for (var row = 0; row < Zee.Length; row++)
            {
                for (var column = 0; column < Zee[row].Length; column++)
                {
                    if (Zee[row][column] == 1)
                    {
                        Fill(color, left + (column * size), top + (row * size), size, size);
                    }
                }
            }
        }
    }

    /// <summary>The top of a desk, lit along its far edge, with a few lines of grain.</summary>
    public void Top(double x, double y, double width, double depth, (double Offset, double Down, double Length)[] grain)
    {
        Fill(0xB98D69, x, y, width, depth);
        // The light comes from the window, on the left: the top is paler there.
        Fill(White(0.07), x, y, width / 3, depth);
        Fill(Black(0.07), x + (width * 0.72), y, width * 0.28, depth);
        Fill(0xC9A07B, x, y, width, 0.5);
        foreach (var (offset, down, length) in grain)
        {
            Fill(0xAD815F, x + offset, y + down, length, 0.5);
        }
    }

    /// <summary>The front of a desk: its lip, its drawers, its feet.</summary>
    public void Front(double x, double y, double width, double height, double[] dividers, double[] handles)
    {
        Fill(0x9A6F52, x, y, width, 1);
        Fill(0x8C6248, x, y + 1, width, height - 1);
        foreach (var divider in dividers)
        {
            Fill(0x6E4A3C, x + divider, y + 1, 0.5, height - 1);
        }
        foreach (var handle in handles)
        {
            Fill(0xC9A07B, x + handle, y + 3, 3, 0.5);
        }
        Fill(0x68463C, x, y + height - 0.5, width, 0.5);
        Fill(0x4A332B, x + 1, y + height, 2, 1);
        Fill(0x4A332B, x + width - 3, y + height, 2, 1);
    }

    public void Keyboard(double x, double y, int keys, bool isLit)
    {
        var width = (keys * 1.5) + 2;
        Fill(0x3A3F48, x, y, width, 3.5);
        Fill(isLit ? 0x9A9CA6u : 0x5A5C64u, x + 0.5, y + 0.5, width - 1, 2.5);
        for (var key = 0; key < keys; key++)
        {
            Fill(0x3A3F48, x + 1.5 + (key * 1.5), y + 0.5, 0.5, 2.5);
        }
    }
}
