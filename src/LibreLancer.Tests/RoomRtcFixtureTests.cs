using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using LibreLancer.Client;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.MBases;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.Utf.Anm;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// Binds the room fixtures of the retail mbases.ini to the characters of the retail cutscenes, as RoomGameplay does
// when it starts a cutscene.
public class RoomRtcFixtureTests
{
    // The RTC INI, then the room NPC and the THN entity that the cutscene binds. No NPC means no binding.
    [Theory]
    [InlineData(@"MISSIONS\M01a\M001a_s003x_Li01_01_nrml.ini", "li0101_fix_bartender", "Char_li_bartender_Fixture")]
    [InlineData(@"MISSIONS\M03\M003_s012xb_Li01_01_nrml.ini", "li0101_fix_bartender", "Fixture_bartender")]
    [InlineData(@"MISSIONS\M01a\M001a_s005d_Li01_01_reoffer.ini", "li0101_fix_weaponsdealer", "Char_li_equipment_dealer")]
    [InlineData(@"MISSIONS\M07\M007_s037a_Br04_01_offer.ini", "br0401_fix_weaponsdealer", "Fixture_Equipment")]
    [InlineData(@"MISSIONS\M02\M002_s009a_Li01_01_offer.ini", "li0101_fix_bartender", "Char_li_manhattan_bartender")]
    [InlineData(@"MISSIONS\M05\M005_s026a_Br03_01_offer.ini", "br0301_fix_bartender", "Char_li_bartender_LtG02")]
    [InlineData(@"MISSIONS\M06\M006_s032a_Br05_01_offer.ini", "br0501_fix_bartender", "Char_Bartender")]
    [InlineData(@"MISSIONS\M01a\M001a_s004x_Li01_01_nrml.ini", null, null)]
    [InlineData(@"MISSIONS\M01a\M000_s002xe_Li01_01_nrml.ini", null, null)]
    public void AnRtcBindsTheFixtureOfItsRoom(string rtc, string? npc, string? entity)
    {
        var data = RetailData.Open();
        var ini = new StoryCutsceneIni(data.DataFilePath(rtc), data.VFS);
        var encounter = Assert.Single(ini.Encounters);
        var room = LoadRoom(LoadMBases(data), encounter.Location![0], encounter.Location[1]);
        var script = (encounter.Action ?? encounter.Offer)!;

        var bound = BaseNpcPopulation.FindFixtureActors(room,
            LoadCharacters(data, data.ReadDataFile(script), script), ini.Chars);

        string[] expected = npc == null ? [] : [$"{npc} -> {entity}"];
        Assert.Equal(expected, bound.Select(x => $"{x.Npc.Nickname} -> {x.Entity.Name}"));
    }

    // Cutscene.BeginScene adds a bound actor before the scripts make their entities. ConstructEntities then keeps
    // the bound actor under the name of the entity, and the events of that entity move the bound actor.
    [Fact]
    public void TheEventsOfAnEntityMoveItsBoundActor()
    {
        var entity = Deformable("Char_li_bartender_Fixture", "bartender");
        var script = new ThnScript() { Duration = 10 };
        script.Entities.Add(entity.Name, entity);
        script.Events.Add(new AttachEntityEvent()
        {
            Targets = [entity.Name, "marker"],
            TargetType = TargetTypes.Root,
            Flags = AttachFlags.Position | AttachFlags.Orientation
        });
        var bound = new ThnSceneObject()
        {
            Name = entity.Name, Rotate = Quaternion.Identity, Object = new GameObject()
        };
        var marker = new ThnSceneObject()
        {
            Name = "marker", Translate = new Vector3(1, 2, 3), Rotate = Quaternion.Identity
        };
        var objects = new Dictionary<string, ThnSceneObject>(StringComparer.OrdinalIgnoreCase)
        {
            [bound.Name] = bound,
            [marker.Name] = marker
        };
        // A cutscene without a game or a world. ConstructEntities makes no object, because each entity has one.
        var cutscene = new Cutscene(new ThnScriptContext(null), null!, null!, null, new Rectangle(0, 0, 1, 1), null!);
        var instance = new ThnScriptInstance(cutscene, script);

        // A room scene makes its objects, as Cutscene.SceneSetup does. Then the events at T=0 run, and each scene
        // object applies its attachments.
        instance.ConstructEntities(objects, true);
        instance.Update(0);
        bound.Update();

        Assert.Same(bound, objects[entity.Name]);
        Assert.Equal(marker.Translate, bound.Object!.LocalTransform.Position);
    }

    // The bound actor of li0101_fix_bartender in s003x. It has the costume and the voice of the room NPC (mbases.ini),
    // and the name, the start pose and the floor height of the entity.
    [Fact]
    public void ABoundActorHasTheRoomCostumeAndThePoseOfItsEntity()
    {
        var data = RetailData.Open();
        var npc = new BaseNpc("li0101_fix_bartender")
        {
            Body = data.GameBodypart("li_bartender_body"),
            Head = data.GameBodypart("rh_bartender_head"),
            Voice = "rvp101"
        };
        var entity = Deformable("Char_li_bartender_Fixture", "bartender");
        entity.Template = "li_bartender_1";
        entity.Position = new Vector3(-0.33f, 0.5f, -1.85f);
        entity.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        entity.FloorHeight = 0.75f;

        var actor = ThnRoomHandler.CreateBoundNpc(data.Resources, new AnmFile(), npc, entity)!;

        Assert.Equal(("Char_li_bartender_Fixture", "rvp101", "bartender"), (actor.Name, actor.Voice, actor.Actor));
        Assert.Same(entity, actor.Entity);
        Assert.Equal((entity.Position.Value, entity.Rotation), (actor.Translate, actor.Rotate));
        Assert.Equal(new Transform3D(entity.Position.Value, entity.Rotation), actor.Object!.LocalTransform);
        var skeleton = Assert.IsType<CharacterRenderer>(actor.Object.RenderComponent).Skeleton;
        Assert.Equal(0.75f, skeleton.FloorHeight);
        Assert.NotNull(skeleton.HeadSkinning);
        // An NPC without a body is not bound. The room places it as before.
        Assert.Null(ThnRoomHandler.CreateBoundNpc(data.Resources, new AnmFile(), new BaseNpc("nobody"), entity));
    }

    // Only story cutscenes have a DEFORMABLE with the role of a room fixture. Thus the binding does not change the
    // land, launch, enter and ambient scripts of the rooms, the vendor fidgets or the GCS scripts.
    [Fact]
    public void OnlyStoryScriptsHaveFixtureActors()
    {
        var data = RetailData.Open();
        // A room with one fixture for each role in mbases.ini: bartender, trader, Equipment and ShipDealer.
        var roles = LoadMBases(data).MBases
            .SelectMany(x => x.Rooms)
            .SelectMany(x => x.NPCs)
            .Select(x => x.Action)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, roles.Count);
        var room = new BaseRoom
        {
            SourceFile = "mbases.ini",
            Npcs = roles.Select(x => new BaseNpc(x) { Placement = new BaseNpcPlacement("spot", null!, x) }).ToList()
        };

        var story = Path.Combine(data.Folder, "DATA", "SCRIPTS", "STORY") + Path.DirectorySeparatorChar;
        var storyCharacters = 0;
        var storyScripts = 0;
        var storyBindings = 0;
        var others = new List<string>();
        foreach (var path in Directory.EnumerateFiles(data.Folder, "*.thn", SearchOption.AllDirectories))
        {
            var characters = LoadCharacters(data, File.ReadAllBytes(path), path);
            var bound = BaseNpcPopulation.FindFixtureActors(room, characters);
            if (!path.StartsWith(story, StringComparison.OrdinalIgnoreCase))
            {
                others.AddRange(bound.Select(x => $"{path}: {x.Entity.Name}"));
                continue;
            }

            storyCharacters += characters.Entities.Values.Count(x => x.Actor != null && roles.Contains(x.Actor));
            if (bound.Count > 0)
            {
                storyScripts++;
                storyBindings += bound.Count;
            }
        }

        Assert.Empty(others);
        // 21 DEFORMABLEs in 18 story scripts have a fixture role. s005d has two with the role Equipment, and only
        // the first one is bound.
        Assert.Equal(21, storyCharacters);
        Assert.Equal(18, storyScripts);
        Assert.Equal(20, storyBindings);
    }

    private static ThnEntity Deformable(string name, string actor) =>
        new(new ThornTable { ["entity_name"] = name, ["type"] = EntityTypes.Deformable }) { Actor = actor };

    private static MBasesIni LoadMBases(RetailData data)
    {
        var mbases = new MBasesIni();
        mbases.AddFile(data.DataFilePath(@"MISSIONS\mbases.ini"), data.VFS);
        return mbases;
    }

    // The fixtures of a room, as GameItemDb places them. A room without an [MRoom] has no fixture.
    private static BaseRoom LoadRoom(MBasesIni mbases, string baseName, string roomName)
    {
        var mroom = mbases.MBases
            .Single(x => x.Nickname.Equals(baseName, StringComparison.OrdinalIgnoreCase))
            .Rooms.SingleOrDefault(x => x.Nickname.Equals(roomName, StringComparison.OrdinalIgnoreCase));
        return new BaseRoom
        {
            SourceFile = "mbases.ini",
            Npcs = (mroom?.NPCs ?? []).Select(x => new BaseNpc(x.Npc)
            {
                Placement = new BaseNpcPlacement(x.StandMarker, null!, x.Action ?? "")
            }).ToList()
        };
    }

    // The DEFORMABLE entities of a script in file order, with their actors, as ThnScript reads them. The test does
    // not make a ThnScript from the file, because a motion path needs the native thorncompiler.
    private static ThnScript LoadCharacters(RetailData data, byte[] thn, string name)
    {
        var output = new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(thn, name);
        var script = new ThnScript();
        foreach (var table in ((ThornTable)output["entities"]).Values.Cast<ThornTable>())
        {
            var entity = new ThnEntity(table);
            if (entity.Type != EntityTypes.Deformable || !table.TryGetValue("userprops", out var props))
            {
                continue;
            }

            foreach (var pair in ((ThornTable)props).Pairs)
            {
                if ("Actor".Equals(pair.Key?.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    entity.Actor = pair.Value?.ToString();
                }
            }

            script.Entities[entity.Name] = entity;
        }

        return script;
    }
}
