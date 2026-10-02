using System.Globalization;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class UsageFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(3_140_000_000, "3.14 B")]
    [InlineData(2_340_000, "2.34 M")]
    [InlineData(18_000_000, "18 M")]
    [InlineData(640_000, "640 k")]
    [InlineData(1_500, "1.5 k")]
    [InlineData(999, "999")]
    public void TokensReadLikeClaudy(long count, string expected) =>
        Assert.Equal(expected, UsageFormat.Tokens(count, CultureInfo.InvariantCulture));

    [Fact]
    public void TheDecimalSeparatorFollowsTheCulture() =>
        Assert.Equal("1,5 M", UsageFormat.Tokens(1_500_000, CultureInfo.GetCultureInfo("fr-FR")));

    [Theory]
    [InlineData(0, "any moment")]
    [InlineData(14 * 60, "14 min")]
    [InlineData((2 * 3600) + (14 * 60), "2 h 14 min")]
    [InlineData((74 * 3600) + 60, "3 d 2 h")]
    public void CountdownReadsAtAGlance(int seconds, string expected) =>
        Assert.Equal(expected, UsageFormat.Countdown(Now.AddSeconds(seconds), Now));

    [Fact]
    public void ADashStandsForAMissingMeasurement()
    {
        var window = new UsageWindow { Title = "Session", Window = "5h", Percent = 0, IsMeasured = false };
        Assert.Equal(UsageFormat.NoFigure, UsageFormat.Percent(window, English));
        Assert.Equal("42", UsageFormat.Percent(window with { Percent = 0.429, IsMeasured = true }, English));
    }

    [Theory]
    [InlineData(0.52, "on pace", Accent.Sage)]
    [InlineData(0.65, "15 pts ahead of pace", Accent.Amber)]
    [InlineData(0.36, "14 pts behind pace", Accent.Sage)]
    public void PaceSpellsTheGapOut(double percent, string text, Accent accent)
    {
        var pace = UsageFormat.PaceOf(Halfway(percent), Now)!;
        Assert.Equal(text, pace.Text);
        Assert.Equal(Theme.Color(accent), pace.Color);
    }

    [Fact]
    public void FarAheadOfPaceTurnsRed() =>
        Assert.Equal(Theme.Danger, UsageFormat.PaceOf(Halfway(0.75), Now)!.Color);

    [Fact]
    public void NoPaceWithoutARunningWindow()
    {
        var idle = Halfway(0.5) with { ResetDate = Now.AddMinutes(-1) };
        Assert.Null(UsageFormat.PaceOf(idle, Now));
        Assert.Null(UsageFormat.PaceSign(idle, Now));
    }

    [Fact]
    public void ColumnsReduceThePaceToASign()
    {
        Assert.Equal("+15", UsageFormat.PaceSign(Halfway(0.65), Now));
        Assert.Equal("−14", UsageFormat.PaceSign(Halfway(0.36), Now));
        Assert.Null(UsageFormat.PaceSign(Halfway(0.52), Now));
    }

    [Theory]
    [InlineData(30, "less than a minute")]
    [InlineData(4 * 60, "4 min")]
    [InlineData(2 * 3600, "2 h")]
    [InlineData(3 * 86_400, "3 d")]
    public void AgeIsOneShortPhrase(int seconds, string expected) =>
        Assert.Equal(expected, UsageFormat.Age(Now.AddSeconds(-seconds), Now));

    [Fact]
    public void ResetShowsTheDateBeyondADay()
    {
        Assert.DoesNotContain(" ", UsageFormat.ResetTime(Now.AddHours(3), Now, English), StringComparison.Ordinal);
        Assert.Equal(Now.AddDays(20).ToLocalTime().ToString("d MMM", English), UsageFormat.ResetTime(Now.AddDays(20), Now, English));
    }

    [Fact]
    public void SpendReadsInTheAccountsCurrency()
    {
        var spend = new SpendReading(46.31, 500, "USD", false, Now);
        Assert.Equal("$46.31 of $500.00", UsageFormat.Spent(spend, English));
    }

    [Fact]
    public void TheStaleBadgeSaysHowOld() =>
        Assert.Equal("Account momentarily unreachable. Reading from 12 min ago.",
                     UsageFormat.SourceExplanation(QuotaSource.Stale(Now.AddMinutes(-12)), Now));

    [Theory]
    [InlineData(0, "0 session")]
    [InlineData(1, "1 session")]
    [InlineData(3, "3 sessions")]
    public void SessionsAreCounted(int count, string expected) => Assert.Equal(expected, UsageFormat.Sessions(count));

    [Fact]
    public void DayInitialsStayInEnglish() =>
        Assert.Equal("SMTWTFS", string.Concat(Enumerable.Range(0, 7).Select(day =>
            UsageFormat.DayInitial(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 27))).AddDays(day)))));

    /// <summary>A five-hour window exactly halfway through.</summary>
    private static UsageWindow Halfway(double percent) => new()
    {
        Title = "Session",
        Window = "5h",
        Percent = percent,
        WindowStart = Now.AddMinutes(-150),
        ResetDate = Now.AddMinutes(150),
        Accent = Accent.Coral,
    };
}
