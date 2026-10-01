using System.Text.Json;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// A request-free source, as Claudy's <c>UsageBridge</c>: the <c>anthropic-ratelimit-unified-*</c>
/// counters Claude Code already receives on every API response and pipes to its status line, which
/// one <c>tee</c> can drop in a file for Claudio. Same figures as <c>/api/oauth/usage</c>, immune to
/// rate limiting, but they only move while Claude Code works and cover the 5-hour and weekly
/// windows only. A safety net, not the primary source.
/// </summary>
public static class UsageBridge
{
    /// <summary>Past this age a reading describes a session long finished.</summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Status-line command to add to <c>settings.json</c>, offered for copying rather than
    /// installed: overwriting someone's status line unasked is not ours to do. Claude Code runs it
    /// in Git Bash on Windows, hence the POSIX form.
    /// </summary>
    public static string StatusLineSnippet(string file) => $"tee \"{file.Replace('\\', '/')}\" > /dev/null";

    /// <summary>
    /// Latest reading dropped by Claude Code, when it is still current. The file is rewritten on
    /// every status-line render, so its modification date times the reading better than its contents.
    /// </summary>
    public static QuotaReading? Read(string file, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(file) || now - File.GetLastWriteTimeUtc(file) >= Freshness)
            {
                return null;
            }
            return Parse(File.ReadAllText(file));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static QuotaReading? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("rate_limits", out var limits)
                || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var session = Window(limits, "five_hour");
            var weekly = Window(limits, "seven_day");
            return session is null && weekly is null
                ? null
                : new QuotaReading { Session = session, Weekly = weekly, Source = QuotaSource.Bridge };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Two shapes coexist depending on the Claude Code version: <c>used_percentage</c> (0…100) with
    /// a Unix-seconds <c>resets_at</c> on the status-line side, <c>utilization</c> with an ISO
    /// <c>resets_at</c> on the SDK side. Both are accepted.
    /// </summary>
    private static QuotaWindow? Window(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var raw) || raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        double? percent = raw.TryGetProperty("used_percentage", out var used) && used.ValueKind == JsonValueKind.Number ? used.GetDouble()
            : raw.TryGetProperty("utilization", out var utilization) && utilization.ValueKind == JsonValueKind.Number ? utilization.GetDouble()
            : null;
        if (percent is not { } value)
        {
            return null;
        }
        DateTimeOffset? resetsAt = null;
        if (raw.TryGetProperty("resets_at", out var reset))
        {
            resetsAt = reset.ValueKind switch
            {
                JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds((long)(reset.GetDouble() * 1000)),
                JsonValueKind.String => UsageParser.Date(reset.GetString()),
                _ => null,
            };
        }
        return new QuotaWindow(value / 100, resetsAt);
    }

    /// <summary>
    /// The account's reading, unless it is not fresh and the bridge has one. A last spend reading
    /// stays: the bridge only relays 5-hour and weekly windows, which a plan billed on usage does
    /// not have, so any it carries describe another account.
    /// </summary>
    public static QuotaReading? Merge(QuotaReading? account, QuotaReading? bridge)
    {
        if (bridge is null)
        {
            return account;
        }
        return account is not null && (account.Source.Kind == QuotaSourceKind.Api || account.Spend is not null) ? account : bridge;
    }
}
