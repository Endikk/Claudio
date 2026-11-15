namespace Claudio.Core.Models;

/// <summary>A point of the agent room, in the scene's own units (480 by 270), y counting downwards.</summary>
public readonly record struct RoomPoint(double X, double Y)
{
    public static RoomPoint Zero { get; }
}

/// <summary>A size in the agent room's units.</summary>
public readonly record struct RoomSize(double Width, double Height);

/// <summary>A step in the agent room's units: the way something faces.</summary>
public readonly record struct RoomVector(double Dx, double Dy);

/// <summary>One desk of the agent room: its area, and its place among the area's six.</summary>
public readonly record struct RoomSeat(int Zone, int Index);

/// <summary>
/// Who sits where in the agent room, as Claudy's <c>RoomPlan</c>. Four areas of six desks: three
/// have a kind of work of their own, the fourth is spare. An agent sits in the area of its work;
/// when that area is full, an empty one takes the same work until it empties again.
/// </summary>
/// <remarks>
/// Claudy's plan is a value; this one hands out snapshots: <see cref="Families"/>,
/// <see cref="Seats"/> and <see cref="Unseated"/> are replaced, never changed, by <see cref="Seat"/>,
/// so what a caller kept from before a pass still says what it said.
/// </remarks>
public sealed class RoomPlan
{
    public const int SeatsPerZone = 6;

    /// <summary>What each area is for when nobody borrows it. <c>null</c> is the spare one.</summary>
    public static IReadOnlyList<WorkFamily?> Homes { get; } = [WorkFamily.Writing, WorkFamily.Commands, WorkFamily.Research, null];

    private WorkFamily?[] _families = [.. Homes];
    private Dictionary<string, RoomSeat> _seats = new(StringComparer.Ordinal);
    private List<string> _unseated = [];

    /// <summary>What each area is for now.</summary>
    public IReadOnlyList<WorkFamily?> Families => _families;

    public IReadOnlyDictionary<string, RoomSeat> Seats => _seats;

    /// <summary>Agents the room has no desk for.</summary>
    public IReadOnlyList<string> Unseated => _unseated;

    /// <summary>
    /// Seats the agents listed, oldest first, each with the area its work belongs to. An agent
    /// whose work says nothing (<c>null</c>) keeps its desk, or starts in research: agents begin by
    /// reading.
    /// </summary>
    public void Seat(IReadOnlyList<(string Id, WorkFamily? Family)> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var listed = agents.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        var previous = _seats;
        _families = [.. _families];
        _seats = previous.Where(pair => listed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _unseated = [];

        var movers = new List<(string Id, WorkFamily Family)>();
        foreach (var agent in agents)
        {
            if (_seats.TryGetValue(agent.Id, out var seat))
            {
                if (agent.Family is not { } family || _families[seat.Zone] == family)
                {
                    continue;
                }
                _seats.Remove(agent.Id);
                movers.Add((agent.Id, family));
            }
            else
            {
                movers.Add((agent.Id, agent.Family ?? WorkFamily.Research));
            }
        }
        ReturnEmptyZones();

        foreach (var mover in movers)
        {
            if (FreeSeat(mover.Family) is { } seat)
            {
                _seats[mover.Id] = seat;
            }
            else if (previous.TryGetValue(mover.Id, out var old) && !_seats.ContainsValue(old) && listed.Contains(mover.Id))
            {
                // Nowhere to go: it keeps the desk it had, its screen says what it does.
                _seats[mover.Id] = old;
                _families[old.Zone] ??= mover.Family;
            }
            else
            {
                _unseated.Add(mover.Id);
            }
        }
        ReturnEmptyZones();
    }

    /// <summary>How many sit in an area.</summary>
    public int Count(int zone) => _seats.Values.Count(seat => seat.Zone == zone);

    /// <summary>
    /// A desk in an area of this family, or in an empty area that takes the family: the spare
    /// one first.
    /// </summary>
    private RoomSeat? FreeSeat(WorkFamily family)
    {
        for (var zone = 0; zone < _families.Length; zone++)
        {
            if (_families[zone] == family && FreeIndex(zone) is { } index)
            {
                return new RoomSeat(zone, index);
            }
        }
        var empty = Enumerable.Range(0, _families.Length).Where(zone => Count(zone) == 0).ToList();
        int? chosen = empty.Where(zone => Homes[zone] is null).Select(zone => (int?)zone).FirstOrDefault()
                      ?? empty.Select(zone => (int?)zone).FirstOrDefault();
        if (chosen is not { } free)
        {
            return null;
        }
        _families[free] = family;
        return new RoomSeat(free, 0);
    }

    private int? FreeIndex(int zone)
    {
        var taken = _seats.Values.Where(seat => seat.Zone == zone).Select(seat => seat.Index).ToHashSet();
        for (var index = 0; index < SeatsPerZone; index++)
        {
            if (!taken.Contains(index))
            {
                return index;
            }
        }
        return null;
    }

    /// <summary>An area nobody sits in goes back to its own work.</summary>
    private void ReturnEmptyZones()
    {
        for (var zone = 0; zone < _families.Length; zone++)
        {
            if (Count(zone) == 0)
            {
                _families[zone] = Homes[zone];
            }
        }
    }
}

/// <summary>
/// Where things stand in the agent room, in the scene's own units (480 by 270), and the way from
/// one desk to another along its corridors, as Claudy's <c>RoomGeometry</c>.
/// </summary>
public static class RoomGeometry
{
    public static RoomSize Size { get; } = new(480, 270);

    public static IReadOnlyList<RoomPoint> ZoneOrigins { get; } = [new(12, 60), new(344, 60), new(12, 156), new(344, 156)];

    public static RoomSize ZoneSize { get; } = new(124, 84);

    public static RoomSize DeskSize { get; } = new(36, 31);

    public static RoomPoint Door { get; } = new(240, 268);

    /// <summary>The corridor that runs between the upper and the lower areas.</summary>
    public const double Crossing = 152;

    /// <summary>
    /// The corridor along the front wall, from the door to the two side corridors: it keeps a
    /// newcomer out of the lounge.
    /// </summary>
    public const double Entrance = 258;

    /// <summary>
    /// How far below their own coordinates Claudy's desk and the lounge are drawn: both moved down
    /// with the areas, to leave more floor under the back wall.
    /// </summary>
    public const double CentreDrop = 6;

    /// <summary>Units a Claudy walks in a second.</summary>
    public const double Pace = 50;

    /// <summary>The top left corner of a desk.</summary>
    public static RoomPoint Desk(RoomSeat seat)
    {
        var origin = ZoneOrigins[seat.Zone];
        return new RoomPoint(origin.X + 6 + (seat.Index % 3 * 38), origin.Y + 16 + (seat.Index / 3 * 33));
    }

    /// <summary>Where the feet of the Claudy at this desk are.</summary>
    public static RoomPoint Feet(RoomSeat seat)
    {
        var desk = Desk(seat);
        return new RoomPoint(desk.X + 25, desk.Y + 18);
    }

    /// <summary>The corridor beside an area: left areas open to the right, right ones to the left.</summary>
    public static double Corridor(int zone) => ZoneOrigins[zone].X < Size.Width / 2 ? 152 : 330;

    /// <summary>
    /// The aisle behind a desk's row, where a Claudy walks without passing through anyone: above
    /// the first row it runs under the area's sign, above the second between the two rows.
    /// </summary>
    public static double Aisle(RoomSeat seat) => Desk(seat).Y + 1;

    /// <summary>From a desk out to its corridor: back from the chair to the aisle, then along it.</summary>
    private static List<RoomPoint> Exit(RoomSeat seat)
    {
        var feet = Feet(seat);
        var aisle = Aisle(seat);
        return [feet, new RoomPoint(feet.X, aisle), new RoomPoint(Corridor(seat.Zone), aisle)];
    }

    /// <summary>The way to a desk, corner by corner: from another desk, or from the door.</summary>
    public static IReadOnlyList<RoomPoint> Route(RoomSeat? start, RoomSeat end)
    {
        var arrival = Exit(end);
        arrival.Reverse();
        var lane = Corridor(end.Zone);
        if (start is not { } from)
        {
            return [Door, new RoomPoint(Door.X, Entrance), new RoomPoint(lane, Entrance), .. arrival];
        }
        var points = Exit(from);
        var own = Corridor(from.Zone);
        if (own != lane)
        {
            points.Add(new RoomPoint(own, Crossing));
            points.Add(new RoomPoint(lane, Crossing));
        }
        points.AddRange(arrival);
        return points;
    }

    // The lounge

    /// <summary>How many can sit on the sofa.</summary>
    public const int SofaSeats = 4;

    /// <summary>Where the feet of the Claudy sitting at a place of the sofa are.</summary>
    public static RoomPoint Sofa(int slot)
    {
        double[] across = [217, 232, 248, 263];
        return new RoomPoint(across[Math.Clamp(slot, 0, across.Length - 1)], 210 + CentreDrop);
    }

    /// <summary>The strip of rug between the sofa and the low table: the way in and out of the sofa.</summary>
    private const double SofaFront = 218 + CentreDrop;

    /// <summary>From a desk to a place on the sofa: out by the corridor, in by the front of the sofa.</summary>
    public static IReadOnlyList<RoomPoint> RouteToSofa(RoomSeat start, int slot)
    {
        var seat = Sofa(slot);
        var own = Corridor(start.Zone);
        return [.. Exit(start), new RoomPoint(own, SofaFront), new RoomPoint(seat.X, SofaFront), seat];
    }

    /// <summary>From the sofa to the door, round the low table.</summary>
    public static IReadOnlyList<RoomPoint> RouteFromSofa(int slot)
    {
        var seat = Sofa(slot);
        double side = slot < SofaSeats / 2 ? 204 : 276;
        return [seat, new RoomPoint(seat.X, SofaFront), new RoomPoint(side, SofaFront), new RoomPoint(side, Entrance),
                new RoomPoint(Door.X, Entrance), Door];
    }

    /// <summary>From a desk straight to the door: for an agent that is done when the sofa is full.</summary>
    public static IReadOnlyList<RoomPoint> RouteOut(RoomSeat start)
    {
        var own = Corridor(start.Zone);
        return [.. Exit(start), new RoomPoint(own, Entrance), new RoomPoint(Door.X, Entrance), Door];
    }

    public static double Length(IReadOnlyList<RoomPoint> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var length = 0.0;
        for (var index = 1; index < route.Count; index++)
        {
            length += Math.Abs(route[index].X - route[index - 1].X) + Math.Abs(route[index].Y - route[index - 1].Y);
        }
        return length;
    }

    /// <summary>The point <paramref name="distance"/> units along a route, its end past its length.</summary>
    public static RoomPoint Point(IReadOnlyList<RoomPoint> route, double distance)
    {
        ArgumentNullException.ThrowIfNull(route);
        var left = Math.Max(0, distance);
        for (var index = 1; index < route.Count; index++)
        {
            RoomPoint from = route[index - 1], to = route[index];
            var leg = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
            if (left <= leg && leg > 0)
            {
                var part = left / leg;
                return new RoomPoint(from.X + ((to.X - from.X) * part), from.Y + ((to.Y - from.Y) * part));
            }
            left -= leg;
        }
        return route.Count > 0 ? route[^1] : RoomPoint.Zero;
    }
}
