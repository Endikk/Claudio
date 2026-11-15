using Claudio.Core.Models;

namespace Claudio.Core.Tests;

/// <summary>The robot vacuum of the agent room: where it is, from the time alone.</summary>
public sealed class RoomVacuumTests
{
    [Fact]
    public void ARoundLeavesTheDockAndComesBackToIt()
    {
        Assert.Equal(RoomVacuum.Dock, RoomVacuum.Round[0]);
        Assert.Equal(RoomVacuum.Dock, RoomVacuum.Round[^1]);
        Assert.Equal(RoomVacuum.Dock, RoomVacuum.StateAt(0).Position);
    }

    [Fact]
    public void ItTurnsAtRightAngles()
    {
        for (var index = 1; index < RoomVacuum.Round.Count; index++)
        {
            RoomPoint from = RoomVacuum.Round[index - 1], to = RoomVacuum.Round[index];
            Assert.True(from.X == to.X || from.Y == to.Y, $"{from} to {to} is a diagonal");
        }
    }

    [Fact]
    public void ItIsOutDuringARoundAndFacesWhereItGoes()
    {
        var leaving = RoomVacuum.StateAt(0.5);
        Assert.False(leaving.IsDocked);
        Assert.Equal(new RoomVector(0, 1), leaving.Heading);
        Assert.Equal(RoomVacuum.Dock.Y + (0.5 * RoomVacuum.Pace), leaving.Position.Y, 0.001);

        var underTheWall = RoomVacuum.StateAt(5);
        Assert.Equal(new RoomVector(-1, 0), underTheWall.Heading);
    }

    [Fact]
    public void ItChargesOnItsDockBetweenTwoRounds()
    {
        var charging = RoomVacuum.StateAt(RoomVacuum.TravelTime + 1);
        Assert.True(charging.IsDocked);
        Assert.Equal(RoomVacuum.Dock, charging.Position);
        Assert.False(RoomVacuum.StateAt(RoomVacuum.Period + 1).IsDocked);
    }

    [Fact]
    public void ItsTrailStartsUnderItAndFollowsTheCornersItTook()
    {
        const double Time = 3;
        var trail = RoomVacuum.Trail(Time);
        Assert.Equal(RoomVacuum.StateAt(Time).Position, trail[0]);
        // Three seconds in, it has left the dock and turned left under the wall: both legs show.
        Assert.Contains(trail, point => point.X == RoomVacuum.Dock.X && point.Y < 56);
        Assert.Contains(trail, point => point.Y == 56 && point.X < RoomVacuum.Dock.X);
        Assert.Empty(RoomVacuum.Trail(RoomVacuum.TravelTime + 1));
    }

    [Fact]
    public void ItStaysClearOfTheDesks()
    {
        static bool InArea(RoomPoint origin, RoomPoint position) =>
            position.X >= origin.X - 3 && position.X < origin.X + RoomGeometry.ZoneSize.Width + 3
            && position.Y >= origin.Y - 3 && position.Y < origin.Y + RoomGeometry.ZoneSize.Height + 3;

        for (var time = 0.0; time < RoomVacuum.TravelTime; time += 0.5)
        {
            var position = RoomVacuum.StateAt(time).Position;
            Assert.False(RoomGeometry.ZoneOrigins.Any(origin => InArea(origin, position)), $"in an area at {position}");
        }
    }
}
