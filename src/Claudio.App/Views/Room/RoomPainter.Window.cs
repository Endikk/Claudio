using Claudio.Core.Models;
using Windows.Foundation;

namespace Claudio.App;

/// <summary>
/// The window of the agent room and the city behind it. The hour decides everything in it: the
/// colour of the sky, where the sun or the moon stands, how dark the buildings are, and how many of
/// their windows are lit.
/// </summary>
internal sealed partial class RoomPainter
{
    private enum Roof
    {
        Flat,
        Antenna,
        Tank,
        Step,
        Slant,
    }

    /// <param name="Tone">Which of the four facades it is built in.</param>
    private readonly record struct Building(double X, double Width, double Height, int Tone, Roof Roof);

    /// <summary>The glass, in units.</summary>
    private static readonly Rect Glass = new(17, 9, 140, 27);

    private static readonly Building[] Near =
    [
        new(17, 13, 12, 0, Roof.Tank),
        new(31, 10, 18, 1, Roof.Antenna),
        new(42, 15, 9, 2, Roof.Flat),
        new(58, 11, 22, 3, Roof.Step),
        new(70, 14, 11, 0, Roof.Flat),
        new(85, 9, 8, 1, Roof.Slant),
        new(95, 16, 11, 2, Roof.Tank),
        new(112, 12, 23, 3, Roof.Antenna),
        new(125, 13, 15, 0, Roof.Step),
        new(139, 18, 10, 1, Roof.Flat),
    ];

    /// <summary>
    /// The towers further off, hazier, between the nearer ones. The skyline dips in the middle of
    /// the window: that is where the sun stands at noon and the moon at midnight.
    /// </summary>
    private static readonly (double X, double Width, double Height)[] Far =
    [
        (22, 12, 17), (48, 9, 21), (73, 10, 13), (103, 8, 17), (121, 9, 26), (133, 10, 21), (150, 7, 18),
    ];

    private static readonly uint[] Facades = [0x8793A0, 0x73818F, 0x9A9186, 0x66747F];
    private const uint Silhouette = 0x0D1017;

    /// <summary>
    /// A number from 0 to 1 that is always the same for the same three integers: which windows of
    /// the city light up first is decided once and for all.
    /// </summary>
    private static double Chance(int a, int b, int c)
    {
        var hash = unchecked((uint)((a * 73_856_093L) ^ (b * 19_349_663L) ^ (c * 83_492_791L)));
        hash ^= hash >> 13;
        hash = unchecked(hash * 1_274_126_177u);
        hash ^= hash >> 16;
        return (hash & 0xFFFF) / 65_535.0;
    }

    /// <summary>The frame of the window in units: all <see cref="Window"/> draws stays inside it.</summary>
    public static Rect WindowFrame { get; } = new(12, 6, 150, 35);

    /// <summary>
    /// What of the window a pass draws. The sky and the city move slowly and cost the most; what
    /// falls is cheap and fast. The room draws them at their own rates, a small room all at once.
    /// </summary>
    public enum WindowPart
    {
        All,

        /// <summary>The frame, the sky and the city.</summary>
        View,

        /// <summary>The rain or the snow, and what stands in front of the glass.</summary>
        Falling,
    }

    public void Window(RoomSky sky, RoomWeather weather, int step, double time, WindowPart part = WindowPart.All)
    {
        // Everything behind the glass is cut to it: a cloud or a halo stops at the frame.
        var clip = new Rect(Px(Glass.X), Py(Glass.Y), Length(Glass.Width), Length(Glass.Height));
        if (part != WindowPart.Falling)
        {
            Fill(0x4A3C34, 14, 6, 146, 33);
            Fill(0x5E4D42, 15, 7, 144, 31);
            Fill(0x3A2F29, 16, 8, 142, 29);
            using (_context.CreateLayer(1, clip))
            {
                Heavens(sky, weather, step, time);
                City(sky, weather, step, time);
            }
        }
        if (part != WindowPart.View)
        {
            using (_context.CreateLayer(1, clip))
            {
                Falling(weather, time);
            }
        }
        // With rain or snow on its own pass, what stands in front of the glass is drawn there.
        if (part == WindowPart.View && weather.IsFalling())
        {
            return;
        }

        // The glass catches the light: two slanted sheens.
        var sheen = White(0.1 - (0.06 * sky.Darkness));
        Shape([new(38, 9), new(47, 9), new(33, 36), new(24, 36)], sheen);
        Shape([new(133, 9), new(137, 9), new(123, 36), new(119, 36)], sheen);
        // A blind rolled most of the way up, and its cord.
        Fill(0xEDE3D3, 17, 9, 140, 2.5);
        Fill(0xCFC3AE, 17, 11, 140, 0.5);
        Fill(0xCFC3AE, 153.5, 11.5, 0.5, 5);
        Fill(0xEDE3D3, 153, 16.5, 1.5, 1.5);
        Fill(0x5E4D42, 62.5, 8, 2, 29);
        Fill(0x5E4D42, 110, 8, 2, 29);
        Fill(0x7A6556, 62.5, 8, 0.5, 29);
        Fill(0x7A6556, 110, 8, 0.5, 29);
        Fill(0x6E5B4E, 12, 38, 150, 2.5);
        Fill(0x8A7363, 12, 38, 150, 0.5);
        Fill(0x4A3C34, 12, 40, 150, 0.5);
    }

    /// <summary>The sky, its stars, its clouds, and the sun or the moon.</summary>
    private void Heavens(RoomSky sky, RoomWeather weather, int step, double time)
    {
        var glass = Glass;
        var bands = sky.Bands;
        var bandHeight = glass.Height / bands.Count;
        for (var index = 0; index < bands.Count; index++)
        {
            Fill(bands[index], glass.X, glass.Y + (index * bandHeight), glass.Width, bandHeight + 0.5);
        }
        if (sky.Starlight > 0 && weather == RoomWeather.Clear)
        {
            for (var star = 0; star < 22; star++)
            {
                var twinkle = (step + (star * 5)) % 17 > 2 ? 1.0 : 0.35;
                var size = star % 5 == 0 ? 1 : 0.5;
                var starX = glass.X + 3 + (star * 37 % 134);
                var starY = glass.Y + 3.5 + (star * 11 % 13);
                Fill(Hex(0xE8ECF8, sky.Starlight * twinkle), starX, starY, size, size);
            }
        }
        if (sky.Moon is { } moon)
        {
            var centre = new RoomPoint(glass.X + (glass.Width * moon.Across), 34 - (19 * moon.Height));
            Disc(centre, 7, Hex(0xC8D4F0, 0.12));
            Disc(centre, 3.25, Hex(0xE8ECF8));
            Disc(new RoomPoint(centre.X + 1.5, centre.Y - 0.75), 2.75, Hex(bands[2]));
        }
        if (sky.Sun is { } sun)
        {
            var centre = new RoomPoint(glass.X + (glass.Width * sun.Across), 34 - (19 * sun.Height));
            var color = RoomSky.Mix(0xFF9A4A, 0xFFF4C8, sun.Height);
            var middle = Point(centre.X, centre.Y);
            using (var halo = Radial(middle, Length(14), Hex(color, 0.55), Hex(color, 0)))
            {
                _context.FillCircle(middle, Length(14), halo);
            }
            Disc(centre, 3.5, Hex(color));
            Disc(centre, 2.5, White(0.35));
        }
        // Under a covered sky a grey veil takes the colour out, and the sun is a pale patch in it.
        if (weather != RoomWeather.Clear)
        {
            Fill(RoomSky.Mix(0x9AA3AE, 0x1E232D, sky.Darkness), glass.X, glass.Y, glass.Width, glass.Height, opacity: 0.66);
        }
        var cloudiness = Math.Max(0, 1 - (sky.Darkness * 1.4));
        if (cloudiness <= 0)
        {
            return;
        }
        var white = weather == RoomWeather.Clear ? White(0.9 * cloudiness)
            : Hex(RoomSky.Mix(0xD3D9E0, 0x596270, sky.Darkness), 0.75 * cloudiness);
        var clouds = new List<(double Start, double Y, double Scale, double Speed)> { (10, 15, 1, 0.5), (70, 19.5, 0.75, 0.3), (118, 13.5, 0.6, 0.4) };
        if (weather != RoomWeather.Clear)
        {
            clouds.AddRange([(40, 13, 1.2, 0.35), (100, 17, 1.1, 0.45), (150, 14.5, 0.9, 0.25)]);
        }
        foreach (var (start, y, scale, speed) in clouds)
        {
            var drift = ((start + (time * speed)) % 170) - 15;
            var x = glass.X + drift;
            Fill(white, x, y + (1.5 * scale), 16 * scale, 2 * scale);
            Fill(white, x + (3 * scale), y, 9 * scale, 2 * scale);
            Fill(white, x + (6 * scale), y - (1 * scale), 4 * scale, 1.5 * scale);
        }
    }

    /// <summary>
    /// How lit a window of the city is, 0 to 1. Each has its own moment to come on, set by
    /// <paramref name="luck"/>; around that moment it wavers, on and off over a minute or so, and it
    /// fades rather than switches.
    /// </summary>
    private static double CityGlow(double luck, double share, double time)
    {
        var waver = 0.035 * Math.Sin((time * (0.08 + (luck * 0.12))) + (luck * 40));
        var lead = (share - luck + waver) / 0.05;
        if (lead <= 0)
        {
            return 0;
        }
        var part = Math.Min(lead, 1);
        return part * part * (3 - (2 * part));
    }

    /// <summary>Two rows of buildings: hazy towers behind, and in front the ones whose windows light up.</summary>
    private void City(RoomSky sky, RoomWeather weather, int step, double time)
    {
        var ground = Glass.Bottom;
        var bands = sky.Bands;
        var litShare = sky.LitShare;
        var shade = Math.Pow(sky.Darkness, 0.8);
        var haze = RoomSky.Mix(RoomSky.Mix(0x9AA6B2, Silhouette, shade), bands[5], 0.5);
        for (var index = 0; index < Far.Length; index++)
        {
            var tower = Far[index];
            Fill(haze, tower.X, ground - tower.Height, tower.Width, tower.Height);
            Fill(White(0.06), tower.X, ground - tower.Height, tower.Width, 0.5);
            for (var y = ground - tower.Height + 2; y < ground - 1; y += 3)
            {
                var column = 0;
                for (var x = tower.X + 1.5; x < tower.X + tower.Width - 1; x += 2.5)
                {
                    var glow = CityGlow(Chance(index + 40, (int)(y * 2), column), litShare * 0.7, time);
                    if (glow > 0)
                    {
                        Fill(Hex(0xFFD98A, 0.7 * glow), x, y, 0.75, 0.75);
                    }
                    column++;
                }
            }
        }
        var pane = RoomSky.Mix(bands[3], 0x1B2230, sky.Darkness);
        for (var index = 0; index < Near.Length; index++)
        {
            var building = Near[index];
            var facade = RoomSky.Mix(Facades[building.Tone], Silhouette, shade);
            var top = ground - building.Height;
            RoofOf(building, top, facade, sky, step, index);
            Fill(facade, building.X, top, building.Width, building.Height);
            Shadow(building.X + building.Width - 1.5, top, 1.5, building.Height, opacity: 0.2);
            Fill(White(0.12 - (0.08 * sky.Darkness)), building.X, top, building.Width, 0.5);
            if (weather == RoomWeather.Snow)
            {
                Fill(White(0.8 - (0.45 * sky.Darkness)), building.X, top - 0.5, building.Width, 1);
            }
            var dark = RoomSky.Mix(facade, pane, 0.45);
            var row = 0;
            for (var y = top + 2; y < ground - 1.5; y += 2.75)
            {
                var column = 0;
                for (var x = building.X + 1.5; x < building.X + building.Width - 3; x += 2.5)
                {
                    var luck = Chance(index, row, column);
                    Fill(dark, x, y, 1.25, 1.5);
                    var glow = CityGlow(luck, litShare, time);
                    if (glow > 0)
                    {
                        // One window in nine has a screen on: a cooler light.
                        var light = (int)(luck * 9_000) % 9 == 0 ? 0xCFE4FFu : 0xFFD98Au;
                        Fill(light, x, y, 1.25, 1.5, opacity: glow);
                    }
                    column++;
                }
                row++;
            }
        }
    }

    /// <summary>Rain in slanted streaks, or snow in slow flakes, in front of the city.</summary>
    private void Falling(RoomWeather weather, double time)
    {
        var glass = Glass;
        switch (weather)
        {
            case RoomWeather.Rain:
                var streak = White(0.32);
                for (var drop = 0; drop < 46; drop++)
                {
                    var across = ((drop * 29.0) + (time * 30)) % (glass.Width + 8);
                    var down = ((drop * drop % 97 * 3.0) + (time * 70)) % glass.Height;
                    double x = glass.Right + 4 - across, y = glass.Y + down;
                    _context.DrawLine(Point(x, y), Point(x - 1.1, y + 3.2), streak, Length(0.35));
                }
                break;
            case RoomWeather.Snow:
                var flakeColor = White(0.9);
                for (var flake = 0; flake < 42; flake++)
                {
                    var sway = Math.Sin((time * 0.8) + flake) * 2;
                    var down = ((flake * 13.0) + (time * (4 + (flake % 4)))) % glass.Height;
                    var size = flake % 3 == 0 ? 0.75 : 0.5;
                    Fill(flakeColor, glass.X + (flake * 23 % (int)glass.Width) + sway, glass.Y + down, size, size);
                }
                break;
            default:
                break;
        }
    }

    private void RoofOf(Building building, double top, uint facade, RoomSky sky, int step, int seed)
    {
        var middle = building.X + (building.Width / 2);
        switch (building.Roof)
        {
            case Roof.Flat:
                Fill(facade, building.X + 2, top - 1, 3, 1);
                break;
            case Roof.Antenna:
                Fill(facade, middle - 0.25, top - 5, 0.5, 5);
                Fill(facade, middle - 1.25, top - 2, 2.5, 0.5);
                var isOn = sky.Darkness > 0.4 && (step + seed) % 8 < 4;
                Fill(isOn ? 0xF0604Du : facade, middle - 0.5, top - 5.5, 1, 1);
                break;
            case Roof.Tank:
                Fill(facade, building.X + 2.5, top - 3, 4.5, 2.5);
                Fill(facade, building.X + 3, top - 0.5, 0.5, 0.5);
                Fill(facade, building.X + 6, top - 0.5, 0.5, 0.5);
                break;
            case Roof.Step:
                Fill(facade, building.X + 2, top - 3, building.Width - 5, 3);
                Shadow(building.X + building.Width - 4.5, top - 3, 1.5, 3, opacity: 0.2);
                break;
            case Roof.Slant:
                Shape([new(building.X, top), new(building.X + building.Width, top), new(building.X + building.Width, top - 4)], Hex(facade));
                break;
        }
    }
}
