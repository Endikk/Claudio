using Claudio.Core.Services;

namespace Claudio.Core.Design;

/// <summary>
/// Claudy's <c>Theme</c>, read from the shared tokens: the accents, the danger red, and the bands a
/// gauge moves through. Every visible colour goes through here, so no raw value appears in a view.
/// </summary>
public static class Theme
{
    private static readonly DesignTokens Tokens = DesignTokens.Shared;

    public static Rgba Danger { get; } = Tokens.Color("color.danger");

    /// <summary>Fractions at which a gauge turns amber, then red, and the card's hairline strains.</summary>
    public static double AmberThreshold { get; } = Tokens.Number("threshold.amber");
    public static double DangerThreshold { get; } = Tokens.Number("threshold.danger");
    public static double StrainThreshold { get; } = Tokens.Number("threshold.strain");

    public static Rgba Color(Accent accent) => Tokens.Color($"color.accent.{accent.ToString().ToLowerInvariant()}");

    /// <summary>
    /// Effective tint of a gauge: its base accent, except in the high-load band where the warning
    /// meaning takes over.
    /// </summary>
    public static Rgba Tint(Accent accent, double percent) =>
        percent >= DangerThreshold ? Danger
        : percent >= AmberThreshold ? Color(Accent.Amber)
        : Color(accent);

    public static double Metric(string name) => Tokens.Dimension($"dimension.{name}");
}
