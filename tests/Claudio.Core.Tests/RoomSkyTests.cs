using Claudio.Core.Models;

namespace Claudio.Core.Tests;

/// <summary>What the hour does to the agent room: the sun's place, the city's windows, the lamps.</summary>
public sealed class RoomSkyTests
{
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void TheSunIsInTheMiddleOfTheWindowAtNoon()
    {
        var sun = new RoomSky(12).Sun;
        Assert.NotNull(sun);
        Assert.Equal(0.5, sun.Value.Across, 0.001);
        Assert.Equal(1, sun.Value.Height, 0.001);
    }

    [Fact]
    public void TheSunRisesOnTheLeftAndSetsOnTheRight()
    {
        var morning = new RoomSky(8).Sun;
        var afternoon = new RoomSky(16).Sun;
        Assert.NotNull(morning);
        Assert.NotNull(afternoon);
        Assert.True(morning.Value.Across < 0.5);
        Assert.True(afternoon.Value.Across > 0.5);
        Assert.Null(new RoomSky(20).Sun);
        Assert.Null(new RoomSky(3).Sun);
    }

    [Fact]
    public void TheMoonIsHighestAtMidnightAndGoneByDay()
    {
        var moon = new RoomSky(0).Moon;
        Assert.NotNull(moon);
        Assert.Equal(0.5, moon.Value.Across, 0.001);
        Assert.Null(new RoomSky(12).Moon);
    }

    [Fact]
    public void NoWindowOfTheCityIsLitByDay()
    {
        Assert.Equal(0, new RoomSky(12).LitShare);
        Assert.Equal(0, new RoomSky(15).LitShare);
    }

    [Fact]
    public void TheCityLightsUpThroughTheEveningThenGoesToSleep()
    {
        static double Share(double hour) => new RoomSky(hour).LitShare;
        Assert.True(Share(17.5) > 0);
        Assert.True(Share(17.5) < Share(19));
        Assert.True(Share(19) < Share(21));
        Assert.True(Share(21) > 0.9);
        Assert.True(Share(21) > Share(22.5));
        Assert.True(Share(22.5) > Share(23.9));
        Assert.True(Share(23.9) > Share(3));
    }

    [Fact]
    public void TheRoomIsLeftAsDrawnInFullDaylight()
    {
        Assert.Null(new RoomSky(12).Dim);
        Assert.False(new RoomSky(12).HasLamps);
        Assert.Equal(0, new RoomSky(12).Darkness);
    }

    [Fact]
    public void NightDimsTheRoomAndTurnsTheLampsOn()
    {
        var night = new RoomSky(23);
        Assert.Equal(0x7A80AAu, night.Dim);
        Assert.True(night.HasLamps);
        Assert.Equal(1, night.Darkness);
        Assert.Equal(1, night.Starlight);
    }

    [Fact]
    public void TheSkyBlendsFromOneMomentToTheNext()
    {
        var before = new RoomSky(16).Bands;
        var during = new RoomSky(17).Bands;
        var after = new RoomSky(18.25).Bands;
        Assert.NotEqual(before, during);
        Assert.NotEqual(after, during);
        Assert.Equal(0x808080u, RoomSky.Mix(0x000000, 0xFFFFFF, 0.5));
    }

    [Fact]
    public void AnHourPastMidnightWrapsAround()
    {
        Assert.Equal(1, new RoomSky(25).Hour);
        Assert.Equal(23, new RoomSky(-1).Hour);
    }

    [Fact]
    public void ADayAlwaysHasTheSameWeather()
    {
        var morning = Utc(2026, 11, 15, 8);
        var evening = Utc(2026, 11, 15, 22);
        Assert.Equal(RoomWeathers.Of(morning, TimeZoneInfo.Utc), RoomWeathers.Of(evening, TimeZoneInfo.Utc));
    }

    [Fact]
    public void AYearHasEveryWeatherAndSnowOnlyInWinter()
    {
        var start = Utc(2026, 1, 1, 12);
        var seen = new Dictionary<RoomWeather, int>();
        for (var day = 0; day < 365; day++)
        {
            var date = start.AddDays(day);
            var weather = RoomWeathers.Of(date, TimeZoneInfo.Utc);
            seen[weather] = seen.GetValueOrDefault(weather) + 1;
            if (weather == RoomWeather.Snow)
            {
                Assert.True(date.Month is 12 or 1 or 2, $"snow on {date}");
            }
        }
        Assert.Equal(RoomWeathers.AllCases.ToHashSet(), seen.Keys.ToHashSet());
        Assert.True(seen.GetValueOrDefault(RoomWeather.Clear) > 120);
    }

    [Fact]
    public void AChosenWeatherWinsOverTheDaysOwn()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1_793_468_000);
        Assert.Equal(RoomWeather.Snow, RoomWeathers.Chosen("snow", date));
        Assert.Equal(RoomWeathers.Of(date), RoomWeathers.Chosen(RoomWeathers.AsItComes, date));
        Assert.True(RoomWeather.Rain.IsFalling());
        Assert.False(RoomWeather.Overcast.IsFalling());
    }

    [Fact]
    public void TheRoomIsLitForTheHourOnThePcsClock()
    {
        var date = Utc(2026, 11, 14, 21, 30);
        Assert.Equal(21.5, RoomSky.HourAt(date, TimeZoneInfo.Utc), 0.001);
    }
}
