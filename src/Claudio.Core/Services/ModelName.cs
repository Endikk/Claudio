using System.Globalization;

namespace Claudio.Core.Services;

/// <summary>Accent palette: every gauge and model carries one, never a raw colour.</summary>
public enum Accent
{
    Coral,
    Amber,
    Violet,
    Sage,
    Sky,
}

/// <summary>
/// Turns a raw model identifier into a display label without a table, so any past or future
/// model named <c>claude-&lt;family&gt;-&lt;version&gt;</c> or <c>claude-&lt;version&gt;-&lt;family&gt;</c>
/// reads well: <c>claude-opus-4-8</c> is "Opus 4.8", <c>claude-3-5-haiku-20241022</c> "Haiku 3.5".
/// </summary>
public static class ModelName
{
    public static string Display(string identifier)
    {
        var parts = Parts(identifier);
        var family = parts.FirstOrDefault(part => !IsNumber(part)) ?? identifier;
        var label = char.ToUpperInvariant(family[0]) + family[1..];
        var version = VersionNumbers(identifier);
        return version.Count == 0 ? label : $"{label} {string.Join('.', version)}";
    }

    /// <summary>Unknown families fall through to the neutral tone.</summary>
    public static Accent AccentOf(string identifier)
    {
        var lowered = identifier.ToLowerInvariant();
        if (lowered.Contains("opus", StringComparison.Ordinal)) return Accent.Coral;
        if (lowered.Contains("sonnet", StringComparison.Ordinal)) return Accent.Violet;
        if (lowered.Contains("haiku", StringComparison.Ordinal)) return Accent.Sky;
        return Accent.Sage;
    }

    /// <summary>"opus", "sonnet", "haiku": groups successive versions of one family.</summary>
    public static string Family(string identifier) =>
        (Parts(identifier).FirstOrDefault(part => !IsNumber(part)) ?? identifier).ToLowerInvariant();

    /// <summary>Eight-digit components are release dates, not version numbers.</summary>
    private static List<int> VersionNumbers(string identifier) =>
        Parts(identifier).Where(part => part.Length < 8 && IsNumber(part))
                         .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
                         .ToList();

    private static string[] Parts(string identifier) =>
        identifier.Replace("claude-", string.Empty, StringComparison.Ordinal)
                  .Split('-', StringSplitOptions.RemoveEmptyEntries);

    private static bool IsNumber(string part) =>
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _);
}
