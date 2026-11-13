using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Client;
using LibreLancer.Net;
using Xunit;

namespace LibreLancer.Tests;

// The HUD waypoint of a mission objective (OL-0076). The server can spawn the object of an objective in the same tick
// as the objective, so the client does not have the object yet. The client used the route to the system of the
// objective and did not look again. The OpenLancer play of M08 ku04_init showed it. The HUD showed the waypoint at the
// jump hole to the next system (45k). The object of the objective was in the current system (24k).
public class MissionWaypointTests
{
    private const string CurrentSystem = "sys_here";
    private const string OtherSystem = "sys_there";
    private static readonly Vector3 JumpPoint = new(-45000, 0, 100);
    private static readonly Vector3 ObjectPoint = new(1200, -50, 24300);

    // The objects in the world of the client, by nickname.
    private readonly Dictionary<string, Vector3> world = new(StringComparer.OrdinalIgnoreCase);
    private readonly MissionWaypoint waypoint;

    public MissionWaypointTests()
    {
        waypoint = new MissionWaypoint(WorldPoint, JumpPointToward);
    }

    private Vector3? WorldPoint(string nickname) => world.TryGetValue(nickname, out var point) ? point : null;

    // The jump object of the current system toward the other system.
    private static Vector3? JumpPointToward(string system) =>
        system.Equals(OtherSystem, StringComparison.OrdinalIgnoreCase) ? JumpPoint : null;

    private MissionWaypoint.Choice Choose(NetObjective objective) =>
        MissionWaypoint.Choose(objective, CurrentSystem, WorldPoint, JumpPointToward);

    // One Draw frame of SpaceGameplay: the check, then a new waypoint when the check tells that it must move.
    private bool Frame(NetObjective objective, out Vector3? point)
    {
        point = null;
        if (!waypoint.MustMove(objective))
            return false;
        point = waypoint.Move(objective, CurrentSystem);
        return true;
    }

    [Fact]
    public void AnObjectThatSpawnsAfterTheObjectiveGetsTheWaypoint()
    {
        // The retail objective names the next system, and its object spawns in the current system.
        var objective = new NetObjective(1, 2, OtherSystem, "ol_transport");

        Assert.True(Frame(objective, out var first));
        Assert.Equal(JumpPoint, first);
        Assert.False(Frame(objective, out _));

        world["ol_transport"] = ObjectPoint;

        Assert.True(Frame(objective, out var afterSpawn));
        Assert.Equal(ObjectPoint, afterSpawn);
        Assert.False(Frame(objective, out _));
    }

    [Fact]
    public void AnObjectiveOfTheCurrentSystemShowsTheWaypointWhenItsObjectSpawns()
    {
        var objective = new NetObjective(1, 2, CurrentSystem, "ol_transport");

        Assert.True(Frame(objective, out var first));
        Assert.Null(first);
        Assert.False(Frame(objective, out _));

        world["ol_transport"] = ObjectPoint;

        Assert.True(Frame(objective, out var afterSpawn));
        Assert.Equal(ObjectPoint, afterSpawn);
    }

    [Fact]
    public void AnObjectInTheWorldGivesItsPointAndDoesNotWait()
    {
        world["ol_transport"] = ObjectPoint;

        Assert.Equal(new MissionWaypoint.Choice(ObjectPoint, false),
            Choose(new NetObjective(1, 2, OtherSystem, "OL_Transport")));
    }

    [Fact]
    public void AnObjectNotInTheWorldOfAnotherSystemGivesTheRouteAndWaits()
    {
        Assert.Equal(new MissionWaypoint.Choice(JumpPoint, true),
            Choose(new NetObjective(1, 2, OtherSystem, "ol_transport")));
    }

    [Fact]
    public void AnObjectNotInTheWorldOfTheCurrentSystemGivesNoPointAndWaits()
    {
        Assert.Equal(new MissionWaypoint.Choice(null, true),
            Choose(new NetObjective(1, 2, CurrentSystem, "ol_transport")));
    }

    [Fact]
    public void ANavMarkerOfTheCurrentSystemGivesTheMarkerPoint()
    {
        var marker = new Vector3(500, 10, -800);

        Assert.Equal(new MissionWaypoint.Choice(marker, false),
            Choose(new NetObjective(1, 2, CurrentSystem, marker)));
    }

    [Fact]
    public void ANavMarkerAtTheOriginOfTheCurrentSystemHasNoWaypoint()
    {
        // Retail gives an objective without a place a NavMarker at the origin, for example the defend objectives of M04.
        var objective = new NetObjective(1, 2, CurrentSystem, Vector3.Zero);

        Assert.Equal(new MissionWaypoint.Choice(null, false), Choose(objective));
        Assert.True(Frame(objective, out var point));
        Assert.Null(point);
        Assert.False(Frame(objective, out _));

        // The marker point does not apply in another system, so the route stays.
        Assert.Equal(new MissionWaypoint.Choice(JumpPoint, false),
            Choose(new NetObjective(1, 2, OtherSystem, Vector3.Zero)));
    }

    [Fact]
    public void TheSystemOfTheObjectiveMatchesTheCurrentSystemInAnyCase()
    {
        var marker = new Vector3(500, 10, -800);

        Assert.Equal(new MissionWaypoint.Choice(marker, false),
            Choose(new NetObjective(1, 2, CurrentSystem.ToUpperInvariant(), marker)));
    }

    [Fact]
    public void ANavMarkerOfAnotherSystemGivesTheRouteAndDoesNotWait()
    {
        Assert.Equal(new MissionWaypoint.Choice(JumpPoint, false),
            Choose(new NetObjective(1, 2, OtherSystem, new Vector3(500, 10, -800))));
    }

    [Fact]
    public void AnObjectAtTheOriginGetsItsWaypoint()
    {
        world["ol_transport"] = Vector3.Zero;
        var objective = new NetObjective(1, 2, CurrentSystem, "ol_transport");

        Assert.Equal(new MissionWaypoint.Choice(Vector3.Zero, false), Choose(objective));
        Assert.True(Frame(objective, out var point));
        Assert.Equal(Vector3.Zero, point);
    }

    [Fact]
    public void ANewObjectiveMovesTheWaypoint()
    {
        world["ol_transport"] = ObjectPoint;
        var marker = new Vector3(500, 10, -800);

        Assert.True(Frame(new NetObjective(1, 2, CurrentSystem, "ol_transport"), out _));
        Assert.True(Frame(new NetObjective(3, 4, CurrentSystem, marker), out var point));
        Assert.Equal(marker, point);
        Assert.False(Frame(new NetObjective(3, 4, CurrentSystem, marker), out _));
    }

    [Fact]
    public void ANewObjectiveWithTheSameTextMovesTheWaypoint()
    {
        // A mission can give a new objective with the same text and another object or marker point.
        var escortPoint = new Vector3(-3000, 20, 9000);
        world["ol_transport"] = ObjectPoint;
        world["ol_escort"] = escortPoint;
        var marker = new Vector3(500, 10, -800);
        var otherMarker = new Vector3(-700, 0, 1500);

        Assert.True(Frame(new NetObjective(1, 2, CurrentSystem, "ol_transport"), out _));
        Assert.True(Frame(new NetObjective(1, 2, CurrentSystem, "ol_escort"), out var escort));
        Assert.Equal(escortPoint, escort);

        Assert.True(Frame(new NetObjective(1, 2, CurrentSystem, marker), out _));
        Assert.True(Frame(new NetObjective(1, 2, CurrentSystem, otherMarker), out var moved));
        Assert.Equal(otherMarker, moved);
    }

    [Fact]
    public void AnObjectiveWithoutAPointHasNoWaypoint()
    {
        Assert.Equal(new MissionWaypoint.Choice(null, false), Choose(new NetObjective(12345)));
        Assert.Equal(new MissionWaypoint.Choice(null, false), Choose(new NetObjective()));
    }
}
