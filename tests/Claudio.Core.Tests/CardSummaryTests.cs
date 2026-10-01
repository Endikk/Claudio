using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Presentation;

namespace Claudio.Core.Tests;

public sealed class CardSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TheSessionLeads()
    {
        var reading = new QuotaReading { Session = new QuotaWindow(0.42, Now.AddHours(4).AddMinutes(5)), Source = QuotaSource.Api };
        var card = CardSummary.From(new AccountPayload(reading, null, true), true, Now, English);

        Assert.Equal("42", card.Percent);
        Assert.True(card.IsMeasured);
        Assert.True(card.IsRunning);
        Assert.EndsWith("in 4 h 5 min", card.Detail, StringComparison.Ordinal);
        Assert.Null(card.Badge);
        Assert.False(card.IsDanger);
    }

    [Fact]
    public void AMonthlyCapLeadsAPlanBilledOnUsage()
    {
        var spend = new SpendReading(46.31, 500, "USD", false, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var card = CardSummary.From(new AccountPayload(new QuotaReading { Spend = spend, Source = QuotaSource.Api }, null, true), true, Now, English);

        Assert.Equal("Spend", card.Title);
        Assert.Equal("9", card.Percent);
        Assert.StartsWith("$46.31 of $500.00", card.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NoMeasurementShowsADashNeverAnEstimate()
    {
        var card = CardSummary.From(new AccountPayload(null, null, true), true, Now, English);

        Assert.Equal(CardSummary.NoFigure, card.Percent);
        Assert.False(card.IsMeasured);
        Assert.Equal("offline", card.Badge);
    }

    [Fact]
    public void AStaleReadingSaysSo()
    {
        var reading = new QuotaReading { Session = new QuotaWindow(0.5, Now.AddHours(1)), Source = QuotaSource.Stale(Now.AddMinutes(-10)) };
        Assert.Equal("⟳", CardSummary.From(new AccountPayload(reading, null, true), true, Now, English).Badge);
    }

    [Fact]
    public void NinetyPercentTurnsTheGaugeToDanger()
    {
        var reading = new QuotaReading { Session = new QuotaWindow(0.9, Now.AddHours(1)), Source = QuotaSource.Api };
        Assert.True(CardSummary.From(new AccountPayload(reading, null, true), true, Now, English).IsDanger);
    }

    [Fact]
    public void WithoutClaudeCodeOrASignInThereIsNoFigure()
    {
        Assert.Equal(CardSummary.NoFigure, CardSummary.From(null, false, Now, English).Percent);
        Assert.Contains("Sign in", CardSummary.From(new AccountPayload(null, null, false), true, Now, English).Detail, StringComparison.Ordinal);
    }
}
