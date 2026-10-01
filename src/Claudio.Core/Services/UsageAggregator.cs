using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Turns raw readings into a displayable snapshot, as Claudy's <c>UsageAggregator</c> does.
/// Percentages come from the account alone. Nothing is estimated: Anthropic's answer carries
/// <c>limit_dollars: null</c>, so the quota is not a token tally and no local count could
/// reproduce it. Without a real quota the gauges declare themselves unmeasured and the UI shows
/// "—". Local transcripts keep one role only: the token detail, which describes <i>this machine</i>.
/// </summary>
public static class UsageAggregator
{
    /// <summary>Length of one Claude Code session window.</summary>
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    private static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>Rows shown per split.</summary>
    private const int RowLimit = 4;

    public static UsageSnapshot Snapshot(IReadOnlyList<TranscriptEntry> entries, Account account, QuotaReading? reading,
                                         DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = StartOfDay(now, zone);
        var source = reading?.Source ?? QuotaSource.Unavailable;

        var session = SessionGauge(reading?.Session, entries, now, source);
        var weekly = WeeklyGauge(reading?.Weekly, entries, now, source, "Weekly", Accent.Amber, family: null);
        var scopedName = reading?.Scoped?.Label;
        var scoped = WeeklyGauge(reading?.Scoped, entries, now, source, scopedName ?? "Per model",
                                 ModelName.AccentOf(scopedName ?? string.Empty), scopedName?.ToLowerInvariant());

        var rolling = entries.Where(entry => entry.Date >= now - TimeSpan.FromDays(7)).ToList();
        return new UsageSnapshot
        {
            Session = session,
            Weekly = weekly,
            Scoped = scoped,
            Spend = SpendGauge(reading?.Spend, source),
            History = History(rolling, today, zone),
            Models = Models(rolling),
            Projects = Projects(rolling),
            Account = account,
            ActiveModel = DominantModel(entries, session.WindowStart),
            TodayTokens = entries.Where(entry => entry.Date >= today).Sum(entry => entry.Tokens),
            WeekTokens = rolling.Sum(entry => entry.Tokens),
            SessionCount = entries.Where(entry => entry.Date >= today && !entry.IsSidechain)
                                  .Select(entry => entry.SessionId).Distinct(StringComparer.Ordinal).Count(),
            UpdatedAt = now,
            QuotaSource = source,
        };
    }

    /// <summary>
    /// Five-hour window. The percentage is the account's, the tokens are this machine's, and the
    /// two never merge into one number.
    /// </summary>
    private static UsageWindow SessionGauge(QuotaWindow? quota, IReadOnlyList<TranscriptEntry> entries, DateTimeOffset now,
                                            QuotaSource source)
    {
        var block = CurrentLocalBlock(entries, now);
        if (quota is null || !source.IsMeasured)
        {
            return new UsageWindow
            {
                Title = "Session",
                Window = "5h",
                Percent = 0,
                TokensUsed = block?.Tokens ?? 0,
                WindowStart = block?.Start ?? now,
                ResetDate = block?.End ?? now,
                Accent = Accent.Coral,
                IsMeasured = false,
            };
        }

        var start = quota.ResetsAt is { } reset ? reset - SessionWindow : now;
        return new UsageWindow
        {
            Title = "Session",
            Window = "5h",
            Percent = quota.Percent,
            TokensUsed = entries.Where(entry => entry.Date >= start).Sum(entry => entry.Tokens),
            WindowStart = start,
            ResetDate = quota.ResetsAt ?? now,
            Accent = Accent.Coral,
            LocalActivityEnd = block?.End,
        };
    }

    /// <summary>Seven-day window, optionally narrowed to one model family for the per-model gauge.</summary>
    private static UsageWindow WeeklyGauge(QuotaWindow? quota, IReadOnlyList<TranscriptEntry> entries, DateTimeOffset now,
                                           QuotaSource source, string title, Accent accent, string? family)
    {
        if (quota is null || !source.IsMeasured)
        {
            return new UsageWindow
            {
                Title = title,
                Window = "7d",
                WindowStart = now,
                ResetDate = now,
                Accent = accent,
                IsMeasured = false,
            };
        }

        var start = quota.ResetsAt is { } reset ? reset - WeeklyWindow : now;
        return new UsageWindow
        {
            Title = title,
            Window = "7d",
            Percent = quota.Percent,
            TokensUsed = entries.Where(entry => entry.Date >= start && (family is null || ModelName.Family(entry.Model) == family))
                                .Sum(entry => entry.Tokens),
            WindowStart = start,
            ResetDate = quota.ResetsAt ?? now,
            Accent = accent,
        };
    }

    /// <summary>
    /// Monthly spend cap. The window is the calendar month, so the pace marker reads as it does
    /// elsewhere: halfway through the month, steady spending sits at 50 %.
    /// </summary>
    private static UsageWindow? SpendGauge(SpendReading? spend, QuotaSource source) =>
        spend is null || !source.IsMeasured
            ? null
            : new UsageWindow
            {
                Title = "Spend",
                Window = "month",
                Percent = spend.Percent,
                WindowStart = spend.StartsAt,
                ResetDate = spend.ResetsAt,
                Accent = Accent.Coral,
                Amount = spend,
            };

    private sealed record Block(DateTimeOffset Start, long Tokens)
    {
        public DateTimeOffset End => Start + SessionWindow;
    }

    /// <summary>
    /// Current local window, used only to situate activity when the account gives no quota. Split
    /// the way Claude Code does: opens on the first message, closes after five hours or a longer idle.
    /// </summary>
    private static Block? CurrentLocalBlock(IReadOnlyList<TranscriptEntry> entries, DateTimeOffset now)
    {
        Block? last = null;
        DateTimeOffset? previous = null;
        foreach (var entry in entries)
        {
            var expired = last is null || entry.Date >= last.End;
            var idle = previous is not { } before || entry.Date - before >= SessionWindow;
            last = expired || idle ? new Block(FloorToHour(entry.Date), entry.Tokens) : last! with { Tokens = last.Tokens + entry.Tokens };
            previous = entry.Date;
        }
        return last is not null && last.End > now ? last : null;
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset date) =>
        new(date.Ticks - (date.Ticks % TimeSpan.TicksPerHour), date.Offset);

    /// <summary>
    /// Model that weighed the most since a given date. The <i>last</i> line's model is often a hook
    /// or a subagent; the window's dominant model is what describes the real work.
    /// </summary>
    private static string DominantModel(IReadOnlyList<TranscriptEntry> entries, DateTimeOffset since) =>
        entries.Where(entry => entry.Date >= since)
               .GroupBy(entry => ModelName.Display(entry.Model), StringComparer.Ordinal)
               .Select(group => (Name: group.Key, Weight: group.Sum(entry => entry.Weight)))
               .OrderByDescending(pair => pair.Weight)
               .Select(pair => pair.Name)
               .FirstOrDefault() ?? string.Empty;

    /// <summary>Seven rolling days. Idle days must exist as points, or the curve skips its own troughs.</summary>
    private static List<TokenSample> History(IEnumerable<TranscriptEntry> entries, DateTimeOffset today, TimeZoneInfo zone)
    {
        var totals = entries.GroupBy(entry => StartOfDay(entry.Date, zone))
                            .ToDictionary(group => group.Key, group => group.Sum(entry => entry.Tokens));
        return Enumerable.Range(0, 7).Reverse()
                         .Select(offset => StartOfDay(today.AddDays(-offset).AddHours(12), zone))
                         .Select(day => new TokenSample(day, totals.GetValueOrDefault(day)))
                         .ToList();
    }

    /// <summary>
    /// Split by model, ranked and shared by weight: raw tokens are mostly cache reads and would
    /// crown whichever model re-reads the longest context, not the one using the quota. Dated
    /// snapshots of one version are one row. Below 1 % a row adds nothing but a "0 %".
    /// </summary>
    private static List<ModelUsage> Models(IReadOnlyList<TranscriptEntry> entries)
    {
        var rows = Tally(entries, entry => ModelName.Display(entry.Model));
        var total = rows.Sum(row => row.Weight);
        if (total <= 0)
        {
            return [];
        }
        return rows.Where(row => row.Weight / total >= 0.01).Take(RowLimit)
                   .Select(row => new ModelUsage(row.Key, row.Key, row.Tokens, row.Weight / total, ModelName.AccentOf(row.Key)))
                   .ToList();
    }

    /// <summary>Split by project, ranked and shared by weight like the models.</summary>
    private static List<ProjectUsage> Projects(IReadOnlyList<TranscriptEntry> entries)
    {
        var rows = Tally(entries, entry => entry.Project);
        var total = rows.Sum(row => row.Weight);
        if (total <= 0)
        {
            return [];
        }
        var top = rows.Take(RowLimit).ToList();
        var names = DisplayNames(top.Select(row => row.Key).ToList());
        return top.Select(row => new ProjectUsage(row.Key, names[row.Key], row.Tokens, row.Weight / total)).ToList();
    }

    private static List<(string Key, long Tokens, double Weight)> Tally(IEnumerable<TranscriptEntry> entries,
                                                                         Func<TranscriptEntry, string> key) =>
        entries.GroupBy(key, StringComparer.Ordinal)
               .Select(group => (group.Key, group.Sum(entry => entry.Tokens), group.Sum(entry => entry.Weight)))
               .OrderByDescending(row => row.Item3)
               .ToList();

    /// <summary>
    /// Folder name, prefixed by its parent when two shown projects share one, so <c>work/api</c>
    /// and <c>personal/api</c> stay distinguishable. Both separators are read: a WSL project keeps
    /// its forward slashes on Windows.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DisplayNames(IReadOnlyList<string> paths)
    {
        static string[] Components(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        static string Name(string path) => Components(path) is { Length: > 0 } parts && !parts[^1].EndsWith(':') ? parts[^1] : "—";

        var counts = paths.GroupBy(Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var name = Name(path);
            var parts = Components(path);
            names[path] = counts[name] > 1 && parts.Length > 1 && !parts[^2].EndsWith(':') ? $"{parts[^2]}/{name}" : name;
        }
        return names;
    }

    /// <summary>Local midnight of the day holding <paramref name="date"/>, as an instant.</summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset date, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(date, zone);
        var midnight = local.Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
