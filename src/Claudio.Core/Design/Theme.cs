using Claudio.Core.Services;

namespace Claudio.Core.Design;

/// <summary>An accent used as text, as Claudy's <c>Theme.Ink</c>.</summary>
public enum Ink
{
    Coral,
    Amber,
    Danger,
    Positive,
    Sky,
    Violet,
    Teal,
}

/// <summary>
/// Claudy's <c>Theme</c>, read from the shared tokens: the accents, the danger red, and the bands a
/// gauge moves through. Every visible colour goes through here, so no raw value appears in a view.
/// </summary>
public static class Theme
{
    private static readonly DesignTokens Tokens = DesignTokens.Shared;

    public static Rgba Danger { get; } = Tokens.Color("color.danger");

    // Claudy's export carries the accents, the danger red and the mascot's inks. The colours below
    // are Theme.swift's own, which the export does not write yet: they are copied as they stand.

    /// <summary>Good news: on pace, or behind it.</summary>
    public static Rgba Positive { get; } = Rgba.Parse("#4cc38a");

    /// <summary>Text set on a filled accent: white on coral is 3.1 to 1, this dark ink is above 5.</summary>
    public static Rgba OnAccent { get; } = Rgba.Parse("#1f1a17");

    /// <summary>An agent fallen silent: its Claudy loses its colour.</summary>
    public static Rgba Idle { get; } = Rgba.Parse("#8f8a86");

    /// <summary>
    /// The agents' Claudys, one colour each in launch order. Never coral, the main Claudy's, nor
    /// amber, which warns: an agent's Claudy must not pass for the quota's.
    /// </summary>
    public static IReadOnlyList<Rgba> AgentTints { get; } =
    [
        Color(Accent.Sky),
        Color(Accent.Violet),
        Rgba.Parse("#d9779b"), // rose
        Color(Accent.Sage),
    ];

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

    /// <summary>
    /// An accent used as text. Fills keep the accent itself; text on a light surface takes a darker
    /// shade of it, or amber and green fall under 2.5 to 1.
    /// </summary>
    public static Rgba Color(Ink ink, bool isDark) => ink switch
    {
        Ink.Coral => isDark ? Color(Accent.Coral) : Rgba.Parse("#ad5130"),
        Ink.Amber => isDark ? Color(Accent.Amber) : Rgba.Parse("#95600f"),
        Ink.Danger => isDark ? Danger : Rgba.Parse("#bb3828"),
        Ink.Positive => isDark ? Positive : Rgba.Parse("#2b7550"),
        Ink.Sky => isDark ? Color(Accent.Sky) : Rgba.Parse("#1f6ab0"),
        Ink.Violet => isDark ? Color(Accent.Violet) : Rgba.Parse("#6b44c9"),
        Ink.Teal => isDark ? Color(Accent.Teal) : Rgba.Parse("#1d7f78"),
        _ => Color(Accent.Coral),
    };

    /// <summary>
    /// The warm veil laid over the card's glass: dark clay at night, paper by day. Without it the
    /// glass takes the wallpaper's colour and the text goes grey on grey.
    /// </summary>
    public static Rgba Veil(bool isDark) => isDark ? new Rgba(0x1C, 0x17, 0x14, 163) : new Rgba(0xFF, 0xF8, 0xEE, 115);

    public static double Metric(string name) => Tokens.Dimension($"dimension.{name}");
}
