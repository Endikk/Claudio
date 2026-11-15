using System.Globalization;
using Claudio.Core.Models;
using Claudio.Core.Presentation;

namespace Claudio.Core.Services;

/// <summary>Each kind of notification Claudio sends, each with its own switch in the menu, as Claudy's <c>NotificationKind</c>.</summary>
public enum NotificationKind
{
    SessionHigh,
    LimitBeforeReset,
    SessionBack,
    SessionWaiting,
    AgentSilent,
    AgentDone,
}

public static class NotificationKinds
{
    public static IReadOnlyList<NotificationKind> All { get; } = Enum.GetValues<NotificationKind>();

    public static string MenuTitle(this NotificationKind kind) => kind switch
    {
        NotificationKind.SessionHigh => "Session at 80%",
        NotificationKind.LimitBeforeReset => "Limit before the reset",
        NotificationKind.SessionBack => "Session back",
        NotificationKind.SessionWaiting => "A session waits for you",
        NotificationKind.AgentSilent => "An agent falls silent",
        NotificationKind.AgentDone => "An agent is done",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool IsQuota(this NotificationKind kind) =>
        kind is NotificationKind.SessionHigh or NotificationKind.LimitBeforeReset or NotificationKind.SessionBack;

    /// <summary>On until turned off, except an agent finishing: that happens often enough to be asked for.</summary>
    public static bool IsOnByDefault(this NotificationKind kind) => kind != NotificationKind.AgentDone;

    /// <summary>Claudy's raw value, <c>sessionHigh</c>: the name its switch is kept under.</summary>
    public static string RawValue(this NotificationKind kind)
    {
        var name = kind.ToString();
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

/// <summary>One notification to post: what it says, and the event it stands for, as Claudy's <c>Alert</c>.</summary>
/// <param name="Id">
/// Stable per event, so the same event is never posted twice and its banner can be taken back once
/// it no longer holds.
/// </param>
/// <param name="SessionId">The session a click on the banner brings forward.</param>
/// <param name="AgentId">The agent the banner is about: a click then opens the agent room on its desk.</param>
public sealed record Alert(
    NotificationKind Kind,
    string Id,
    string Title,
    string Body,
    string? SessionId = null,
    string? AgentId = null);

/// <summary>What one pass over the sessions calls for: banners to post, and banners to take back.</summary>
public sealed record ActivityAlertsResult(IReadOnlyList<Alert> Post, IReadOnlyList<string> Withdraw);

/// <summary>
/// When the 5-hour session deserves a notification, worked out reading after reading, as Claudy's
/// <c>QuotaAlerts</c>. Each alert goes once per session window. The first reading after launch only
/// sets the scene: a threshold already crossed then is old news. The clock is the caller's: each
/// reading comes with its <c>now</c>. Not thread-safe: one caller, the UI thread.
/// </summary>
public sealed class QuotaAlerts
{
    public const double HighMark = 0.8;

    /// <summary>The limit must come at least this long before the reset to be worth a warning.</summary>
    public static readonly TimeSpan WarningMargin = TimeSpan.FromMinutes(10);

    /// <summary>Before this share of the quota, a projection says more about one busy minute than about the window.</summary>
    public const double ProjectionFloor = 0.5;

    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);
    private UsageWindow? _previous;

    /// <summary>
    /// The window seen full, its reset, and since when it was seen full: from a reading below, or
    /// only from launch, in which case how long it lasted is not known.
    /// </summary>
    private (DateTimeOffset Reset, DateTimeOffset Since, bool IsWitnessed)? _full;

    /// <summary>The alerts this reading of the session window calls for.</summary>
    public IReadOnlyList<Alert> Evaluate(UsageWindow window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.IsMeasured)
        {
            return [];
        }
        try
        {
            var alerts = new List<Alert>();
            if (SessionBack(window, now) is { } back)
            {
                alerts.Add(back);
            }
            if (window.Percent >= 1 && window.ResetDate > now && _full is null)
            {
                _full = (window.ResetDate, now, _previous is { } before && before.Percent < 1);
            }
            if (_previous is not { } previous || window.ResetDate <= now)
            {
                return alerts;
            }
            var key = WindowKey(window.ResetDate);
            var sameWindow = WindowKey(previous.ResetDate) == key;

            if (sameWindow && previous.Percent < HighMark && window.Percent >= HighMark && window.Percent < 1
                && _sent.Add($"high@{key}"))
            {
                alerts.Add(High(window, now, key));
            }
            if (ProjectedFull(window, now) is { } fullAt && fullAt < window.ResetDate - WarningMargin
                && _sent.Add($"limit@{key}"))
            {
                alerts.Add(Limit(window, fullAt, key));
            }
            return alerts;
        }
        finally
        {
            _previous = window;
        }
    }

    /// <summary>When the window fills at the pace it has had so far; null while too little is used to say, or once it is full.</summary>
    public static DateTimeOffset? ProjectedFull(UsageWindow window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(window);
        var spent = (now - window.WindowStart).TotalSeconds;
        if (window.Percent < ProjectionFloor || window.Percent >= 1 || spent <= 0)
        {
            return null;
        }
        var rate = window.Percent / spent;
        return now.AddSeconds((1 - window.Percent) / rate);
    }

    /// <summary>Readings of one window disagree on its reset by a few seconds: five-minute buckets.</summary>
    private static long WindowKey(DateTimeOffset reset) =>
        (long)(((reset - DateTimeOffset.UnixEpoch).TotalSeconds + 150) / 300);

    private static Alert High(UsageWindow window, DateTimeOffset now, long key)
    {
        var fills = ProjectedFull(window, now) is { } fullAt && fullAt < window.ResetDate;
        var pace = fills ? "At this pace it fills before then." : "At this pace you stay under the limit.";
        var percent = ((int)(window.Percent * 100)).ToString(CultureInfo.InvariantCulture);
        return new Alert(NotificationKind.SessionHigh, Id("high", key),
                         $"Session at {percent}%",
                         $"Resets at {UsageFormat.Clock(window.ResetDate)}, in {UsageFormat.Countdown(window.ResetDate, now)}. {pace}");
    }

    private static Alert Limit(UsageWindow window, DateTimeOffset fullAt, long key)
    {
        var early = ActivityAlerts.Duration(window.ResetDate - fullAt);
        return new Alert(NotificationKind.LimitBeforeReset, Id("limit", key),
                         "The limit comes before the reset",
                         $"At this pace the session is full around {UsageFormat.Clock(fullAt)}, "
                         + $"{early} before it resets at {UsageFormat.Clock(window.ResetDate)}.");
    }

    /// <summary>The full window has reset: work can start again.</summary>
    private Alert? SessionBack(UsageWindow window, DateTimeOffset now)
    {
        if (_full is not { } full || (window.Percent >= 1 && now < full.Reset))
        {
            return null;
        }
        var isNewWindow = WindowKey(window.ResetDate) != WindowKey(full.Reset) || now >= full.Reset;
        if (!isNewWindow)
        {
            return null;
        }
        _full = null;
        var body = $"The 5-hour quota reset at {UsageFormat.Clock(full.Reset)}.";
        if (full.IsWitnessed)
        {
            body += $" You were stopped for {ActivityAlerts.Duration(full.Reset - full.Since)}.";
        }
        return new Alert(NotificationKind.SessionBack, Id("back", WindowKey(full.Reset)), "Your session is back", body);
    }

    private static string Id(string prefix, long key) => string.Create(CultureInfo.InvariantCulture, $"{prefix}-{key}");
}

/// <summary>
/// When sessions and agents deserve a notification, worked out pass after pass, as Claudy's
/// <c>ActivityAlerts</c>: a session that waits for the user, an agent that falls silent, an agent
/// that ends. Each event goes once; a session's waiting banner is taken back once it no longer
/// waits, or asks something new. As for the quota, the first pass only sets the scene: what was
/// already so at launch is old news. Not thread-safe: one caller, the UI thread.
/// </summary>
public sealed class ActivityAlerts
{
    /// <summary>Someone watching the session answers within this: no banner for them.</summary>
    public static readonly TimeSpan WaitingDelay = TimeSpan.FromSeconds(20);

    private readonly HashSet<string> _sent = new(StringComparer.Ordinal);

    /// <summary>Banners of sessions waiting now, by session: taken back once they stop.</summary>
    private Dictionary<string, string> _waitingBanners = new(StringComparer.Ordinal);

    /// <summary>When each waiting session was first seen waiting, for a file that does not say since when.</summary>
    private Dictionary<string, DateTimeOffset> _firstSeenWaiting = new(StringComparer.Ordinal);

    /// <summary>Agents seen at work: only those are said to be done, not the ones found finished.</summary>
    private HashSet<string> _seenRunning = new(StringComparer.Ordinal);

    private bool _hasSetTheScene;

    public ActivityAlertsResult Evaluate(IReadOnlyList<ClaudeSession> sessions, IReadOnlyList<RunningAgent> agents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(agents);
        var post = new List<Alert>();
        var withdraw = new List<string>();

        var waiting = sessions.Where(session => session.IsWaiting && !session.IsBackground).ToList();
        var stillWaiting = waiting.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        _firstSeenWaiting = _firstSeenWaiting.Where(pair => stillWaiting.Contains(pair.Key))
                                             .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var session in waiting)
        {
            var since = session.StatusSince ?? (_firstSeenWaiting.TryGetValue(session.Id, out var first) ? first : now);
            _firstSeenWaiting.TryAdd(session.Id, since);
            var id = string.Create(CultureInfo.InvariantCulture, $"waiting-{session.Id}-{since.ToUnixTimeSeconds()}");
            if (now - since < WaitingDelay || !_sent.Add(id))
            {
                continue;
            }
            // A new question replaces the last one: its banner goes.
            if (_waitingBanners.TryGetValue(session.Id, out var previous))
            {
                withdraw.Add(previous);
            }
            _waitingBanners[session.Id] = id;
            post.Add(new Alert(NotificationKind.SessionWaiting, id, $"{session.Name} is waiting for you",
                               WaitingBody(session), SessionId: session.Id));
        }
        withdraw.AddRange(_waitingBanners.Where(pair => !stillWaiting.Contains(pair.Key))
                                         .Select(pair => pair.Value)
                                         .Order(StringComparer.Ordinal));
        _waitingBanners = _waitingBanners.Where(pair => stillWaiting.Contains(pair.Key))
                                         .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // A program's own session has nobody at it: its agents are not worth a banner either.
        var unattended = sessions.Where(session => session.IsBackground).Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var agent in agents.Where(agent => !unattended.Contains(agent.SessionId)))
        {
            switch (agent.Status(now))
            {
                case AgentStatus.Stalled:
                    var silentId = string.Create(CultureInfo.InvariantCulture, $"silent-{agent.Id}-{agent.LastActivity.ToUnixTimeSeconds()}");
                    if (!_sent.Add(silentId))
                    {
                        continue;
                    }
                    post.Add(new Alert(NotificationKind.AgentSilent, silentId, "An agent is silent",
                                       $"“{agent.Title}” has said nothing for {Duration(agent.Silence(now))}. {agent.SessionName}",
                                       agent.SessionId, agent.Id));
                    break;
                case AgentStatus.Done:
                    var doneId = $"done-{agent.Id}";
                    if (!_seenRunning.Contains(agent.Id) || !_sent.Add(doneId))
                    {
                        continue;
                    }
                    post.Add(new Alert(NotificationKind.AgentDone, doneId, "Agent done", DoneBody(agent), agent.SessionId, agent.Id));
                    break;
                case AgentStatus.Working:
                case AgentStatus.Idle:
                default:
                    break;
            }
            if (!agent.IsFinished)
            {
                _seenRunning.Add(agent.Id);
            }
        }
        var listed = agents.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        _seenRunning = _seenRunning.Where(listed.Contains).ToHashSet(StringComparer.Ordinal);

        if (!_hasSetTheScene)
        {
            // Marked as said, not said: nothing in the first pass is news.
            _hasSetTheScene = true;
            _waitingBanners = new(StringComparer.Ordinal);
            return new ActivityAlertsResult([], []);
        }
        return new ActivityAlertsResult(post, withdraw);
    }

    /// <summary>
    /// "&lt;1 min", "4 min", "1 h 05": a span in the agents list's words, as Claudy's
    /// <c>AgentsViewModel.duration</c>, kept here until that view model is ported.
    /// </summary>
    internal static string Duration(TimeSpan span)
    {
        var seconds = (long)span.TotalSeconds;
        return seconds switch
        {
            < 60 => "<1 min",
            < 3600 => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{seconds / 3600} h {seconds % 3600 / 60:00}"),
        };
    }

    private static string WaitingBody(ClaudeSession session) =>
        string.Join(" · ", new[] { session.WaitingReason ?? "Waiting for you", session.FolderName, session.Host?.ShortName }
                               .Where(part => part is not null));

    private static string DoneBody(RunningAgent agent)
    {
        var head = $"“{agent.Title}” in {Duration(agent.LastActivity - agent.StartedAt)}.";
        return agent.EditedFiles.Count == 0 ? head : head + " Wrote " + string.Join(", ", agent.EditedFiles);
    }
}
