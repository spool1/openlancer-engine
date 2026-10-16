using System;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// Uses Trent from the retail data: pl_trent_body, pi_pirate5_head and the benchmark male hands.
// The ship tests use li_elite.
public class ThnCharacterBoneTests
{
    private const float Tolerance = 1e-4f;
    private static readonly Quaternion Turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1);

    // The tests use an animated pose, because cutscenes read the bones after animation.
    private static DfmSkeletonManager StandingTrent() => RetailData.Open().LoadPosedCostume("trent",
        @"CHARACTERS\ANIMATIONS\BodyGenericMale.anm", "Sc_MLBODY_STND_IDLE_000LV_xa_04");

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");
    }

    private static void AssertNear(Transform3D expected, Transform3D actual)
    {
        AssertNear(expected.Position, actual.Position);
        Assert.True(MathF.Abs(Quaternion.Dot(expected.Orientation, actual.Orientation)) > 1 - Tolerance,
            $"Expected {expected.Orientation}, got {actual.Orientation}");
    }

    [Fact]
    public void BodyHeadHasTheFrameThatTheIKEventsUse()
    {
        var skeleton = StandingTrent();
        var root = skeleton.GetBone("Root")!.Transform;
        var head = skeleton.GetBone("Body_Head")!.Transform;

        Assert.InRange(head.Position.Y - root.Position.Y, 0.6f, 0.75f);
        // The IK events give Body_Head up = NEG_Y_AXIS and front = Z_AXIS. The model faces -Z.
        Assert.InRange(Vector3.Dot(Vector3.Transform(-Vector3.UnitY, head.Orientation), Vector3.UnitY), 0.9f, 1f);
        Assert.InRange(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, head.Orientation), -Vector3.UnitZ), 0.9f, 1f);
        // Head_End is an IK target at the face. It is about 0.16 m in front of Body_Head.
        var headEnd = head.InverseTransform(skeleton.GetBone("Head_End")!.Transform.Position);
        Assert.InRange(Vector3.Distance(new Vector3(0, 0, 0.16f), headEnd), 0f, 0.02f);
    }

    [Fact]
    public void RootIsTheBodyRoot()
    {
        var skeleton = StandingTrent();

        AssertNear(skeleton.BodySkinning.Bones["Root"].LocalTransform, skeleton.GetBone("Root")!.Transform);
    }

    [Fact]
    public void HeadAndHandBonesGoThroughTheirConnection()
    {
        var skeleton = StandingTrent();
        skeleton.GetTransforms(Matrix4x4.Identity, out var head, out var leftHand, out var rightHand);

        AssertNear(skeleton.HeadSkinning!.Bones["Eye IK Left"].LocalTransform * Transform3D.FromMatrix(head),
            skeleton.GetBone("Eye IK Left")!.Transform);
        AssertNear(skeleton.LeftHandSkinning!.Bones["L Index 1"].LocalTransform * Transform3D.FromMatrix(leftHand),
            skeleton.GetBone("L Index 1")!.Transform);
        AssertNear(skeleton.RightHandSkinning!.Bones["R Index 1"].LocalTransform * Transform3D.FromMatrix(rightHand),
            skeleton.GetBone("R Index 1")!.Transform);
    }

    [Fact]
    public void AnUnknownNameGivesNull()
    {
        Assert.Null(StandingTrent().GetBone("No Such Bone"));
    }

    private static ThnSceneObject Character(DfmSkeletonManager skeleton, Vector3 spot) => new()
    {
        Name = "target",
        Translate = spot,
        Rotate = Turn,
        Object = new GameObject() { RenderComponent = new CharacterRenderer(skeleton) }
    };

    // Attaches a marker to the target for 50 s and runs the events of the first frame.
    private static (ThnScriptInstance Instance, ThnSceneObject Marker) AttachScene(ThnSceneObject target,
        TargetTypes type, string part)
    {
        var script = new ThnScript() { Duration = 100 };
        script.Events.Add(new AttachEntityEvent()
        {
            Duration = 50,
            Targets = ["marker", target.Name],
            TargetType = type,
            TargetPart = part,
            Flags = AttachFlags.Position | AttachFlags.Orientation
        });
        var instance = new ThnScriptInstance(null!, script);
        var marker = new ThnSceneObject() { Name = "marker", Rotate = Quaternion.Identity };
        instance.Objects[marker.Name] = marker;
        instance.Objects[target.Name] = target;
        instance.Update(0);
        return (instance, marker);
    }

    private static Transform3D TransformOf(ThnSceneObject obj) => new(obj.Translate, obj.Rotate);

    // ThnSceneObject.UpdateEngineObjects puts the character model at the floor height plus the root height.
    // This height replaces the Y of the scene position. A marker on a bone or on a hardpoint must stay on the model.
    [Theory]
    [InlineData(TargetTypes.Part, "Body_Head")]
    [InlineData(TargetTypes.Part, "Eye IK Left")]
    [InlineData(TargetTypes.Hardpoint, "hp_head")]
    public void AMarkerOnACharacterFollowsTheModel(TargetTypes type, string part)
    {
        var skeleton = StandingTrent();
        // A zero time step keeps the pose and stops the root motion. Thus the model stays on the scene spot.
        skeleton.UpdateScripts(0);
        // Retail base scenes put a character at its floor height. An example is character_trent in
        // Rh_04_Shipdealer_enter_01, with Y = floor_height = 1.76.
        skeleton.FloorHeight = 1.76f;
        var character = Character(skeleton, new Vector3(5, 1.76f, -3));
        var (_, marker) = AttachScene(character, type, part);
        // The marker updates first, so it reads the scene position of the character.
        marker.Update();
        character.Update();

        var local = type == TargetTypes.Part ? skeleton.GetBone(part)! : skeleton.Hardpoints[part];
        AssertNear(local.Transform * character.Object!.WorldTransform, TransformOf(marker));
        // The head of a standing man is about 1.7 m above the floor.
        Assert.InRange(marker.Translate.Y - skeleton.FloorHeight, 1.5f, 1.9f);
    }

    // A marker on a bone reads the bone again on each frame. Each frame uses the order of Cutscene._Update: the
    // events and processors, then the scene objects, then GameWorld.Update, which moves the bones.
    [Theory]
    [InlineData("Body_Head")]
    [InlineData("Eye IK Left")]
    public void AMarkerOnABoneFollowsTheAnimation(string bone)
    {
        var skeleton = StandingTrent();
        var character = Character(skeleton, new Vector3(5, 1.76f, -3));
        var (instance, marker) = AttachScene(character, TargetTypes.Part, bone);
        var start = skeleton.GetBone(bone)!.Transform.Position;
        var moved = 0f;

        for (int i = 0; i < 40; i++)
        {
            instance.Update(0.1);
            character.Update();
            marker.Update();
            var pose = skeleton.GetBone(bone)!.Transform;
            AssertNear(pose * character.Object!.WorldTransform, TransformOf(marker));
            moved = MathF.Max(moved, Vector3.Distance(start, pose.Position));
            skeleton.UpdateScripts(0.1);
        }

        // The standing idle moves the head by some millimetres. The test is valid only if the bone moves.
        Assert.True(moved > 10 * Tolerance, $"The bone moved only {moved} m");
    }

    // ROOT gives the scene position. A part that is not found gives ROOT.
    [Theory]
    [InlineData(TargetTypes.Root, "Body_Head")]
    [InlineData(TargetTypes.Part, "No Such Bone")]
    public void AMarkerOnTheCharacterRootKeepsTheScenePosition(TargetTypes type, string part)
    {
        var skeleton = StandingTrent();
        skeleton.FloorHeight = 1.76f;
        var spot = new Vector3(5, 1.76f, -3);
        var (_, marker) = AttachScene(Character(skeleton, spot), type, part);
        marker.Update();

        AssertNear(new Transform3D(spot, Turn), TransformOf(marker));
    }

    // GetAttachParent also finds the parts and hardpoints of rigid models. The marker must be on the part or the
    // hardpoint of the model. ThnScriptInstance mounts a ship on its HpMount, so the model is not at the scene
    // position.
    [Theory]
    [InlineData(TargetTypes.Part, "Li_port_wing_lod1")]
    [InlineData(TargetTypes.Hardpoint, "HpEngine01")]
    public void AMarkerOnAShipFollowsTheModel(TargetTypes type, string part)
    {
        // li_elite has a turned wing part, and its HpMount is not at the origin.
        var ship = RetailData.Open().LoadModel(@"SHIPS\LIBERTY\li_elite\li_elite.cmp");
        var target = new ThnSceneObject()
        {
            Name = "target",
            Translate = new Vector3(5, 1.76f, -3),
            Rotate = Turn,
            Object = ship,
            HpMount = ship.GetHardpoint("HpMount")
        };
        Assert.NotNull(target.HpMount);
        var (_, marker) = AttachScene(target, type, part);
        marker.Update();
        target.Update();

        var local = type == TargetTypes.Part
            ? ship.Model!.RigidModel.Parts[part].LocalTransform
            : ship.GetHardpoint(part)!.Transform;
        AssertNear(local * ship.WorldTransform, TransformOf(marker));
    }
}
