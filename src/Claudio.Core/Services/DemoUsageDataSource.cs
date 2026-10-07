using System.Globalization;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Claudy's sample data set, shown while Claude Code is not on this PC and nobody signed in to
/// Claudio: the card then shows what it would say, rather than an empty frame. Nothing identifying
/// is written into it: the name comes from the Windows session, the projects carry neutral names,
/// and the snapshot is marked as demo, so every face announces it and none takes it for a reading.
/// </summary>
public sealed class DemoUsageDataSource
{
    private const double Peak = 2_200_000;
    private const long WeeklyLimit = 16_000_000;
    private const long ScopedLimit = 7_000_000;
    private static readonly double[] Daily = [0.52, 0.94, 0.28, 0.71, 1.0, 0.61, 0.38];

    /// <summary>
    /// The demo opens partway through a session: this share of the quota is already used and the
    /// same share of the window is already gone, so the gauge starts on its pace marker. A window
    /// opening at launch would show 34 % used for 0 % elapsed, "ahead of pace" in red all cycle.
    /// </summary>
    private const double SessionShareAtStart = 0.34;
    private static readonly TimeSpan SessionElapsedAtStart = SessionShareAtStart * UsageAggregator.SessionWindow;

    private readonly Func<Account> _account;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _zone;
    private readonly Random _random;
    private readonly TimeSpan _delay;
    private readonly DayOfWeek _firstDay;
    private double _drift;
    private DateTimeOffset? _start;

    /// <param name="delay">A moment of loading, as a real reading takes; none in the tests.</param>
    public DemoUsageDataSource(Func<Account> account, Func<DateTimeOffset>? now = null, TimeZoneInfo? zone = null,
                               Random? random = null, TimeSpan? delay = null, DayOfWeek? firstDayOfWeek = null)
    {
        _account = account;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _zone = zone ?? TimeZoneInfo.Local;
        _random = random ?? Random.Shared;
        _delay = delay ?? TimeSpan.FromMilliseconds(250);
        _firstDay = firstDayOfWeek ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancel = default)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, cancel).ConfigureAwait(false);
        }

        var now = _now();
        var local = TimeZoneInfo.ConvertTime(now, _zone);
        var today = new DateTimeOffset(local.Date, local.Offset);
        var weekStart = today.AddDays(-(((int)local.DayOfWeek - (int)_firstDay + 7) % 7));
        var weekEnd = weekStart.AddDays(7);

        // The session creeps up at each reading, then starts over: the mascot and the gauges move.
        _drift += 0.006 + (_random.NextDouble() * 0.014);
        var opening = now - SessionElapsedAtStart;
        var start = _start ??= opening;
        if (_drift >= 0.62)
        {
            _drift = 0;
            _start = opening;
            start = opening;
        }

        var sessionPercent = Math.Min(SessionShareAtStart + _drift, 0.99);
        var history = Daily.Select((factor, index) =>
        {
            var day = today.AddDays(index - (Daily.Length - 1));
            var share = index == Daily.Length - 1 ? factor + (_drift / 2) : factor;
            return new TokenSample(day, (long)(Peak * share));
        }).ToList();

        var weekTokens = history.Sum(sample => sample.Tokens);
        var scopedTokens = (long)(weekTokens * 0.29);

        return new UsageSnapshot
        {
            Session = new UsageWindow
            {
                Title = "Session",
                Window = "5h",
                Percent = sessionPercent,
                TokensUsed = (long)(2_400_000 * sessionPercent),
                WindowStart = start,
                ResetDate = start + UsageAggregator.SessionWindow,
                Accent = Accent.Coral,
            },
            Weekly = new UsageWindow
            {
                Title = "Weekly",
                Window = "7d",
                Percent = Math.Min((double)weekTokens / WeeklyLimit, 1),
                TokensUsed = weekTokens,
                WindowStart = weekStart,
                ResetDate = weekEnd,
                Accent = Accent.Amber,
            },
            Scoped = new UsageWindow
            {
                Title = "Sonnet",
                Window = "7d",
                Percent = Math.Min((double)scopedTokens / ScopedLimit, 1),
                TokensUsed = scopedTokens,
                WindowStart = weekStart,
                ResetDate = weekEnd,
                Accent = Accent.Violet,
            },
            History = history,
            Models =
            [
                new ModelUsage("opus", "Opus", (long)(weekTokens * 0.54), 0.54, Accent.Coral),
                new ModelUsage("sonnet", "Sonnet", scopedTokens, 0.29, Accent.Violet),
                new ModelUsage("haiku", "Haiku", (long)(weekTokens * 0.17), 0.17, Accent.Sky),
            ],
            Projects =
            [
                new ProjectUsage("a", "main-project", (long)(weekTokens * 0.44), 0.44),
                new ProjectUsage("b", "api", (long)(weekTokens * 0.27), 0.27),
                new ProjectUsage("c", "website", (long)(weekTokens * 0.18), 0.18),
                new ProjectUsage("d", "scripts", (long)(weekTokens * 0.11), 0.11),
            ],
            Account = _account(),
            ActiveModel = "Opus",
            TodayTokens = history[^1].Tokens,
            WeekTokens = weekTokens,
            SessionCount = 3,
            UpdatedAt = now,
            QuotaSource = QuotaSource.Demo,
        };
    }
}
