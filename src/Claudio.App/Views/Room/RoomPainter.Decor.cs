using System.Numerics;
using Claudio.Core.Models;
using Microsoft.Graphics.Canvas.Geometry;

namespace Claudio.App;

/// <summary>
/// What furnishes the room. In a niche of the back wall, where racks are seen as they stand: the
/// server room behind its glass. In the middle of the floor, clear of the corridors the agents
/// walk: the lounge. By the front wall: the door.
/// </summary>
internal sealed partial class RoomPainter
{
    // Robot vacuum

    /// <summary>
    /// The vacuum's dock against the back wall: a tower, the ramp the vacuum parks on with its two
    /// contacts, and the cable to its socket. Claudy's <c>dock()</c>.
    /// </summary>
    public void DockStation()
    {
        var x = RoomVacuum.Dock.X;
        // Socket and cable.
        Fill(0xEDE3D3, x + 9, 29.5, 3.5, 4.5);
        Fill(0xCFC3AE, x + 9, 33.5, 3.5, 0.5);
        Fill(0x2A1418, x + 10, 31, 0.5, 1.25);
        Fill(0x2A1418, x + 11.25, 31, 0.5, 1.25);
        Fill(0x16181C, x + 10.5, 34, 0.5, 9.5);
        Fill(0x16181C, x + 5, 43, 6, 0.5);
        // Ramp.
        Shadow(x - 6.5, 51.5, 13, 1.5, opacity: 0.3);
        Shape([new(x - 5, 45.5), new(x + 5, 45.5), new(x + 6.5, 52), new(x - 6.5, 52)], Hex(0x2B2F36));
        Fill(0x3A3F48, x - 5, 45.5, 10, 0.5);
        for (var rib = 0; rib < 3; rib++)
        {
            Fill(0x23262C, x - 5.25 - (rib * 0.3), 47.75 + (rib * 1.4), 10.5 + (rib * 0.6), 0.5);
        }
        Fill(0xC6C8D0, x - 2.75, 46.5, 1.5, 1);
        Fill(0xC6C8D0, x + 1.25, 46.5, 1.5, 1);
        Fill(0x16181C, x - 6.5, 51.5, 13, 0.5);
        // Tower.
        Fill(0x16181C, x - 5.5, 29.5, 11, 16.5);
        Fill(0x2B2F36, x - 5, 30, 10, 15.5);
        Fill(0x3A3F48, x - 5, 30, 10, 2);
        Fill(0x4A505B, x - 5, 30, 10, 0.5);
        Fill(0x16181C, x - 5, 32, 10, 0.5);
        Fill(White(0.06), x - 5, 32.5, 1, 13);
        Fill(Black(0.25), x + 3.5, 32.5, 1.5, 13);
        Fill(0x23262C, x - 3.5, 34, 7, 8);
        Fill(0x16181C, x - 3.5, 34, 7, 0.5);
        Fill(0x4A505B, x - 2.5, 38, 5, 0.5);
        for (var slot = 0; slot < 3; slot++)
        {
            Fill(0x16181C, x - 2.5, 39.5 + slot, 5, 0.5);
        }
        Fill(0x111316, x - 2, 43.5, 4, 1.5);
        Fill(0xF0604D, x - 0.5, 44, 1, 0.5, opacity: 0.7);
    }

    /// <summary>The dock's three lights: they climb in green while the vacuum charges; one amber light waits while it is out.</summary>
    public void DockLights(bool isCharging, int step)
    {
        var x = RoomVacuum.Dock.X;
        var lit = isCharging ? step / 3 % 4 : 1;
        for (var light = 0; light < 3; light++)
        {
            var color = light < lit ? (isCharging ? 0x4CC38Au : 0xF2A93Bu) : 0x16181Cu;
            Fill(color, x - 2.75 + (light * 2), 35.5, 1.5, 1);
        }
    }

    /// <summary>
    /// The floor the vacuum just went over: a faint, clean strip that follows the corners it took
    /// and fades behind it.
    /// </summary>
    public void VacuumTrail(IReadOnlyList<RoomPoint> points)
    {
        if (points.Count <= 1)
        {
            return;
        }
        var count = (double)(points.Count - 1);
        for (var index = 0; index + 1 < points.Count; index++)
        {
            RoomPoint from = points[index], to = points[index + 1];
            var fade = 1 - (index / count);
            _context.DrawLine(Point(from.X, from.Y), Point(to.X, to.Y), White(0.07 * fade * fade), Length(7));
        }
    }

    /// <summary>
    /// The vacuum, seen a little from above: a low round body, its lid and its turret, a bumper on
    /// the side it is heading to, a light, and two side brushes that turn while it works.
    /// </summary>
    public void Vacuum(RoomVacuum.State state, double time)
    {
        double x = state.Position.X, y = state.Position.Y;
        var ahead = state.Heading;
        Ellipse(new RoomPoint(x, y + 2.4), 5.6, 2.6, Black(0.3));
        if (!state.IsDocked)
        {
            // Side brushes, at the two front corners.
            var side = new RoomVector(-ahead.Dy, ahead.Dx);
            foreach (var sign in new[] { -1.0, 1.0 })
            {
                var hub = new RoomPoint(x + (ahead.Dx * 4) + (side.Dx * sign * 3.6), y + (((ahead.Dy * 4) + (side.Dy * sign * 3.6)) * 0.7));
                for (var spoke = 0; spoke < 3; spoke++)
                {
                    var angle = (time * 9 * sign) + (spoke * 2 * Math.PI / 3);
                    _context.DrawLine(Point(hub.X, hub.Y), Point(hub.X + (1.7 * Math.Cos(angle)), hub.Y + (1.2 * Math.Sin(angle))),
                                      Hex(0x9A9CA6), Length(0.4));
                }
            }
        }
        Ellipse(new RoomPoint(x, y + 1.1), 5, 3.5, Hex(0x111316));
        Ellipse(new RoomPoint(x, y), 5, 3.5, Hex(0x23262C));
        Ellipse(new RoomPoint(x, y - 0.1), 4.4, 3, Hex(0x363B44));
        Ellipse(new RoomPoint(x - 1, y - 0.9), 2.6, 1.3, White(0.07));
        Ellipse(new RoomPoint(x, y + 0.3), 2.7, 1.8, Hex(0x23262C));
        Ellipse(new RoomPoint(x, y - 0.3), 1.6, 1.1, Hex(0x16181C));
        Ellipse(new RoomPoint(x, y - 0.6), 1.5, 1, Hex(0x4A505B));
        Fill(0x9A9CA6, x - 0.75, y - 1.2, 1, 0.5);

        // The bumper: a quarter of the rim, on the side it is heading to. The rim starts on the
        // right and turns clockwise, as SwiftUI's ellipse does.
        var quarter = ahead.Dx > 0 ? 0 : ahead.Dy > 0 ? 0.25 : ahead.Dx < 0 ? 0.5 : 0.75;
        Rim(new RoomPoint(x, y), 5, 3.5, quarter - 0.11, quarter + 0.11);

        var isBlinking = (int)(time * 2) % 2 == 0;
        var light = state.IsDocked ? 0x4CC38Au : (isBlinking ? 0x4FA8F7u : 0x1E3A56u);
        Fill(light, x + (ahead.Dx * 2.6) - 0.5, y + (ahead.Dy * 1.7) - 0.1, 1, 0.5);
    }

    /// <summary>A stretch of an ellipse's rim, from one share of a turn to another, 0 on the right.</summary>
    private void Rim(RoomPoint centre, double across, double down, double from, double to)
    {
        Vector2 At(double share) => Point(centre.X + (across * Math.Cos(share * 2 * Math.PI)), centre.Y + (down * Math.Sin(share * 2 * Math.PI)));
        using var builder = new CanvasPathBuilder(_context);
        builder.BeginFigure(At(from));
        // Short arcs, so the turn never wraps the wrong way round.
        const int Steps = 4;
        for (var step = 1; step <= Steps; step++)
        {
            builder.AddArc(At(from + ((to - from) * step / Steps)), Length(across), Length(down), 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
        }
        builder.EndFigure(CanvasFigureLoop.Open);
        using var path = CanvasGeometry.CreatePath(builder);
        _context.DrawGeometry(path, Hex(0x9A9CA6), Length(0.7), RoundCaps);
    }

    // Server room

    /// <summary>Three racks in a niche of the back wall, behind a glass front, on a raised floor.</summary>
    public void Servers()
    {
        Fill(0x111316, 398, 5, 74, 41);
        Fill(0x1A1D22, 400, 6, 70, 40);
        Fill(0x2B2F36, 398, 46, 74, 6);
        Fill(0x3A3F48, 398, 46, 74, 0.5);
        for (double tile = 398; tile < 472; tile += 9.25)
        {
            Fill(0x3A3F48, tile, 46, 0.5, 6);
        }
        Fill(0x191310, 398, 51.5, 74, 0.5);
        // The cable tray overhead.
        Fill(0x3A3F48, 400, 6.5, 70, 1);
        Fill(0x4A505B, 400, 6.5, 70, 0.5);
        foreach (var (x, color) in new[] { (411.0, 0x4FA8F7u), (433.5, 0xE0B872u), (456.0, 0xD97757u) })
        {
            Fill(color, x, 7.5, 0.5, 2);
            Fill(color, x + 1.5, 7.5, 0.5, 2);
        }
        for (var index = 0; index < 3; index++)
        {
            Rack(RackLeft + (index * RackPitch), RackTop);
        }
        Fill(0x4FA8F7, 398, 44, 74, 2, opacity: 0.1);
        // The glass front: its frame and two sheens.
        var sheen = White(0.05);
        Shape([new(412, 5.5), new(421, 5.5), new(408, 46), new(399, 46)], sheen);
        Shape([new(458, 5.5), new(462, 5.5), new(449, 46), new(445, 46)], sheen);
        Fill(0x70727E, 397, 4, 76, 1.5);
        Fill(0x9A9CA6, 397, 4, 76, 0.5);
        Fill(0x70727E, 397, 4, 1.5, 48);
        Fill(0x70727E, 471.5, 4, 1.5, 48);
        Fill(0x9A9CA6, 397, 4, 0.5, 48);
        // The keypad by the door.
        Fill(0x2B2F36, 392.5, 24, 3, 5);
        Fill(0x4A505B, 392.5, 24, 3, 0.5);
        Fill(0x4CC38A, 393.5, 25, 1, 1);
        Fill(0x70727E, 393, 27, 0.75, 0.75);
        Fill(0x70727E, 394.25, 27, 0.75, 0.75);
    }

    private const double RackLeft = 402;
    private const double RackPitch = 22.5;
    private const double RackTop = 9.5;

    /// <summary>
    /// The racks' lights, blinking: the only thing that moves in the server room. They follow the
    /// session quota: green, then amber from three quarters, then red from nine tenths, with a red
    /// bar flashing over the door.
    /// </summary>
    public void ServerLights(int step, double quota)
    {
        var busy = quota >= StrainedFrom ? 0xF0604Du : quota >= WarnedFrom ? 0xF2A93Bu : 0x4CC38Au;
        var idle = quota >= StrainedFrom ? 0x3A1E1Bu : quota >= WarnedFrom ? 0x3A2C1Au : 0x1F3A2Cu;
        if (quota >= StrainedFrom)
        {
            Fill(step % 4 < 2 ? 0xF0604Du : 0x3A1E1Bu, 399, 5.5, 72, 1);
        }
        for (var index = 0; index < 3; index++)
        {
            var x = RackLeft + (index * RackPitch);
            for (var row = 0; row < 7; row++)
            {
                var top = RackTop + 3 + (row * 4.5);
                var beat = step + (index * 3) + (row * 2);
                Fill(beat % 5 < 3 ? busy : idle, x + 3.5, top + 1.5, 1, 1);
                Fill((beat + row) % 9 < 2 ? 0xF2A93Bu : 0x3A2C1Au, x + 5.5, top + 1.5, 1, 1);
            }
        }
    }

    private void Rack(double x, double y)
    {
        Shadow(x, y + 37, 20, 1.5, opacity: 0.35);
        Fill(0x23262C, x, y, 20, 37);
        Fill(0x3A3F48, x, y, 20, 0.5);
        Fill(0x2F333B, x, y, 0.5, 37);
        Fill(0x16181C, x + 19.5, y, 0.5, 37);
        Fill(0x1C1F24, x + 1.5, y + 2, 17, 33.5);
        for (var row = 0; row < 7; row++)
        {
            var top = y + 3 + (row * 4.5);
            Fill(0x2B2F36, x + 2.5, top, 15, 3.5);
            Fill(0x3A3F48, x + 2.5, top, 15, 0.5);
            Fill(0x1F3A2C, x + 3.5, top + 1.5, 1, 1);
            Fill(0x3A2C1A, x + 5.5, top + 1.5, 1, 1);
            for (var bay = 0; bay < 4; bay++)
            {
                Fill(0x3A3F48, x + 9 + (bay * 2), top + 1, 1.25, 1.75);
            }
        }
        Fill(0x111316, x + 1, y + 37, 2, 1);
        Fill(0x111316, x + 17, y + 37, 2, 1);
    }

    // Lounge

    /// <summary>A rug in the middle of the floor, a sofa, a low table and a lamp.</summary>
    public void Lounge(bool isLit)
    {
        const uint Cream = 0xEDE3D3;
        Fill(Cream, 178, 184, 124, 60, opacity: 0.13);
        foreach (var (x, y, width, height) in new[] { (178.0, 184.0, 124.0, 1.0), (178.0, 243.0, 124.0, 1.0), (178.0, 184.0, 1.0, 60.0), (301.0, 184.0, 1.0, 60.0) })
        {
            Fill(Cream, x, y, width, height, opacity: 0.4);
        }
        foreach (var (x, y, width, height) in new[] { (181.0, 187.0, 118.0, 0.5), (181.0, 240.5, 118.0, 0.5), (181.0, 187.0, 0.5, 54.0), (298.5, 187.0, 0.5, 54.0) })
        {
            Fill(0xD97757, x, y, width, height, opacity: 0.55);
        }
        for (double fringe = 185; fringe < 243; fringe += 2)
        {
            Fill(Cream, 176.5, fringe, 1.5, 0.5, opacity: 0.35);
            Fill(Cream, 302, fringe, 1.5, 0.5, opacity: 0.35);
        }

        // Lamp.
        Shadow(187.5, 219, 8, 1.5);
        Fill(0x2B2F36, 190.5, 197, 1, 21);
        Fill(0x3A3F48, 190.5, 197, 0.5, 21);
        Fill(0x2B2F36, 187, 217.5, 8, 1.5);
        Fill(isLit ? 0xF2A93Bu : 0xD8CDB8u, 186, 189, 10, 8);
        Fill(isLit ? 0xC98A2Cu : 0xB5A898u, 186, 189, 10, 1);
        Fill(isLit ? 0xFFE3B0u : 0xEDE3D3u, 186, 196, 10, 1);
        Fill(White(0.18), 186, 190, 1.5, 6);

        // Sofa.
        Shadow(205, 213.5, 70, 2.5);
        Fill(0x7A3D36, 208, 190, 64, 10);
        Fill(0x9B4E45, 208, 190.5, 64, 3.5);
        Fill(0xB5604F, 208, 190, 64, 0.5);
        foreach (var x in new[] { 204.0, 270.0 })
        {
            Fill(0x9B4E45, x, 194, 6, 18);
            Fill(0xB5604F, x, 194, 6, 0.5);
            Fill(0x7A3D36, x, 211.5, 6, 0.5);
        }
        foreach (var x in new[] { 210.0, 240.5 })
        {
            Fill(0xD97757, x, 199, 29.5, 9);
            Fill(0xE9A07C, x, 199, 29.5, 1.5);
            Fill(0xC0644A, x, 207, 29.5, 1);
        }
        Fill(0x9B4E45, 239.5, 199, 1, 9);
        Fill(0x9B4E45, 210, 208, 60, 4);
        Fill(0x7A3D36, 210, 211.5, 60, 0.5);
        Fill(0x4A332B, 206, 212, 2, 1.5);
        Fill(0x4A332B, 272, 212, 2, 1.5);
        foreach (var (x, color, light) in new[] { (212.0, 0xE0B872u, 0xEDD29Cu), (260.0, 0x6FAE8Fu, 0x8FC7A8u) })
        {
            Fill(color, x, 194.5, 8, 6.5);
            Fill(light, x, 194.5, 8, 1);
            Shadow(x, 200.5, 8, 0.5, opacity: 0.2);
        }

        // Low table.
        Shadow(223, 231.5, 34, 2);
        Fill(0xB98D69, 222, 220, 36, 7);
        Fill(0xC9A07B, 222, 220, 36, 0.5);
        Fill(0xAD815F, 226, 224, 14, 0.5);
        Fill(0x8C6248, 222, 227, 36, 2.5);
        Fill(0x68463C, 222, 229, 36, 0.5);
        Fill(0x4A332B, 223.5, 229.5, 1.5, 2.5);
        Fill(0x4A332B, 255, 229.5, 1.5, 2.5);
        Fill(0x4FA8F7, 227, 222, 8, 3.5);
        Fill(0x9CCBF7, 227, 222, 8, 0.5);
        Fill(Bubble, 227, 225.5, 8, 0.5);
        Fill(Bubble, 240, 222.5, 3, 3);
        Fill(Bubble, 243, 223.25, 0.75, 1.5);
        Fill(0x8C6248, 240.5, 223, 2, 0.5);
        Fill(0x9B4E45, 249.5, 223.5, 4, 2.5);
        Fill(0x4F8A6D, 248.5, 221, 6, 3);
        Fill(0x8FC7A8, 250.5, 220, 2, 2);

        TallPlant(new RoomPoint(286, 197));
    }

    // Door and plants

    /// <summary>The way in, in the front wall, and its mat.</summary>
    public void Door()
    {
        Fill(0x1F1815, 0, 264, 480, 6);
        Fill(0x2E2520, 6, 264, 468, 0.5);
        Fill(0x382A22, 222, 264, 36, 6);
        Fill(0x5E4D42, 220, 263, 2, 7);
        Fill(0x5E4D42, 258, 263, 2, 7);
        Fill(0x7A6556, 220, 263, 2, 0.5);
        Fill(0x7A6556, 258, 263, 2, 0.5);
        Shadow(224.5, 262, 32, 1, opacity: 0.2);
        Fill(0x68463C, 224, 253, 32, 9);
        Fill(0xB08462, 225, 254, 30, 7);
        for (var line = 0; line < 4; line++)
        {
            Fill(0x8C6248, 227, 255.5 + (line * 1.5), 26, 0.5);
        }
    }

    /// <summary>A round, leafy plant in a clay pot, 10 by 15 from its top left corner.</summary>
    public void Bush(RoomPoint spot)
    {
        double x = spot.X, y = spot.Y;
        Shadow(x + 1, y + 13.5, 8, 1.5);
        Fill(0x7A3D36, x + 2.5, y + 13, 5, 1);
        Fill(0x9B4E45, x + 2, y + 9, 6, 4.5);
        Fill(0xB5604F, x + 1.5, y + 8.5, 7, 1.5);
        Fill(0xC97A66, x + 1.5, y + 8.5, 7, 0.5);
        Fill(0x4F8A6D, x, y + 3.5, 4, 5);
        Fill(0x4F8A6D, x + 6.5, y + 4.5, 3.5, 4);
        Fill(0x6FAE8F, x + 1, y + 1.5, 8, 7);
        Fill(0x8FC7A8, x + 5, y, 3, 4);
        Fill(0x8FC7A8, x + 2, y + 2.5, 2, 2);
        Fill(0x4F8A6D, x + 4, y + 5, 0.5, 3.5);
        Fill(0x4F8A6D, x + 6.5, y + 3, 0.5, 2);
    }

    /// <summary>A tall plant with upright leaves, 10 by 22 from its top left corner.</summary>
    public void TallPlant(RoomPoint spot)
    {
        double x = spot.X, y = spot.Y;
        Shadow(x + 1, y + 20.5, 8, 1.5);
        foreach (var (offset, top, color) in new[] { (1.5, 6.0, 0x4F8A6Du), (3.0, 1.0, 0x6FAE8Fu), (4.75, 3.5, 0x8FC7A8u), (6.25, 0.0, 0x6FAE8Fu), (7.75, 7.0, 0x4F8A6Du) })
        {
            Fill(color, x + offset, y + top, 1.5, 16 - top);
            Fill(White(0.15), x + offset, y + top, 0.5, 16 - top);
        }
        Fill(0x7A3D36, x + 2.5, y + 20, 5, 1);
        Fill(0x9B4E45, x + 2, y + 16, 6, 4.5);
        Fill(0xB5604F, x + 1.5, y + 15.5, 7, 1.5);
        Fill(0xC97A66, x + 1.5, y + 15.5, 7, 0.5);
    }
}
