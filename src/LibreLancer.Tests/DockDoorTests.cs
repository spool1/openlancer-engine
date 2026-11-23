using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using LibreLancer.Data.GameData.World;
using DockSphereType = LibreLancer.Data.Schema.Solar.DockSphereType;
using LibreLancer.Server;
using LibreLancer.Server.Components;
using LibreLancer.Utf.Cmp;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The dock door of a base stays open while a ship that docks there flies in (OL-0079). Before, the door closed 10
// seconds after it opened. The autopilot flies the last part of the dock path slowly, so a ship often came to the
// closed door and stopped at the hull. The door now stays open while the ship is near a point of its dock path, and it
// closes 10 seconds after that.
public class DockDoorTests
{
    // A station at the origin with one berth. The dock path goes from the outer point through the inner point to the
    // dock mount, along the Z axis. The ship has no physics body, so the dock component gives it a radius of 15.
    private static readonly Vector3 Mount = new(0, 0, 50);
    private static readonly Vector3 Inner = new(0, 0, 110);
    private static readonly Vector3 Outer = new(0, 0, 280);

    // The door opens when the ship is nearer than this to a point of its dock path: 100 plus the radius of the ship.
    private const float OpenRange = 115;

    // The ship docks when it is nearer than this to the dock mount: the radius 4 of the berth plus the radius 15.
    private const float DockRange = 19;

    private const float Step = 0.1f;

    // A slow last approach, as a large ship flies it. At this speed, the ship needs about 40 seconds from the moment
    // that the door opens to the dock mount.
    private const float Speed = 8;

    private sealed class Station : IDisposable
    {
        private readonly GameWorld world = new(null, null, null, null, initPhys: false);
        private readonly GameObject ship = new() { Nickname = "ol_ship" };
        private readonly SDockableComponent dock;

        public Station()
        {
            // The dock component tells the players when the docking lights change. A server world without players is
            // enough for that, so the test makes it without a star system and sets only the fields that the docking
            // lights use.
            var server = (ServerWorld)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorld));
            server.Players = new Dictionary<Player, GameObject>();
            typeof(ServerWorld).GetField("dockingLights", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(server, new HashSet<GameObject>());
            world.Server = server;

            var root = new RigidModelPart { Name = "Root" };
            foreach (var (name, position) in new[]
                     { ("HpDockMountA", Mount), ("HpDockPointA01", Inner), ("HpDockPointA02", Outer) })
            {
                root.Hardpoints.Add(new Hardpoint(new FixedHardpointDefinition(name) { Position = position }, root));
            }

            var model = new RigidModel
            {
                Root = root, AllParts = [root], Parts = new(), Source = RigidModelSource.SinglePart
            };
            var station = new GameObject { Nickname = "ol_station", Model = new DestructibleModel(model, []) };
            station.SetTransform(Transform3D.Identity);
            dock = new SDockableComponent(station, new DockAction { Kind = DockKinds.Base, Target = "ol_base" },
                [new DockSphere { Type = DockSphereType.berth, Hardpoint = "HpDockMountA", Radius = 4, Script = "Sc_open dock1" }]);
            station.AddComponent(dock);
            // The world adds and registers each object as it does for the objects of a system. A ship that is not
            // registered does not exist for the dock component.
            world.AddObject(station);
            station.Register(world);
            world.AddObject(ship);
            ship.Register(world);
        }

        public DockingPoint Door => dock.DockPoints[0];

        public void MoveShip(float z) => ship.SetTransform(new Transform3D(new Vector3(0, 0, z), Quaternion.Identity));

        public void StartDock() => dock.StartDock(ship, 0, world: world);

        public void Update(double time = Step) => dock.Update(time, world);

        public void Dispose() => world.Dispose();
    }

    // Flies the ship from the start along the dock path to the dock mount, and gives the position of the ship at each
    // update after the door opened.
    private static IEnumerable<float> FlyIn(Station station, float start)
    {
        station.MoveShip(start);
        station.StartDock();
        var opened = false;
        for (var z = start; z > Mount.Z + DockRange; z -= Speed * Step)
        {
            station.MoveShip(z);
            station.Update();
            opened |= station.Door.Open;
            if (opened)
                yield return z;
        }
    }

    [Fact]
    public void TheDoorStaysOpenUntilASlowShipReachesTheDockMount()
    {
        using var station = new Station();
        var start = Outer.Z + OpenRange + 5;

        var updates = 0;
        foreach (var z in FlyIn(station, start))
        {
            Assert.True(station.Door.Open, $"The door closed with the ship at z = {z:0.0}.");
            updates++;
        }

        // The door opened when the ship came near the outer point.
        Assert.True(updates > 30 / Step);
    }

    [Fact]
    public void TheDoorClosesTenSecondsAfterTheShipDocks()
    {
        using var station = new Station();
        foreach (var _ in FlyIn(station, Outer.Z + OpenRange + 5))
        {
        }

        // The ship is in the dock sphere now. The next update ends the docking, and nothing keeps the door open after
        // that.
        station.MoveShip(Mount.Z);
        station.Update();
        StaysOpenThenCloses(station);
    }

    [Fact]
    public void TheDoorClosesTenSecondsAfterTheShipFliesAway()
    {
        using var station = new Station();
        station.MoveShip(Outer.Z + 50);
        station.StartDock();
        station.Update();
        Assert.True(station.Door.Open);

        // The ship turns away before it docks. Far from the dock path, it no longer keeps the door open.
        station.MoveShip(Outer.Z + 2000);
        StaysOpenThenCloses(station);
    }

    // The last update kept the door open for 10 seconds, less the time of that update. The door is open 9.5 seconds
    // later, and closed 10 seconds later.
    private static void StaysOpenThenCloses(Station station)
    {
        for (var i = 0; i < 95; i++)
        {
            station.Update();
            Assert.True(station.Door.Open);
        }

        station.Update(0.5);
        Assert.False(station.Door.Open);
    }
}
