using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using LibreLancer.Missions.Conditions;
using LibreLancer.Server;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The distance conditions at the radius (OL-0078). Retail is strict on both sides of a distance check. So a ship exactly
// at the radius is neither inside nor outside. Before the change, the engine counted that ship as inside.
public class MissionDistanceConditionTests
{
    // The center of the conditions below. The center, the radius 1000 and each ship position are exact in a float. So a
    // ship 1000 units from the center is exactly at the radius.
    private static readonly Vector3 Center = new(500, 0, -2000);

    private const float Radius = 1000;

    // The side, the distance of the ship from the center, and the result of the condition.
    public static TheoryData<string, float, bool> Sides => new()
    {
        { "inside", Radius - 1, true },
        { "inside", Radius, false },
        { "inside", Radius + 1, false },
        { "outside", Radius - 1, false },
        { "outside", Radius, false },
        { "outside", Radius + 1, true },
    };

    // A mission runtime whose player is in a space world. The conditions read only the game world of that space world,
    // so the server world has no star system.
    private sealed class Mission : IDisposable
    {
        private readonly GameWorld world = new(null, null, null, null, initPhys: false);

        public MissionRuntime Runtime { get; }

        public Mission()
        {
            var server = (ServerWorld)RuntimeHelpers.GetUninitializedObject(typeof(ServerWorld));
            server.GameWorld = world;
            var player = new Player(null!, null!, Guid.Empty, default);
            player.Space = new SpacePlayer(server, player);
            Runtime = new MissionRuntime(new MissionScript(), player, []);
        }

        public GameObject Add(string nickname, Vector3 position)
        {
            var obj = new GameObject { Nickname = nickname };
            obj.SetTransform(new Transform3D(position, Quaternion.Identity));
            world.AddObject(obj);
            return obj;
        }

        // Puts a ship at a distance from the center, on the X axis.
        public GameObject AddAt(string nickname, float distance) =>
            Add(nickname, Center + new Vector3(distance, 0, 0));

        public void Dispose() => world.Dispose();
    }

    // Reads one condition line of a trigger, as the engine reads a mission file. Then it starts the condition.
    private static (ScriptedCondition Condition, ActiveCondition Self) Start(Mission mission, string line)
    {
        var ini = new MissionIni();
        var text = $"[Trigger]\nnickname = ol_trigger\n{line}\n";
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        var condition = Assert.Single(ScriptedTrigger.FromIni(Assert.Single(ini.Triggers)).Conditions);
        var self = new ActiveCondition(null!, condition);
        condition.Init(mission.Runtime, self);
        return (condition, self);
    }

    private static bool Check(Mission mission, (ScriptedCondition Condition, ActiveCondition Self) started,
        double elapsed = 0) =>
        started.Condition.CheckCondition(mission.Runtime, started.Self, elapsed);

    [Theory]
    [MemberData(nameof(Sides))]
    public void DistVecCountsAShipAtTheRadiusOnNeitherSide(string side, float distance, bool expected)
    {
        using var mission = new Mission();
        mission.AddAt("ol_ship", distance);

        var started = Start(mission, $"Cnd_DistVec = {side}, ol_ship, 500, 0, -2000, 1000");

        Assert.IsType<Cnd_DistVec>(started.Condition);
        Assert.Equal(expected, Check(mission, started));
    }

    [Theory]
    [MemberData(nameof(Sides))]
    public void DistShipCountsAShipAtTheRadiusOnNeitherSide(string side, float distance, bool expected)
    {
        using var mission = new Mission();
        mission.Add("ol_target", Center);
        mission.AddAt("ol_ship", distance);

        var started = Start(mission, $"Cnd_DistShip = {side}, ol_ship, ol_target, 1000");

        Assert.IsType<Cnd_DistShip>(started.Condition);
        Assert.Equal(expected, Check(mission, started));
    }

    // With one ship in the label, ANY and ALL give the result of that ship.
    [Theory]
    [MemberData(nameof(Sides))]
    public void DistVecLblCountsAShipAtTheRadiusOnNeitherSide(string side, float distance, bool expected)
    {
        foreach (var mode in new[] { "ANY", "ALL" })
        {
            using var mission = new Mission();
            mission.Runtime.Labels["ol_label"] = new MissionLabel("ol_label", ["ol_ship"]);
            mission.AddAt("ol_ship", distance);

            var started = Start(mission, $"Cnd_DistVecLbl = {side}, {mode}, ol_label, 500, 0, -2000, 1000");

            Assert.IsType<Cnd_DistVecLbl>(started.Condition);
            Assert.Equal(expected, Check(mission, started));
        }
    }

    // Two ships of a label: one at the radius and one at the other distance. ANY needs one ship on the side, and ALL
    // needs each ship on the side. The ship at the radius is on neither side. The label order sets which ship the
    // condition tests first. With the ship at the radius first, ANY must test the second ship. With the ship at the
    // radius last, ALL must test the second ship.
    [Theory]
    [InlineData("inside", Radius - 1, "ANY", true, true)]
    [InlineData("inside", Radius - 1, "ALL", true, false)]
    [InlineData("inside", Radius - 1, "ALL", false, false)]
    [InlineData("inside", Radius, "ANY", true, false)]
    [InlineData("outside", Radius + 1, "ANY", true, true)]
    [InlineData("outside", Radius + 1, "ALL", true, false)]
    [InlineData("outside", Radius + 1, "ALL", false, false)]
    [InlineData("outside", Radius, "ANY", true, false)]
    public void DistVecLblWithAShipAtTheRadius(string side, float other, string mode, bool edgeFirst, bool expected)
    {
        using var mission = new Mission();
        string[] order = edgeFirst ? ["ol_edge", "ol_other"] : ["ol_other", "ol_edge"];
        mission.Runtime.Labels["ol_label"] = new MissionLabel("ol_label", order);
        Assert.Equal(order, mission.Runtime.Labels["ol_label"].Objects);
        mission.AddAt("ol_edge", Radius);
        mission.AddAt("ol_other", other);

        var started = Start(mission, $"Cnd_DistVecLbl = {side}, {mode}, ol_label, 500, 0, -2000, 1000");

        Assert.Equal(expected, Check(mission, started));
    }

    // A hold time of 5 s (tick_away). Two checks of 3 s each pass the hold time only when the ship stays on the side.
    [Theory]
    [MemberData(nameof(Sides))]
    public void TheHoldTimeCountsOnlyWhileTheShipIsOnTheSide(string side, float distance, bool expected)
    {
        foreach (var line in new[]
                 {
                     $"Cnd_DistVec = {side}, ol_ship, 500, 0, -2000, 1000, 5, tick_away",
                     $"Cnd_DistShip = {side}, ol_ship, ol_target, 1000, 5, tick_away",
                 })
        {
            using var mission = new Mission();
            mission.Add("ol_target", Center);
            mission.AddAt("ol_ship", distance);
            var started = Start(mission, line);

            Assert.False(Check(mission, started, 3));
            Assert.Equal(expected, Check(mission, started, 3));
        }
    }

    // A check at the radius starts the hold time again, because the ship is not on the side.
    [Theory]
    [InlineData("Cnd_DistVec = inside, ol_ship, 500, 0, -2000, 1000, 5, tick_away", Radius - 1)]
    [InlineData("Cnd_DistShip = inside, ol_ship, ol_target, 1000, 5, tick_away", Radius - 1)]
    [InlineData("Cnd_DistVec = outside, ol_ship, 500, 0, -2000, 1000, 5, tick_away", Radius + 1)]
    [InlineData("Cnd_DistShip = outside, ol_ship, ol_target, 1000, 5, tick_away", Radius + 1)]
    public void AShipAtTheRadiusStartsTheHoldTimeAgain(string line, float onSide)
    {
        using var mission = new Mission();
        mission.Add("ol_target", Center);
        var ship = mission.AddAt("ol_ship", onSide);
        var started = Start(mission, line);

        Assert.False(Check(mission, started, 3));
        ship.SetTransform(new Transform3D(Center + new Vector3(Radius, 0, 0), Quaternion.Identity));
        Assert.False(Check(mission, started, 3));
        ship.SetTransform(new Transform3D(Center + new Vector3(onSide, 0, 0), Quaternion.Identity));
        Assert.False(Check(mission, started, 3));
        Assert.True(Check(mission, started, 3));
    }
}
