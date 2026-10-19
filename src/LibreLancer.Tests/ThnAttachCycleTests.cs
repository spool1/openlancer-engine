using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using Xunit;

namespace LibreLancer.Tests;

// Attachment cycles. The scripts copy only the structure of the camera rigs of the retail data: the names, the poses
// and the offsets are new. Each frame uses the order of Cutscene._Update: the events and processors run, then each
// scene object updates.
public class ThnAttachCycleTests
{
    private static ThnSceneObject Marker(string name, Vector3 position) =>
        new() { Name = name, Translate = position, Rotate = Quaternion.Identity };

    private static AttachEntityEvent Attach(float time, float duration, string child, string parent,
        AttachFlags flags, Vector3 offset = default) => new()
    {
        Time = time,
        Duration = duration,
        Targets = [child, parent],
        TargetType = TargetTypes.Root,
        Flags = flags,
        Offset = offset,
    };

    private static ThnScriptInstance Instance(IEnumerable<ThnEvent> events, params ThnSceneObject[] objects)
    {
        var script = new ThnScript { Duration = 100 };
        script.Events.AddRange(events);
        var instance = new ThnScriptInstance(null!, script);
        foreach (var obj in objects)
        {
            instance.Objects[obj.Name] = obj;
        }

        return instance;
    }

    // One frame in the order of Cutscene._Update.
    private static void Frame(ThnScriptInstance instance, double seconds)
    {
        instance.Update(seconds);
        foreach (var obj in instance.Objects.Values)
        {
            obj.Update();
        }
    }

    private static void AssertFinite(ThnSceneObject obj)
    {
        Assert.True(float.IsFinite(obj.Translate.X) && float.IsFinite(obj.Translate.Y) &&
                    float.IsFinite(obj.Translate.Z), obj.Name);
        Assert.True(float.IsFinite(obj.Rotate.X) && float.IsFinite(obj.Rotate.Y) && float.IsFinite(obj.Rotate.Z) &&
                    float.IsFinite(obj.Rotate.W), obj.Name);
    }

    // The rig of a sit conversation: at 0 s a reference marker takes the pose of the camera for one update, an offset
    // marker follows the reference 1 m below it, and the camera moves to the offset marker for one update. The three
    // attachments make a cycle at 0 s. Retail moves the camera down by the offset one time.
    [Fact]
    public void ACycleAtTimeZeroMovesTheCameraByTheOffsetOnce()
    {
        var start = new Vector3(2, 3, 4);
        var camera = Marker("camera", start);
        var reference = Marker("reference", start);
        var offset = Marker("offset", start);
        var instance = Instance(
        [
            Attach(0, 0, "reference", "camera", AttachFlags.Position | AttachFlags.Orientation),
            Attach(0, 50, "offset", "reference", AttachFlags.Position | AttachFlags.Orientation, new Vector3(0, -1, 0)),
            Attach(0, 0, "camera", "offset", AttachFlags.Position),
        ], camera, reference, offset);

        // Cutscene.SceneSetup runs the events at 0 s, then the build applies the poses of 0 s.
        instance.Update(0);
        Frame(instance, 0);
        for (var i = 0; i < 10; i++)
        {
            Frame(instance, 1.0 / 60);
        }

        Assert.Equal(start + new Vector3(0, -1, 0), camera.Translate);
        Assert.Empty(camera.Attachments);
        Assert.Empty(reference.Attachments);
        foreach (var obj in instance.Objects.Values)
        {
            AssertFinite(obj);
        }
    }

    // The rig of a stand conversation: the events at 1, 2, 3 and 4 ms start in the same 60 Hz frame. A target marker
    // goes in front of the camera for one update, then follows the actor. A chaser marker goes to the target for one
    // update, and the camera looks at the chaser. In that frame, the camera, the chaser and the target make a cycle.
    [Fact]
    public void ACycleThatClosesInOneFrameDoesNotRecurse()
    {
        var camera = Marker("camera", new Vector3(0, 2, 5));
        var actor = Marker("actor", new Vector3(1, 0, 0));
        var target = Marker("target", new Vector3(5, 0, 5));
        var chaser = Marker("chaser", new Vector3(5, 0, 5));
        var instance = Instance(
        [
            Attach(0.001f, 0, "target", "camera",
                AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative, new Vector3(0, 0, -2)),
            Attach(0.002f, 50, "target", "actor",
                AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative | AttachFlags.ParentChild),
            Attach(0.003f, 0, "chaser", "target", AttachFlags.Position | AttachFlags.Orientation),
            Attach(0.004f, 50, "camera", "chaser", AttachFlags.LookAt),
        ], camera, actor, target, chaser);

        instance.Update(0);
        Frame(instance, 0);
        for (var i = 0; i < 10; i++)
        {
            Frame(instance, 1.0 / 60);
        }

        // The target and the chaser are 2 m in front of the camera, and the camera looks at the chaser.
        Assert.Equal(new Vector3(0, 2, 3), chaser.Translate);
        var forward = Vector3.Transform(-Vector3.UnitZ, camera.Rotate);
        Assert.True(Vector3.Distance(Vector3.Normalize(chaser.Translate - camera.Translate), forward) < 1e-4f);
        Assert.Single(camera.Attachments);
        foreach (var obj in instance.Objects.Values)
        {
            AssertFinite(obj);
        }
    }

    [Fact]
    public void AnObjectAttachedToItselfKeepsItsPose()
    {
        var marker = Marker("marker", new Vector3(1, 2, 3));
        var instance = Instance([Attach(0, 10, "marker", "marker", AttachFlags.Position | AttachFlags.Orientation)],
            marker);

        instance.Update(0);
        for (var i = 0; i < 10; i++)
        {
            Frame(instance, 1.0 / 60);
        }

        Assert.Equal(new Vector3(1, 2, 3), marker.Translate);
        AssertFinite(marker);
    }

    // A chain of attachments that is too deep for the stack stops with an exception that the caller can catch. The
    // editor preview then stops and shows the message. A stack overflow would end the process.
    [Fact]
    public void ADeepChainStopsWithAnExceptionThatTheCallerCanCatch()
    {
        const int depth = 200_000;
        var chain = new ThnSceneObject[depth];
        for (var i = depth - 1; i >= 0; i--)
        {
            chain[i] = Marker($"m{i}", Vector3.Zero);
            if (i < depth - 1)
            {
                chain[i].Attachments.Add(new ThnAttachment(new ThnObjectParent(chain[i + 1], null, null))
                {
                    Position = true,
                });
            }
        }

        var error = Assert.Throws<InvalidOperationException>(() => chain[0].GetTransform());
        Assert.Contains("make a chain that is too deep to resolve", error.Message);
        // The exception resets the guard of each object that it left. Else the first object would give its stored pose
        // as the end of a cycle, and the second call would not throw.
        Assert.Throws<InvalidOperationException>(() => chain[0].GetTransform());
        // A short chain from the end resolves.
        Assert.Equal(Vector3.Zero, chain[depth - 3].GetTransform().Position);
    }

    // Retail ends an attach of 0 s at its start time: it applies the attach one time and stops it. An attach of 0 s at
    // 0 s takes the pose of its parent at 0 s, also when the parent moves in the first frame.
    [Fact]
    public void AZeroDurationAttachAtTimeZeroTakesThePoseOfTimeZero()
    {
        var parent = Marker("parent", Vector3.Zero);
        var child = Marker("child", new Vector3(5, 5, 5));
        var instance = Instance(
        [
            new StartSpatialPropAnimEvent
            {
                Duration = 1, Targets = ["parent"], SetFlags = StartSpatialPropAnimEvent.AnimVars.Pos,
                Pos = new Vector3(64, 0, 0),
            },
            Attach(0, 0, "child", "parent", AttachFlags.Position),
        ], parent, child);

        instance.Update(0);
        Frame(instance, 0);
        for (var i = 0; i < 4; i++)
        {
            Frame(instance, 1.0 / 64);
        }

        Assert.Equal(new Vector3(4, 0, 0), parent.Translate);
        Assert.Equal(Vector3.Zero, child.Translate);
        Assert.Empty(child.Attachments);
    }
}
