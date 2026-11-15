#if DEBUG
using System.Globalization;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>
/// A plain window holding the agent room, fed by a scene made up for it: Debug builds only, opened
/// by <c>--room-preview</c> instead of Claudio itself. A few agents in each area, one walking in by
/// the door, one on the sofa, Claudy at work. <c>--room-preview 21.5</c> lights the room for that
/// hour, <c>--room-weather snow</c> chooses the weather, <c>--room-quota 0.95</c> the session quota,
/// <c>--room-snapshot room.png</c> writes a frame to a file.
/// A click on a desk selects it; a click on Claudy's second screen opens and closes the summary.
/// </summary>
internal sealed partial class RoomPreviewWindow : Window
{
    private readonly RoomCanvas _room = new();
    private readonly DateTimeOffset _opened = DateTimeOffset.Now;
    private readonly double _quota;
    private int? _selected;

    public RoomPreviewWindow()
    {
        Title = "Claudio · agent room preview";
        var hour = Argument("--room-preview") is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : (double?)null;
        _quota = Argument("--room-quota") is { } quota && double.TryParse(quota, NumberStyles.Float, CultureInfo.InvariantCulture, out var share)
            ? share : 0.62;
        if (hour is { } wanted)
        {
            // The room at that hour, today, and time running on from there.
            var start = new DateTimeOffset(_opened.Date, _opened.Offset).AddHours(wanted);
            _room.Clock = () => start + (DateTimeOffset.Now - _opened);
        }
        _room.WeatherChoice = Argument("--room-weather") ?? RoomWeathers.AsItComes;
        _room.Title = "storefront";
        _room.Week = [0.35, 0.6, 0.2, 0.85, 1, 0.45, 0.7];
        _room.Scene = Scene;
        _room.DeskTapped += desk => _selected = _selected == desk ? null : desk;
        _room.SummaryTapped += () => _room.IsSummaryOpen = !_room.IsSummaryOpen;
        Content = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x1C, 0x17, 0x14)),
            Children = { _room },
        };
        _room.Loaded += async (_, _) =>
        {
            var scale = _room.XamlRoot?.RasterizationScale ?? 1;
            AppWindow.ResizeClient(new Windows.Graphics.SizeInt32((int)Math.Ceiling(RoomCanvas.Size.Width * scale),
                                                                  (int)Math.Ceiling(RoomCanvas.Size.Height * scale)));
            if (Argument("--room-snapshot") is { } path)
            {
                // Once the canvas has its device: a frame drawn off screen, written to a file.
                await Task.Delay(1500);
                await _room.SaveFrameAsync(Path.GetFullPath(path));
            }
        };
    }

    /// <summary><c>--room-preview</c> is on the command line.</summary>
    public static bool IsRequested => Environment.GetCommandLineArgs().Contains("--room-preview");

    /// <summary>The word after a switch, unless it is another switch.</summary>
    private static string? Argument(string name)
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? arguments[index + 1] : null;
    }

    private static Rgba Tint(int index) => Theme.AgentTints[index % Theme.AgentTints.Count];

    /// <summary>Who sits where: area, place, state, work.</summary>
    private static readonly (int Zone, int Index, RoomOccupantState State, AgentWork Work)[] Seated =
    [
        (0, 0, RoomOccupantState.Working, AgentWork.Code),
        (0, 1, RoomOccupantState.Idle, AgentWork.Docs),
        (0, 3, RoomOccupantState.Working, AgentWork.Plan),
        (1, 0, RoomOccupantState.Working, AgentWork.Tests),
        (1, 1, RoomOccupantState.Waiting, AgentWork.Build),
        (1, 2, RoomOccupantState.Working, AgentWork.Terminal),
        (1, 4, RoomOccupantState.Stalled, AgentWork.Install),
        (2, 0, RoomOccupantState.Working, AgentWork.Search),
        (2, 1, RoomOccupantState.Done, AgentWork.Web),
        (2, 3, RoomOccupantState.Working, AgentWork.Review),
        (3, 0, RoomOccupantState.Working, AgentWork.Git),
        (3, 1, RoomOccupantState.Working, AgentWork.Deploy),
    ];

    private RoomScene Scene(DateTimeOffset date, bool withHelp)
    {
        var desks = new RoomOccupant?[RoomPlan.SeatsPerZone * 4];
        for (var index = 0; index < Seated.Length; index++)
        {
            var (zone, place, state, work) = Seated[index];
            var flashes = index == 2 && (date - _opened).TotalSeconds % 6 < 0.4;
            desks[(zone * RoomPlan.SeatsPerZone) + place] = new RoomOccupant
            {
                Id = $"agent-{index}",
                Tint = Tint(index),
                State = state,
                Glyph = RoomGlyph.Symbol(work),
                Phase = index * 5,
                IsFlashing = flashes,
                Help = withHelp ? $"Agent {index} · {work.Label()}" : "",
            };
        }
        int Count(int zone) => Seated.Count(seat => seat.Zone == zone);

        // One Claudy walks in by the door to the empty desk of research, again and again.
        var route = RoomGeometry.Route(null, new RoomSeat(2, 4));
        var walk = RoomGeometry.Length(route) / RoomGeometry.Pace;
        var elapsed = (date - _opened).TotalSeconds % (walk + 2);
        var walker = new RoomWalker("walker", RoomGeometry.Point(route, elapsed * RoomGeometry.Pace), Tint(3), RoomGlyph.Symbol(AgentWork.Search));

        return new RoomScene
        {
            Zones = new([
                new RoomZone { Family = WorkFamily.Writing, Count = Count(0) },
                new RoomZone { Family = WorkFamily.Commands, Count = Count(1) },
                new RoomZone { Family = WorkFamily.Research, Count = Count(2) },
                new RoomZone { Family = WorkFamily.Commands, Count = Count(3) },
            ]),
            Desks = new(desks),
            Walkers = new([walker]),
            Loungers = new([new RoomLounger("lounger", 1, Tint(2), withHelp ? "Agent done · resting" : "")]),
            Chief = new RoomChief
            {
                State = RoomChiefState.Working,
                Glyph = RoomGlyph.Delegating,
                Quota = _quota,
                Help = withHelp ? "storefront · runs its agents" : "",
            },
            SelectedDesk = _selected,
        };
    }
}
#endif
