using System.Numerics;
using Claudio.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI;
using Windows.UI.Text;

namespace Claudio.App;

/// <summary>
/// The room itself: its floor and its back wall, with the board, the clock and what hangs there.
/// Nothing here depends on the agents. The window is in <c>RoomPainter.Window</c>, what furnishes
/// the room in <c>RoomPainter.Decor</c>.
/// </summary>
internal sealed partial class RoomPainter
{
    /// <summary>
    /// What never moves: the floor, the walls and all that furnishes the room. Drawn once and kept
    /// as an image, since it is most of the room and none of it changes from one frame to the next.
    /// <paramref name="week"/> is the tokens of the last seven days, each as a share of the busiest one.
    /// </summary>
    public void StillBackdrop(string title, IReadOnlyList<double> week, bool isLit)
    {
        Floorboards();
        Walls();
        Board(title, week);
        Thermostat();
        ClockFace();
        Servers();
        DockStation();
        Lowered(RoomGeometry.CentreDrop).Lounge(isLit);
        Door();
        // Two plants under the back wall, clear of the vacuum's way and of the areas.
        Bush(new RoomPoint(346, 44));
        Bush(new RoomPoint(378, 44));
        TallPlant(new RoomPoint(11, 241));
        Bush(new RoomPoint(24, 248));
        Bush(new RoomPoint(446, 248));
        TallPlant(new RoomPoint(459, 241));
    }

    /// <summary>
    /// What moves in the backdrop, drawn over the still part on every frame: the day on the floor,
    /// the clock's hands, and the lights of the racks and of the dock. The window is a drawing of
    /// its own, laid over the room: it changes slowly, and the night must not dim it.
    /// </summary>
    public void LiveBackdrop(RoomSky sky, RoomWeather weather, int step, double time, DateTimeOffset date, double quota, TimeZoneInfo? zone = null)
    {
        Sunlight(sky, weather == RoomWeather.Clear ? 1 : 0.3);
        ClockHands(date, zone);
        ServerLights(step, quota);
        DockLights(RoomVacuum.StateAt(time).IsDocked, step);
    }

    /// <summary>A filled shape through points given in units: what a rectangle cannot draw.</summary>
    public void Shape(ReadOnlySpan<RoomPoint> points, Color color)
    {
        if (points.Length == 0)
        {
            return;
        }
        var corners = new Vector2[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            corners[index] = Point(points[index].X, points[index].Y);
        }
        using var polygon = CanvasGeometry.CreatePolygon(_context, corners);
        _context.FillGeometry(polygon, color);
    }

    /// <summary>A filled ellipse, centre and radii in units.</summary>
    public void Ellipse(RoomPoint centre, double across, double down, Color color) =>
        _context.FillEllipse(Point(centre.X, centre.Y), Length(across), Length(down), color);

    /// <summary>A filled disc, centre and radius in units.</summary>
    public void Disc(RoomPoint centre, double radius, Color color) =>
        _context.FillCircle(Point(centre.X, centre.Y), Length(radius), color);

    /// <summary>
    /// SwiftUI's radial gradient: its colours spread evenly from the centre out to
    /// <paramref name="radius"/> pixels, the last one beyond.
    /// </summary>
    public CanvasRadialGradientBrush Radial(Vector2 centre, float radius, params Color[] colors)
    {
        var stops = colors.Select((color, index) => new CanvasGradientStop
        {
            Position = colors.Length > 1 ? (float)index / (colors.Length - 1) : 0,
            Color = color,
        }).ToArray();
        return new CanvasRadialGradientBrush(_context, stops, CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Straight)
        {
            Center = centre,
            RadiusX = radius,
            RadiusY = radius,
        };
    }

    private void Floorboards()
    {
        for (var row = 0; row < 29; row++)
        {
            var y = 44 + (row * 8.0);
            Fill(row % 2 == 0 ? 0x382A22u : 0x3B2D25u, 0, y, 480, 8);
            Fill(0x2F231C, 0, y, 480, 0.5);
            var x = (row % 3 * 22.0) + 10;
            var plank = 0;
            while (x < 480 + 62)
            {
                // No two planks are the same wood: each has its own tone and its own grain.
                var wood = ((row * 31) + (plank * 17)) % 5;
                var left = Math.Max(0, x - 62);
                if (wood is 1 or 3)
                {
                    Fill(White(wood == 1 ? 0.03 : 0.015), left, y + 0.5, x - left, 7.5);
                }
                if (wood is 0 or 4)
                {
                    Fill(Black(wood == 0 ? 0.05 : 0.025), left, y + 0.5, x - left, 7.5);
                }
                var grainX = left + 6 + (((row * 7) + (plank * 13)) % 24);
                var grainY = y + 2.5 + (wood % 3 * 1.5);
                Fill(Black(0.07), grainX, grainY, 16, 0.5);
                Fill(0x2F231C, x, y, 0.5, 8);
                Fill(0x43342B, x + 0.5, y + 0.5, 0.5, 7.5);
                x += 62;
                plank++;
            }
        }
    }

    /// <summary>The light the window's three panes throw on the floor, leaning away from the sun.</summary>
    private void Sunlight(RoomSky sky, double strength)
    {
        if (sky.Sun is not { } sun || sun.Height <= 0.15)
        {
            return;
        }
        var lean = (0.5 - sun.Across) * 22;
        // The day spreads from the window over the floor, before the panes draw their shapes.
        const uint Warm = 0xFFE9B8;
        var source = Point(87 + lean, 44);
        var reachLeft = Math.Max(Px(6), source.X - Length(150));
        using (var glow = Radial(source, Length(110), Hex(Warm, 0.13 * sun.Height * strength), Hex(Warm, 0)))
        {
            _context.FillRectangle(reachLeft, Py(46.5), Length(300), Length(108), glow);
        }
        var color = Hex(Warm, 0.07 * sun.Height * strength);
        foreach (var (left, right) in new[] { (17.0, 62.0), (65.0, 109.5), (112.5, 157.0) })
        {
            Shape([new(left, 46.5), new(right, 46.5), new(Math.Max(7, right + lean), 58.5), new(Math.Max(7, left + lean), 58.5)], color);
        }
    }

    private void Walls()
    {
        Fill(0x2C231F, 6, 4, 468, 40);
        Fill(0x3A2F29, 6, 4, 468, 0.5);
        Fill(0x251D19, 6, 34, 468, 10);
        Fill(0x3A2F29, 6, 33.5, 468, 0.5);
        for (double panel = 18; panel < 474; panel += 24)
        {
            Fill(0x1F1815, panel, 35, 0.5, 6.5);
        }
        // The wall is darker towards the ceiling.
        using (var dark = new CanvasLinearGradientBrush(_context, Black(0.28), Black(0)) { StartPoint = Point(0, 4), EndPoint = Point(0, 34) })
        {
            _context.FillRectangle(Px(6), Py(4), Length(468), Length(30), dark);
        }
        Fill(0x191310, 6, 42, 468, 2);
        Fill(0x33291F, 6, 42, 468, 0.5);
        Shadow(6, 44, 468, 2.5, opacity: 0.28);
        Shadow(6, 46.5, 468, 2, opacity: 0.12);
        Fill(0x1A1411, 0, 0, 480, 4);
        Fill(0x1F1815, 0, 0, 6, 270);
        Fill(0x1F1815, 474, 0, 6, 270);
        Fill(0x2E2520, 5.5, 44, 0.5, 220);
        Fill(0x2E2520, 474, 44, 0.5, 220);
    }

    /// <summary>
    /// The whiteboard: the project's name, a few lines, three notes, and the week as the card draws
    /// it, one bar a day, today last. No bar for a day with nothing.
    /// </summary>
    private void Board(string title, IReadOnlyList<double> week)
    {
        Shadow(174, 38.5, 134, 1.5, opacity: 0.25);
        Fill(0x5E4D42, 173, 6, 134, 32);
        Fill(0x7A6556, 173, 6, 134, 0.5);
        Fill(Bubble, 175, 8, 130, 27);
        Fill(0xFFFFFF, 175, 8, 130, 0.5);
        Text(title, 182, 14.5, 14, FontWeights.Bold, 0x2A1418, 50);
        Fill(0xD97757, 182, 21, 34, 1);
        Fill(0xCFC3AE, 182, 25, 40, 0.5);
        Fill(0xCFC3AE, 182, 28, 30, 0.5);
        Fill(0xCFC3AE, 182, 31, 36, 0.5);
        var days = week.Skip(Math.Max(0, week.Count - 7)).ToList();
        for (var index = 0; index < days.Count; index++)
        {
            var share = days[index];
            if (share <= 0)
            {
                continue;
            }
            var x = 232 + (index * 7.0);
            var height = Math.Max(1, Math.Round(Math.Min(share, 1) * 14 * 2, MidpointRounding.AwayFromZero) / 2);
            Fill(0xD97757, x, 31 - height, 5, height);
            Fill(0xE9A07C, x, 31 - height, 5, 0.5);
        }
        Fill(0xB5A898, 230, 31, 51, 0.5);
        foreach (var (x, y, color) in new[] { (286.0, 11.0, 0xE0B872u), (293.0, 12.0, 0x9CCBF7u), (288.0, 19.0, 0xE9A07Cu) })
        {
            Shadow(x + 0.5, y + 0.5, 6, 6, opacity: 0.15);
            Fill(color, x, y, 6, 6);
            Fill(0x2A1418, x + 1, y + 2, 3.5, 0.5, opacity: 0.5);
            Fill(0x2A1418, x + 1, y + 3.5, 2.5, 0.5, opacity: 0.5);
        }
        Fill(0x6E5B4E, 173, 38, 134, 1.5);
        Fill(0x8A7363, 173, 38, 134, 0.5);
        Fill(0xD97757, 190, 37, 5, 1);
        Fill(0x4FA8F7, 198, 37, 5, 1);
        Fill(0x2A1418, 206, 37, 5, 1);
        Fill(0x3A3F48, 288, 36.5, 8, 1.5);
        Fill(0xEDE3D3, 288, 37.5, 8, 0.5);
    }

    /// <summary>A thermostat on the wall between the window and the board.</summary>
    private void Thermostat()
    {
        Fill(0x4A3C34, 164, 17.5, 5, 6);
        Fill(0xEDE3D3, 164.5, 18, 4, 5);
        Fill(0x2B2F36, 165, 18.5, 3, 2);
        Fill(0x4CC38A, 165.5, 19, 1, 1);
        Fill(0xB5A898, 165, 21.5, 3, 0.5);
    }

    private static readonly RoomPoint ClockCentre = new(366.5, 17);

    /// <summary>The wall clock without its hands: its frame and its twelve marks.</summary>
    private void ClockFace()
    {
        var centre = ClockCentre;
        var left = centre.X - 7.5;
        Shadow(left + 0.5, 10, 15, 15, opacity: 0.25);
        Fill(0x5E4D42, left, 9, 15, 16);
        Fill(0x4A3C34, left - 0.5, 9.5, 16, 15);
        Fill(0x7A6556, left, 9, 15, 0.5);
        Fill(Bubble, left + 1, 10.5, 13, 13);
        for (var mark = 0; mark < 12; mark++)
        {
            var angle = mark / 12.0 * 2 * Math.PI;
            var size = mark % 3 == 0 ? 1 : 0.5;
            Fill(0x8A7363, centre.X + (5.25 * Math.Sin(angle)) - (size / 2), centre.Y - (5.25 * Math.Cos(angle)) - (size / 2), size, size);
        }
    }

    /// <summary>The clock's hands, at the PC's time.</summary>
    private void ClockHands(DateTimeOffset date, TimeZoneInfo? zone)
    {
        var centre = ClockCentre;
        var local = TimeZoneInfo.ConvertTime(date, zone ?? TimeZoneInfo.Local);
        double minutes = local.Minute;
        var hours = (local.Hour % 12) + (minutes / 60);
        Hand(centre, hours / 12 * 2 * Math.PI, 3, 1, 0x2A1418);
        Hand(centre, minutes / 60 * 2 * Math.PI, 4.5, 0.75, 0x2A1418);
        Fill(0xD97757, centre.X - 0.5, centre.Y - 0.5, 1, 1);
    }

    public static string LabelTag(string text, double size, FontWeight weight, uint color) =>
        FormattableString.Invariant($"text|{size}|{RoomSprites.Tag(weight)}|{color}|{text}");

    /// <summary>A line with round ends: SwiftUI's <c>lineCap: .round</c>.</summary>
    public static CanvasStrokeStyle RoundCaps { get; } = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
    };

    private void Hand(RoomPoint centre, double angle, double length, double width, uint color)
    {
        var tip = Point(centre.X + (length * Math.Sin(angle)), centre.Y - (length * Math.Cos(angle)));
        _context.DrawLine(Point(centre.X, centre.Y), tip, Hex(color), Length(width), RoundCaps);
    }

    /// <summary>
    /// A line of text in the app's rounded face, in a box <paramref name="width"/> wide from
    /// <paramref name="x"/>, its middle at <paramref name="y"/>. <paramref name="size"/> is in points
    /// at the room's usual scale. A line the canvas made into a drawing once (<see cref="LabelTag"/>)
    /// is stamped: laying text out on every frame costs far more.
    /// </summary>
    public void Text(string text, double x, double y, double size, FontWeight weight, uint color, double width, bool isCentred = false)
    {
        if (Stamps.TryGetValue(LabelTag(text, size, weight, color), out var stamp))
        {
            Stamp(stamp, Point(isCentred ? x + (width / 2) : x, y), isCentred);
            return;
        }
        var pixels = size * Unit / 2;
        using var format = RoomSprites.Format(pixels, weight, CanvasWordWrapping.EmergencyBreak);
        using var layout = new CanvasTextLayout(_context, text, format, Length(width), 100);
        var measured = layout.LayoutBounds;
        var left = isCentred ? Px(x) + ((Length(width) - (float)measured.Width) / 2) - (float)measured.X : Px(x) - (float)measured.X;
        var top = Py(y) - (float)RoomSprites.Middle(layout, pixels);
        _context.DrawTextLayout(layout, left, top, Hex(color));
    }
}
