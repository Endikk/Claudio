namespace Claudio.App;

/// <summary>The agent room's window, opened from the menus, the Sessions tab, Ctrl+Alt+R and a banner.</summary>
public partial class App
{
    /// <summary>Opens the agent room in the middle of the screen, or closes it.</summary>
    private void ToggleRoom()
    {
    }

    /// <summary>A banner about an agent leads to its desk.</summary>
    private void ShowAgent(string agentId) => ToggleRoom();

    /// <summary>Follows the reading and the sessions while the room is open.</summary>
    private void RenderRoom()
    {
    }

    private void CloseRoom()
    {
    }
}
