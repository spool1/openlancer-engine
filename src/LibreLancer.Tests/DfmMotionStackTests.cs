using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.Utf.Anm;
using LibreLancer.World;
using LibreLancer.World.Components;
using Xunit;

namespace LibreLancer.Tests;

// The motion stack of DfmSkeletonManager, with retail characters and animations. The tests call UpdateScripts on
// each frame, as AnimationComponent does.
public class DfmMotionStackTests
{
    internal const string GenericMale = @"CHARACTERS\ANIMATIONS\BodyGenericMale.anm";
    private const string HandMale = @"CHARACTERS\ANIMATIONS\HandMale.anm";
    private const string Idle = "Sc_MLBODY_STND_IDLE_SMALL_000LV_xa_05";
    private const string Run = "Sc_MLBODY_RNNG_STND_TRNS_000LV_XA_07";
    private const string Turn = "Sc_MLBODY_STND_TURN_180LV_XA_04";
    private const string Walk = "Sc_MLBODY_WLKG_000LV_XA_01";
    private const string RightHand = "Sc_MLHAND_NEUT_RGHT_000LV_A_00";
    private const double Frame = 1.0 / 60;
    private const float OneDegree = MathF.PI / 180;

    // The animation files are large, so the tests load them one time.
    private static readonly Lazy<(RetailData Data, AnmFile Animations)> retail = new(() =>
    {
        var data = RetailData.Open();
        return (data, data.LoadAnimations(GenericMale, HandMale));
    });

    private static DfmSkeletonManager Character(string costume) => retail.Value.Data.LoadCostume(costume)!;

    private static Script Motion(string name) => retail.Value.Animations.Scripts[name];

    // Calls UpdateScripts for a time in steps of dt.
    private static void Step(DfmSkeletonManager skeleton, double seconds, double dt = Frame)
    {
        var count = (int)Math.Round(seconds / dt);
        for (var i = 0; i < count; i++)
        {
            skeleton.UpdateScripts(dt);
        }
    }

    // The bones of all parts by name. A body bone comes first, as in FindBone.
    private static Dictionary<string, BoneInstance> Bones(DfmSkeletonManager skeleton)
    {
        var bones = new Dictionary<string, BoneInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in new[]
                 {
                     skeleton.BodySkinning, skeleton.HeadSkinning, skeleton.LeftHandSkinning,
                     skeleton.RightHandSkinning
                 })
        {
            foreach (var (name, bone) in part?.Bones ?? [])
            {
                bones.TryAdd(name, bone);
            }
        }

        return bones;
    }

    // The rotation samples of a motion at a motion time, by bone name. The rule for loops is the rule of
    // DfmSkeletonManager.
    private static Dictionary<string, Quaternion> Sample(Script script, double time, bool loop = false)
    {
        var samples = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < script.JointMaps.Count; i++)
        {
            ref var jm = ref script.JointMaps[i];
            if (!jm.Channel.HasOrientation)
            {
                continue;
            }

            var t = (float)time;
            if (loop && t > jm.Channel.Duration)
            {
                t %= jm.Channel.Duration;
            }

            var cursor = 0;
            samples[jm.ChildName] = jm.Channel.QuaternionAtTime(t, ref cursor);
        }

        return samples;
    }

    private static Channel RootChannel(Script script) =>
        script.ObjectMaps.First(x => x.ParentName.Equals("Root", StringComparison.OrdinalIgnoreCase)).Channel;

    // The angle between two rotations. It is accurate also for small angles.
    private static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        return 2 * MathF.Asin(Math.Min(1f, new Vector3(d.X, d.Y, d.Z).Length()));
    }

    // Each bone that has an expected rotation must have it.
    private static void AssertPose(DfmSkeletonManager skeleton, Dictionary<string, Quaternion> expected,
        float tolerance)
    {
        var bones = Bones(skeleton);
        var count = 0;
        foreach (var (name, rotation) in expected)
        {
            if (!bones.TryGetValue(name, out var bone))
            {
                continue;
            }

            var angle = Angle(rotation, bone.Rotation);
            Assert.True(angle < tolerance, $"{name} is {angle} radians from the expected rotation");
            count++;
        }

        Assert.True(count > 0, "No bone of the expected pose is in the skeleton");
    }

    // The expected rotation of each bone of b: the blend of a and b with the weight w.
    private static Dictionary<string, Quaternion> Blend(Dictionary<string, Quaternion> a,
        Dictionary<string, Quaternion> b, float w) =>
        b.Where(x => a.ContainsKey(x.Key))
            .ToDictionary(x => x.Key, x => Quaternion.Slerp(a[x.Key], x.Value, w), StringComparer.OrdinalIgnoreCase);

    // The largest angle between the rotations of the bones that a and b both have.
    private static float LargestAngle(Dictionary<string, Quaternion> a, Dictionary<string, Quaternion> b) =>
        b.Where(x => a.ContainsKey(x.Key)).Max(x => Angle(a[x.Key], x.Value));

    // The motion time of a motion after a number of frames, as ScriptInstance.RunScript calculates it.
    private static double MotionTime(int frames, float timeScale = 1, float startTime = 0)
    {
        var t = 0.0;
        for (var i = 0; i < frames; i++)
        {
            t += Frame;
        }

        return (float)(t * timeScale) + startTime;
    }

    [Theory]
    [InlineData(0.25, 0.1464466f)]
    [InlineData(0.5, 0.5f)]
    [InlineData(0.75, 0.8535534f)]
    [InlineData(-1, 0f)]
    [InlineData(1, 1f)]
    [InlineData(2, 1f)]
    public void TheTransitionWeightIsACosineEase(double fraction, float weight)
    {
        Assert.Equal(weight, DfmSkeletonManager.TransitionWeight(fraction * 2, 2), 1e-6f);
    }

    [Fact]
    public void AZeroTransitionGivesTheFullWeight()
    {
        Assert.Equal(1f, DfmSkeletonManager.TransitionWeight(0, 0));
    }

    // s002xe 55.791: Trent starts to run while an idle runs. The run has no transition, so it takes the root at once.
    [Fact]
    public void TheNewestMotionMovesTheRoot()
    {
        var skeleton = Character("trent");
        skeleton.StartScript(Motion(Idle), 0, 0.5f, 5);
        Step(skeleton, 3.134);
        var run = Motion(Run);
        skeleton.StartScript(run, 0, 1, 3.5f);
        var root = RootChannel(run);
        var cursor = 0;
        var total = Vector3.Zero;
        for (var i = 0; i < 56; i++)
        {
            skeleton.UpdateScripts(Frame);
            Assert.True(skeleton.ApplyRootMotion, $"No root motion in frame {i}");
            if (i == 0)
            {
                // The idle does not move the root, also not in the first frame.
                var first = root.PositionAtTime((float)Frame, ref cursor) - root.PositionAtTime(0, ref cursor);
                Assert.True(Vector3.Distance(first, skeleton.RootTranslation) < 1e-5f,
                    $"Expected {first}, got {skeleton.RootTranslation} in the first frame");
            }

            total += skeleton.RootTranslation;
        }

        var expected = root.PositionAtTime((float)MotionTime(56), ref cursor) - root.PositionAtTime(0, ref cursor);
        Assert.InRange(expected.Length(), 3.6f, 3.9f);
        Assert.True(Vector3.Distance(expected, total) < 0.02f * expected.Length(), $"Expected {expected}, got {total}");
    }

    // s002xe 69.966: Syd turns half a circle while an idle runs. The turn has a transition of 0.7 s, but it moves the
    // root from its first frame.
    [Fact]
    public void TheTurnGivesItsWholeYaw()
    {
        var skeleton = Character("syd");
        skeleton.StartScript(Motion(Idle), 0, 0.5f, 10);
        Step(skeleton, 1.011);
        var turn = Motion(Turn);
        skeleton.StartScript(turn, 0, 1.5f, 2.953f, transition: 0.7f);
        var rotate = Quaternion.Identity;
        for (var i = 0; i < 200; i++)
        {
            skeleton.UpdateScripts(Frame);
            if (skeleton.ApplyRootMotion)
            {
                // As ThnSceneObject.UpdateEngineObjects.
                rotate *= skeleton.RootRotation;
            }
        }

        // The turn plays its whole channel: 2.953 s at the time scale 1.5.
        var root = RootChannel(turn);
        var cursor = 0;
        var expected = Quaternion.Inverse(root.QuaternionAtTime(0, ref cursor)) *
                       root.QuaternionAtTime(root.Duration, ref cursor);
        Assert.InRange(Angle(Quaternion.Identity, expected), 175 * OneDegree, 180 * OneDegree);
        Assert.True(Angle(expected, rotate) < 2 * OneDegree,
            $"The root turns {Angle(expected, rotate) / OneDegree} degrees less or more than the motion");
    }

    [Fact]
    public void AZeroTransitionTakesTheBonesAtOnce()
    {
        var skeleton = Character("trent");
        skeleton.StartScript(Motion(Idle), 0, 1, 0, true);
        Step(skeleton, 1);
        skeleton.StartScript(Motion(Walk), 0, 1, 0, true);
        skeleton.UpdateScripts(Frame);

        AssertPose(skeleton, Sample(Motion(Walk), MotionTime(1), true), 1e-4f);
    }

    // The new motion blends over the idle with the weight (1 - cos(pi * t / transition)) / 2.
    [Fact]
    public void ATransitionUsesTheCosineEase()
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        var walk = Motion(Walk);
        skeleton.StartScript(idle, 0, 1, 0, true);
        Step(skeleton, 1);
        skeleton.StartScript(walk, 0, 1, 0, true, transition: 1);

        Step(skeleton, 0.25);
        var under = Sample(idle, MotionTime(75), true);
        var over = Sample(walk, MotionTime(15), true);
        Assert.True(LargestAngle(under, over) > 10 * OneDegree, "The two motions have nearly the same pose");
        AssertPose(skeleton, Blend(under, over, 0.1464466f), 1e-3f);

        Step(skeleton, 0.25);
        AssertPose(skeleton, Blend(Sample(idle, MotionTime(90), true), Sample(walk, MotionTime(30), true), 0.5f),
            1e-3f);
    }

    // The walk ends and holds its last pose. Then the idle blends in from that pose. Held is the pose of the walk
    // bones before the idle starts.
    private static (Dictionary<string, Quaternion> Held, Dictionary<string, Quaternion> Pose) PoseAfterABlend(
        double dt)
    {
        var skeleton = Character("trent");
        var walk = Sample(Motion(Walk), 0);
        skeleton.StartScript(Motion(Walk), 0, 1, 0);
        Step(skeleton, 1.5, dt);
        var held = Bones(skeleton).Where(x => walk.ContainsKey(x.Key))
            .ToDictionary(x => x.Key, x => x.Value.Rotation, StringComparer.OrdinalIgnoreCase);
        skeleton.StartScript(Motion(Idle), 0, 1, 0, true, transition: 1);
        Step(skeleton, 0.5, dt);
        return (held, Bones(skeleton).ToDictionary(x => x.Key, x => x.Value.Rotation));
    }

    // No motion runs under the idle. It blends from the pose that the ended walk holds, not from the pose of the
    // last frame.
    [Fact]
    public void TheBlendDoesNotDependOnTheFrameRate()
    {
        var slow = PoseAfterABlend(1.0 / 30);
        var fast = PoseAfterABlend(1.0 / 144);

        AssertPoseEqual(slow.Pose, fast.Pose);
        var idle = Sample(Motion(Idle), 0.5, true);
        Assert.True(LargestAngle(slow.Held, idle) > 10 * OneDegree, "The two motions have nearly the same pose");
        AssertPoseEqual(Blend(slow.Held, idle, 0.5f), slow.Pose);
    }

    private static void AssertPoseEqual(Dictionary<string, Quaternion> expected, Dictionary<string, Quaternion> actual)
    {
        foreach (var (name, rotation) in expected)
        {
            var angle = Angle(rotation, actual[name]);
            Assert.True(angle < 1e-3f, $"{name} is {angle} radians from the expected rotation");
        }
    }

    // The turn replaces a walk that loops. After the turn ends, the bones keep its last pose, and the walk does not
    // come back. The walk also does not move the root again. The walk has a long duration, thus it stays in the
    // stack without its bones until the turn takes its root.
    [Fact]
    public void AnEndedMotionHoldsItsLastPose()
    {
        var skeleton = Character("trent");
        skeleton.StartScript(Motion(Walk), 0, 1, 100, true);
        Step(skeleton, 1);
        var turn = Motion(Turn);
        skeleton.StartScript(turn, 0, 1, 0);
        var end = turn.JointMaps.Max(x => x.Channel.Duration);
        Step(skeleton, end + 0.1);

        for (var i = 0; i < 60; i++)
        {
            skeleton.UpdateScripts(Frame);
            Assert.False(skeleton.ApplyRootMotion, $"A motion moves the root {i} frames after the turn ended");
        }

        AssertPose(skeleton, Sample(turn, end), 1e-4f);
    }

    // A bone that no motion has posed has the flat Cons pose. A new motion takes such a bone at once, also when the
    // motion has a transition and other bones already have a pose.
    [Fact]
    public void AMotionTakesABoneWithoutAPoseAtOnce()
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        skeleton.StartScript(idle, 0, 1, 0, true, transition: 1);
        skeleton.UpdateScripts(Frame);
        AssertPose(skeleton, Sample(idle, MotionTime(1), true), 1e-4f);

        // The body motion does not move the hand bones.
        var hand = Motion(RightHand);
        var handBones = Sample(hand, 0).Keys;
        Assert.DoesNotContain(handBones, x => Sample(idle, 0).ContainsKey(x));
        Step(skeleton, 1);
        skeleton.StartScript(hand, 0, 1, 0, true, transition: 0.5f);
        skeleton.UpdateScripts(Frame);
        AssertPose(skeleton, Sample(hand, MotionTime(1), true), 1e-4f);
    }

    // An overlay (event_flags 128) keeps the motion under it. When it ends, it blends back to that motion over its
    // transition.
    [Fact]
    public void AnOverlayBlendsBackToTheMotionUnderIt()
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        var walk = Motion(Walk);
        skeleton.StartScript(idle, 0, 1, 0, true);
        Step(skeleton, 1);
        // The overlay ends in the frame at 61/60 s.
        skeleton.StartScript(walk, 0, 1, 1.01f, transition: 0.5f, overlay: true);
        Step(skeleton, 61 * Frame);
        var last = Sample(walk, MotionTime(61));
        AssertPose(skeleton, last, 1e-4f);

        // The blend back uses the cosine ease. 0.1 s after the end, the overlay weight is 1 - (1 - cos(0.2 pi)) / 2.
        // A linear ramp gives 0.8.
        Step(skeleton, 6 * Frame);
        var under = Sample(idle, MotionTime(60 + 61 + 6), true);
        Assert.True(LargestAngle(under, last) > 10 * OneDegree, "The two motions have nearly the same pose");
        AssertPose(skeleton, Blend(under, last, 0.9045085f), 1e-3f);

        // A quarter of a second after the end, the blend back is at one half.
        Step(skeleton, 9 * Frame);
        AssertPose(skeleton, Blend(Sample(idle, MotionTime(60 + 61 + 15), true), last, 0.5f), 1e-3f);

        Step(skeleton, 20 * Frame);
        AssertPose(skeleton, Sample(idle, MotionTime(60 + 61 + 15 + 20), true), 1e-4f);
    }

    // An overlay that has no motion under it holds its last pose when it ends, as a motion that is not an overlay.
    [Fact]
    public void AnOverlayWithNoMotionUnderItHoldsItsLastPose()
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        skeleton.StartScript(Motion(Walk), 0, 1, 0);
        Step(skeleton, 1.5);
        skeleton.StartScript(idle, 0, 1, 1.01f, transition: 0.5f, overlay: true);
        Step(skeleton, 61 * Frame);
        var last = Sample(idle, MotionTime(61));
        Step(skeleton, 1);

        AssertPose(skeleton, last, 1e-4f);
    }

    // Without a transition, a new motion frees the motions under it, also when it is an overlay. Thus the overlay holds
    // its last pose when it ends.
    [Fact]
    public void AnOverlayWithoutATransitionHoldsItsLastPose()
    {
        var skeleton = Character("trent");
        var walk = Motion(Walk);
        skeleton.StartScript(Motion(Idle), 0, 1, 0, true);
        Step(skeleton, 1);
        skeleton.StartScript(walk, 0, 1, 1.01f, overlay: true);
        Step(skeleton, 61 * Frame);
        var last = Sample(walk, MotionTime(61));
        Step(skeleton, 1);

        AssertPose(skeleton, last, 1e-4f);
    }

    private static StartMotionEvent ReadEvent(float flags, float? transition, float duration = 2)
    {
        var targets = new ThornTable();
        targets.Append("Char_trent");
        var props = new ThornTable();
        props["animation"] = Walk;
        props["duration"] = duration;
        props["time_scale"] = 1f;
        props["weight"] = 1f;
        props["heading"] = -1f;
        props["event_flags"] = flags;
        if (transition != null)
        {
            props["trans_time"] = transition.Value;
        }

        var table = new ThornTable();
        table.Append(10.656f);
        table.Append(EventTypes.StartMotion);
        table.Append(targets);
        table.Append(props);
        return new StartMotionEvent(table);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(128, false, true)]
    [InlineData(130, true, true)]
    public void AStartMotionEventReadsLoopAndOverlayFromItsFlags(float flags, bool loop, bool overlay)
    {
        var ev = ReadEvent(flags, null);

        Assert.Equal(loop, ev.Loop);
        Assert.Equal(overlay, ev.Overlay);
    }

    [Fact]
    public void AStartMotionEventReadsItsTransition()
    {
        Assert.Equal(0f, ReadEvent(2, null).TransitionTime);
        var ev = ReadEvent(2, 0.7f);
        Assert.Equal(0.7f, ev.TransitionTime);

        // Cutscene.FidgetScript uses Clone.
        var clone = ev.Clone("Char_syd");
        Assert.Equal(["Char_syd"], clone.Targets);
        Assert.Equal(0.7f, clone.TransitionTime);
        Assert.Equal(ev.Flags, clone.Flags);
    }

    // Runs the event at 0 s on a character through a THN script, as a cutscene does.
    private static void RunEvent(DfmSkeletonManager skeleton, StartMotionEvent ev)
    {
        var obj = new GameObject() { RenderComponent = new CharacterRenderer(skeleton) };
        obj.AnimationComponent = new AnimationComponent(obj, retail.Value.Animations);
        ev.Time = 0;
        var script = new ThnScript() { Duration = 100 };
        script.Events.Add(ev);
        var instance = new ThnScriptInstance(null!, script);
        instance.Objects["Char_trent"] = new ThnSceneObject() { Name = "Char_trent", Object = obj };
        instance.Update(0);
    }

    // The event gives its transition to the skeleton of the character.
    [Fact]
    public void AStartMotionEventBlendsOverItsTransition()
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        skeleton.StartScript(idle, 0, 1, 0, true);
        Step(skeleton, 1);

        RunEvent(skeleton, ReadEvent(2, 1));
        Step(skeleton, 0.5);

        AssertPose(skeleton, Blend(Sample(idle, MotionTime(90), true), Sample(Motion(Walk), MotionTime(30), true), 0.5f),
            1e-3f);
    }

    // The event gives its loop and overlay flags to the skeleton of the character. The overlay plays past the end of
    // its channels, then it blends back to the idle under it.
    [Theory]
    [InlineData(128)]
    [InlineData(130)]
    public void AStartMotionEventPlaysItsFlags(int flags)
    {
        var skeleton = Character("trent");
        var idle = Motion(Idle);
        var walk = Motion(Walk);
        var loop = (flags & 2) != 0;
        skeleton.StartScript(idle, 0, 1, 0, true);
        Step(skeleton, 1);

        // The check comes about 0.25 s after the end of the walk channels. The event ends 16 frames after the check.
        var frames = (int)Math.Ceiling(walk.JointMaps.Max(x => x.Channel.Duration) / Frame) + 15;
        RunEvent(skeleton, ReadEvent(flags, 0.5f, (frames + 15.5f) * (float)Frame));
        Step(skeleton, frames * Frame);
        var played = Sample(walk, MotionTime(frames), loop);
        Assert.True(LargestAngle(played, Sample(walk, MotionTime(frames), !loop)) > 10 * OneDegree,
            "The walk has nearly the same pose with and without a loop");
        AssertPose(skeleton, played, 1e-4f);

        // The blend back takes 30 frames.
        Step(skeleton, 60 * Frame);
        var under = Sample(idle, MotionTime(60 + frames + 60), true);
        Assert.True(LargestAngle(under, Sample(walk, MotionTime(frames + 16), loop)) > 10 * OneDegree,
            "The walk ends in nearly the pose of the idle");
        AssertPose(skeleton, under, 1e-4f);
    }

    // Retail s009b has 43 events at 0 s. A stable sort keeps them in the order of the file. List.Sort is not stable
    // for more than 16 elements.
    [Fact]
    public void ThnScriptKeepsTheFileOrderOfEventsAtTheSameTime()
    {
        var data = retail.Value.Data;
        const string path = @"SCRIPTS\STORY\s009b_accept_King_li_01_Bar_01.thn";
        var bytes = data.ReadDataFile(path);
        var tables = ((ThornTable)new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(bytes, path)["events"])
            .Values.Cast<ThornTable>();
        var expected = tables.Select(x => (Time: (float)x[1], Text: Describe(x))).OrderBy(x => x.Time)
            .Select(x => x.Text).ToList();
        Assert.Equal(43, tables.Count(x => (float)x[1] == 0));

        var actual = new ThnScript(bytes, data.ReadThornFile, path).Events
            .Select(x => FormattableString.Invariant($"{x.Time} {x.Type} {string.Join(",", x.Targets)}")).ToList();

        Assert.Equal(expected, actual);
    }

    private static string Describe(ThornTable table)
    {
        var targets = (ThornTable)table[3];
        var names = Enumerable.Range(1, targets.Length).Select(i => (string)targets[i]);
        return FormattableString.Invariant(
            $"{(float)table[1]} {ThnTypes.Convert<EventTypes>(table[2])} {string.Join(",", names)}");
    }
}
