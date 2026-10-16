using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Thn.Events;
using Xunit;

namespace LibreLancer.Tests;

// Replays the retail s002xe (the Manhattan intro) without a renderer. The replay runs the motions, the moves and the
// attachments of Trent, the two medics and Syd. Each frame uses the order of Cutscene._Update: the events and
// processors, then the scene objects, then the world.
public class S002xeChoreographyTests
{
    private const string Script = @"SCRIPTS\STORY\s002xe_nrml_sinclair_li_01_Cityscape_01.thn";
    private static readonly string[] Characters = ["Char_trent", "Char_medic_1", "Char_medic_2", "Char_syd"];
    // The times in seconds at which the replay keeps the poses of the characters.
    private static readonly double[] Times = [55.8, 57.4, 60.0, 62.0, 65.26, 69.9, 72.0, 73.3, 75.8, 77.3];

    private static readonly Lazy<Replay> replay = new(() => new Replay());

    private sealed class Replay
    {
        private readonly Dictionary<(double, string), Transform3D> poses = [];
        private readonly RetailThnReplay thn;

        // The scene pose of a character at one of the Times.
        public Transform3D Pose(double time, string character) => poses[(time, character)];

        // The position of an entity in the script.
        public Vector3 Position(string entity) => thn.Spawn(entity).Position;

        public Replay()
        {
            // The replay keeps the motions, the moves and the zero-duration attaches of the characters.
            thn = new RetailThnReplay(Script, RetailThnReplay.MaleBodies, ev =>
                Characters.Contains(ev.Targets[0], StringComparer.OrdinalIgnoreCase) &&
                ev is not AttachEntityEvent { Duration: > 0 });
            foreach (var time in Times)
            {
                thn.RunTo(time);
                foreach (var name in Characters)
                {
                    var obj = thn[name];
                    poses[(time, name)] = new Transform3D(obj.Translate, obj.Rotate);
                }
            }
        }
    }

    private static float GroundDistance(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));

    // 55.8-62 s: Trent runs up to medic_2 and then walks with the medics. At 56.9-59.5 s his left hand reaches the
    // shoulder of medic_2. The cut at 65.277 s moves Trent and medic_1 to their next marks, so they must be near
    // these marks before the cut.
    [Fact]
    public void TrentRunsUpToTheMedics()
    {
        var r = replay.Value;
        var run = r.Pose(55.8, "Char_trent").Position.X - r.Pose(57.4, "Char_trent").Position.X;
        Assert.True(run >= 4.5f, $"Trent runs {run} m toward -X");
        foreach (var time in new[] { 57.4, 60.0, 62.0 })
        {
            var distance = GroundDistance(r.Pose(time, "Char_trent").Position, r.Pose(time, "Char_medic_2").Position);
            Assert.True(distance <= 1.5f, $"Trent is {distance} m from medic_2 at {time} s");
        }

        var trent = GroundDistance(r.Pose(65.26, "Char_trent").Position, r.Position("Char_Tr_Mk_loc_05_Stack_Correctly"));
        Assert.True(trent <= 1.5f, $"Trent is {trent} m from his mark before the cut");
        var medic = GroundDistance(r.Pose(65.26, "Char_medic_1").Position,
            r.Position("Char_Medic_1_Mk_loc_04_Stack_Correctly"));
        Assert.True(medic <= 1f, $"medic_1 is {medic} m from the mark before the cut");
    }

    // 70-72 s: Syd turns half a circle to the camera. From 73.3 s the camera pans up to his face. He lifts the
    // cigarette at 75.8 s and blows smoke toward the camera at 77.3 s.
    [Fact]
    public void SydTurnsToTheCamera()
    {
        var r = replay.Value;

        Assert.True(Facing(r, 69.9) < -0.9f, $"Syd faces the camera before the turn: {Facing(r, 69.9)}");
        foreach (var time in new[] { 72.0, 73.3, 75.8, 77.3 })
        {
            Assert.True(Facing(r, time) > 0.9f, $"Syd does not face the camera at {time} s: {Facing(r, time)}");
        }
    }

    // The cosine of the angle between the front of Syd and the direction to the camera, on the ground.
    private static float Facing(Replay r, double time)
    {
        var pose = r.Pose(time, "Char_syd");
        var front = Vector3.Transform(-Vector3.UnitZ, pose.Orientation);
        var camera = r.Position("Cam_12_Wide_to_ACU_C_Syd_Smoke") - pose.Position;
        return Vector2.Dot(Vector2.Normalize(new(front.X, front.Z)), Vector2.Normalize(new(camera.X, camera.Z)));
    }
}
