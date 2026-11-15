namespace Claudio.Core.Models;

/// <summary>
/// The robot vacuum of the agent room, as Claudy's <c>RoomVacuum</c>: it leaves its dock by the
/// back wall, goes round every corridor of the room, comes back to charge, and starts again. Where
/// it is depends on the time alone, so nothing has to remember it. Times are in seconds.
/// </summary>
public static class RoomVacuum
{
    /// <summary>Where it charges, against the back wall between the board and the clock.</summary>
    public static RoomPoint Dock { get; } = new(333, 49);

    /// <summary>Units it covers in a second.</summary>
    public const double Pace = 9;

    /// <summary>How long it stays on its dock between two rounds, in seconds.</summary>
    public const double Charge = 25;

    /// <summary>
    /// One round, corner by corner, from the dock and back to it: under the back wall, down the
    /// left corridor, along the front wall and back across the lounge rug's edge, up the right
    /// corridor, across the room in front of Claudy's desk, and between that desk and the lounge.
    /// </summary>
    public static IReadOnlyList<RoomPoint> Round { get; } =
    [
        Dock,
        new(333, 56), new(146, 56), new(146, 254), new(44, 254),
        new(44, 246), new(436, 246), new(436, 254), new(333, 254),
        new(333, 152), new(160, 152), new(160, 166), new(320, 166),
        new(320, 178), new(333, 178), new(333, 56),
        Dock,
    ];

    /// <summary>Where the vacuum is and which way it faces.</summary>
    /// <param name="Heading">The way it faces: one of the four directions, as a unit step.</param>
    public readonly record struct State(RoomPoint Position, RoomVector Heading, bool IsDocked);

    public static double TravelTime => RoomGeometry.Length(Round) / Pace;

    public static double Period => TravelTime + Charge;

    /// <summary>Seconds into the current round, the charge included.</summary>
    private static double Elapsed(double time)
    {
        var moment = time % Period;
        return moment < 0 ? moment + Period : moment;
    }

    /// <summary>
    /// The floor it went over in the last <paramref name="length"/> units, newest point first,
    /// following the corners it took. Empty on the dock.
    /// </summary>
    public static IReadOnlyList<RoomPoint> Trail(double time, double length = 44, double spacing = 2)
    {
        var elapsed = Elapsed(time);
        if (elapsed >= TravelTime)
        {
            return [];
        }
        var head = elapsed * Pace;
        var end = Math.Max(0, head - length);
        var points = new List<RoomPoint>();
        for (var step = 0; head - (step * spacing) >= end; step++)
        {
            points.Add(RoomGeometry.Point(Round, head - (step * spacing)));
        }
        return points;
    }

    /// <summary>Where the vacuum is <paramref name="time"/> seconds into its day. Claudy's <c>state(at:)</c>.</summary>
    public static State StateAt(double time)
    {
        var docked = new State(Dock, new RoomVector(0, 1), true);
        var elapsed = Elapsed(time);
        if (elapsed >= TravelTime)
        {
            return docked;
        }
        var distance = elapsed * Pace;
        var left = distance;
        for (var index = 1; index < Round.Count; index++)
        {
            RoomPoint from = Round[index - 1], to = Round[index];
            var leg = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
            if (left <= leg && leg > 0)
            {
                var heading = new RoomVector((to.X - from.X) / leg, (to.Y - from.Y) / leg);
                return new State(RoomGeometry.Point(Round, distance), heading, false);
            }
            left -= leg;
        }
        return docked;
    }
}
