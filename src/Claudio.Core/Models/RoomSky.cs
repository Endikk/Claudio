namespace Claudio.Core.Models;

/// <summary>
/// What an hour of the day does to the room, as Claudy's <c>RoomSky</c>: the sky in its window,
/// how many of the city's windows are lit, where the sun or the moon stands, and how much the room
/// itself dims. Colours are <c>0xRRGGBB</c>.
/// </summary>
public readonly record struct RoomSky
{
    public RoomSky(double hour)
    {
        var wrapped = hour % 24;
        Hour = wrapped < 0 ? wrapped + 24 : wrapped;
    }

    /// <summary>0 to 24.</summary>
    public double Hour { get; }

    /// <summary>
    /// The hour on the PC's clock, minutes and seconds as a fraction: the room is lit for it.
    /// Claudy's <c>hour(at:calendar:)</c>; <paramref name="zone"/> is the PC's own unless given.
    /// </summary>
    public static double HourAt(DateTimeOffset date, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(date, zone ?? TimeZoneInfo.Local);
        return local.Hour + (local.Minute / 60.0) + (local.Second / 3600.0);
    }

    // Moments

    /// <summary>The four skies the day passes through, and what each does to the room.</summary>
    /// <param name="Darkness">0 by day, 1 at night: how far the city has turned to silhouettes.</param>
    /// <param name="Dim">The colour the room is multiplied by. White leaves it as drawn.</param>
    private sealed record Moment(uint[] Bands, double Darkness, uint Dim)
    {
        public static readonly Moment Night = new([0x101421, 0x131827, 0x161C2E, 0x192035, 0x1D253D, 0x212A45, 0x25304D, 0x2A3656], 1, 0x7A80AA);
        public static readonly Moment Dawn = new([0x5A6A8C, 0x7C7A98, 0xA68A9A, 0xCC9A90, 0xE8A882, 0xF2B87A, 0xF6C988, 0xF9DA9E], 0.45, 0xE9DCE2);
        public static readonly Moment Day = new([0x5FA8E4, 0x6FB3E8, 0x80BEEB, 0x92C8EE, 0xA4D2F1, 0xB6DBF3, 0xC8E4F5, 0xDAECF7], 0, 0xFFFFFF);
        public static readonly Moment Dusk = new([0x3D2A45, 0x5E3548, 0x86444A, 0xAE5646, 0xCE6C48, 0xE28450, 0xEE9C4E, 0xF6B452], 0.6, 0xD9C2B8);
    }

    /// <summary>When each sky is at its fullest. Between two of them the sky is a blend.</summary>
    private static readonly (double Hour, Moment Moment)[] Day =
    [
        (0, Moment.Night), (5, Moment.Night), (6.5, Moment.Dawn), (8.5, Moment.Day), (16, Moment.Day), (18.25, Moment.Dusk),
        (20.5, Moment.Night), (24, Moment.Night),
    ];

    /// <summary>The two moments around this hour, and how far it is from the first to the second.</summary>
    private (Moment From, Moment To, double Part) Blend
    {
        get
        {
            for (var index = 1; index < Day.Length; index++)
            {
                var (before, after) = (Day[index - 1], Day[index]);
                if (Hour <= after.Hour)
                {
                    var span = after.Hour - before.Hour;
                    return (before.Moment, after.Moment, span > 0 ? (Hour - before.Hour) / span : 0);
                }
            }
            return (Moment.Night, Moment.Night, 0);
        }
    }

    // The sky

    /// <summary>Eight bands of sky, top to bottom.</summary>
    public IReadOnlyList<uint> Bands
    {
        get
        {
            var blend = Blend;
            return blend.From.Bands.Zip(blend.To.Bands, (from, to) => Mix(from, to, blend.Part)).ToArray();
        }
    }

    /// <summary>0 by day, 1 at night.</summary>
    public double Darkness
    {
        get
        {
            var blend = Blend;
            return blend.From.Darkness + ((blend.To.Darkness - blend.From.Darkness) * blend.Part);
        }
    }

    /// <summary>The colour to multiply the room by, <c>null</c> in full daylight.</summary>
    public uint? Dim
    {
        get
        {
            var blend = Blend;
            var color = Mix(blend.From.Dim, blend.To.Dim, blend.Part);
            return color == 0xFFFFFF ? null : color;
        }
    }

    /// <summary>Lamps are on, and every screen lights its desk.</summary>
    public bool HasLamps => Hour is >= 17.5 or < 7;

    /// <summary>How bright the stars are, 0 to 1.</summary>
    public double Starlight => Math.Max(0, (Darkness - 0.6) / 0.4);

    private static readonly (double Hour, double Share)[] LitPoints =
    [
        (0, 0.2), (2, 0.07), (5, 0.05), (6.5, 0.22), (8, 0.04), (9, 0), (16, 0), (17.5, 0.3), (19, 0.6),
        (21, 0.95), (22.5, 0.6), (24, 0.2),
    ];

    /// <summary>
    /// The share of the city's windows that are lit, 0 to 1: none by day, a few as the sun goes
    /// down, nearly all at nine, fewer and fewer towards midnight, a handful through the night,
    /// some again before sunrise.
    /// </summary>
    public double LitShare
    {
        get
        {
            for (var index = 1; index < LitPoints.Length; index++)
            {
                var (before, after) = (LitPoints[index - 1], LitPoints[index]);
                if (Hour <= after.Hour)
                {
                    var span = after.Hour - before.Hour;
                    return before.Share + ((after.Share - before.Share) * (span > 0 ? (Hour - before.Hour) / span : 0));
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// Where the sun stands, from six to six: <c>Across</c> runs 0 to 1 over the window, the middle
    /// at noon; <c>Height</c> is 0 on the horizon and 1 at noon. <c>null</c> once it is down.
    /// </summary>
    public (double Across, double Height)? Sun
    {
        get
        {
            if (Hour is < 6 or > 18)
            {
                return null;
            }
            var across = (Hour - 6) / 12;
            return (across, Math.Sin(Math.PI * across));
        }
    }

    /// <summary>
    /// Where the moon stands, from six in the evening to six in the morning, highest at midnight.
    /// <c>null</c> by day.
    /// </summary>
    public (double Across, double Height)? Moon
    {
        get
        {
            if (Hour is < 18 and > 6)
            {
                return null;
            }
            var across = (Hour >= 18 ? Hour - 18 : Hour + 6) / 12;
            return (across, Math.Sin(Math.PI * across));
        }
    }

    /// <summary>A colour between two, channel by channel.</summary>
    public static uint Mix(uint from, uint to, double part)
    {
        var clamped = Math.Clamp(part, 0, 1);
        uint Channel(int shift)
        {
            double a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            return (uint)Math.Round(a + ((b - a) * clamped), MidpointRounding.AwayFromZero) << shift;
        }
        return Channel(16) | Channel(8) | Channel(0);
    }
}

/// <summary>
/// The weather in the room's window, as Claudy's <c>RoomWeather</c>. Nothing is asked of the
/// network: each day has its own, decided by its date, so it rains some days and, in winter, snows.
/// </summary>
public enum RoomWeather
{
    Clear,
    Overcast,
    Rain,
    Snow,
}

/// <summary>What Claudy's <c>RoomWeather</c> holds as static members and properties.</summary>
public static class RoomWeathers
{
    public const string StorageKey = "roomWeather";

    /// <summary>The stored choice that lets each day have its own weather.</summary>
    public const string AsItComes = "auto";

    public static IReadOnlyList<RoomWeather> AllCases { get; } = [RoomWeather.Clear, RoomWeather.Overcast, RoomWeather.Rain, RoomWeather.Snow];

    /// <summary>Its stored name, Claudy's raw value: "clear", "overcast", "rain", "snow".</summary>
    public static string Id(this RoomWeather weather) => weather switch
    {
        RoomWeather.Overcast => "overcast",
        RoomWeather.Rain => "rain",
        RoomWeather.Snow => "snow",
        _ => "clear",
    };

    public static string Title(this RoomWeather weather) => weather switch
    {
        RoomWeather.Overcast => "Overcast",
        RoomWeather.Rain => "Rain",
        RoomWeather.Snow => "Snow",
        _ => "Clear",
    };

    /// <summary>Something falls: the window is redrawn often enough to see it fall.</summary>
    public static bool IsFalling(this RoomWeather weather) => weather is RoomWeather.Rain or RoomWeather.Snow;

    /// <summary>
    /// The weather of a day: five days in ten are clear, two overcast, three wet, and what falls
    /// from December to February is snow two times in three. The day is the PC's unless
    /// <paramref name="zone"/> says otherwise.
    /// </summary>
    public static RoomWeather Of(DateTimeOffset date, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(date, zone ?? TimeZoneInfo.Local);
        var month = local.Month;
        var seed = unchecked((uint)((local.Year * 372) + (month * 31) + local.Day));
        seed = unchecked((seed ^ (seed >> 15)) * 2_246_822_519u);
        seed ^= seed >> 13;
        var isWinter = month == 12 || month <= 2;
        return (seed % 10) switch
        {
            0 or 1 => isWinter ? RoomWeather.Snow : RoomWeather.Rain,
            2 => RoomWeather.Rain,
            3 or 4 => RoomWeather.Overcast,
            _ => RoomWeather.Clear,
        };
    }

    /// <summary>The weather to show for a stored choice: the one chosen, or the day's own.</summary>
    public static RoomWeather Chosen(string? stored, DateTimeOffset date, TimeZoneInfo? zone = null)
    {
        foreach (var weather in AllCases)
        {
            if (string.Equals(weather.Id(), stored, StringComparison.Ordinal))
            {
                return weather;
            }
        }
        return Of(date, zone);
    }
}
