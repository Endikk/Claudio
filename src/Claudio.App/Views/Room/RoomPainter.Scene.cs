using System.Numerics;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Text;
using Windows.UI;
using Windows.UI.Text;

namespace Claudio.App;

/// <summary>What changes with the agents: the areas and their signs, Claudy's own desk, and the light.</summary>
internal sealed partial class RoomPainter
{
    /// <summary>The colour of an area's rug and sign. The spare one is grey.</summary>
    public static uint Tint(WorkFamily? family) => family switch
    {
        WorkFamily.Writing => 0x3EC6BC,
        WorkFamily.Commands => 0xE0B872,
        WorkFamily.Research => 0x4FA8F7,
        _ => 0x8F8A86,
    };

    /// <summary>The style of an area's sign, for the canvas to keep its words as an image.</summary>
    public static (double Size, FontWeight Weight, uint Color) SignStyle { get; } = (11, FontWeights.SemiBold, Ink);

    public static (double Size, FontWeight Weight, uint Color) NameplateStyle { get; } = (10, FontWeights.Bold, 0x2A1418);

    /// <summary>
    /// The name on the plate of the centre desk. Claudy's reads "Claudy"; the desk is the app's,
    /// so Claudio's reads its own name.
    /// </summary>
    public const string ChiefName = "Claudio";

    /// <summary>The rug's weave, one thread every two units, made once per device and scale.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CanvasDevice, Dictionary<double, CanvasGeometry>> Weaves = [];

    private CanvasGeometry Weave()
    {
        var cache = Weaves.GetValue(_context.Device, _ => []);
        if (cache.TryGetValue(Unit, out var weave))
        {
            return weave;
        }
        var size = RoomGeometry.ZoneSize;
        using var builder = new CanvasPathBuilder(_context.Device);
        for (double thread = 2; thread < size.Height - 1; thread += 2)
        {
            float left = Length(1), right = Length(size.Width - 1), top = Length(thread), bottom = Length(thread + 0.5);
            builder.BeginFigure(left, top);
            builder.AddLine(right, top);
            builder.AddLine(right, bottom);
            builder.AddLine(left, bottom);
            builder.EndFigure(CanvasFigureLoop.Closed);
        }
        weave = CanvasGeometry.CreatePath(builder);
        cache[Unit] = weave;
        return weave;
    }

    /// <summary>
    /// One area: its rug, and its sign. While the area changes work the new colour sweeps across
    /// the rug and the sign flips.
    /// </summary>
    public void Zone(RoomZone zone, RoomPoint origin)
    {
        double x = origin.X, y = origin.Y;
        var size = RoomGeometry.ZoneSize;
        var old = Tint(zone.Family);
        var isSwitching = zone.Incoming != zone.Family && zone.Progress > 0;
        var fresh = isSwitching ? Tint(zone.Incoming) : old;
        // A rug has a thickness: it shades the floor along its lower and right edges.
        Shadow(x + 1.5, y + size.Height, size.Width, 1.5, opacity: 0.3);
        Shadow(x + size.Width, y + 1.5, 1.5, size.Height, opacity: 0.3);
        Fill(old, x, y, size.Width, size.Height, opacity: 0.24);
        if (isSwitching)
        {
            var swept = Math.Round(size.Width * zone.Progress, MidpointRounding.AwayFromZero);
            Fill(Floor, x, y, swept, size.Height);
            Fill(fresh, x, y, swept, size.Height, opacity: 0.24);
        }
        var shown = zone.Progress > 0.5 ? fresh : old;
        _context.FillGeometry(Weave(), Point(x, y), White(0.028));
        foreach (var (edge, opacity) in new[] { (0.0, 0.55), (2.0, 0.36) })
        {
            var thickness = edge == 0 ? 1 : 0.5;
            double width = size.Width - (edge * 2), height = size.Height - (edge * 2);
            Fill(shown, x + edge, y + edge, width, thickness, opacity);
            Fill(shown, x + edge, y + size.Height - edge - thickness, width, thickness, opacity);
            Fill(shown, x + edge, y + edge, thickness, height, opacity);
            Fill(shown, x + size.Width - edge - thickness, y + edge, thickness, height, opacity);
        }
        var flip = isSwitching ? Math.Abs(1 - (2 * zone.Progress)) : 1;
        var signHeight = Math.Max(1, Math.Round(10 * flip, MidpointRounding.AwayFromZero));
        var top = y + 3 + Math.Floor((10 - signHeight) / 2);
        Shadow(x + 4.5, top + signHeight, 70, 1, opacity: 0.25);
        Fill(shown, x + 4, top, 70, signHeight);
        Fill(White(0.25), x + 4, top, 70, 0.5);
        if (signHeight >= 8)
        {
            Text(zone.Title, x + 7.5, y + 8, SignStyle.Size, SignStyle.Weight, SignStyle.Color, 64);
        }
    }

    private static readonly Rgba Coral = Theme.Color(Accent.Coral);

    /// <summary>
    /// Claudy's desk in the middle of the room: a wide desk on its own rug, two screens, a lamp and
    /// a name plate.
    /// </summary>
    public void Chief(RoomChief chief, int step, bool isLit, bool isSummaryOpen)
    {
        Fill(Floor, 182, 88, 116, 62);
        Fill(0x9B4E45, 182, 88, 116, 62, opacity: 0.45);
        foreach (var (x, y, width, height) in new[] { (182.0, 88.0, 116.0, 1.0), (182.0, 149.0, 116.0, 1.0), (182.0, 88.0, 1.0, 62.0), (297.0, 88.0, 1.0, 62.0) })
        {
            Fill(0xB08462, x, y, width, height);
        }
        Fill(0xD97757, 185, 91, 110, 0.5, opacity: 0.5);
        Fill(0xD97757, 185, 146.5, 110, 0.5, opacity: 0.5);
        Shadow(199, 138, 82, 2.5, opacity: 0.25);
        Fill(0x4A2A22, 228, 94, 24, 24);
        Fill(0x5A3328, 228.5, 94.5, 23, 23);
        Fill(0x6E4032, 230, 96, 20, 20);
        Fill(0x84503F, 230, 96, 20, 0.5);

        var isPresent = chief.State != RoomChiefState.Away;
        if (isPresent)
        {
            Claudy(step % 23 == 0 ? RoomSprite.Pose.SitBlink : RoomSprite.Pose.Sit, new RoomPoint(240, 120), Coral);
        }
        Top(198, 117, 84, 10, [(6, 5, 16), (54, 8.5, 20)]);
        Front(198, 127, 84, 10, [22, 62], [9, 70]);
        Fill(0xCFC3AE, 225.5, 128.5, 29, 8);
        Fill(Bubble, 226, 129, 28, 7);
        Fill(0xFFFFFF, 226, 129, 28, 0.5);
        Text(ChiefName, 226, 132.5, NameplateStyle.Size, NameplateStyle.Weight, NameplateStyle.Color, 28, isCentred: true);

        ChiefScreens(chief, step, isSummaryOpen);
        Keyboard(232, 120, 9, isPresent);
        if (isPresent)
        {
            Arms(232, 114, Coral, chief.State == RoomChiefState.Working, step);
        }
        Fill(Bubble, 223, 121, 2.5, 3);
        Fill(Bubble, 225.5, 121.5, 0.5, 1.5);
        Fill(0xEDE3D3, 251, 121, 5, 3.5);
        Fill(0xB5A898, 252, 122, 3, 0.5);
        Fill(0xB5A898, 252, 123, 2.5, 0.5);
        Fill(0x2B2F36, 279.5, 111, 0.5, 8);
        Fill(0x2B2F36, 277, 118.5, 5, 1);
        Fill(isLit ? 0xC98A2Cu : 0x6E655Au, 276.5, 107, 6, 4);
        Fill(isLit ? 0xF2A93Bu : 0x8A7F72u, 277, 107, 5, 3);
        Fill(isLit ? 0xFFE3B0u : 0x9A8E7Eu, 277.5, 110, 4, 0.5);
    }

    /// <summary>Where Claudy's second screen stands, and the middle of its glass.</summary>
    public static RoomPoint QuotaScreen { get; } = new(259, 102.5);

    public static RoomPoint QuotaCentre { get; } = new(QuotaScreen.X + 7.5, QuotaScreen.Y + 6.25);

    /// <summary>
    /// Claudy's two screens: what the session does on the left, the session quota as the card's
    /// ring on the right. The right one opens the summary, and says so with a coral frame while the
    /// summary is closed.
    /// </summary>
    public void ChiefScreens(RoomChief chief, int step, bool isSummaryOpen)
    {
        var isWaiting = chief.State == RoomChiefState.Waiting;
        Monitor(202, 102.5, isWaiting ? (step % 4 >= 2 ? 0xC98A2Cu : 0xF2A93Bu) : 0x1E2B36u, chief.Glyph, isWaiting ? Ink : 0xC8E4FFu);
        var screen = QuotaScreen;
        Monitor(screen.X, screen.Y, 0x1E2B36, null);
        var middle = Point(QuotaCentre.X, QuotaCentre.Y);
        var radius = Length(3.5);
        var stroke = Length(1.25);
        _context.DrawCircle(middle, radius, Hex(0x3A4A58), stroke, RoundCaps);
        if (chief.Quota is { } quota && quota > 0)
        {
            var colour = Paint(Theme.Tint(Accent.Coral, quota));
            var sweep = 360 * Math.Min(quota, 1);
            if (sweep >= 359.99)
            {
                _context.DrawCircle(middle, radius, colour, stroke, RoundCaps);
            }
            else
            {
                // From the top, clockwise, as the card's ring fills.
                Vector2 At(double degrees) => middle + new Vector2(radius * (float)Math.Cos(degrees * Math.PI / 180), radius * (float)Math.Sin(degrees * Math.PI / 180));
                using var builder = new CanvasPathBuilder(_context);
                builder.BeginFigure(At(-90));
                builder.AddArc(At(-90 + sweep), radius, radius, 0, CanvasSweepDirection.Clockwise, sweep > 180 ? CanvasArcSize.Large : CanvasArcSize.Small);
                builder.EndFigure(CanvasFigureLoop.Open);
                using var arc = CanvasGeometry.CreatePath(builder);
                _context.DrawGeometry(arc, colour, stroke, RoundCaps);
            }
        }
        if (!isSummaryOpen && step % 8 < 4)
        {
            Fill(0xD97757, screen.X - 1, screen.Y - 1, 17, 0.5);
            Fill(0xD97757, screen.X - 1, screen.Y + 13, 17, 0.5);
            Fill(0xD97757, screen.X - 1, screen.Y - 1, 0.5, 14.5);
            Fill(0xD97757, screen.X + 15.5, screen.Y - 1, 0.5, 14.5);
        }
    }

    /// <summary>A Claudy that is done, sitting on the sofa.</summary>
    public void Lounger(RoomLounger lounger, int step)
    {
        var seat = RoomGeometry.Sofa(lounger.Slot);
        Claudy((step + (lounger.Slot * 7)) % 23 == 0 ? RoomSprite.Pose.Blink : RoomSprite.Pose.Rest, seat, lounger.Tint);
    }

    /// <summary>Coral corners round the desk whose agent is being looked at.</summary>
    public void Selection(RoomPoint desk)
    {
        var size = RoomGeometry.DeskSize;
        double left = desk.X - 2, right = desk.X + size.Width + 1, top = desk.Y - 1.5, bottom = desk.Y + size.Height + 0.5;
        foreach (var (x, y, across, down) in new[]
        {
            (left, top, 5.0, 1.0), (left, top, 1.0, 5.0), (right - 4, top, 5.0, 1.0), (right, top, 1.0, 5.0),
            (left, bottom, 5.0, 1.0), (left, bottom - 4, 1.0, 5.0), (right - 4, bottom, 5.0, 1.0), (right, bottom - 4, 1.0, 5.0),
        })
        {
            Fill(0xD97757, x, y, across, down);
        }
    }

    /// <summary>
    /// Past nine tenths of the session quota the room says so, as the card does: a red glow
    /// breathes at its edges.
    /// </summary>
    public void Strain(double quota, double time)
    {
        if (quota < StrainedFrom)
        {
            return;
        }
        var size = RoomGeometry.Size;
        var breath = 0.5 + (0.5 * Math.Sin(time * 2.5));
        const uint Red = 0xF0604D;
        using var glow = Radial(Point(size.Width / 2, size.Height / 2), Length(size.Width * 0.6), Hex(Red, 0), Hex(Red, 0), Hex(Red, 0.1 + (0.1 * breath)));
        _context.FillRectangle(Px(0), Py(0), Length(size.Width), Length(size.Height), glow);
    }

    /// <summary>The shares of the session quota at which the room warns, the card's own thresholds.</summary>
    public const double WarnedFrom = 0.75;
    public const double StrainedFrom = 0.9;

    /// <summary>The corners of the room fall away a little: the eye stays on the desks.</summary>
    public void Vignette()
    {
        var size = RoomGeometry.Size;
        using var shade = Radial(Point(size.Width / 2, size.Height * 0.52), Length(size.Width * 0.62), Black(0), Black(0), Black(0.3));
        _context.FillRectangle(Px(0), Py(0), Length(size.Width), Length(size.Height), shade);
    }

    /// <summary>
    /// Dims the room for the hour, then lets every lamp and every lit screen shine through. SwiftUI
    /// multiplies and screens on its context; Win2D composes those with effects, so the room drawn
    /// so far comes in as <paramref name="room"/>, drawn from the room's own corner, and leaves
    /// dimmed (a tint multiplies it) and lit (the lamps screened over it) on this painter's session.
    /// </summary>
    public void Light(RoomSky sky, IReadOnlyList<RoomPoint> screens, ICanvasImage room)
    {
        if (sky.Dim is not { } dim)
        {
            _context.DrawImage(room, _offset);
            return;
        }
        using var dimmed = new TintEffect { Source = room, Color = Hex(dim) };
        if (!sky.HasLamps)
        {
            _context.DrawImage(dimmed, _offset);
            return;
        }
        using var glows = new CanvasCommandList(_context);
        using (var session = glows.CreateDrawingSession())
        {
            var lamps = new RoomPainter(session, Unit, Stamps);
            var drop = RoomGeometry.CentreDrop;
            const uint Warm = 0xFFC878;
            lamps.Glow(new RoomPoint(279.5, 110 + drop), 52, Hex(Warm, 0.5));
            lamps.Glow(new RoomPoint(191, 194 + drop), 50, Hex(Warm, 0.45));
            lamps.Glow(new RoomPoint(435, 30), 46, Hex(0x4FA8F7, 0.22));
            var cool = Hex(0xA0C8FF, 0.22);
            foreach (var screen in screens.Concat([new RoomPoint(209.5, 109 + drop), new RoomPoint(QuotaCentre.X, QuotaCentre.Y + drop)]))
            {
                lamps.Glow(screen, 20, cool);
            }
        }
        using var lit = new BlendEffect { Mode = BlendEffectMode.Screen, Background = dimmed, Foreground = glows };
        _context.DrawImage(lit, _offset);
    }

    private void Glow(RoomPoint centre, double radius, Color color)
    {
        var middle = Point(centre.X, centre.Y);
        using var brush = Radial(middle, Length(radius), color, Black(0));
        _context.FillRectangle(middle.X - Length(radius), middle.Y - Length(radius), Length(radius * 2), Length(radius * 2), brush);
    }
}
