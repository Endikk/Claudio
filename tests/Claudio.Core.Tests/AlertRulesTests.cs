using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>When Claudio speaks up, reading after reading, against an injected clock.</summary>
public sealed class AlertRulesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1_793_400_000);

    /// <summary>A 5-hour window opened at <see cref="Start"/>.</summary>
    private static UsageWindow Window(double percent, DateTimeOffset? reset = null, bool measured = true) => new()
    {
        Title = "Session",
        Window = "5h",
        Percent = percent,
        WindowStart = Start,
        ResetDate = reset ?? Start.AddHours(5),
        Accent = Accent.Coral,
        IsMeasured = measured,
    };

    private static DateTimeOffset At(double minutes) => Start.AddMinutes(minutes);

    // Quota

    /// <summary>The first reading only sets the scene: a threshold crossed before Claudio looked is old news.</summary>
    [Fact]
    public void FirstReadingSendsNothing() => Assert.Empty(new QuotaAlerts().Evaluate(Window(0.85), At(240)));

    /// <summary>Crossing 80 % sends one alert per window, never again in it.</summary>
    [Fact]
    public void CrossingEightyPercentAlertsOncePerWindow()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(0.70), At(270));
        var alerts = rules.Evaluate(Window(0.82), At(280));
        Assert.Equal([NotificationKind.SessionHigh], alerts.Select(alert => alert.Kind));
        Assert.Equal("Session at 82%", alerts[0].Title);
        Assert.EndsWith("At this pace you stay under the limit.", alerts[0].Body, StringComparison.Ordinal);

        rules.Evaluate(Window(0.79), At(285));
        Assert.Empty(rules.Evaluate(Window(0.83), At(290)));
    }

    /// <summary>Crossed early, at a pace that fills the window before it resets: the alert says so.</summary>
    [Fact]
    public void EightyPercentSaysWhenThePaceFillsTheWindow()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(0.70), At(230));
        var high = rules.Evaluate(Window(0.82), At(240)).Single(alert => alert.Kind == NotificationKind.SessionHigh);
        Assert.EndsWith("At this pace it fills before then.", high.Body, StringComparison.Ordinal);
    }

    /// <summary>At this pace the quota fills well before the reset: said once, with when.</summary>
    [Fact]
    public void LimitBeforeTheResetIsProjected()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(0.40), At(50));
        var alert = rules.Evaluate(Window(0.60), At(60)).Single(alert => alert.Kind == NotificationKind.LimitBeforeReset);
        // 60 % in an hour fills at 1 h 40, 3 h 20 before the reset.
        Assert.Equal($"At this pace the session is full around {UsageFormat.Clock(At(100))}, "
                     + $"3 h 20 before it resets at {UsageFormat.Clock(At(300))}.", alert.Body);
        Assert.DoesNotContain(rules.Evaluate(Window(0.65), At(65)), next => next.Kind == NotificationKind.LimitBeforeReset);
    }

    /// <summary>A projection on a little use, or one that lands near the reset, says nothing.</summary>
    [Fact]
    public void NoProjectionOnLittleUseOrNearTheReset()
    {
        var early = new QuotaAlerts();
        early.Evaluate(Window(0.20), At(10));
        Assert.Empty(early.Evaluate(Window(0.30), At(15)));

        var close = new QuotaAlerts();
        close.Evaluate(Window(0.55), At(160));
        // 0.6 in 175 min fills at about 292 min: within ten minutes of the reset at 300.
        Assert.Empty(close.Evaluate(Window(0.6), At(175)));
    }

    /// <summary>The full window reset: the session is back, and how long it was stopped when it was seen filling.</summary>
    [Fact]
    public void SessionBackAfterAFullWindow()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(0.95), At(250));
        rules.Evaluate(Window(1.0), At(255));
        Assert.Empty(rules.Evaluate(Window(1.0), At(280)));

        var next = Window(0.0, reset: At(300));
        var alert = Assert.Single(rules.Evaluate(next, At(302)));
        Assert.Equal(NotificationKind.SessionBack, alert.Kind);
        Assert.Equal($"The 5-hour quota reset at {UsageFormat.Clock(At(300))}. You were stopped for 45 min.", alert.Body);
        Assert.Empty(rules.Evaluate(next, At(305)));
    }

    /// <summary>Found full at launch: the reset is still news, how long it lasted is not known.</summary>
    [Fact]
    public void SessionBackWithoutAWitnessedStart()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(1.0), At(280));
        var alert = Assert.Single(rules.Evaluate(Window(0.0, reset: At(300)), At(301)));
        Assert.Equal($"The 5-hour quota reset at {UsageFormat.Clock(At(300))}.", alert.Body);
    }

    [Fact]
    public void UnmeasuredReadingSendsNothing()
    {
        var rules = new QuotaAlerts();
        rules.Evaluate(Window(0.7), At(230));
        Assert.Empty(rules.Evaluate(Window(0.9, measured: false), At(240)));
    }

    // Sessions and agents

    private static ClaudeSession Session(string id = "s1", SessionState status = SessionState.Busy, string? waitingFor = null,
                                         DateTimeOffset? since = null, bool background = false) => new()
                                         {
                                             Id = id,
                                             Pid = 4242,
                                             Name = "evan-b6",
                                             Cwd = @"C:\Users\me\EVAN",
                                             Status = status,
                                             WaitingFor = waitingFor,
                                             StatusSince = since,
                                             IsBackground = background,
                                             Host = new HostApp("Visual Studio Code", 1200),
                                         };

    private static RunningAgent Agent(string id, DateTimeOffset started, DateTimeOffset last, bool finished = false,
                                      string[]? edited = null) => new()
                                      {
                                          Id = id,
                                          SessionId = "s1",
                                          SessionPid = 4242,
                                          SessionName = "claudy-a5",
                                          Title = "Run the test suite",
                                          StartedAt = started,
                                          LastActivity = last,
                                          IsFinished = finished,
                                          ActionCount = 3,
                                          EditedFiles = edited ?? [],
                                      };

    /// <summary>
    /// A session asking for a permission is announced once it has waited a moment, once, and its
    /// banner is taken back when it stops waiting.
    /// </summary>
    [Fact]
    public void WaitingSessionIsAnnouncedThenWithdrawn()
    {
        var rules = new ActivityAlerts();
        var since = At(0);
        var waiting = Session(status: SessionState.Waiting, waitingFor: "permission prompt", since: since);

        Assert.Empty(rules.Evaluate([waiting], [], since.AddSeconds(5)).Post);
        var alert = Assert.Single(rules.Evaluate([waiting], [], since.AddSeconds(25)).Post);
        Assert.Equal(NotificationKind.SessionWaiting, alert.Kind);
        Assert.Equal("evan-b6 is waiting for you", alert.Title);
        Assert.Equal("Permission prompt · EVAN · VS Code", alert.Body);
        Assert.Equal("s1", alert.SessionId);
        Assert.Empty(rules.Evaluate([waiting], [], since.AddSeconds(60)).Post);

        var answered = rules.Evaluate([Session(since: At(2))], [], At(2));
        Assert.Equal([alert.Id], answered.Withdraw);
    }

    /// <summary>
    /// A session already waiting at launch is old news; a new question replaces the old banner; a
    /// file that does not say since when still gets its banner once the wait is long enough.
    /// </summary>
    [Fact]
    public void WaitingAtLaunchNewQuestionAndUnknownStart()
    {
        var rules = new ActivityAlerts();
        var old = Session(status: SessionState.Waiting, waitingFor: "permission prompt", since: At(0));
        Assert.Empty(rules.Evaluate([old], [], At(30)).Post);
        Assert.Empty(rules.Evaluate([old], [], At(31)).Post);

        var first = Assert.Single(rules.Evaluate([Session(status: SessionState.Waiting, since: At(40))], [], At(41)).Post);
        var second = rules.Evaluate([Session(status: SessionState.Waiting, since: At(42))], [], At(43));
        Assert.Single(second.Post);
        Assert.Equal([first.Id], second.Withdraw);

        var unknown = new ActivityAlerts();
        var undated = new ClaudeSession { Id = "s9", Pid = 1, Name = "x", Status = SessionState.Waiting };
        Assert.Empty(unknown.Evaluate([], [], At(0)).Post);
        Assert.Empty(unknown.Evaluate([undated], [], At(1)).Post);
        Assert.Equal([NotificationKind.SessionWaiting], unknown.Evaluate([undated], [], At(1.5)).Post.Select(alert => alert.Kind));
    }

    /// <summary>Nobody sits at a program's session: it never says it waits.</summary>
    [Fact]
    public void BackgroundSessionNeverAnnouncesWaiting()
    {
        var rules = new ActivityAlerts();
        var waiting = Session(status: SessionState.Waiting, since: At(0), background: true);
        rules.Evaluate([waiting], [], At(1));
        Assert.Empty(rules.Evaluate([waiting], [], At(5)).Post);
    }

    /// <summary>An agent silent past three minutes is announced once per silence.</summary>
    [Fact]
    public void SilentAgentIsAnnouncedOnce()
    {
        var rules = new ActivityAlerts();
        var quiet = Agent("a", At(0), At(10));
        Assert.Empty(rules.Evaluate([], [quiet], At(11)).Post);
        var alert = Assert.Single(rules.Evaluate([], [quiet], At(14)).Post);
        Assert.Equal(NotificationKind.AgentSilent, alert.Kind);
        Assert.Equal("“Run the test suite” has said nothing for 4 min. claudy-a5", alert.Body);
        Assert.Equal("a", alert.AgentId);
        Assert.Empty(rules.Evaluate([], [quiet], At(20)).Post);
    }

    /// <summary>Done is said of an agent seen at work, not of one found already finished.</summary>
    [Fact]
    public void DoneIsSaidOfAnAgentSeenAtWork()
    {
        var rules = new ActivityAlerts();
        rules.Evaluate([], [Agent("a", At(0), At(9))], At(9.5));
        var done = Agent("a", At(0), At(10), finished: true, edited: ["CHANGELOG.md"]);
        var alert = Assert.Single(rules.Evaluate([], [done], At(10.5)).Post);
        Assert.Equal(NotificationKind.AgentDone, alert.Kind);
        Assert.Equal("“Run the test suite” in 10 min. Wrote CHANGELOG.md", alert.Body);

        var fresh = new ActivityAlerts();
        Assert.Empty(fresh.Evaluate([], [done], At(10.5)).Post);
    }

    /// <summary>The agents of a program's session go unannounced, like the session.</summary>
    [Fact]
    public void AgentsOfABackgroundSessionAreNotAnnounced()
    {
        var rules = new ActivityAlerts();
        var quiet = Agent("a", At(0), At(10));
        var program = Session(since: At(0), background: true);
        rules.Evaluate([program], [quiet], At(11));
        Assert.Empty(rules.Evaluate([program], [quiet], At(14)).Post);
    }

    // Kinds

    /// <summary>Every kind is on by default but an agent finishing, which happens often enough to be asked for.</summary>
    [Fact]
    public void KindsHaveTheirSwitchesAndDefaults()
    {
        Assert.Equal(6, NotificationKinds.All.Count);
        Assert.Equal([NotificationKind.AgentDone], NotificationKinds.All.Where(kind => !kind.IsOnByDefault()));
        Assert.Equal([NotificationKind.SessionHigh, NotificationKind.LimitBeforeReset, NotificationKind.SessionBack],
                     NotificationKinds.All.Where(kind => kind.IsQuota()));
        Assert.Equal("Session at 80%", NotificationKind.SessionHigh.MenuTitle());
        Assert.Equal("A session waits for you", NotificationKind.SessionWaiting.MenuTitle());
        Assert.Equal("limitBeforeReset", NotificationKind.LimitBeforeReset.RawValue());
    }
}
