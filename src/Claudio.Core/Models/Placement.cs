namespace Claudio.Core.Models;

/// <summary>
/// Where Claudio shows itself, one at a time, as Claudy's <c>Placement</c>: the floating card, the
/// notification area (Claudy's menu bar item), where a click on the icon opens the short card above
/// the clock, or the island at the top of the screen. Claudy hangs its island round the notch; a
/// PC has none, so Claudio's hangs from the top centre of the main screen, round a notch of its own
/// (<see cref="Services.NotchGeometry"/>).
/// </summary>
public enum Placement
{
    Widget,
    NotificationArea,
    Notch,
}

public static class PlacementExtensions
{
    /// <summary>The placement saved in the settings; anything unknown is the card.</summary>
    public static Placement Stored(string? raw) =>
        Enum.TryParse<Placement>(raw, ignoreCase: true, out var placement) && Enum.IsDefined(placement) ? placement : Placement.Widget;

    /// <summary>
    /// What is actually shown. The island needs a notch: without one Claudio waits in the
    /// notification area, and the choice itself is kept for when the screen comes back.
    /// </summary>
    public static Placement Effective(this Placement placement, bool hasNotch) =>
        placement == Placement.Notch && !hasNotch ? Placement.NotificationArea : placement;

    /// <summary>
    /// What a menu proposes: every placement but the one on screen, the island only when a screen
    /// can hold it. Measured from the effective placement, so the notification area standing in for
    /// the island does not offer the notification area.
    /// </summary>
    public static IReadOnlyList<Placement> Offered(this Placement placement, bool hasNotch)
    {
        var shown = placement.Effective(hasNotch);
        return Enum.GetValues<Placement>().Where(offered => offered != shown && (offered != Placement.Notch || hasNotch)).ToList();
    }

    public static string MenuTitle(this Placement placement) => placement switch
    {
        Placement.NotificationArea => "Show in notification area",
        Placement.Notch => "Show at the top of the screen",
        _ => "Show floating widget",
    };
}
