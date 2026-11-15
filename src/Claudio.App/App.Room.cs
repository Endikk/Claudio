using Claudio.Core.ViewModels;

namespace Claudio.App;

/// <summary>
/// The agent room's window, opened from the menus, the Sessions tab, Ctrl+Alt+R, <c>--room</c> and
/// a banner, as Claudy's app delegate drives its <c>RoomWindowController</c>. The room's model is
/// made the first time the room is wanted, and follows the sessions from then on; a closed room
/// shows nothing and walks nobody.
/// </summary>
public partial class App
{
    private RoomWindow? _roomWindow;
    private RoomViewModel? _room;
    private RoomView? _roomView;

    /// <summary>The room's model, made once: it reads the sessions the card already follows.</summary>
    private RoomViewModel? Room()
    {
        if (_room is not null || _agents is not { } agents)
        {
            return _room;
        }
        _room = new RoomViewModel(agents, new PreferencesRoomStore(), reducesMotion: () => Motion.ReducesMotion);
        _room.Changed += RenderRoom;
        _room.Sync();
        return _room;
    }

    /// <summary>Opens the agent room in the middle of the screen, or closes it.</summary>
    private void ToggleRoom()
    {
        if (_roomWindow?.IsOpen == true)
        {
            CloseRoom();
        }
        else
        {
            OpenRoom();
        }
    }

    private void OpenRoom()
    {
        if (_roomWindow is not { } window || Room() is not { } room || _agents is not { } agents || _model is not { } model)
        {
            return;
        }
        if (_roomView is null)
        {
            _roomView = new RoomView(room, agents, model, CloseRoom);
            window.ThemeFlipped += RenderRoom;
        }
        room.IsVisible = true;
        window.Open(_roomView);
        RenderRoom();
    }

    /// <summary>A banner about an agent leads to its desk: its project's room, its card open.</summary>
    private void ShowAgent(string agentId)
    {
        if (Room() is not { } room)
        {
            return;
        }
        room.Sync();
        room.Show(agentId);
        OpenRoom();
    }

    /// <summary>Follows the reading and the sessions while the room is open.</summary>
    private void RenderRoom()
    {
        if (_roomWindow?.IsOpen != true || _roomView is not { } view)
        {
            return;
        }
        try
        {
            view.Update();
        }
#pragma warning disable CA1031 // A room that fails to redraw is written down; Claudio carries on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"room: {error}");
        }
    }

    /// <summary>Closes the room: nobody walks while it is away, and its summary is folded.</summary>
    private void CloseRoom()
    {
        if (_room is { } room)
        {
            room.IsVisible = false;
            room.IsSummaryOpen = false;
        }
        _roomWindow?.Dismiss();
    }

    /// <summary>What the room keeps across launches, in Claudio's settings file.</summary>
    private sealed class PreferencesRoomStore : IRoomStore
    {
        public IReadOnlyList<string> HiddenRooms
        {
            get => Preferences.HiddenRooms;
            set => Preferences.HiddenRooms = value;
        }

        public string WeatherChoice
        {
            get => Preferences.RoomWeather;
            set => Preferences.RoomWeather = value;
        }
    }
}
