using System;
using System.Numerics;
using LibreLancer.Net;

namespace LibreLancer.Client;

// The rule for the HUD waypoint of a mission objective (OL-0076). The server can spawn the object of an objective in
// the same tick as the objective, so the client does not have the object yet. The waypoint then shows the route
// toward the system of the objective. The check of each frame moves the waypoint to the object when the object spawns.
internal sealed class MissionWaypoint(Func<string, Vector3?> objectPoint, Func<string, Vector3?> jumpPointToward)
{
    // The point of the waypoint, or null for no waypoint. WaitsForObject is true while the objective names an object
    // that is not in the world of the client.
    internal readonly record struct Choice(Vector3? Point, bool WaitsForObject);

    // The objective of the current waypoint, and if its choice waits for the object.
    private NetObjective? shown;
    private bool waits;

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
        if (objective.Kind == ObjectiveKind.Object && !string.IsNullOrWhiteSpace(objective.Object))
        {
            if (objectPoint(objective.Object) is { } found)
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

    // The check of each frame. It returns true when the waypoint must move. That occurs when the objective changes, or
    // when the object that the waypoint waits for is now in the world. The check does one lookup of the object while
    // the waypoint waits, and no lookup at other times.
    internal bool MustMove(NetObjective objective)
    {
        if (shown is not { } last || !last.Equals(objective))
            return true;
        return waits && objectPoint(objective.Object!) != null;
    }

    // Chooses the waypoint of the objective and keeps the objective and the wait state for the next check.
    internal Vector3? Move(NetObjective objective, string currentSystem)
    {
        var choice = Choose(objective, currentSystem, objectPoint, jumpPointToward);
        shown = objective;
        waits = choice.WaitsForObject;
        return choice.Point;
    }
}
