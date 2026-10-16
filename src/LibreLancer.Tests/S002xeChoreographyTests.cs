using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.World;
using LibreLancer.World.Components;
using Xunit;

namespace LibreLancer.Tests;

// Replays the retail s002xe (the Manhattan intro) without a renderer. The replay runs the motions, the moves and the
// attachments of Trent, the two medics and Syd. Each frame uses the order of Cutscene._Update: the events and
// processors, then the scene objects, then the world.
public class S002xeChoreographyTests
{
    private const string Script = @"SCRIPTS\STORY\s002xe_nrml_sinclair_li_01_Cityscape_01.thn";
    private const string StoryMale = @"CHARACTERS\ANIMATIONS\BodyStoryMale.anm";
    private const double Frame = 1.0 / 60;
    private static readonly string[] Characters = ["Char_trent", "Char_medic_1", "Char_medic_2", "Char_syd"];
    // The times in seconds at which the replay keeps the poses of the characters.
    private static readonly double[] Times = [55.8, 57.4, 60.0, 62.0, 65.26, 69.9, 72.0, 73.3, 75.8, 77.3];

    private static readonly Lazy<Replay> replay = new(() => new Replay());

    private sealed class Replay
    {
        private readonly Dictionary<(double, string), Transform3D> poses = [];
        private readonly Dictionary<string, Vector3> positions = new(StringComparer.OrdinalIgnoreCase);

        // The scene pose of a character at one of the Times.
        public Transform3D Pose(double time, string character) => poses[(time, character)];

        // The position of an entity in the script.
        public Vector3 Position(string entity) => positions[entity];

        public Replay()
        {
            var data = RetailData.Open();
            var bytes = data.ReadDataFile(Script);
            var output = new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(bytes, Script);
            // The head and hand motions do not move a character, so the replay does not load them.
            var animations = data.LoadAnimations(DfmMotionStackTests.GenericMale, StoryMale);

            var entities = new Dictionary<string, ThornTable>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in ((ThornTable)output["entities"]).Values.Cast<ThornTable>())
            {
                var name = (string)entity["entity_name"];
                entities[name] = entity;
                if (entity.TryGetValue("spatialprops", out var sp) && ((ThornTable)sp).TryGetVector3("pos", out var pos))
                {
                    positions[name] = pos;
                }
            }

            var script = new ThnScript() { Duration = (float)output["duration"] };
            foreach (var table in ((ThornTable)output["events"]).Values.Cast<ThornTable>())
            {
                if (!Characters.Contains((string)((ThornTable)table[3])[1], StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                ThnEvent? ev = ThnTypes.Convert<EventTypes>(table[2]) switch
                {
                    EventTypes.StartMotion => new StartMotionEvent(table),
                    EventTypes.StartSpatialPropAnim => new StartSpatialPropAnimEvent(table, Script),
                    EventTypes.AttachEntity => new AttachEntityEvent(table),
                    _ => null
                };
                if (ev == null || ev is AttachEntityEvent { Duration: > 0 } ||
                    ev is StartMotionEvent motion && !animations.Scripts.ContainsKey(motion.Animation))
                {
                    continue;
                }

                script.Events.Add(ev);
            }

            // ThnScript sorts the events in the same way.
            script.Events = script.Events.OrderBy(x => x.Time).ToList();
            var instance = new ThnScriptInstance(null!, script);
            var characters = new List<AnimationComponent>();
            var names = script.Events.SelectMany(x => x.Targets).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, entity) in entities)
            {
                if (!names.Contains(name))
                {
                    continue;
                }

                var obj = new ThnSceneObject()
                {
                    Name = name, Translate = positions.GetValueOrDefault(name), Rotate = Quaternion.Identity
                };
                if (entity.TryGetValue("spatialprops", out var sp) && ((ThornTable)sp).TryGetValue("orient", out var o))
                {
                    obj.Rotate = ThnScript.GetQuaternion((ThornTable)o);
                }

                if (ThnTypes.Convert<EntityTypes>(entity["type"]) == EntityTypes.Deformable)
                {
                    var skeleton = data.LoadCostume((string)entity["template_name"]);
                    Assert.NotNull(skeleton);
                    var props = (ThornTable)entity["compoundprops"];
                    skeleton.FloorHeight = (float)props["floor_height"];
                    obj.Object = new GameObject() { RenderComponent = new CharacterRenderer(skeleton) };
                    var animation = new AnimationComponent(obj.Object, animations);
                    obj.Object.AnimationComponent = animation;
                    characters.Add(animation);
                }

                instance.Objects[name] = obj;
            }

            // Cutscene.SceneSetup runs the events at 0 s before the first frame.
            instance.Update(0);
            var next = 0;
            while (next < Times.Length)
            {
                instance.Update(Frame);
                foreach (var obj in instance.Objects.Values)
                {
                    obj.Update();
                }

                foreach (var animation in characters)
                {
                    animation.Update(Frame, null!);
                }

                for (; next < Times.Length && instance.CurrentTime >= Times[next] - 1e-6; next++)
                {
                    foreach (var name in Characters)
                    {
                        var obj = instance.Objects[name];
                        poses[(Times[next], name)] = new Transform3D(obj.Translate, obj.Rotate);
                    }
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
