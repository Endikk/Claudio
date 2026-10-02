using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The notch is what the system leaves between the two usable parts of the top edge, as in
/// Claudy's NotchGeometryTests; y counts downwards here, as on Windows. A PC reports none, so
/// Claudio gives its main screen one.
/// </summary>
public sealed class NotchGeometryTests
{
    /// <summary>Claudy's measurement of a 16-inch MacBook Pro at its default size.</summary>
    private static readonly ScreenMetrics BuiltIn = new(new ScreenRect(0, 0, 1728, 1117), 32, 771.5, 771.5);

    private static readonly ScreenMetrics External = new(new ScreenRect(0, 0, 2560, 1440));

    [Fact]
    public void TheNotchSitsBetweenTheTwoUsableAreas()
    {
        var geometry = NotchGeometry.Of(BuiltIn)!;

        Assert.Equal(new ScreenRect(771.5, 0, 185, 32), geometry.Notch);
        Assert.Equal(BuiltIn.Frame, geometry.ScreenFrame);
    }

    [Fact]
    public void AScreenWithoutTopInsetHasNoNotch() => Assert.Null(NotchGeometry.Of(External));

    [Fact]
    public void AnInsetWithoutUsableAreasIsNotANotch() =>
        Assert.Null(NotchGeometry.Of(BuiltIn with { LeftAreaWidth = null, RightAreaWidth = null }));

    [Fact]
    public void AreasCoveringTheWholeWidthLeaveNoNotch() =>
        Assert.Null(NotchGeometry.Of(BuiltIn with { LeftAreaWidth = 864, RightAreaWidth = 864 }));

    [Fact]
    public void TheNotchedScreenIsFoundBehindAnotherMainScreen()
    {
        var leftOfMain = BuiltIn with { Frame = new ScreenRect(-1728, 0, 1728, 1117) };

        var geometry = NotchGeometry.Find([External, leftOfMain])!;

        Assert.Equal(new ScreenRect(-956.5, 0, 185, 32), geometry.Notch);
        Assert.Equal(leftOfMain.Frame, geometry.ScreenFrame);
    }

    [Fact]
    public void WithoutANotchedScreenNothingIsFound()
    {
        Assert.Null(NotchGeometry.Find([External]));
        Assert.Null(NotchGeometry.Find([]));
    }

    [Fact]
    public void WithoutSimulationTheScreensAreLeftAlone() =>
        Assert.Equal([BuiltIn, External], NotchGeometry.Simulating(null, [BuiltIn, External]));

    [Fact]
    public void SimulatingNoneRemovesEveryNotch()
    {
        var screens = NotchGeometry.Simulating("none", [BuiltIn, External]);

        Assert.Null(NotchGeometry.Find(screens));
        Assert.Equal([BuiltIn.Frame, External.Frame], screens.Select(screen => screen.Frame));
    }

    [Fact]
    public void SimulatingASizePutsThatNotchAtTheTopCentreOfTheFirstScreen()
    {
        var screens = NotchGeometry.Simulating("230x44", [External, BuiltIn]);

        Assert.Equal(new ScreenRect(1165, 0, 230, 44), NotchGeometry.Find(screens)!.Notch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("wide")]
    [InlineData("0x32")]
    [InlineData("200x0")]
    [InlineData("200")]
    [InlineData("-5x32")]
    [InlineData("3000x32")]
    public void AnUnreadableSimulationIsIgnored(string value) =>
        Assert.Equal([BuiltIn], NotchGeometry.Simulating(value, [BuiltIn]));

    /// <summary>No PC has a notch: the island hangs from the top centre of the main screen.</summary>
    [Fact]
    public void OnAPcTheMainScreenGetsWindowsNotch()
    {
        var main = new ScreenMetrics(new ScreenRect(0, 0, 1920, 1032));
        var other = new ScreenMetrics(new ScreenRect(-1280, 120, 1280, 984));

        var geometry = NotchGeometry.Current([main, other])!;

        Assert.Equal(new ScreenRect(948, 0, 24, 32), geometry.Notch);
        Assert.Equal(main.Frame, geometry.ScreenFrame);
    }

    [Fact]
    public void TheSimulationStillDecidesOnAPc()
    {
        var main = new ScreenMetrics(new ScreenRect(0, 0, 1920, 1032));

        Assert.Null(NotchGeometry.Current([main], "none"));
        Assert.Equal(new ScreenRect(867.5, 0, 185, 32), NotchGeometry.Current([main], "185x32")!.Notch);
        Assert.Null(NotchGeometry.Current([]));
    }
}
