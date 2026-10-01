using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// Reads <c>GET /api/oauth/usage</c>. Two representations coexist in one answer: the top-level
/// fields (<c>utilization</c>, 0–100) that Claude Code's <c>/usage</c> reads, and <c>limits[]</c>,
/// the only place naming the model of the per-model window. The former leads, the latter
/// completes. An answer with neither window belongs to a usage-billed plan, whose only quota is
/// its monthly spend cap.
/// </summary>
public static partial class UsageParser
{
    private static readonly (string Key, string Name)[] ScopedWindows =
    [
        ("seven_day_opus", "Opus"),
        ("seven_day_sonnet", "Sonnet"),
        ("seven_day_oauth_apps", "OAuth apps"),
    ];

    /// <summary>The reading, or null when the answer carries no quota at all.</summary>
    public static QuotaReading? Parse(string json, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var session = Window(Property(root, "five_hour"));
            var weekly = Window(Property(root, "seven_day"));

            QuotaWindow? scoped = null;
            foreach (var (key, name) in ScopedWindows)
            {
                var candidate = Window(Property(root, key), name);
                if (candidate is not null && candidate.Percent > (scoped?.Percent ?? -1))
                {
                    scoped = candidate;
                }
            }

            if (Property(root, "limits") is { ValueKind: JsonValueKind.Array } limits)
            {
                foreach (var raw in limits.EnumerateArray())
                {
                    if (String(raw, "kind") is not { } kind || Number(raw, "percent") is not { } percent)
                    {
                        continue;
                    }
                    var model = Property(Property(raw, "scope"), "model");
                    var candidate = new QuotaWindow(percent / 100, Date(String(raw, "resets_at")),
                                                    String(model, "display_name"));
                    switch (kind)
                    {
                        case "session":
                            session ??= candidate;
                            break;
                        case "weekly_all":
                            weekly ??= candidate;
                            break;
                        case "weekly_scoped" when candidate.Label is not null || scoped is null:
                            scoped = candidate;
                            break;
                    }
                }
            }

            var reading = new QuotaReading
            {
                Session = session,
                Weekly = weekly,
                Scoped = scoped,
                Spend = session is null && weekly is null ? Spend(root, now) : null,
                Source = QuotaSource.Api,
            };
            return reading.IsEmpty ? null : reading;
        }
    }

    /// <summary>
    /// Enterprise plans are billed on usage: <c>spend</c> carries each amount in minor units with
    /// its exponent (4631 at exponent 2 is $46.31). <c>extra_usage</c>, the older shape of the
    /// same budget, is the fallback.
    /// </summary>
    private static SpendReading? Spend(JsonElement root, DateTimeOffset now)
    {
        var extra = Property(root, "extra_usage");
        var reached = Bool(extra, "spend_limit_reached") ?? false;
        var resetsAt = SpendReading.PeriodEnd(now);

        if (Property(root, "spend") is { ValueKind: JsonValueKind.Object } block
            && Bool(block, "enabled") != false
            && Amount(Property(block, "used")) is { } used
            && Amount(Property(block, "limit")) is { } limit
            && limit.Value > 0)
        {
            return new SpendReading(used.Value, limit.Value, limit.Currency, reached, resetsAt);
        }

        if (extra is not { ValueKind: JsonValueKind.Object }
            || Bool(extra, "is_enabled") != true
            || Number(extra, "used_credits") is not { } usedMinor
            || Number(extra, "monthly_limit") is not { } limitMinor
            || limitMinor <= 0)
        {
            return null;
        }
        var scale = Math.Pow(10, Number(extra, "decimal_places") ?? 2);
        return new SpendReading(usedMinor / scale, limitMinor / scale, String(extra, "currency") ?? "USD",
                                reached, resetsAt);
    }

    /// <summary><c>{ "amount_minor": 4631, "currency": "USD", "exponent": 2 }</c> is 46.31 USD.</summary>
    private static (double Value, string Currency)? Amount(JsonElement? raw)
    {
        if (Number(raw, "amount_minor") is not { } minor)
        {
            return null;
        }
        var exponent = Number(raw, "exponent") ?? 2;
        return (minor / Math.Pow(10, exponent), String(raw, "currency") ?? "USD");
    }

    /// <summary>One top-level block, <c>{ "utilization": 59.0, "resets_at": "…" }</c>.</summary>
    private static QuotaWindow? Window(JsonElement? raw, string? label = null) =>
        Number(raw, "utilization") is { } utilization
            ? new QuotaWindow(utilization / 100, Date(String(raw, "resets_at")), label)
            : null;

    /// <summary>
    /// <c>resets_at</c> carries six-digit fractions of a second; they are dropped before parsing,
    /// second precision being ample, exactly as Claudy does.
    /// </summary>
    public static DateTimeOffset? Date(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        var cleaned = Fraction().Replace(raw, string.Empty);
        return DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                                       out var date)
            ? date
            : null;
    }

    [GeneratedRegex(@"\.\d+")]
    private static partial Regex Fraction();

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var found)
            ? found
            : null;

    private static double? Number(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    private static string? String(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static bool? Bool(JsonElement? element, string name) =>
        Property(element, name)?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
}
