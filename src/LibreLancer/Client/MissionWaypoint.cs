using System;
using System.Numerics;
using LibreLancer.Net;

namespace LibreLancer.Client;

// The rule for the HUD waypoint of a mission objective (OL-0076, OL-0077). The server can spawn the object of an
// objective in the same tick as the objective, so the client does not have the object yet. The waypoint then shows the
// route toward the system of the objective. The first point of a new object in the client is not always its spawn
// point, and the object can move. So the waypoint follows the object in each frame while the objective names it.
internal sealed class MissionWaypoint(Func<string, Vector3?> objectPoint, Func<string, Vector3?> jumpPointToward)
{
    // The point of the waypoint, or null for no waypoint. WaitsForObject is true while the objective names an object
    // that is not in the world of the client.
    internal readonly record struct Choice(Vector3? Point, bool WaitsForObject);

    // What the waypoint must do after the step of a frame.
    internal enum Change
    {
        // The waypoint stays as it is.
        None,

        // The waypoint stays and goes to the new point. The waypoint follows the object of the objective.
        Move,

        // The old waypoint goes. A new waypoint comes at the new point, or no waypoint comes for no point.
        Make,
    }

    // The objective of the current waypoint, its point, and if that point is the point of the object of the objective.
    private NetObjective? shown;
    private Vector3? shownPoint;
    private bool onObject;

    // Chooses the waypoint of the objective. objectPoint gives the point of a world object by its nickname.
    // jumpPointToward gives the point of the jump object of the current system on the route to a system.
    // An object in the world wins. Without it, a NavMarker of the current system gives its marker point, and an
    // objective of another system gives the route point. An object of the current system has no point until it spawns.
    // A NavMarker at the origin of the current system has no point. Retail gives that point to an objective without a
    // place.
    internal static Choice Choose(
        NetObjective objective,
        string currentSystem,
        Func<string, Vector3?> objectPoint,
        Func<string, Vector3?> jumpPointToward)
    {
        if (objective.Kind != ObjectiveKind.Object && objective.Kind != ObjectiveKind.NavMarker)
            return new Choice(null, false);

        var waitsForObject = false;
        if (NamesObject(objective))
        {
            if (objectPoint(objective.Object!) is { } found)
                return new Choice(found, false);
            waitsForObject = true;
        }

        var objectiveSystem = objective.System;
        if (string.IsNullOrWhiteSpace(objectiveSystem))
            return new Choice(null, waitsForObject);

        if (currentSystem.Equals(objectiveSystem, StringComparison.OrdinalIgnoreCase))
        {
            if (objective.Kind == ObjectiveKind.Object)
                return new Choice(null, waitsForObject);
            return objective.Position == Vector3.Zero
                ? new Choice(null, false)
                : new Choice(objective.Position, false);
        }

        return new Choice(jumpPointToward(objectiveSystem), waitsForObject);
    }

    // The step of each frame. point is the point of the waypoint after the step, or null for no waypoint.
    // A new objective makes the waypoint again. While the objective names an object, the step does one lookup of the
    // object. The waypoint then follows the object, and makes the choice without the object again when the object
    // leaves the world. For other objectives, the step does no lookup.
    internal Change Update(NetObjective objective, string currentSystem, out Vector3? point)
    {
        if (shown is not { } last || !last.Equals(objective))
        {
            var choice = Choose(objective, currentSystem, objectPoint, jumpPointToward);
            return Show(objective, choice.Point, NamesObject(objective) && !choice.WaitsForObject, out point);
        }

        point = shownPoint;
        if (!NamesObject(objective))
            return Change.None;

        if (objectPoint(objective.Object!) is not { } found)
        {
            if (!onObject)
                return Change.None;
            var without = Choose(objective, currentSystem, objectPoint, jumpPointToward);
            return Show(objective, without.Point, false, out point);
        }

        if (!onObject)
            return Show(objective, found, true, out point);
        if (found == shownPoint)
            return Change.None;
        point = shownPoint = found;
        return Change.Move;
    }

    // Keeps the objective and the point of a new waypoint for the next step.
    private Change Show(NetObjective objective, Vector3? point, bool ofObject, out Vector3? shownNow)
    {
        shown = objective;
        shownNow = shownPoint = point;
        onObject = ofObject;
        return Change.Make;
    }

    private static bool NamesObject(NetObjective objective) =>
        objective.Kind == ObjectiveKind.Object && !string.IsNullOrWhiteSpace(objective.Object);
}
