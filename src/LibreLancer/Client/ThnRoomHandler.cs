using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.GCS;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.Thn;
using LibreLancer.Utf.Anm;
using LibreLancer.World;
using LibreLancer.World.Components;

namespace LibreLancer.Client;

public static class ThnRoomHandler
{
    public static ThnScriptContext CreateContext(Base currentBase, BaseRoom currentRoom)
    {
        var ctx = new ThnScriptContext(currentRoom.OpenSet());
        if (currentBase.TerrainTiny != null)
        {
            ctx.Substitutions.Add("$terrain_tiny", currentBase.TerrainTiny);
        }

        if (currentBase.TerrainSml != null)
        {
            ctx.Substitutions.Add("$terrain_sml", currentBase.TerrainSml);
        }

        if (currentBase.TerrainMdm != null)
        {
            ctx.Substitutions.Add("$terrain_mdm", currentBase.TerrainMdm);
        }

        if (currentBase.TerrainLrg != null)
        {
            ctx.Substitutions.Add("$terrain_lrg", currentBase.TerrainLrg);
        }

        if (currentBase.TerrainDyna1 != null)
        {
            ctx.Substitutions.Add("$terrain_dyna_01", currentBase.TerrainDyna1);
        }

        if (currentBase.TerrainDyna2 != null)
        {
            ctx.Substitutions.Add("$terrain_dyna_02", currentBase.TerrainDyna2);
        }
        return ctx;
    }

    private static Regex markerRegex = new(@"^Z([sg])\/(\w+)\/(\w+)\/(\d\d)\/(\w+)\/?(\w+)?$");


    public static RoomNpcSpot[] GetSpots(BaseRoom currentRoom)
    {
        var script = currentRoom.SetScript.LoadScript();
        //HashSet<int> indices = new();
        List<RoomNpcSpot> allSpots = new();
        foreach (var e in script.Entities.Values.Where(e => e.Type == EntityTypes.Marker))
        {
            var match = markerRegex.Match(e.Name);
            if (!match.Success)
            {
                continue;
            }

            // Exclude:
            // Prop (doesn't have 7th group)
            // Non-NPC markers
            // Groups that are not Group A (not used)
            if(match.Groups.Count != 7 ||
               match.Groups[2].Value != "NPC" || match.Groups[5].Value != "A")
            {
                continue;
            }

            bool isDynamic = match.Groups[1].Value == "g";
            // One each of 01/02/03 for dynamic
            /*if (isDynamic)
            {
                if (!int.TryParse(match.Groups[4].Value, out var index))
                    continue;
                if (!indices.Add(index))
                    continue;
            }*/

            if (!Enum.TryParse<Posture>(match.Groups[6].ValueSpan, out var posture))
                continue;
            allSpots.Add(new(e.Name, isDynamic, posture));
        }

        return allSpots.ToArray();
    }

    public static ThnSceneObject AddNpc(Cutscene scene,
        ResourceManager resources,
        AnmFile charAnimations,
        string name,
        string spot,
        string? voice,
        Bodypart? head,
        Bodypart? body,
        Bodypart? rightHand,
        Bodypart? leftHand,
        Accessory? accessory,
        ResolvedThn? fidgetScript)
    {
        var spotObj = scene!.GetObject(spot)!;
        var thnObj = CreateNpc(resources, charAnimations, name, voice, head, body, rightHand, leftHand, accessory,
            new Transform3D(spotObj.Translate with { Y = 0 }, spotObj.Rotate), 0);
        scene.AddObject(thnObj);
        if (fidgetScript != null)
        {
            scene.FidgetScript(fidgetScript.LoadScript(), name);
        }
        return thnObj;
    }

    // Makes a room NPC that plays a THN entity, for Cutscene.BeginScene. It has the costume and the voice of the room
    // NPC, and the name, the start pose and the floor height of the entity. It has no fidget. An NPC without a body
    // gives null.
    public static ThnSceneObject? CreateBoundNpc(
        ResourceManager resources,
        AnmFile charAnimations,
        BaseNpc npc,
        ThnEntity entity)
    {
        var appearance = BaseNpcPopulation.ResolveAppearance(npc);
        if (!appearance.HasBody)
        {
            return null;
        }

        var actor = CreateNpc(resources, charAnimations, entity.Name, npc.Voice, appearance.Head, appearance.Body,
            appearance.RightHand, appearance.LeftHand, appearance.Accessory,
            new Transform3D(entity.Position ?? Vector3.Zero, entity.Rotation), entity.FloorHeight);
        actor.Actor = entity.Actor;
        actor.Entity = entity;
        return actor;
    }

    private static ThnSceneObject CreateNpc(
        ResourceManager resources,
        AnmFile charAnimations,
        string name,
        string? voice,
        Bodypart? head,
        Bodypart? body,
        Bodypart? rightHand,
        Bodypart? leftHand,
        Accessory? accessory,
        Transform3D start,
        float floorHeight)
    {
        var skel = new DfmSkeletonManager(
            body?.LoadModel(resources)!, head?.LoadModel(resources),
            leftHand?.LoadModel(resources), rightHand?.LoadModel(resources))
        {
            FloorHeight = floorHeight
        };
        var obj = new GameObject
        {
            Nickname = name
        };
        var accessoryModel = accessory?.ModelFile?.LoadFile(resources)?.Drawable as IRigidModelFile;
        obj.RenderComponent = new CharacterRenderer(skel)
        {
            Accessory = accessory,
            AccessoryModel = accessoryModel?.CreateRigidModel(true, resources)
        };
        var animation = new AnimationComponent(obj, charAnimations);
        obj.AnimationComponent = animation;
        obj.AddComponent(animation);
        obj.SetTransform(start);
        return new ThnSceneObject
        {
            Name = name,
            Translate = start.Position,
            Rotate = start.Orientation,
            Object = obj,
            Voice = voice
        };
    }

}

public record struct RoomNpcSpot(string Nickname, bool Dynamic, Posture Posture);
