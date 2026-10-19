using System.Linq;
using System.Numerics;
using LibreLancer.Data.GameData;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// A cutscene in a world that the host owns, for example the star system of an editor preview. The scripts make their
// objects in the world of the host, and Dispose removes them. The cutscene has no renderer, so it changes no lighting
// and does not step or draw the world. Each script has a CAMERA entity, because each update reads the camera.
public class HostWorldCutsceneTests
{
    private static ThnEntity Entity(string name, EntityTypes type) =>
        new(new ThornTable { ["entity_name"] = name, ["type"] = type }) { Position = Vector3.Zero };

    private static ThnScript Script(params ThnEntity[] entities)
    {
        var script = new ThnScript { Duration = 10 };
        script.Entities["camera"] = Entity("camera", EntityTypes.Camera);
        foreach (var entity in entities)
        {
            script.Entities[entity.Name] = entity;
        }

        return script;
    }

    // A world without a renderer and without physics, with one object of the host.
    private static (GameWorld World, GameObject Host) HostWorld()
    {
        var world = new GameWorld(null, null, null, null, false);
        var host = new GameObject { Name = new ObjectName("host"), Nickname = "host" };
        world.AddObject(host);
        return (world, host);
    }

    private static Cutscene Cutscene(GameWorld world) =>
        new(new ThnScriptContext(null), world, null!, null!, null, new Rectangle(0, 0, 1, 1), null!);

    [Fact]
    public void ObjectsGoIntoTheHostWorldAndLeaveAtDispose()
    {
        var (world, host) = HostWorld();
        var cutscene = Cutscene(world);

        cutscene.BeginScene(Script(Entity("marker", EntityTypes.Marker)));
        cutscene._Update(1.0 / 60);

        var marker = cutscene.GetObject("marker")!.Object!;
        Assert.Equal(new[] { host, marker }, world.Objects);
        Assert.Null(cutscene.Renderer);
        Assert.Same(world, cutscene.World);

        cutscene.Dispose();
        Assert.Equal(new[] { host }, world.Objects);
    }

    [Fact]
    public void LightsAndAmbientChangeNothing()
    {
        var (world, host) = HostWorld();
        var cutscene = Cutscene(world);
        var light = Entity("light", EntityTypes.Light);
        light.LightProps = new ThnLightProps { On = true, Render = new RenderLight { Direction = -Vector3.UnitZ } };
        var scene = Entity("scene", EntityTypes.Scene);
        scene.Ambient = new Vector3(10, 20, 30);

        cutscene.BeginScene(Script(light, scene));
        cutscene._Update(1.0 / 60);

        Assert.NotNull(cutscene.GetObject("light")!.Light);
        Assert.Null(cutscene.Renderer);
        Assert.Equal(new[] { host }, world.Objects);
        cutscene.Dispose();
    }

    [Fact]
    public void AFogEventDoesNothingWithoutARenderer()
    {
        var (world, host) = HostWorld();
        var cutscene = Cutscene(world);
        var script = Script(Entity("marker", EntityTypes.Marker));
        script.Events.Add(new StartFogPropAnimEvent
        {
            Targets = ["camera"],
            SetFlags = StartFogPropAnimEvent.AnimVars.FogOn | StartFogPropAnimEvent.AnimVars.FogColor,
            FogOn = true,
            FogColor = Vector3.One,
        });

        // The event runs at 0 s, in BeginScene.
        cutscene.BeginScene(script);
        cutscene._Update(1.0 / 60);

        Assert.Null(cutscene.Renderer);
        cutscene.Dispose();
        Assert.Equal(new[] { host }, world.Objects);
    }

    [Fact]
    public void ARemovedObjectIsNotRemovedAgain()
    {
        var (world, host) = HostWorld();
        var cutscene = Cutscene(world);
        cutscene.BeginScene(Script(Entity("marker", EntityTypes.Marker)));

        cutscene.RemoveObject(cutscene.GetObject("marker")!);
        Assert.Equal(new[] { host }, world.Objects);
        cutscene.Dispose();

        Assert.Equal(new[] { host }, world.Objects);
        Assert.Empty(cutscene.AllObjects.Where(o => o.Name == "marker"));
    }
}
