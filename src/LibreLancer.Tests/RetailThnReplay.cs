using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.World;
using LibreLancer.World.Components;
using Xunit;

namespace LibreLancer.Tests;

// Replays some events of a retail THN script without a renderer. The replay makes the entities that these events
// name, with a skeleton for each character. Each frame uses the order of Cutscene._Update: the events and processors,
// then the scene objects, then the world.
internal sealed class RetailThnReplay
{
    // The male body motions. The head and hand motions do not move a character, so the replays do not load them.
    public static readonly string[] MaleBodies =
        [DfmMotionStackTests.GenericMale, @"CHARACTERS\ANIMATIONS\BodyStoryMale.anm"];

    private const double Frame = 1.0 / 60;

    private readonly ThnScriptInstance instance;
    private readonly List<AnimationComponent> characters = [];
    private readonly Dictionary<string, Transform3D> spawns = new(StringComparer.OrdinalIgnoreCase);

    // keep selects the START_MOTION, START_SPATIAL_PROP_ANIM and ATTACH_ENTITY events to replay. The replay skips a
    // motion that the animation files do not have.
    public RetailThnReplay(string script, string[] animationFiles, Func<ThnEvent, bool> keep)
    {
        var data = RetailData.Open();
        var output = new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(data.ReadDataFile(script), script);
        var animations = data.LoadAnimations(animationFiles);

        var entities = new Dictionary<string, ThornTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in ((ThornTable)output["entities"]).Values.Cast<ThornTable>())
        {
            var name = (string)entity["entity_name"];
            entities[name] = entity;
            var spawn = Transform3D.Identity;
            if (entity.TryGetValue("spatialprops", out var sp))
            {
                if (((ThornTable)sp).TryGetVector3("pos", out var pos))
                {
                    spawn.Position = pos;
                }

                if (((ThornTable)sp).TryGetValue("orient", out var o))
                {
                    spawn.Orientation = ThnScript.GetQuaternion((ThornTable)o);
                }
            }

            spawns[name] = spawn;
        }

        var thn = new ThnScript() { Duration = (float)output["duration"] };
        foreach (var table in ((ThornTable)output["events"]).Values.Cast<ThornTable>())
        {
            ThnEvent? ev = ThnTypes.Convert<EventTypes>(table[2]) switch
            {
                EventTypes.StartMotion => new StartMotionEvent(table),
                EventTypes.StartSpatialPropAnim => new StartSpatialPropAnimEvent(table, script),
                EventTypes.AttachEntity => new AttachEntityEvent(table, script),
                _ => null
            };
            if (ev == null || !keep(ev) ||
                ev is StartMotionEvent motion && !animations.Scripts.ContainsKey(motion.Animation))
            {
                continue;
            }

            thn.Events.Add(ev);
        }

        // ThnScript sorts the events in the same way.
        thn.Events = thn.Events.OrderBy(x => x.Time).ToList();
        instance = new ThnScriptInstance(null!, thn);
        var names = thn.Events.SelectMany(x => x.Targets).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, entity) in entities)
        {
            if (!names.Contains(name))
            {
                continue;
            }

            var obj = new ThnSceneObject()
            {
                Name = name, Translate = spawns[name].Position, Rotate = spawns[name].Orientation
            };
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
    }

    // The scene object of an entity that the events name.
    public ThnSceneObject this[string name] => instance.Objects[name];

    // The pose of an entity in the script.
    public Transform3D Spawn(string entity) => spawns[entity];

    // Runs frames until the script time reaches the time.
    public void RunTo(double time)
    {
        while (instance.CurrentTime < time - 1e-6)
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
        }
    }
}
