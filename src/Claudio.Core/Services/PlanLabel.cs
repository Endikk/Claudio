using System.Globalization;

namespace Claudio.Core.Services;

/// <summary>The plan a tier field reads as: "default_claude_max_5x" is "Max 5×".</summary>
public static class PlanLabel
{
    /// <summary>
    /// Label from the first non-empty field, except that Enterprise wins outright wherever it
    /// appears: its tier ("default_claude_zero") is internal quota plumbing and would read "Zero".
    /// </summary>
    public static string From(IEnumerable<string?> candidates)
    {
        var values = candidates.Select(value => value?.Trim())
                               .Where(value => !string.IsNullOrEmpty(value))
                               .Select(value => value!)
                               .ToList();
        if (values.Exists(value => value.Contains("enterprise", StringComparison.OrdinalIgnoreCase)))
        {
            return "Enterprise";
        }
        return From(values.FirstOrDefault() ?? string.Empty);
    }

    /// <summary>Purely generic: no tier is enumerated, so tiers introduced later read correctly.</summary>
    public static string From(string raw)
    {
        var words = raw.Replace("default_", string.Empty, StringComparison.Ordinal)
                       .Replace("claude_", string.Empty, StringComparison.Ordinal)
                       .Split('_', StringSplitOptions.RemoveEmptyEntries);

        return string.Join(' ', words.Select(word =>
        {
            if (word.Length <= 3 && word.EndsWith('x') && int.TryParse(word[..^1], NumberStyles.None,
                                                                       CultureInfo.InvariantCulture, out _))
            {
                return word[..^1] + "×";
            }
            return char.ToUpperInvariant(word[0]) + word[1..];
        }));
    }
}
