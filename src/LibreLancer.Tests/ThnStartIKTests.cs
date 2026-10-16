using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.Utf.Anm;
using LibreLancer.World;
using LibreLancer.World.Components;
using Xunit;

namespace LibreLancer.Tests;

// Uses Trent from the retail data in the standing idle. Each frame calls UpdateScripts and then ApplyIK, as
// AnimationComponent does.
public class ThnStartIKTests
{
    internal const float OneDegree = MathF.PI / 180;

    // The character stands on a slope, so world up is not model up.
    internal static readonly Transform3D World =
        new(new Vector3(5, 1.76f, -3), Quaternion.CreateFromYawPitchRoll(1, 0.2f, 0.1f));

    private static DfmSkeletonManager StandingTrent() => RetailData.Open().LoadPosedCostume("trent",
        @"CHARACTERS\ANIMATIONS\BodyGenericMale.anm", "Sc_MLBODY_STND_IDLE_000LV_xa_04");

    internal static void Frame(DfmSkeletonManager skeleton)
    {
        // A zero time step keeps the animation pose. Thus only IK changes the bones.
        skeleton.UpdateScripts(0);
        skeleton.ApplyIK(World);
    }

    internal static DfmSkeletonManager.IKGoal Goal(string bone, ThnAxis up, ThnAxis front, Vector3 target,
        float weight = 1, int count = 1) => new()
    {
        Bone = bone,
        CountToRoot = count,
        Up = ThnTypes.AxisVector(up),
        Front = ThnTypes.AxisVector(front),
        TargetWorld = target,
        Weight = weight
    };

    // The body, the head and the hands.
    private static IEnumerable<DfmSkinning> Parts(DfmSkeletonManager skeleton) =>
        new[] { skeleton.BodySkinning, skeleton.HeadSkinning, skeleton.LeftHandSkinning, skeleton.RightHandSkinning }
            .OfType<DfmSkinning>();

    internal static BoneInstance Bone(DfmSkeletonManager skeleton, string name) =>
        Parts(skeleton).First(x => x.Bones.ContainsKey(name)).Bones[name];

    // A point near a bone. The offset is in model space: -X is to the left of the character, +Y is up and -Z is
    // to the front.
    internal static Vector3 Near(DfmSkeletonManager skeleton, string bone, Vector3 offset) =>
        World.Transform(skeleton.GetBone(bone)!.Transform.Position + offset);

    // The world orientation of the IK axes of a bone. The IK axes do not include the Cons rotation of the bone.
    private static Quaternion Axes(DfmSkeletonManager skeleton, string bone) =>
        (skeleton.GetBone(bone)!.Transform * World).Orientation *
        Quaternion.Inverse(Bone(skeleton, bone).OriginalRotation);

    private static float Angle(Vector3 a, Vector3 b) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f));

    private static float Angle(Quaternion q) => 2 * MathF.Acos(Math.Min(1f, MathF.Abs(q.W)));

    // The angle between the front axis of a bone and the direction to the target.
    private static float AngleToTarget(DfmSkeletonManager skeleton, string bone, ThnAxis front, Vector3 target) =>
        Angle(Vector3.Transform(ThnTypes.AxisVector(front), Axes(skeleton, bone)),
            target - (skeleton.GetBone(bone)!.Transform * World).Position);

    internal static void AssertPointsAt(DfmSkeletonManager skeleton, string bone, ThnAxis front, Vector3 target)
    {
        var angle = AngleToTarget(skeleton, bone, front, target);
        Assert.True(angle < OneDegree, $"{bone} points {angle / OneDegree} degrees away from the target");
    }

    // When an eye does not roll, the X axis of the joint in its Cons frame stays level and points to +X.
    private static void AssertNoRoll(BoneInstance eye)
    {
        var joint = Quaternion.Inverse(eye.OriginalRotation) * eye.IKRotation * eye.Rotation * eye.OriginalRotation;
        var x = Vector3.Transform(Vector3.UnitX, joint);
        Assert.InRange(x.Y, -1e-3f, 1e-3f);
        Assert.True(x.X > 0.8f, $"The eye rolls: its X axis is {x}");
    }

    // The eye limits are +-15 degrees of pitch, +-30 degrees of yaw and no roll. The eye must be inside the limits
    // and at the yaw limit.
    private static void AssertAtTheYawLimit(BoneInstance eye)
    {
        var joint = Quaternion.Inverse(eye.OriginalRotation) * eye.IKRotation * eye.Rotation * eye.OriginalRotation;
        var front = Vector3.Transform(Vector3.UnitZ, joint);
        Assert.InRange(MathF.Asin(-front.Y), -15.01f * OneDegree, 15.01f * OneDegree);
        Assert.InRange(MathF.Abs(MathF.Atan2(front.X, front.Z)), 29.9f * OneDegree, 30.01f * OneDegree);
        AssertNoRoll(eye);
    }

    private static void AssertNear(Transform3D expected, Transform3D actual)
    {
        Assert.True(Vector3.Distance(expected.Position, actual.Position) < 1e-4f,
            $"Expected {expected.Position}, got {actual.Position}");
        Assert.True(MathF.Abs(Quaternion.Dot(expected.Orientation, actual.Orientation)) > 1 - 1e-5f,
            $"Expected {expected.Orientation}, got {actual.Orientation}");
    }

    internal static void AssertFinite(DfmSkeletonManager skeleton)
    {
        foreach (var skinning in Parts(skeleton))
        {
            foreach (var bone in skinning.Bones.Values)
            {
                var q = bone.IKRotation;
                var p = bone.LocalTransform.Position;
                Assert.True(float.IsFinite(q.X + q.Y + q.Z + q.W + p.X + p.Y + p.Z), $"{bone.Name} is not finite");
            }
        }
    }

    // The axis pairs of the retail events on bones without limits. The offset of the target is from the bone.
    [Theory]
    [InlineData("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, -0.5f, 0.2f, -1f)]
    [InlineData("Body_Neck", ThnAxis.ZAxis, ThnAxis.NegYAxis, 0.4f, 0.3f, -1f)]
    [InlineData("LLowArm", ThnAxis.XAxis, ThnAxis.ZAxis, -0.2f, -0.4f, -0.5f)]
    [InlineData("LUpArm", ThnAxis.NegYAxis, ThnAxis.ZAxis, -0.5f, -0.5f, -0.4f)]
    public void ABoneWithoutLimitsPointsAtTheTarget(string bone, ThnAxis up, ThnAxis front, float x, float y, float z)
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, bone, new Vector3(x, y, z));
        skeleton.SetIK(this, Goal(bone, up, front, target));
        Frame(skeleton);

        AssertPointsAt(skeleton, bone, front, target);
        // The up axis goes as near as possible to world up. Thus it is in the plane of world up and the front axis.
        var axes = Axes(skeleton, bone);
        var upAxis = Vector3.Transform(ThnTypes.AxisVector(up), axes);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.Transform(ThnTypes.AxisVector(front), axes), Vector3.UnitY));
        Assert.InRange(Vector3.Dot(upAxis, side), -0.01f, 0.01f);
        Assert.True(upAxis.Y > 0, $"The up axis {upAxis} points down");
    }

    // The axis pairs of the retail eye events. The targets are inside the limits. With up = NEG_Y_AXIS, the goal
    // turns the eye half a turn about its front axis, and the roll lock keeps the roll at 0.
    [Theory]
    [InlineData("Eye IK Left", ThnAxis.YAxis, ThnAxis.ZAxis, -0.2f, 0.1f, -1f)]
    [InlineData("Eye IK Left", ThnAxis.NegYAxis, ThnAxis.ZAxis, -0.2f, 0.1f, -1f)]
    [InlineData("Eye IK Right", ThnAxis.YAxis, ThnAxis.ZAxis, 0.15f, 0.1f, -1f)]
    public void AnEyePointsAtATargetInsideItsLimits(string bone, ThnAxis up, ThnAxis front, float x, float y, float z)
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, bone, new Vector3(x, y, z));
        skeleton.SetIK(this, Goal(bone, up, front, target));
        Frame(skeleton);

        AssertPointsAt(skeleton, bone, front, target);
        AssertNoRoll(Bone(skeleton, bone));
    }

    // The front axis of the eye is the direction of the pupil, which is on the -Z axis of the eye bone.
    [Fact]
    public void TheFrontAxisOfAnEyeIsThePupil()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "Eye IK Left", new Vector3(-0.2f, 0.1f, -1));
        skeleton.SetIK(this, Goal("Eye IK Left", ThnAxis.YAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);

        var eye = skeleton.GetBone("Eye IK Left")!.Transform * World;
        var angle = Angle(Vector3.Transform(-Vector3.UnitZ, eye.Orientation), target - eye.Position);
        Assert.True(angle < OneDegree, $"The pupil points {angle / OneDegree} degrees away from the target");
    }

    // The eye limits are +-15 degrees of pitch, +-30 degrees of yaw and no roll. A target behind the head is outside
    // the limits, so the eye turns to the yaw limit on the side of the target.
    [Fact]
    public void AnEyeStaysInsideItsLimitsForATargetBehind()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "Eye IK Left", new Vector3(-0.5f, 0, 1));
        var before = AngleToTarget(skeleton, "Eye IK Left", ThnAxis.ZAxis, target);
        skeleton.SetIK(this, Goal("Eye IK Left", ThnAxis.YAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);

        AssertAtTheYawLimit(Bone(skeleton, "Eye IK Left"));
        var after = AngleToTarget(skeleton, "Eye IK Left", ThnAxis.ZAxis, target);
        Assert.True(after < before - 25 * OneDegree, $"The eye turned from {before} to {after} radians");
    }

    // A known retail authoring outlier: s002xe 69.055 is the only retail event with front = NEG_Z_AXIS. Its target
    // Char_Syd_Mk_IKeyes_Center is in front of the face of Syd, but NEG_Z_AXIS is the back of her right eye. Thus
    // the eye turns as far as it can from the target, to the yaw limit. Compare this event with retail in the game.
    [Fact]
    public void TheRetailEyeEventWithFrontNegZTurnsTheEyeToTheYawLimit()
    {
        var skeleton = RetailData.Open().LoadPosedCostume("syd", @"CHARACTERS\ANIMATIONS\BodyGenericFemale.anm",
            "Sc_FMBODY_STND_IDLE_000LV_xa_05");
        // The marker is on Body_Head with the offset of the ATTACH_ENTITY at 68.75 (flags 38).
        var target = World.Transform(
            skeleton.GetBone("Body_Head")!.Transform.Transform(new Vector3(0, -0.0877f, 0.3341f)));
        var before = AngleToTarget(skeleton, "Eye IK Right", ThnAxis.ZAxis, target);
        skeleton.SetIK(this, Goal("Eye IK Right", ThnAxis.YAxis, ThnAxis.NegZAxis, target));
        Frame(skeleton);

        AssertFinite(skeleton);
        AssertAtTheYawLimit(Bone(skeleton, "Eye IK Right"));
        // The pupil (Z_AXIS) looked near the target before IK, and IK turns it away.
        var after = AngleToTarget(skeleton, "Eye IK Right", ThnAxis.ZAxis, target);
        Assert.True(before < 15 * OneDegree, $"The pupil points {before / OneDegree} degrees away before IK");
        Assert.True(after > before + 20 * OneDegree, $"The pupil turned from {before} to {after} radians");
    }

    // The head hangs on Body_Head, so the eye goal must use the head pose after the head goal.
    [Fact]
    public void AHeadGoalAndAnEyeGoalBothPointAtTheTarget()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "Body_Head", new Vector3(-0.8f, 0.2f, -1));
        skeleton.SetIK(this, Goal("Eye IK Left", ThnAxis.YAxis, ThnAxis.ZAxis, target));
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);

        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, target);
        AssertPointsAt(skeleton, "Eye IK Left", ThnAxis.ZAxis, target);
    }

    // Retail has an event with the same up and front axis (s009d, LHand). The goal then has no up direction.
    [Fact]
    public void AGoalWithTheSameUpAndFrontAxisPointsAtTheTarget()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "LHand", new Vector3(-0.3f, 0.2f, -0.5f));
        skeleton.SetIK(this, Goal("LHand", ThnAxis.NegXAxis, ThnAxis.NegXAxis, target));
        Frame(skeleton);

        AssertFinite(skeleton);
        AssertPointsAt(skeleton, "LHand", ThnAxis.NegXAxis, target);
    }

    // A target straight above the bone also gives no up direction. The bone then takes the shortest turn.
    [Fact]
    public void AGoalStraightUpPointsAtTheTarget()
    {
        var skeleton = StandingTrent();
        var target = World.Transform(skeleton.GetBone("Body_Head")!.Transform.Position) + Vector3.UnitY;
        var before = AngleToTarget(skeleton, "Body_Head", ThnAxis.ZAxis, target);
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);

        AssertFinite(skeleton);
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, target);
        Assert.InRange(Angle(Bone(skeleton, "Body_Head").IKRotation), before - OneDegree, before + OneDegree);
    }

    [Fact]
    public void ATargetAtTheBoneDoesNotTurnIt()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "Body_Head", Vector3.Zero);
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);

        AssertFinite(skeleton);
        Assert.True(Angle(Bone(skeleton, "Body_Head").IKRotation) < 1e-3f, "The head turns");
    }

    [Fact]
    public void ACountOfTwoTurnsTheNeckAndTheHead()
    {
        var skeleton = StandingTrent();
        var target = Near(skeleton, "Body_Head", new Vector3(-0.8f, 0.3f, -0.6f));
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target, count: 2));
        Frame(skeleton);

        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, target);
        Assert.True(Angle(Bone(skeleton, "Body_Neck").IKRotation) > 5 * OneDegree, "The neck does not turn");
        Assert.True(Angle(Bone(skeleton, "Body_Head").IKRotation) > 5 * OneDegree, "The head does not turn");
    }

    // Each IK processor sets its goal on each frame until its event ends, also after a newer goal replaces it.
    [Fact]
    public void ASecondGoalOnTheSameBoneReplacesTheFirst()
    {
        var skeleton = StandingTrent();
        var head = Bone(skeleton, "Body_Head");
        object first = new(), second = new();
        var left = Near(skeleton, "Body_Head", new Vector3(-0.6f, 0, -1));
        var right = Near(skeleton, "Body_Head", new Vector3(0.6f, 0, -1));
        skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
        Frame(skeleton);
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, left);
        var firstPose = head.IKRotation;

        var poses = new List<Quaternion>();
        foreach (var weight in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
        {
            skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
            skeleton.SetIK(second, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, right, weight));
            Frame(skeleton);
            poses.Add(head.IKRotation);
        }

        // The new goal blends from the pose of the old goal. The blend is linear, so the pose at the weight 0.5 is
        // halfway between the two poses.
        Assert.True(Angle(Quaternion.Inverse(firstPose) * poses[0]) < 1e-3f,
            "The blend does not start at the old pose");
        var error = Angle(Quaternion.Inverse(Quaternion.Slerp(firstPose, poses[4], 0.5f)) * poses[2]);
        Assert.True(error < 0.1f * OneDegree,
            $"The pose at the weight 0.5 is {error / OneDegree} degrees from halfway");
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, right);

        // The end of the old goal does not remove the new goal.
        skeleton.ClearIK(first);
        skeleton.SetIK(second, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, right));
        Frame(skeleton);
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, right);
    }

    // When the newer goal ends first, the bone goes back to the animation pose. The replaced goal does not come back.
    [Fact]
    public void AReplacedGoalDoesNotComeBackAfterTheNewGoalEnds()
    {
        var skeleton = StandingTrent();
        var head = Bone(skeleton, "Body_Head");
        object first = new(), second = new();
        var left = Near(skeleton, "Body_Head", new Vector3(-0.6f, 0, -1));
        var right = Near(skeleton, "Body_Head", new Vector3(0.6f, 0, -1));
        skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
        Frame(skeleton);
        skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
        skeleton.SetIK(second, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, right));
        Frame(skeleton);
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, right);

        skeleton.ClearIK(second);
        skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
        Frame(skeleton);
        Assert.Equal(Quaternion.Identity, head.IKRotation);

        // ClearIK also ends the replaced goal. After that, the owner can set a new goal.
        skeleton.ClearIK(first);
        skeleton.SetIK(first, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, left));
        Frame(skeleton);
        AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, left);
    }

    [Fact]
    public void AfterClearIKThePoseIsTheAnimationPose()
    {
        var skeleton = StandingTrent();
        var names = new[] { "Body_Head", "Head_End", "Eye IK Left" };
        var animation = names.Select(x => skeleton.GetBone(x)!.Transform).ToArray();
        var head = Bone(skeleton, "Body_Head");
        var rotation = head.Rotation;
        var target = Near(skeleton, "Body_Head", new Vector3(-0.6f, 0.3f, -1));
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target));
        skeleton.SetIK(this, Goal("Eye IK Left", ThnAxis.YAxis, ThnAxis.ZAxis, target));
        Frame(skeleton);
        Assert.True(Angle(head.IKRotation) > 10 * OneDegree, "The head does not turn");
        // IK keeps its rotation apart from the animation rotation.
        Assert.Equal(rotation, head.Rotation);

        skeleton.ClearIK(this);
        Frame(skeleton);

        for (int i = 0; i < names.Length; i++)
        {
            AssertNear(animation[i], skeleton.GetBone(names[i])!.Transform);
        }

        Assert.Equal(rotation, head.Rotation);
        Assert.Equal(Quaternion.Identity, head.IKRotation);
    }

    // Before the first animation frame, UpdateBones gives a flat pose. IK must not cause it.
    [Fact]
    public void ApplyIKDoesNothingBeforeAnAnimation()
    {
        var skeleton = RetailData.Open().LoadCostume("trent")!;
        var head = Bone(skeleton, "Body_Head");
        var bind = head.LocalTransform;
        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis,
            World.Transform(new Vector3(-1, 0.7f, -1))));
        Frame(skeleton);

        Assert.Equal(bind, head.LocalTransform);
        Assert.Equal(Quaternion.Identity, head.IKRotation);
    }

    // A character without IK goals costs nothing: ApplyIK does not update the bones. This is also true after the
    // last goal ends.
    [Fact]
    public void ApplyIKDoesNothingWithoutGoals()
    {
        var skeleton = StandingTrent();
        AssertApplyIKDoesNotUpdateTheBones(skeleton);

        skeleton.SetIK(this, Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis,
            Near(skeleton, "Body_Head", new Vector3(-0.6f, 0.3f, -1))));
        Frame(skeleton);
        skeleton.ClearIK(this);
        Frame(skeleton);
        AssertApplyIKDoesNotUpdateTheBones(skeleton);
    }

    // Changes an animation rotation without UpdateBones. If ApplyIK updates the bones, the pose changes.
    private static void AssertApplyIKDoesNotUpdateTheBones(DfmSkeletonManager skeleton)
    {
        var head = Bone(skeleton, "Body_Head");
        var pose = head.LocalTransform;
        var rotation = head.Rotation;
        head.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1) * rotation;
        skeleton.ApplyIK(World);
        Assert.Equal(pose, head.LocalTransform);
        head.Rotation = rotation;
    }

    // The renderer uses the bounds to cull and light the character. Thus the bounds must contain the IK pose.
    [Fact]
    public void TheBoundsContainTheIKPose()
    {
        var skeleton = StandingTrent();
        var animation = skeleton.Bounds;
        // The left arm points up.
        skeleton.SetIK(this, Goal("LUpArm", ThnAxis.NegYAxis, ThnAxis.ZAxis,
            Near(skeleton, "LUpArm", new Vector3(-0.2f, 1, 0))));
        Frame(skeleton);
        var ik = Bounds(skeleton);
        Assert.True(ik[0].Max.Y > animation.Max.Y + 0.1f, $"The bounds {ik[0]} do not contain the arm");

        // UpdateScripts calculates the bounds of the bones again. IK keeps its rotations, so the bounds stay the same.
        skeleton.UpdateScripts(0);
        var expected = Bounds(skeleton);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.True(Vector3.Distance(expected[i].Min, ik[i].Min) < 1e-4f &&
                Vector3.Distance(expected[i].Max, ik[i].Max) < 1e-4f, $"Expected {expected[i]}, got {ik[i]}");
        }
    }

    // The bounds of the character, then the bounds of each part. The bounds of the parts give the bounds of the
    // character.
    private static BoundingBox[] Bounds(DfmSkeletonManager skeleton) =>
        Parts(skeleton).Select(x => x.BoundingBox).Prepend(skeleton.Bounds).ToArray();

    // The Cons rotation turns the bone first, then the animation rotation, then the IK rotation, then the parent.
    [Fact]
    public void TheIKRotationComesAfterTheAnimationRotation()
    {
        var orig = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f);
        var ik = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        var parent = new Transform3D(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f));
        var bone = new BoneInstance("bone", Transform3D.Identity, Vector3.Zero, Vector3.One)
        {
            OriginalRotation = orig, Rotation = rotation, IKRotation = ik, Origin = Vector3.UnitY
        };
        bone.Update(parent);

        var v = new Vector3(0.3f, -0.5f, 0.8f);
        var expected = Vector3.Transform(
            Vector3.Transform(Vector3.Transform(Vector3.Transform(v, orig), rotation), ik), parent.Orientation);
        var actual = Vector3.Transform(v, bone.LocalTransform.Orientation);
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"Expected {expected}, got {actual}");
        Assert.True(Vector3.Distance(parent.Transform(Vector3.UnitY), bone.LocalTransform.Position) < 1e-5f);
    }

    internal static StartIKEvent HeadAtMarker(float duration, float transition) => new()
    {
        Duration = duration,
        TransitionDuration = transition,
        Targets = ["trent", "marker"],
        EndEffector = "Body_Head",
        CountToRoot = 1,
        TargetType = TargetTypes.Root,
        Up = ThnAxis.NegYAxis,
        Front = ThnAxis.ZAxis,
        PointAt = true
    };

    // A cutscene with Trent and a marker in front of his head. Each frame uses the order of Cutscene._Update: the
    // events and processors, then the scene objects, then GameWorld.Update, which runs the animation component.
    internal sealed class Scene
    {
        public readonly DfmSkeletonManager Skeleton = StandingTrent();
        public readonly ThnSceneObject Marker;
        public bool ScriptEnded { get; private set; }
        private readonly ThnScriptInstance instance;
        private readonly ThnSceneObject trent;
        private readonly AnimationComponent animation;

        public Scene(StartIKEvent ik, double scriptDuration = 100)
        {
            var script = new ThnScript() { Duration = scriptDuration };
            script.Events.Add(ik);
            // A cutscene without a game or a world. The script instance tells it when the script ends.
            var cutscene = new Cutscene(new ThnScriptContext(null), null!, null!, null, new Rectangle(0, 0, 1, 1),
                null!);
            cutscene.ScriptFinished += _ => ScriptEnded = true;
            instance = new ThnScriptInstance(cutscene, script);
            var obj = new GameObject() { RenderComponent = new CharacterRenderer(Skeleton) };
            animation = new AnimationComponent(obj, new AnmFile());
            // A zero time step stops the root motion. Thus the model stays at World.
            Skeleton.UpdateScripts(0);
            // The model stands at the floor height plus the root height. This puts the model at World.
            Skeleton.FloorHeight = World.Position.Y - Skeleton.RootHeight;
            trent = new ThnSceneObject() { Name = "trent", Translate = World.Position, Rotate = World.Orientation, Object = obj };
            Marker = new ThnSceneObject()
            {
                Name = "marker", Translate = Near(Skeleton, "Body_Head", new Vector3(-0.6f, 0.2f, -1)),
                Rotate = Quaternion.Identity
            };
            instance.Objects[trent.Name] = trent;
            instance.Objects[Marker.Name] = Marker;
            // Cutscene.SceneSetup runs the events at T=0.
            Frame(0);
        }

        public void Frame(double delta)
        {
            instance.Update(delta);
            trent.Update();
            Marker.Update();
            // A zero time step keeps the animation pose. Thus only IK changes the bones.
            animation.Update(0, null!);
            AssertNear(World, trent.Object!.WorldTransform);
        }
    }

    [Fact]
    public void TheWeightIsOneHalfAtHalfTheTransition()
    {
        var scene = new Scene(HeadAtMarker(10, 1));
        var head = Bone(scene.Skeleton, "Body_Head");
        scene.Frame(0.25);
        scene.Frame(0.25);
        var half = Angle(head.IKRotation);
        scene.Frame(0.25);
        scene.Frame(0.25);
        var full = Angle(head.IKRotation);

        Assert.True(full > 10 * OneDegree, $"The head turns only {full} radians");
        Assert.InRange(half / full, 0.495f, 0.505f);
        AssertPointsAt(scene.Skeleton, "Body_Head", ThnAxis.ZAxis, scene.Marker.Translate);
    }

    // A zero transition gives the full weight at once, also in the setup frame at T=0.
    [Fact]
    public void AZeroTransitionAtTimeZeroGivesTheFullWeight()
    {
        var scene = new Scene(HeadAtMarker(2, 0));

        AssertFinite(scene.Skeleton);
        AssertPointsAt(scene.Skeleton, "Body_Head", ThnAxis.ZAxis, scene.Marker.Translate);
    }

    // The weight is time / transition until the end. Then the goal ends at once.
    [Fact]
    public void ATransitionAsLongAsTheDurationBlendsInUntilTheEnd()
    {
        var scene = new Scene(HeadAtMarker(1, 1));
        var head = Bone(scene.Skeleton, "Body_Head");
        AssertFinite(scene.Skeleton);
        var angles = new List<float>();
        for (int i = 0; i < 9; i++)
        {
            scene.Frame(0.1);
            AssertFinite(scene.Skeleton);
            angles.Add(Angle(head.IKRotation));
        }

        // The angles at 0.4 s and at 0.8 s.
        Assert.True(angles[3] > OneDegree, "The head does not turn");
        Assert.InRange(angles[7] / angles[3], 1.98f, 2.02f);
        scene.Frame(0.2);
        Assert.Equal(Quaternion.Identity, head.IKRotation);
    }

    // An IK event can end after its script. No processor runs after the script ends, so the goal ends with the
    // script.
    [Fact]
    public void AGoalEndsWhenItsScriptEnds()
    {
        var scene = new Scene(HeadAtMarker(3, 0), 1);
        var head = Bone(scene.Skeleton, "Body_Head");
        scene.Frame(0.5);
        Assert.True(Angle(head.IKRotation) > 10 * OneDegree, "The head does not turn");

        scene.Frame(0.6);
        Assert.True(scene.ScriptEnded, "The script does not end");
        Assert.Equal(Quaternion.Identity, head.IKRotation);
        scene.Frame(0.5);
        Assert.Equal(Quaternion.Identity, head.IKRotation);
    }

    [Theory]
    [InlineData(ThnAxis.XAxis, 1, 0, 0)]
    [InlineData(ThnAxis.YAxis, 0, 1, 0)]
    [InlineData(ThnAxis.ZAxis, 0, 0, 1)]
    [InlineData(ThnAxis.NegXAxis, -1, 0, 0)]
    [InlineData(ThnAxis.NegYAxis, 0, -1, 0)]
    [InlineData(ThnAxis.NegZAxis, 0, 0, -1)]
    public void AnAxisGivesItsUnitVector(ThnAxis axis, float x, float y, float z)
    {
        Assert.Equal(new Vector3(x, y, z), ThnTypes.AxisVector(axis));
    }

    [Fact]
    public void AStartIKEventReadsItsProperties()
    {
        var targets = new ThornTable();
        targets.Append("Char_syd");
        targets.Append("Char_trent");
        // Old scripts give an axis as a vector. ThnTypes.ConvertAxis reads this form.
        var up = new ThornTable();
        up.Append(0f);
        up.Append(-1f);
        up.Append(0f);
        var props = new ThornTable();
        props["duration"] = 1.469f;
        props["end_effector"] = "Body_Head";
        props["count_to_root"] = 2f;
        props["target_part"] = "hp_head";
        props["target_type"] = TargetTypes.Hardpoint;
        props["damping"] = 1f;
        props["up"] = up;
        props["front"] = ThnAxis.ZAxis;
        props["point_at"] = 1f;
        props["move_to"] = 0f;
        props["transition_duration"] = 0.3f;
        var table = new ThornTable();
        table.Append(19.218f);
        table.Append(EventTypes.StartIK);
        table.Append(targets);
        table.Append(props);

        var ev = new StartIKEvent(table, "test");

        Assert.Equal(19.218f, ev.Time);
        Assert.Equal(1.469f, ev.Duration);
        Assert.Equal(["Char_syd", "Char_trent"], ev.Targets);
        Assert.Equal("Body_Head", ev.EndEffector);
        Assert.Equal(2, ev.CountToRoot);
        Assert.Equal("hp_head", ev.TargetPart);
        Assert.Equal(TargetTypes.Hardpoint, ev.TargetType);
        Assert.Equal(1f, ev.Damping);
        Assert.Equal(ThnAxis.NegYAxis, ev.Up);
        Assert.Equal(ThnAxis.ZAxis, ev.Front);
        Assert.True(ev.PointAt);
        Assert.False(ev.MoveTo);
        Assert.Equal(0.3f, ev.TransitionDuration);
    }
}

// These tests read the log. The log is global, so no other test runs at the same time.
[Collection(nameof(LogCapture))]
public class ThnStartIKLogTests
{
    // br_darcy_body has Body_Head limits that are not valid. IK must ignore them, and the log must tell this once.
    [Fact]
    public void TheBodyHeadOfDarcyHasNoLimitsAndLogsOnce()
    {
        using var log = new LogCapture();
        var skeleton = RetailData.Open().LoadPosedCostume("jacobi", @"CHARACTERS\ANIMATIONS\BodyGenericFemale.anm",
            "Sc_FMBODY_STND_IDLE_000LV_xa_05");
        var target = ThnStartIKTests.Near(skeleton, "Body_Head", new Vector3(-0.5f, 0.2f, -1));
        for (int i = 0; i < 5; i++)
        {
            skeleton.SetIK(this, ThnStartIKTests.Goal("Body_Head", ThnAxis.NegYAxis, ThnAxis.ZAxis, target));
            ThnStartIKTests.Frame(skeleton);
        }

        Assert.False(ThnStartIKTests.Bone(skeleton, "Body_Head").HasIKLimits);
        ThnStartIKTests.AssertPointsAt(skeleton, "Body_Head", ThnAxis.ZAxis, target);
        Assert.Single(log.Lines("joint limits of Body_Head"));
    }

    [Fact]
    public void AMoveToEventLogsOneWarningAndDoesNothing()
    {
        using var log = new LogCapture();
        var ik = ThnStartIKTests.HeadAtMarker(2, 0);
        ik.PointAt = false;
        ik.MoveTo = true;
        var scene = new ThnStartIKTests.Scene(ik);
        scene.Frame(0.5);

        Assert.Equal(Quaternion.Identity, ThnStartIKTests.Bone(scene.Skeleton, "Body_Head").IKRotation);
        Assert.Single(log.Lines("move_to"));
    }
}

[CollectionDefinition(nameof(LogCapture), DisableParallelization = true)]
public class LogCaptureCollection;

// Collects the lines that FLLog writes. FLLog writes the lines in order, on its own thread.
internal sealed class LogCapture : IDisposable
{
    private static readonly object sync = new();
    private static List<string>? lines;

    static LogCapture()
    {
        // FLLog reads these hooks again for each line. Thus they must not become null again.
        FLLog.UIThread = new ImmediateThread();
        FLLog.AppendLine += (line, _) =>
        {
            lock (sync)
            {
                lines?.Add(line);
            }
        };
    }

    public LogCapture()
    {
        lock (sync)
        {
            lines = [];
        }

        // Earlier tests can still have lines in the queue.
        WaitForQueue();
        lock (sync)
        {
            lines!.Clear();
        }
    }

    // Gives the lines that contain the text, after FLLog writes all the lines from before the call.
    public List<string> Lines(string text)
    {
        WaitForQueue();
        lock (sync)
        {
            return lines!.Where(x => x.Contains(text, StringComparison.Ordinal)).ToList();
        }
    }

    private static void WaitForQueue()
    {
        var marker = "LogCapture " + Guid.NewGuid();
        FLLog.Error("Test", marker);
        var time = Stopwatch.StartNew();
        while (true)
        {
            lock (sync)
            {
                if (lines!.Any(x => x.Contains(marker, StringComparison.Ordinal)))
                {
                    return;
                }
            }

            if (time.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException("FLLog did not write the marker");
            }

            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            lines = null;
        }
    }

    private sealed class ImmediateThread : IUIThread
    {
        public void QueueUIThread(Action work) => work();
    }
}
