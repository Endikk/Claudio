using Claudio.Core.Models;

namespace Claudio.Core.Tests;

/// <summary>Who sits where in the agent room, and the way between two desks.</summary>
public sealed class RoomPlanTests
{
    private static List<(string Id, WorkFamily? Family)> Agents(IEnumerable<WorkFamily?> families, string prefix = "a") =>
        families.Select((family, offset) => ($"{prefix}{offset}", family)).ToList();

    private static List<(string Id, WorkFamily? Family)> Repeat(WorkFamily family, int count, string prefix = "a") =>
        Agents(Enumerable.Repeat<WorkFamily?>(family, count), prefix);

    private static RoomSeat? SeatOf(RoomPlan plan, string id) => plan.Seats.TryGetValue(id, out var seat) ? seat : null;

    private static void AssertRightAngles(IReadOnlyList<RoomPoint> route)
    {
        for (var index = 1; index < route.Count; index++)
        {
            RoomPoint a = route[index - 1], b = route[index];
            Assert.True(a.X == b.X || a.Y == b.Y, $"{a} to {b} is a diagonal");
        }
    }

    [Fact]
    public void AnAgentSitsInTheAreaOfItsWork()
    {
        var plan = new RoomPlan();
        plan.Seat(Agents([WorkFamily.Writing, WorkFamily.Commands, WorkFamily.Research]));
        Assert.Equal(new RoomSeat(0, 0), SeatOf(plan, "a0"));
        Assert.Equal(new RoomSeat(1, 0), SeatOf(plan, "a1"));
        Assert.Equal(new RoomSeat(2, 0), SeatOf(plan, "a2"));
        Assert.Equal(RoomPlan.Homes, plan.Families);
    }

    [Fact]
    public void AnAgentKeepsItsDeskFromOnePassToTheNext()
    {
        var plan = new RoomPlan();
        plan.Seat(Repeat(WorkFamily.Writing, 3));
        plan.Seat([("a2", WorkFamily.Writing), ("a0", WorkFamily.Writing)]);
        Assert.Equal(new RoomSeat(0, 2), SeatOf(plan, "a2"));
        Assert.Equal(new RoomSeat(0, 0), SeatOf(plan, "a0"));
        Assert.Null(SeatOf(plan, "a1"));
    }

    [Fact]
    public void AnAgentWhoseWorkChangesMovesToTheOtherArea()
    {
        var plan = new RoomPlan();
        plan.Seat([("a", WorkFamily.Research)]);
        plan.Seat([("a", WorkFamily.Writing)]);
        Assert.Equal(new RoomSeat(0, 0), SeatOf(plan, "a"));
    }

    [Fact]
    public void AnAgentWhoseWorkSaysNothingStaysWhereItIs()
    {
        var plan = new RoomPlan();
        plan.Seat([("a", WorkFamily.Commands)]);
        plan.Seat([("a", null)]);
        Assert.Equal(new RoomSeat(1, 0), SeatOf(plan, "a"));
    }

    [Fact]
    public void ANewAgentWithNoWorkYetStartsInResearch()
    {
        var plan = new RoomPlan();
        plan.Seat([("a", null)]);
        Assert.Equal(2, SeatOf(plan, "a")?.Zone);
    }

    [Fact]
    public void AFullAreaSpillsIntoTheSpareOne()
    {
        var plan = new RoomPlan();
        plan.Seat(Repeat(WorkFamily.Commands, 7));
        Assert.Equal(6, plan.Count(1));
        Assert.Equal(new RoomSeat(3, 0), SeatOf(plan, "a6"));
        Assert.Equal(WorkFamily.Commands, plan.Families[3]);
    }

    [Fact]
    public void TheSpareAreaIsFreeAgainOnceEmpty()
    {
        var plan = new RoomPlan();
        plan.Seat(Repeat(WorkFamily.Commands, 7));
        plan.Seat(Repeat(WorkFamily.Commands, 6));
        Assert.Null(plan.Families[3]);
    }

    [Fact]
    public void WithTheSpareAreaTakenAnEmptyAreaIsBorrowed()
    {
        var plan = new RoomPlan();
        plan.Seat(Repeat(WorkFamily.Commands, 13));
        Assert.Equal(6, plan.Count(1));
        Assert.Equal(6, plan.Count(3));
        Assert.Equal(0, SeatOf(plan, "a12")?.Zone);
        Assert.Equal(WorkFamily.Commands, plan.Families[0]);
    }

    [Fact]
    public void AnAgentTheRoomHasNoDeskForIsCountedApart()
    {
        var plan = new RoomPlan();
        plan.Seat(Repeat(WorkFamily.Commands, 25));
        Assert.Equal(24, plan.Seats.Count);
        Assert.Equal(["a24"], plan.Unseated);
    }

    [Fact]
    public void AnAgentWithNowhereToGoKeepsItsDesk()
    {
        var plan = new RoomPlan();
        var everyone = Repeat(WorkFamily.Commands, 18);
        everyone.AddRange(Repeat(WorkFamily.Writing, 6, "w"));
        plan.Seat(everyone);
        var before = SeatOf(plan, "w0");
        everyone[18] = ("w0", WorkFamily.Commands);
        plan.Seat(everyone);
        Assert.Equal(before, SeatOf(plan, "w0"));
        Assert.Empty(plan.Unseated);
    }

    // Routes

    [Fact]
    public void ARouteStartsAndEndsAtTheDesksAndTurnsAtRightAngles()
    {
        RoomSeat from = new(0, 2), to = new(3, 0);
        var route = RoomGeometry.Route(from, to);
        Assert.Equal(RoomGeometry.Feet(from), route[0]);
        Assert.Equal(RoomGeometry.Feet(to), route[^1]);
        AssertRightAngles(route);
    }

    [Fact]
    public void AWalkerLeavesItsDeskByTheAisleBehindIt()
    {
        var seat = new RoomSeat(0, 1);
        var route = RoomGeometry.Route(seat, new RoomSeat(1, 4));
        Assert.Equal(new RoomPoint(RoomGeometry.Feet(seat).X, RoomGeometry.Aisle(seat)), route[1]);
        // Along the aisle it is behind its row's chairs and in front of nobody's desk.
        Assert.True(RoomGeometry.Aisle(seat) < RoomGeometry.Desk(seat).Y + 2.5);
    }

    [Fact]
    public void TheWaysToTheSofaAndOutTurnAtRightAngles()
    {
        IReadOnlyList<RoomPoint>[] routes =
        [
            RoomGeometry.RouteToSofa(new RoomSeat(1, 5), 3), RoomGeometry.RouteFromSofa(0),
            RoomGeometry.RouteFromSofa(3), RoomGeometry.RouteOut(new RoomSeat(2, 0)),
        ];
        foreach (var route in routes)
        {
            AssertRightAngles(route);
        }
        Assert.Equal(RoomGeometry.Sofa(3), routes[0][^1]);
        Assert.Equal(RoomGeometry.Door, routes[1][^1]);
        Assert.Equal(RoomGeometry.Door, routes[3][^1]);
    }

    [Fact]
    public void ANewcomerComesInByTheDoor()
    {
        var route = RoomGeometry.Route(null, new RoomSeat(2, 4));
        Assert.Equal(RoomGeometry.Door, route[0]);
        Assert.Equal(RoomGeometry.Feet(new RoomSeat(2, 4)), route[^1]);
    }

    [Fact]
    public void TwoAreasOnTheSameSideShareTheirCorridor()
    {
        var route = RoomGeometry.Route(new RoomSeat(2, 2), new RoomSeat(0, 5));
        Assert.All(route, point => Assert.NotEqual(RoomGeometry.Crossing, point.Y));
    }

    [Fact]
    public void APointAlongARoute()
    {
        RoomPoint[] route = [new(0, 0), new(10, 0), new(10, 10)];
        Assert.Equal(20, RoomGeometry.Length(route));
        Assert.Equal(new RoomPoint(5, 0), RoomGeometry.Point(route, 5));
        Assert.Equal(new RoomPoint(10, 5), RoomGeometry.Point(route, 15));
        Assert.Equal(new RoomPoint(10, 10), RoomGeometry.Point(route, 99));
    }
}
