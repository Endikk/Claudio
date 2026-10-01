namespace Claudio.Core.Models;

/// <summary>
/// Where Claudio shows itself, one at a time, as Claudy's <c>Placement</c>: the floating card, or
/// the notification area, where a click on the icon opens the short card above the clock. Windows
/// has no notch, so Claudy's third place has no counterpart here.
/// </summary>
public enum Placement
{
    Widget,
    NotificationArea,
}

public static class PlacementExtensions
{
    /// <summary>The placement saved in the settings; anything unknown is the card.</summary>
    public static Placement Stored(string? raw) =>
        Enum.TryParse<Placement>(raw, ignoreCase: true, out var placement) && Enum.IsDefined(placement) ? placement : Placement.Widget;

    /// <summary>What a menu proposes: every placement but the one on screen.</summary>
    public static IReadOnlyList<Placement> Offered(this Placement shown) =>
        Enum.GetValues<Placement>().Where(placement => placement != shown).ToList();

    public static string MenuTitle(this Placement placement) => placement switch
    {
        Placement.NotificationArea => "Show in notification area",
        _ => "Show floating widget",
    };
}
