using System.Globalization;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class TokenTotalsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static TranscriptEntry Entry(DateTimeOffset date, long tokens) =>
        new(date, "claude-opus-5", tokens, "", "s", false, null);

    [Fact]
    public void SplitsTodayFromTheWeek()
    {
        var totals = TokenTotals.From(
            [Entry(Now.AddHours(-1), 100), Entry(Now.AddDays(-1), 20), Entry(Now.AddDays(-6), 3), Entry(Now.AddDays(-8), 999)],
            Now, TimeZoneInfo.Utc);

        Assert.Equal(100, totals.Today);
        Assert.Equal(123, totals.Week);
        Assert.Equal([3L, 0, 0, 0, 0, 20, 100], totals.Days);
    }

    [Theory]
    [InlineData(3_140_000_000, "3.14 B")]
    [InlineData(2_340_000, "2.34 M")]
    [InlineData(18_000_000, "18 M")]
    [InlineData(640_000, "640 k")]
    [InlineData(1_500, "1.5 k")]
    [InlineData(999, "999")]
    public void FormatsLikeClaudy(long count, string expected) =>
        Assert.Equal(expected, TokenTotals.Format(count, CultureInfo.InvariantCulture));
}
