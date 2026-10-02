using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// What the open island says, as in Claudy's NotchActivityTests: the lead quota in large, the
/// others in small, and one line on when the lead one resets. Never a countdown to a reset already
/// past, never a figure that was not measured.
/// </summary>
public sealed class NotchActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    private static UsageWindow Window(string title, double percent = 0.28, TimeSpan? resetIn = null, bool isMeasured = true,
                                      SpendReading? amount = null) => new()
                                      {
                                          Title = title,
                                          Window = "5h",
                                          Percent = percent,
                                          WindowStart = Now.AddHours(-1),
                                          ResetDate = Now + (resetIn ?? new TimeSpan(4, 5, 30)),
                                          Accent = Accent.Coral,
                                          IsMeasured = isMeasured,
                                          Amount = amount,
                                      };

    private static UsageSnapshot Snapshot(UsageWindow session, UsageWindow? spend = null) => UsageSnapshot.Placeholder(Now) with
    {
        Session = session,
        Weekly = Window("Weekly", 0.12),
        Scoped = Window("Fable", 0),
        Spend = spend,
    };

    [Fact]
    public void ARunningSessionSaysWhenItResets()
    {
        var session = Window("Session");

        var activity = new NotchActivity(Snapshot(session), Now, Culture);

        Assert.Equal($"reset {UsageFormat.ResetTime(session.ResetDate, Now, Culture)} · in 4 h 5 min", activity.ResetLine);
    }

    [Fact]
    public void AMeasuredSessionWithNoWindowRunningSaysInactive() =>
        Assert.Equal("inactive", new NotchActivity(Snapshot(Window("Session", resetIn: TimeSpan.FromMinutes(-1))), Now, Culture).ResetLine);

    [Fact]
    public void AnUnmeasuredSessionSaysUnavailable() =>
        Assert.Equal("unavailable", new NotchActivity(Snapshot(Window("Session", isMeasured: false)), Now, Culture).ResetLine);

    [Fact]
    public void APlanInWindowsListsWeeklyThenPerModel()
    {
        var activity = new NotchActivity(Snapshot(Window("Session")), Now, Culture);

        Assert.Equal("Session", activity.Lead.Title);
        Assert.Equal(["Weekly", "Fable"], activity.Others.Select(window => window.Title));
        Assert.Null(activity.SpentLine);
    }

    /// <summary>A plan billed on usage has no weekly or per-model quota: the spend leads, alone, with the money behind it.</summary>
    [Fact]
    public void ASpendCapLeadsAloneWithItsAmounts()
    {
        var reading = new SpendReading(46.31, 500, "USD", IsLimitReached: false, Now.AddDays(3));
        var spend = Window("Monthly spend", reading.Percent, TimeSpan.FromDays(3), amount: reading);

        var activity = new NotchActivity(Snapshot(Window("Session"), spend), Now, Culture);

        Assert.Equal("Monthly spend", activity.Lead.Title);
        Assert.Empty(activity.Others);
        Assert.Equal(UsageFormat.Spent(reading, Culture), activity.SpentLine);
    }
}
