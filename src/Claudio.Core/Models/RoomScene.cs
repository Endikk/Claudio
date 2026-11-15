using Claudio.Core.Design;

namespace Claudio.Core.Models;

/// <summary>What a desk's occupant is doing, as Claudy's <c>RoomOccupant.State</c>.</summary>
public enum RoomOccupantState
{
    Working,
    Idle,
    Stalled,
    Done,
    Waiting,
}

/// <summary>Who sits at a desk of the agent room, as the scene draws it.</summary>
public sealed record RoomOccupant
{
    public required string Id { get; init; }
    public required Rgba Tint { get; init; }
    public required RoomOccupantState State { get; init; }

    /// <summary>The glyph on its screen (a <c>RoomGlyph</c> name), <c>null</c> for a dark one.</summary>
    public string? Glyph { get; init; }

    /// <summary>Sets its typing and its blinks apart from its neighbours'.</summary>
    public int Phase { get; init; }

    /// <summary>Its work just changed: the screen lights up for a moment.</summary>
    public bool IsFlashing { get; init; }

    /// <summary>What a pointer resting on the desk is told.</summary>
    public string Help { get; init; } = "";
}

/// <summary>A Claudy on its way to a desk.</summary>
/// <param name="Position">Where its feet are.</param>
/// <param name="Glyph">The work it goes to, in a bubble over its head.</param>
public sealed record RoomWalker(string Id, RoomPoint Position, Rgba Tint, string? Glyph = null);

/// <summary>A Claudy that is done, sitting on the sofa until it leaves.</summary>
/// <param name="Slot">Its place on the sofa, from the left.</param>
public sealed record RoomLounger(string Id, int Slot, Rgba Tint, string Help = "");

/// <summary>One area of the room: what it is for, what it is turning into, and how far along.</summary>
public sealed record RoomZone
{
    public WorkFamily? Family { get; init; }

    /// <summary>The work the area is switching to, while its sign flips.</summary>
    public WorkFamily? Incoming { get; init; }

    /// <summary>0…1 along that switch.</summary>
    public double Progress { get; init; }

    public int Count { get; init; }

    /// <summary>The words on its sign.</summary>
    public string Title
    {
        get
        {
            var shown = Progress > 0.5 ? Incoming : Family;
            return $"{shown?.Title() ?? "Free"} · {Count}/{RoomPlan.SeatsPerZone}";
        }
    }
}

/// <summary>What Claudy at the centre desk is doing, as Claudy's <c>RoomChief.State</c>.</summary>
public enum RoomChiefState
{
    Working,
    Waiting,
    Idle,
    Away,
}

/// <summary>Claudy at the centre desk: the project's session.</summary>
public sealed record RoomChief
{
    public required RoomChiefState State { get; init; }
    public string? Glyph { get; init; }

    /// <summary>0…1 of the session quota, for the ring on its second screen. <c>null</c> with no measurement.</summary>
    public double? Quota { get; init; }

    /// <summary>What a pointer resting on its first screen is told: the session, and what it does.</summary>
    public string Help { get; init; } = "";
}

/// <summary>Everything the room shows at one instant.</summary>
public sealed record RoomScene
{
    public required ValueList<RoomZone> Zones { get; init; }

    /// <summary>One entry per desk, in <see cref="RoomSeat"/> order: area by area, six each.</summary>
    public required ValueList<RoomOccupant?> Desks { get; init; }

    public ValueList<RoomWalker> Walkers { get; init; } = ValueList<RoomWalker>.Empty;
    public required RoomChief Chief { get; init; }

    /// <summary>The Claudys that are done, on the sofa.</summary>
    public ValueList<RoomLounger> Loungers { get; init; } = ValueList<RoomLounger>.Empty;

    /// <summary>The desk whose agent is being looked at, as an index in <see cref="Desks"/>.</summary>
    public int? SelectedDesk { get; init; }
}
