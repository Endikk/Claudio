using Claudio.Core.Models;

namespace Claudio.Core.Tests;

/// <summary>
/// Where Claudio shows itself, as Claudy's PlacementTests. The island needs a notch; without one
/// Claudio waits in the notification area and the choice is kept for when the screen comes back.
/// </summary>
public sealed class PlacementTests
{
    [Fact]
    public void ANewInstallStartsAsTheWidget() => Assert.Equal(Placement.Widget, PlacementExtensions.Stored(null));

    [Fact]
    public void TheSavedPlacementIsKept()
    {
        Assert.Equal(Placement.Notch, PlacementExtensions.Stored("Notch"));
        Assert.Equal(Placement.Notch, PlacementExtensions.Stored("notch"));
        Assert.Equal(Placement.NotificationArea, PlacementExtensions.Stored("NotificationArea"));
    }

    [Fact]
    public void AnUnknownValueIsTheWidget()
    {
        Assert.Equal(Placement.Widget, PlacementExtensions.Stored("dock"));
        Assert.Equal(Placement.Widget, PlacementExtensions.Stored("7"));
    }

    [Fact]
    public void TheIslandNeedsANotch()
    {
        Assert.Equal(Placement.Notch, Placement.Notch.Effective(hasNotch: true));
        Assert.Equal(Placement.NotificationArea, Placement.Notch.Effective(hasNotch: false));
        Assert.Equal(Placement.Widget, Placement.Widget.Effective(hasNotch: false));
        Assert.Equal(Placement.NotificationArea, Placement.NotificationArea.Effective(hasNotch: true));
    }

    [Fact]
    public void MenusOfferTheOtherPlacements()
    {
        Assert.Equal([Placement.NotificationArea, Placement.Notch], Placement.Widget.Offered(hasNotch: true));
        Assert.Equal([Placement.Widget, Placement.Notch], Placement.NotificationArea.Offered(hasNotch: true));
        Assert.Equal([Placement.Widget, Placement.NotificationArea], Placement.Notch.Offered(hasNotch: true));
    }

    [Fact]
    public void WithoutANotchNoMenuOffersIt()
    {
        Assert.Equal([Placement.NotificationArea], Placement.Widget.Offered(hasNotch: false));
        Assert.Equal([Placement.Widget], Placement.NotificationArea.Offered(hasNotch: false));
    }

    /// <summary>The notification area stands in for the island: offering it would do nothing.</summary>
    [Fact]
    public void AnIslandChoiceShownInTheNotificationAreaOffersOnlyTheWidget() =>
        Assert.Equal([Placement.Widget], Placement.Notch.Offered(hasNotch: false));

    [Fact]
    public void EveryPlacementHasItsMenuTitle()
    {
        Assert.Equal("Show floating widget", Placement.Widget.MenuTitle());
        Assert.Equal("Show in notification area", Placement.NotificationArea.MenuTitle());
        Assert.Equal("Show at the top of the screen", Placement.Notch.MenuTitle());
    }
}
