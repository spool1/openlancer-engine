using System;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using Xunit;

namespace LibreLancer.Tests;

// The flags of ATTACH_ENTITY, with the rules of the retail attach update in THORN. Each frame uses the order of
// Cutscene._Update: the events and processors run, then each scene object updates.
public class ThnAttachFlagsTests
{
    private const float Tolerance = 1e-4f;
    private const double Frame = 1.0 / 60;
    private const string S004x = @"SCRIPTS\STORY\s004x_nrml_juni_li_01_Bar_01.thn";
    private const string S009b = @"SCRIPTS\STORY\s009b_accept_King_li_01_Bar_01.thn";
    private const string S027xa = @"SCRIPTS\STORY\s027xa_nrml_sinclair_pl_04_Pad_01.thn";
    private const string S028x = @"SCRIPTS\STORY\s028x_nrml_sinclair_br_08_Bar_01.thn";
    private static readonly Quaternion Yaw90 = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
    private static readonly Quaternion Tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
    private static readonly Quaternion Pitch40 = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 40 * MathF.PI / 180);
    private static readonly Vector3 ParentStart = new(1, 2, 3);

    private static ThnSceneObject Entity(string name, Vector3 position, Quaternion rotation) =>
        new() { Name = name, Translate = position, Rotate = rotation };

    // Attaches "child" to "parent" from 0 s for 100 s.
    private static AttachEntityEvent Attach(AttachFlags flags, Vector3 offset = default) => new()
    {
        Duration = 100,
        Targets = ["child", "parent"],
        TargetType = TargetTypes.Root,
        Flags = flags,
        Offset = offset
    };

    private static ThnScriptInstance Scene(ThnEvent[] events, params ThnSceneObject[] objects)
    {
        var script = new ThnScript() { Duration = 100 };
        script.Events.AddRange(events);
        var instance = new ThnScriptInstance(null!, script);
        foreach (var obj in objects)
        {
            instance.Objects[obj.Name] = obj;
        }

        // Cutscene.SceneSetup runs the events at T=0.
        instance.Update(0);
        return instance;
    }

    private static void Frames(ThnScriptInstance instance, int count)
    {
        for (var i = 0; i < count; i++)
        {
            instance.Update(Frame);
            foreach (var obj in instance.Objects.Values)
            {
                obj.Update();
            }
        }
    }

    // The angle of the turn from a to b. Atan2 keeps small angles exact.
    private static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Normalize(Quaternion.Conjugate(a) * b);
        return 2 * MathF.Atan2(new Vector3(d.X, d.Y, d.Z).Length(), MathF.Abs(d.W));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = Tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"Expected {expected}, got {actual}");

    private static void AssertNear(Quaternion expected, Quaternion actual, float tolerance = Tolerance) =>
        Assert.True(Angle(expected, actual) < tolerance, $"Expected {expected}, got {actual}");

    private static Vector3 Forward(ThnSceneObject obj) => Vector3.Transform(-Vector3.UnitZ, obj.Rotate);

    // The position of a child that keeps its start position in the axes of the parent. The parent starts at
    // ParentStart with the rotation Pitch40.
    private static Vector3 Rigid(Vector3 start, ThnSceneObject parent) => parent.Translate +
        Vector3.Transform(Vector3.Transform(start - ParentStart, Quaternion.Inverse(Pitch40)), parent.Rotate);

    // PARENT_CHILD keeps the child rigid to the parent: the child keeps its pose relative to the parent from the start
    // of the attach. The offset and the other flags have no effect. 230 is the flags of the terrain pieces in
    // Ambi_Terrain_Static_D01. 187 of its 192 pieces have the offset (2100, 0, 0).
    [Theory]
    [InlineData(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.ParentChild)]
    [InlineData(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative |
                AttachFlags.ParentChild | AttachFlags.OrientationRelative)]
    public void AParentChildAttachIsRigid(AttachFlags flags)
    {
        var start = new Vector3(0, 0.7f, 0.3f);
        var child = Entity("child", start, Tilt);
        var parent = Entity("parent", ParentStart, Pitch40);
        var instance = Scene([Attach(flags, new(2100, 0, 0))], child, parent);
        Frames(instance, 1);

        AssertNear(start, child.Translate);
        AssertNear(Tilt, child.Rotate);

        parent.Translate = new(5, 0, 0);
        parent.Rotate = Yaw90;
        Frames(instance, 1);

        AssertNear(Rigid(start, parent), child.Translate);
        AssertNear(Yaw90 * Quaternion.Inverse(Pitch40) * Tilt, child.Rotate);
    }

    // POSITION and ORIENTATION select the half of the rigid pose that PARENT_CHILD writes. PARENT_CHILD reads no other
    // flag, so LOOK_AT does not turn the child. No retail attach has both PARENT_CHILD and LOOK_AT.
    [Theory]
    [InlineData(AttachFlags.Position | AttachFlags.ParentChild)]
    [InlineData(AttachFlags.Position | AttachFlags.ParentChild | AttachFlags.LookAt)]
    public void AParentChildAttachWritesOnlyTheHalfOfItsFlags(AttachFlags flags)
    {
        var start = new Vector3(0, 0.7f, 0.3f);
        var child = Entity("child", start, Tilt);
        var parent = Entity("parent", ParentStart, Pitch40);
        var instance = Scene([Attach(flags)], child, parent);

        parent.Translate = new(5, 0, 0);
        parent.Rotate = Yaw90;
        Frames(instance, 1);

        AssertNear(Rigid(start, parent), child.Translate);
        AssertNear(Tilt, child.Rotate);
    }

    // ORIENTATION_RELATIVE writes P.R * rel.R, so the child keeps its starting turn relative to the parent. A child of
    // the child, or a move onto it, reads its transform before its own update. Such a read must not use up the turn of
    // the parent.
    [Fact]
    public void AnOrientationRelativeAttachKeepsTheStartingTurn()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 6) * Tilt;
        var child = Entity("child", Vector3.Zero, start);
        var parent = Entity("parent", ParentStart, Pitch40);
        var instance = Scene([
            Attach(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.OrientationRelative)
        ], child, parent);
        Frames(instance, 1);
        AssertNear(start, child.Rotate);

        for (var i = 1; i <= 30; i++)
        {
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.1f * i) *
                       Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.05f * i);
            parent.Rotate = turn * Pitch40;
            child.GetTransform();
            Frames(instance, 1);
            AssertNear(turn * start, child.Rotate);
        }
    }

    // Without ORIENTATION, ORIENTATION_RELATIVE writes no rotation. The retail scripts have 6 attaches with POSITION,
    // ENTITY_RELATIVE and ORIENTATION_RELATIVE.
    [Fact]
    public void AnOrientationRelativeAttachWithoutOrientationKeepsTheRotation()
    {
        var child = Entity("child", Vector3.Zero, Tilt);
        var parent = Entity("parent", ParentStart, Pitch40);
        var instance = Scene([
            Attach(AttachFlags.Position | AttachFlags.EntityRelative | AttachFlags.OrientationRelative, new(0, 0, 2))
        ], child, parent);

        parent.Rotate = Yaw90;
        Frames(instance, 1);

        // The yaw of 90 degrees turns +Z to +X.
        AssertNear(ParentStart + new Vector3(2, 0, 0), child.Translate);
        AssertNear(Tilt, child.Rotate);
    }

    // fp7_01 and fp7_03 aim their cameras 28 m below the look target. The offset is in the scene axes, so the pitch of
    // the parent does not turn it.
    [Fact]
    public void ALookAtAimsAtTheParentPlusTheOffset()
    {
        var camera = Entity("child", Vector3.Zero, Quaternion.Identity);
        var target = Entity("parent", new(0, 0, -100), Yaw90 * Pitch40);
        var instance = Scene([Attach(AttachFlags.LookAt, new(0, -28, 0))], camera, target);
        Frames(instance, 1);

        AssertNear(Vector3.Normalize(new(0, -28, -100)), Forward(camera));
    }

    // With ENTITY_RELATIVE, the offset is in the axes of the parent. Ten retail cameras use LOOK_AT with
    // ENTITY_RELATIVE and an offset, for example in M06_Race_Hood1.
    [Fact]
    public void AnEntityRelativeLookAtTurnsTheOffsetWithTheParent()
    {
        var camera = Entity("child", Vector3.Zero, Quaternion.Identity);
        var target = Entity("parent", new(0, 0, -100), Yaw90);
        var instance = Scene([Attach(AttachFlags.LookAt | AttachFlags.EntityRelative, new(0, 0, 25))], camera,
            target);
        Frames(instance, 1);

        // The yaw of 90 degrees turns +Z to +X.
        AssertNear(Vector3.Normalize(new(25, 0, -100)), Forward(camera));
    }

    // LOOK_AT yields to ORIENTATION.
    [Fact]
    public void AnOrientationLookAtCopiesTheRotationOfTheParent()
    {
        var child = Entity("child", Vector3.Zero, Quaternion.Identity);
        var parent = Entity("parent", new(0, 0, -100), Yaw90);
        var instance = Scene([Attach(AttachFlags.Orientation | AttachFlags.LookAt)], child, parent);
        Frames(instance, 1);

        AssertNear(Yaw90, child.Rotate);
    }

    // Retail writes the position before the look. With POSITION, the child is on its target, so the look vector is
    // zero and the child keeps its rotation. The offset is in the scene axes.
    [Fact]
    public void APositionLookAtMovesTheChildAndKeepsItsRotation()
    {
        var child = Entity("child", Vector3.Zero, Tilt);
        var parent = Entity("parent", new(0, 0, -100), Yaw90 * Pitch40);
        var instance = Scene([Attach(AttachFlags.Position | AttachFlags.LookAt, new(0, -28, 0))], child, parent);
        Frames(instance, 1);

        AssertNear(new Vector3(0, -28, -100), child.Translate);
        AssertNear(Tilt, child.Rotate);
    }

    // The look turns the front axis to the target and the up axis toward the scene up. The retail spotlights of s027xa,
    // s051xa and s051xb shine along +Z and use front = Z_AXIS.
    [Theory]
    [InlineData(ThnAxis.ZAxis, ThnAxis.YAxis)]
    [InlineData(ThnAxis.XAxis, ThnAxis.NegZAxis)]
    [InlineData(ThnAxis.NegYAxis, ThnAxis.XAxis)]
    public void ALookAtAimsTheFrontAxisAndTurnsTheUpAxisUp(ThnAxis front, ThnAxis up)
    {
        var child = Entity("child", new(1, 2, 3), Quaternion.Identity);
        var parent = Entity("parent", new(4, 6, -9), Quaternion.Identity);
        var look = Attach(AttachFlags.LookAt);
        look.Front = front;
        look.Up = up;
        var instance = Scene([look], child, parent);
        Frames(instance, 1);

        var f = Vector3.Normalize(parent.Translate - child.Translate);
        AssertNear(f, Vector3.Transform(ThnTypes.AxisVector(front), child.Rotate));
        var u = Vector3.Normalize(Vector3.UnitY - Vector3.Dot(Vector3.UnitY, f) * f);
        AssertNear(u, Vector3.Transform(ThnTypes.AxisVector(up), child.Rotate));
    }

    // Retail runs the path animations of an object before its attachments. Thus a look aims from the point on the path
    // in the same frame, and it replaces the orientation of the path. In s006x and s070x, a path with ORIENTATION
    // starts on a camera that already has a LOOK_AT.
    [Fact]
    public void ALookAtAimsFromThePathAndReplacesItsOrientation()
    {
        var path = ThnPathAnimationTests.PathEntity(ThnPathAnimationTests.ParsePath("OPEN, " +
            ThnPathAnimationTests.Key(Vector3.Zero, Yaw90) + ThnPathAnimationTests.Key(new(100, 0, 0), Yaw90)),
            Quaternion.Identity);
        var camera = Entity("camera", Vector3.Zero, Quaternion.Identity);
        var target = Entity("target", new(0, 0, -100), Quaternion.Identity);
        var look = new AttachEntityEvent()
        {
            Duration = 100, Targets = ["camera", "target"], Flags = AttachFlags.LookAt
        };
        var move = new StartPathAnimationEvent()
        {
            Time = 0.5f,
            Duration = 1,
            StopPercent = 1,
            Targets = ["camera", "path"],
            Flags = AttachFlags.Position | AttachFlags.Orientation
        };
        var instance = Scene([look, move], camera, target, path);
        Frames(instance, 60);

        Assert.True(camera.Translate.X > 20, $"The camera is at {camera.Translate}");
        AssertNear(Vector3.Normalize(target.Translate - camera.Translate), Forward(camera));
    }

    private static AttachEntityEvent ReadEvent(params (string Key, object Value)[] values)
    {
        var targets = new ThornTable();
        targets.Append("child");
        targets.Append("parent");
        var props = new ThornTable();
        props["duration"] = 1f;
        props["flags"] = 8f;
        foreach (var (key, value) in values)
        {
            props[key] = value;
        }

        var table = new ThornTable();
        table.Append(0f);
        table.Append(EventTypes.AttachEntity);
        table.Append(targets);
        table.Append(props);
        return new AttachEntityEvent(table, "test");
    }

    // Retail scripts give an axis as a name or as its number. 722 retail attaches use the numbers.
    [Fact]
    public void AnAttachEventReadsItsAxes()
    {
        var ev = ReadEvent(("front", ThnAxis.ZAxis), ("up", 4f));

        Assert.Equal(ThnAxis.ZAxis, ev.Front);
        Assert.Equal(ThnAxis.NegYAxis, ev.Up);
    }

    // THORN gives up = Y_AXIS and front = NEG_Z_AXIS when the event has no axes.
    [Fact]
    public void AnAttachEventWithoutAxesUsesTheRetailDefaults()
    {
        var ev = ReadEvent();

        Assert.Equal(ThnAxis.YAxis, ev.Up);
        Assert.Equal(ThnAxis.NegZAxis, ev.Front);
    }

    // s027xa at 0 s: ambi_LtG03_Tunnel_Ext_L01 is a spotlight that shines along its +Z axis. Its LOOK_AT has
    // front = Z_AXIS, and its pose in the script aims +Z at the marker. ambi_LtG03_Tunnel_Ext_R01 is 505 m away in the
    // axes of L01, with the rotation of L01 relative to its own (ORIENTATION_RELATIVE). Both lights stay at their poses
    // in the script.
    [Fact]
    public void TheTunnelLightsOfS027xaStayAtTheirPoses()
    {
        var replay = new RetailThnReplay(S027xa, [], ev =>
            ev is AttachEntityEvent && ev.Targets[0].StartsWith("ambi_LtG03_Tunnel_Ext_", StringComparison.Ordinal));
        replay.RunTo(0.5);

        foreach (var name in new[] { "ambi_LtG03_Tunnel_Ext_L01", "ambi_LtG03_Tunnel_Ext_R01" })
        {
            var light = replay[name];
            // The lights are 465 m from the origin, where the step of a float is 0.03 mm.
            AssertNear(replay.Spawn(name).Position, light.Translate, 0.01f);
            AssertNear(replay.Spawn(name).Orientation, light.Rotate, 0.002f);
        }
    }

    // A PARENT_CHILD attach to a hardpoint of Trent keeps the child at its pose in the script, and then keeps it rigid
    // to the hardpoint. s009b: the look marker of Juni is rigid to the neck of Trent from 0 s, and it stays at the
    // neck height. s028x: the crystal is rigid to the left hand of Trent from 0.001 s, and Trent walks 3 m before 6 s.
    // These two attaches start before the first animation update of Trent. Both scripts place the child near the
    // posed hardpoint. Before its first pose, the neck of Trent is 0.97 m lower, so a relative pose from that time
    // puts the marker 0.93 m too high. s028x at 42 s moves the crystal onto a marker, and at 42.169 s the crystal is
    // rigid to the hand again. A relative pose against the origin of Trent, not his hand, puts the crystal 0.5 m off.
    [Theory]
    [InlineData(S009b, "Char_Juni_Mrk_LookAtTrent", "Char_Juni_Mrk_LookAtTrent", "hp_neck", 1.0, 9.0)]
    [InlineData(S028x, "prop_artifact_crystal", "prop_artifact_crystal", "hpleftconnect", 0.1, 6.0)]
    [InlineData(S028x, "prop_artifact_crystal", "prop_artifact_crystal_MKR_p20", "hpleftconnect", 42.2, 46.0)]
    public void AnAttachToAHardpointOfTrentStartsAtThePoseInTheScript(string script, string child, string pose,
        string hardpoint, double start, double end)
    {
        var replay = new RetailThnReplay(script, RetailThnReplay.MaleBodies, ev =>
            ev.Targets[0] == "Char_Trent" || ev.Targets[0] == child);
        var trent = replay["Char_Trent"].Object!;
        var skeleton = ((CharacterRenderer)trent.RenderComponent!).Skeleton;
        // The distance from the child to the hardpoint, with the bones and the model height of the last animation
        // update. A rigid attach keeps it. The vertical root motion changes the model height in the animation update,
        // and the scene gives that height to the model in the next frame. Thus the test puts it into the model
        // transform.
        float Distance()
        {
            var model = trent.Transform;
            model.Position = model.Position with { Y = skeleton.ModelHeight };
            return Vector3.Distance(replay[child].GetTransform().Position,
                (skeleton.Hardpoints[hardpoint].Transform * model).Position);
        }

        replay.RunTo(start);
        Assert.True(skeleton.Posed, "Trent has no pose");
        AssertNear(replay.Spawn(pose).Position, replay[child].Translate, 0.02f);
        var distance = Distance();
        replay.RunTo(end);
        Assert.True(MathF.Abs(Distance() - distance) < 0.005f,
            $"The distance from {child} to {hardpoint} changes from {distance} m to {Distance()} m");
    }

    // s004x from 57.992 s: Cam_H_Mk_Trent is rigid to the Root bone of Trent, and Cam_H_Mk_Offset is rigid to
    // Cam_H_Mk_Trent. The close-up from 62.625 s to 67.125 s aims at a marker that follows Cam_H_Mk_Offset. Without
    // PARENT_CHILD, both markers went onto the Root bone at the hip. The close-up then aimed 8 to 13 degrees low, and
    // the head of Trent left the top of the frame. A relative pose in the wrong order moves Cam_H_Mk_Offset about 9 m,
    // and the head leaves the side of the frame.
    [Fact]
    public void TheLastCloseUpOfS004xKeepsTheHeadOfTrentInTheFrame()
    {
        // The close-up camera has fovh 10, which is the horizontal half-angle on a 4:3 screen. With the letterbox
        // aspect 1.85, the vertical half-angle is 5.4 degrees.
        const float horizontal = 10f;
        const float vertical = 5.4f;
        var replay = new RetailThnReplay(S004x, RetailThnReplay.MaleBodies, ev =>
            (ev.Targets[0] == "Char_Trent" && ev is not AttachEntityEvent { Duration: > 0 }) ||
            ev.Targets[0] is "Cam_H_Mk_Trent" or "Cam_H_Mk_Offset" or "Cam_H_Mk_Follow" or "Cam_H_CU_Static_Trent");

        replay.RunTo(62.625);
        Assert.InRange(replay["Cam_H_Mk_Offset"].Translate.Y, 1.47f, 1.67f);
        foreach (var time in new[] { 62.625, 64.0, 65.5, 67.0 })
        {
            replay.RunTo(time);
            var camera = replay["Cam_H_CU_Static_Trent"];
            var trent = replay["Char_Trent"].Object!;
            var skeleton = ((CharacterRenderer)trent.RenderComponent!).Skeleton;
            var head = (skeleton.Hardpoints["hp_head"].Transform * trent.Transform).Position;
            var view = Vector3.Transform(head - camera.Translate, Quaternion.Inverse(camera.Rotate));
            var elevation = MathF.Atan2(view.Y, -view.Z) * 180 / MathF.PI;
            var azimuth = MathF.Atan2(view.X, -view.Z) * 180 / MathF.PI;
            Assert.True(MathF.Abs(elevation) < vertical && MathF.Abs(azimuth) < horizontal,
                $"At {time} s, the head of Trent is {elevation} degrees above and {azimuth} degrees right of " +
                "the center of the close-up");
        }
    }
}
