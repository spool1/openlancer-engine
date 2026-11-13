using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Client;
using LibreLancer.Net;
using Xunit;
using Change = LibreLancer.Client.MissionWaypoint.Change;

namespace LibreLancer.Tests;

// The HUD waypoint of a mission objective (OL-0076, OL-0077). The server can spawn the object of an objective in the
// same tick as the objective, so the client does not have the object yet. The client used the route to the system of
// the objective and did not look again. The OpenLancer play of M08 ku04_init showed it. The HUD showed the waypoint at
// the jump hole to the next system (45k). The object of the objective was in the current system (24k). After OL-0076,
// the waypoint took the first point of the object in the client, which is not always its spawn point (OL-0077).
public class MissionWaypointTests
{
    private const string CurrentSystem = "sys_here";
    private const string OtherSystem = "sys_there";
    private static readonly Vector3 JumpPoint = new(-45000, 0, 100);
    private static readonly Vector3 ObjectPoint = new(1200, -50, 24300);

    // The objects in the world of the client, by nickname.
    private readonly Dictionary<string, Vector3> world = new(StringComparer.OrdinalIgnoreCase);
    private readonly MissionWaypoint waypoint;
    private int objectLookups;
    private int routeLookups;

    public MissionWaypointTests()
    {
        waypoint = new MissionWaypoint(WorldPoint, JumpPointToward);
    }

    private Vector3? WorldPoint(string nickname)
    {
        objectLookups++;
        return world.TryGetValue(nickname, out var point) ? point : null;
    }

    // The jump object of the current system toward the other system.
    private Vector3? JumpPointToward(string system)
    {
        routeLookups++;
        return system.Equals(OtherSystem, StringComparison.OrdinalIgnoreCase) ? JumpPoint : null;
    }

    private MissionWaypoint.Choice Choose(NetObjective objective) =>
        MissionWaypoint.Choose(objective, CurrentSystem, WorldPoint, JumpPointToward);

    // One Draw frame of SpaceGameplay: the step of the waypoint rule.
    private Change Frame(NetObjective objective, out Vector3? point) =>
        waypoint.Update(objective, CurrentSystem, out point);

    [Fact]
    public void AnObjectThatSpawnsAfterTheObjectiveGetsTheWaypoint()
    {
        // The retail objective names the next system, and its object spawns in the current system.
        var objective = new NetObjective(1, 2, OtherSystem, "ol_transport");

        Assert.Equal(Change.Make, Frame(objective, out var first));
        Assert.Equal(JumpPoint, first);
        Assert.Equal(Change.None, Frame(objective, out var waiting));
        Assert.Equal(JumpPoint, waiting);

        world["ol_transport"] = ObjectPoint;

        Assert.Equal(Change.Make, Frame(objective, out var afterSpawn));
        Assert.Equal(ObjectPoint, afterSpawn);
        Assert.Equal(Change.None, Frame(objective, out var still));
        Assert.Equal(ObjectPoint, still);
    }

    [Fact]
    public void TheWaypointFollowsTheObjectAfterItsFirstPoint()
    {
        // The M08 ku04_init boots of OL-0076: the first frame with the object gave a point 28k from the player. The next
        // frame gave its spawn point, 24k from the player. The waypoint stayed at the first point.
        var objective = new NetObjective(1, 2, OtherSystem, "ol_transport");
        var firstPoint = new Vector3(-32038, 45.6f, -23611);
        var spawnPoint = new Vector3(-35122, 50, -25884);
        Assert.Equal(Change.Make, Frame(objective, out _));

        world["ol_transport"] = firstPoint;
        Assert.Equal(Change.Make, Frame(objective, out var first));
        Assert.Equal(firstPoint, first);

        world["ol_transport"] = spawnPoint;
        Assert.Equal(Change.Move, Frame(objective, out var spawned));
        Assert.Equal(spawnPoint, spawned);

        // The object flies on, and the waypoint stays on it.
        var later = spawnPoint + new Vector3(80, 0, 0);
        world["ol_transport"] = later;
        Assert.Equal(Change.Move, Frame(objective, out var moved));
        Assert.Equal(later, moved);
        Assert.Equal(Change.None, Frame(objective, out var stays));
        Assert.Equal(later, stays);
    }

    [Fact]
    public void AnObjectThatLeavesTheWorldGivesTheChoiceWithoutTheObject()
    {
        var objective = new NetObjective(1, 2, OtherSystem, "ol_transport");
        world["ol_transport"] = ObjectPoint;
        Assert.Equal(Change.Make, Frame(objective, out var onObject));
        Assert.Equal(ObjectPoint, onObject);

        world.Remove("ol_transport");
        Assert.Equal(Change.Make, Frame(objective, out var route));
        Assert.Equal(JumpPoint, route);
        Assert.Equal(Change.None, Frame(objective, out _));

        world["ol_transport"] = ObjectPoint;
        Assert.Equal(Change.Make, Frame(objective, out var back));
        Assert.Equal(ObjectPoint, back);
    }

    [Fact]
    public void AnObjectiveOfTheCurrentSystemShowsTheWaypointWhenItsObjectSpawns()
    {
        var objective = new NetObjective(1, 2, CurrentSystem, "ol_transport");

        Assert.Equal(Change.Make, Frame(objective, out var first));
        Assert.Null(first);
        Assert.Equal(Change.None, Frame(objective, out _));

        world["ol_transport"] = ObjectPoint;

        Assert.Equal(Change.Make, Frame(objective, out var afterSpawn));
        Assert.Equal(ObjectPoint, afterSpawn);
    }

    [Fact]
    public void AnObjectiveThatNamesAnObjectDoesOneLookupInEachFrame()
    {
        var objective = new NetObjective(1, 2, OtherSystem, "ol_transport");
        Frame(objective, out _);
        (objectLookups, routeLookups) = (0, 0);

        Frame(objective, out _);
        Frame(objective, out _);
        Assert.Equal((2, 0), (objectLookups, routeLookups));

        world["ol_transport"] = ObjectPoint;
        Frame(objective, out _);
        Frame(objective, out _);
        Assert.Equal((4, 0), (objectLookups, routeLookups));
    }

    [Fact]
    public void ANavMarkerDoesNoLookupAfterItsFirstFrame()
    {
        var objective = new NetObjective(1, 2, OtherSystem, new Vector3(500, 10, -800));
        Assert.Equal(Change.Make, Frame(objective, out var route));
        Assert.Equal(JumpPoint, route);
        (objectLookups, routeLookups) = (0, 0);

        Assert.Equal(Change.None, Frame(objective, out var stays));
        Assert.Equal(JumpPoint, stays);
        Assert.Equal((0, 0), (objectLookups, routeLookups));
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
        Assert.Equal(Change.Make, Frame(objective, out var point));
        Assert.Null(point);
        Assert.Equal(Change.None, Frame(objective, out _));

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
        Assert.Equal(Change.Make, Frame(objective, out var point));
        Assert.Equal(Vector3.Zero, point);
    }

    [Fact]
    public void ANewObjectiveMakesTheWaypointAgain()
    {
        world["ol_transport"] = ObjectPoint;
        var marker = new Vector3(500, 10, -800);

        Assert.Equal(Change.Make, Frame(new NetObjective(1, 2, CurrentSystem, "ol_transport"), out _));
        Assert.Equal(Change.Make, Frame(new NetObjective(3, 4, CurrentSystem, marker), out var point));
        Assert.Equal(marker, point);
        Assert.Equal(Change.None, Frame(new NetObjective(3, 4, CurrentSystem, marker), out _));
    }

    [Fact]
    public void ANewObjectiveWithTheSameTextMakesTheWaypointAgain()
    {
        // A mission can give a new objective with the same text and another object or marker point.
        var escortPoint = new Vector3(-3000, 20, 9000);
        world["ol_transport"] = ObjectPoint;
        world["ol_escort"] = escortPoint;
        var marker = new Vector3(500, 10, -800);
        var otherMarker = new Vector3(-700, 0, 1500);

        Assert.Equal(Change.Make, Frame(new NetObjective(1, 2, CurrentSystem, "ol_transport"), out _));
        Assert.Equal(Change.Make, Frame(new NetObjective(1, 2, CurrentSystem, "ol_escort"), out var escort));
        Assert.Equal(escortPoint, escort);

        Assert.Equal(Change.Make, Frame(new NetObjective(1, 2, CurrentSystem, marker), out _));
        Assert.Equal(Change.Make, Frame(new NetObjective(1, 2, CurrentSystem, otherMarker), out var moved));
        Assert.Equal(otherMarker, moved);
    }

    [Fact]
    public void AnObjectiveWithoutAPointHasNoWaypoint()
    {
        Assert.Equal(new MissionWaypoint.Choice(null, false), Choose(new NetObjective(12345)));
        Assert.Equal(new MissionWaypoint.Choice(null, false), Choose(new NetObjective()));
        Assert.Equal(Change.Make, Frame(new NetObjective(12345), out var point));
        Assert.Null(point);
        Assert.Equal(Change.None, Frame(new NetObjective(12345), out _));
    }
}
