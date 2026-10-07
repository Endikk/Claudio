using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// Claudy's demo set: only without Claude Code and without anyone signed in, always labelled, never
/// carrying anything identifying, and moving from one reading to the next.
/// </summary>
public sealed class DemoUsageDataSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static DemoUsageDataSource Demo(Func<DateTimeOffset>? now = null) =>
        new(() => new Account("Ada", string.Empty, string.Empty, string.Empty, false), now ?? (() => Now), TimeZoneInfo.Utc,
            new Random(7), TimeSpan.Zero, DayOfWeek.Monday);

    private static LocalUsageDataSource Local(bool installed, AccountPayload payload) =>
        new(() => [], _ => Task.FromResult(payload), () => installed, _ => null, () => Account.Empty, () => Now, TimeZoneInfo.Utc);

    private static AccountPayload SignedIn { get; } = new(new QuotaReading { Source = QuotaSource.Api }, null, IsSignedIn: true);

    private static AccountPayload NobodySignedIn { get; } = new(null, null, IsSignedIn: false);

    [Fact]
    public async Task TheSampleSetIsLabelledAsSuch()
    {
        var snapshot = await Demo().FetchAsync(TestContext.Current.CancellationToken);

        Assert.True(snapshot.IsDemo);
        Assert.Equal("demo", snapshot.QuotaSource.Badge);
        Assert.False(snapshot.IsSignedIn);
    }

    [Fact]
    public async Task NothingIdentifyingIsWrittenIntoIt()
    {
        var snapshot = await Demo().FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Ada", snapshot.Account.Name);
        Assert.Empty(snapshot.Account.Email);
        Assert.Equal(["main-project", "api", "website", "scripts"], snapshot.Projects.Select(project => project.Name));
        Assert.Equal(["Opus", "Sonnet", "Haiku"], snapshot.Models.Select(model => model.Name));
    }

    [Fact]
    public async Task ItLooksLikeAWeekOfWork()
    {
        var snapshot = await Demo().FetchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(7, snapshot.History.Count);
        Assert.Equal(Now.Date, snapshot.History[^1].Date.Date);
        Assert.Equal(snapshot.History.Sum(sample => sample.Tokens), snapshot.WeekTokens);
        Assert.Equal(snapshot.History[^1].Tokens, snapshot.TodayTokens);
        Assert.InRange(snapshot.Session.Percent, 0.34, 0.99);
        Assert.InRange(snapshot.Weekly.Percent, 0, 1);
        Assert.True(snapshot.Session.IsActive(Now));
        // 2 October 2026 is a Friday: the week started on Monday the 28th of September.
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), snapshot.Weekly.WindowStart);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), snapshot.Weekly.ResetDate);
    }

    [Fact]
    public async Task TheSessionCreepsUpThenStartsOver()
    {
        var clock = Now;
        var demo = Demo(() => clock);
        var percents = new List<double>();
        for (var reading = 0; reading < 120; reading++)
        {
            clock = clock.AddMinutes(1);
            percents.Add((await demo.FetchAsync(TestContext.Current.CancellationToken)).Session.Percent);
        }

        Assert.True(percents[1] > percents[0]);
        Assert.Contains(percents.Zip(percents.Skip(1)), pair => pair.Second < pair.First);
        Assert.All(percents, percent => Assert.InRange(percent, 0.34, 0.99));
    }

    /// <summary>
    /// The demo must read like a session already under way: its gauge on the pace marker, not far
    /// ahead of a window that opened the moment the app launched.
    /// </summary>
    [Fact]
    public async Task TheFirstReadingIsOnPace()
    {
        var session = (await Demo().FetchAsync(TestContext.Current.CancellationToken)).Session;

        Assert.True(session.IsActive(Now));
        Assert.True(Math.Abs(session.Percent - session.Elapsed(Now)) < 0.04,
                    $"{session.Percent} used for {session.Elapsed(Now)} of the window elapsed");
        Assert.Equal("on pace", UsageFormat.PaceOf(session, Now)?.Text);
    }

    [Fact]
    public async Task WithoutClaudeCodeAndWithoutASignInTheDemoShows()
    {
        var source = new AdaptiveUsageDataSource(Local(installed: false, NobodySignedIn), Demo());

        Assert.True((await source.FetchAsync(TestContext.Current.CancellationToken)).IsDemo);
    }

    /// <summary>Claudio's own sign-in reads the account without Claude Code: then the account wins.</summary>
    [Fact]
    public async Task ASignInWinsOverTheDemoEvenWithoutClaudeCode()
    {
        var source = new AdaptiveUsageDataSource(Local(installed: false, SignedIn), Demo());

        var snapshot = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsDemo);
        Assert.True(snapshot.IsSignedIn);
    }

    /// <summary>With Claude Code, signed out means the sign-in card, never invented figures.</summary>
    [Fact]
    public async Task WithClaudeCodeASignedOutCardIsNeverTheDemo()
    {
        var source = new AdaptiveUsageDataSource(Local(installed: true, NobodySignedIn), Demo());

        var snapshot = await source.FetchAsync(TestContext.Current.CancellationToken);

        Assert.False(snapshot.IsDemo);
        Assert.False(snapshot.IsSignedIn);
    }

    /// <summary>Only the absence of Claude Code justifies the demo set: any other failure reaches the card.</summary>
    [Fact]
    public async Task AnyOtherFailureSurfacesInsteadOfTheDemo()
    {
        var failing = new LocalUsageDataSource(() => throw new UsageDataException("unreadable"), _ => Task.FromResult(SignedIn),
                                               () => true, _ => null, () => Account.Empty, () => Now, TimeZoneInfo.Utc);
        var source = new AdaptiveUsageDataSource(failing, Demo());

        await Assert.ThrowsAsync<UsageDataException>(() => source.FetchAsync(TestContext.Current.CancellationToken));
    }
}
