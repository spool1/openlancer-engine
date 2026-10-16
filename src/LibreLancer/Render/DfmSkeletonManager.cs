// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using LibreLancer;
using LibreLancer.Graphics;
using LibreLancer.Utf.Anm;
using LibreLancer.Utf.Cmp;
using LibreLancer.Utf.Dfm;
using LibreLancer.World;

namespace LibreLancer.Render
{
    public class DfmSkeletonManager
    {
        public float FloorHeight;
        public float RootHeight;

        public DfmFile? Head;
        public DfmFile Body;
        public DfmFile? LeftHand;
        public DfmFile? RightHand;

        public DfmSkinning? HeadSkinning;
        public DfmSkinning BodySkinning;
        public DfmSkinning? LeftHandSkinning;
        public DfmSkinning? RightHandSkinning;

        private Connection? HeadConnection;
        private Connection? LeftHandConnection;
        private Connection? RightHandConnection;

        public class Connection
        {
            private readonly HardpointDefinition? parentHp;
            private readonly BoneInstance? parentBone;
            private readonly HardpointDefinition? childHp;
            private readonly BoneInstance? childBone;
            private readonly HardpointDefinition? connectionHp;
            private readonly BoneInstance? connectionBone;
            private readonly Transform3D invBindPose;

            public Transform3D Transform { get; private set; }
            public Transform3D Bone { get; private set; }

            public Connection(DfmSkinning parent, DfmSkinning child,
                string parentHpName, string childHpName, string connectionHpName)
            {
                parent.GetHardpoint(parentHpName, out parentHp, out parentBone);
                child.GetHardpoint(childHpName, out childHp, out childBone);
                parent.GetHardpoint(connectionHpName, out connectionHp, out connectionBone);
                CalculateTransform();
                invBindPose = (BoneTransform(connectionHp, connectionBone) * Transform.Inverse()).Inverse();
                CalculateBone();
            }

            Transform3D BoneTransform(HardpointDefinition? hp, BoneInstance? bone)
            {
                if (hp == null || bone == null)
                    return Transform3D.Identity;
                return hp.Transform * bone.LocalTransform;
            }

            private void CalculateTransform()
            {
                var invChild = BoneTransform(childHp, childBone).Inverse();
                var parent = BoneTransform(parentHp, parentBone);
                Transform = invChild * parent;
            }

            private void CalculateBone()
            {
                Bone = invBindPose * BoneTransform(connectionHp, connectionBone) * Transform.Inverse();
            }

            public void Update()
            {
                CalculateTransform();
                CalculateBone();
            }
        }

        // Scripting
        private List<ScriptInstance> RunningScripts = [];

        private struct ResolvedJoint
        {
            public BoneInstance Bone;
            public int JointMapIndex;
            public int JointMapCursor;

            public ResolvedJoint(BoneInstance bone, int jm)
            {
                Bone = bone;
                JointMapIndex = jm;
            }
        }

        private struct BoneBlendData
        {
            public Quaternion Rotation;
            public Vector3 Translation;
            public float RotationWeight;
            public float TranslationWeight;
        }

        private struct BonePose
        {
            public Quaternion Rotation;
            public Vector3 Translation;

            public BonePose(Quaternion rotation, Vector3 translation)
            {
                Rotation = rotation;
                Translation = translation;
            }
        }

        private static void AccumulateRotation(ref BoneBlendData data, Quaternion rotation, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            if (data.RotationWeight <= 0f)
            {
                data.Rotation = rotation;
            }
            else
            {
                var t = weight / (data.RotationWeight + weight);
                data.Rotation = Quaternion.Slerp(data.Rotation, rotation, t);
            }

            data.RotationWeight += weight;
        }

        private static void AccumulateTranslation(ref BoneBlendData data, Vector3 translation, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            if (data.TranslationWeight <= 0f)
            {
                data.Translation = translation;
            }
            else
            {
                var t = weight / (data.TranslationWeight + weight);
                data.Translation = Vector3.Lerp(data.Translation, translation, t);
            }

            data.TranslationWeight += weight;
        }

        // Used for object maps
        public bool ApplyRootMotion => _rootMotionInstance != null;
        public Vector3 RootTranslation => _rootMotionInstance!.RootTranslation;
        public Quaternion RootRotation => _rootMotionInstance!.RootRotation;

        public Quaternion RootRotationAccumulator => _rootMotionInstance!.RootRotationAccumulator;
        public Vector3 LastTranslation;
        public Quaternion LastRotation;

        private ScriptInstance? _rootMotionInstance;
        private float _rootMotionWeight;
        private bool _hasStarted;
        private const float DefaultPoseRotationEpsilon = 0.01f;
        private const float DefaultPoseTranslationEpsilon = 0.01f;

        private class ScriptInstance
        {
            public double T;
            public float StartTime;
            public float TimeScale;
            public float Duration;
            public bool Loop;
            public float BlendInDuration;
            public float BlendOutDuration;

            public RefList<ResolvedJoint> Joints = [];
            public DfmSkeletonManager Parent = null!;
            public Vector3 RootTranslation = Vector3.Zero;
            public Quaternion RootRotation = Quaternion.Identity;
            public Quaternion RootRotationAccumulator = Quaternion.Identity;
            private int rootCursor = 0;

            public Script Script = null!;

            private float lastFt;

            public ScriptInstance(float startTime)
            {
                lastFt = StartTime = startTime;
            }

            private float GetBlendWeight()
            {
                var weight = 1f;
                var t = (float) T;

                if (BlendInDuration > 0f && t < BlendInDuration)
                {
                    weight = t / BlendInDuration;
                }

                if (!Loop && Duration > 0f && BlendOutDuration > 0f)
                {
                    var blendOutStart = Duration - BlendOutDuration;
                    if (t > blendOutStart)
                    {
                        weight = MathF.Min(weight, (Duration - t) / BlendOutDuration);
                    }
                }

                if (weight < 0f)
                {
                    return 0f;
                }

                if (weight > 1f)
                {
                    return 1f;
                }

                return weight;
            }

            private bool EvaluateRoot(float ft, out Quaternion rotate, out Vector3 translate)
            {
                rotate = Quaternion.Identity;
                translate = Vector3.Zero;

                for (var i = 0; i < Script.ObjectMaps.Count; i++)
                {
                    ref var o = ref Script.ObjectMaps[i];
                    if (!o.ParentName.Equals("Root", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var cht = ft;

                    if (Duration > 0 && Loop)
                    {
                        var trOne = Vector3.Zero;
                        Quaternion qOne = Quaternion.Identity;
                        if (o.Channel.HasPosition)
                        {
                            trOne = o.Channel.PositionAtTime(o.Channel.Duration, ref rootCursor);
                        }

                        if (o.Channel.HasOrientation)
                        {
                            qOne = o.Channel.QuaternionAtTime(o.Channel.Duration, ref rootCursor);
                        }

                        var hangDetect = 0;

                        while (cht > o.Channel.Duration)
                        {
                            translate += trOne;
                            rotate *= qOne;
                            cht -= o.Channel.Duration;
                            if (hangDetect++ > 10000)
                            {
                                throw new Exception($"Hang in root object map code: broke {Script.Name}");
                            }
                        }
                    }

                    if (o.Channel.HasPosition)
                    {
                        translate += o.Channel.PositionAtTime(cht, ref rootCursor);
                    }

                    if (o.Channel.HasOrientation)
                    {
                        rotate *= o.Channel.QuaternionAtTime(cht, ref rootCursor);
                    }

                    return true;
                }

                return false;
            }

            public bool RunScript(double delta, Dictionary<BoneInstance, BoneBlendData> blendData)
            {
                T += delta;
                var ft = (float) (T * TimeScale) + StartTime;
                var blendWeight = GetBlendWeight();
                var running = false;

                for (var i = 0; i < Joints.Count; i++)
                {
                    ref var j = ref Joints[i];
                    ref var ch = ref Script.JointMaps[j.JointMapIndex].Channel;
                    var cht = ft;
                    if (Loop && ft > ch.Duration)
                    {
                        cht = ft % ch.Duration;
                    }

                    if (blendWeight > 0f)
                    {
                        if (!blendData.TryGetValue(j.Bone, out var data))
                        {
                            data = default;
                        }

                        if (ch.HasOrientation)
                        {
                            var rot = ch.QuaternionAtTime(cht, ref j.JointMapCursor);
                            DfmSkeletonManager.AccumulateRotation(ref data, rot, blendWeight);
                        }

                        if (ch.HasPosition)
                        {
                            var tr = ch.PositionAtTime(cht, ref j.JointMapCursor);
                            DfmSkeletonManager.AccumulateTranslation(ref data, tr, blendWeight);
                        }

                        blendData[j.Bone] = data;
                    }

                    if (ft < ch.Duration || Loop)
                    {
                        running = true;
                    }
                }

                if (ft - lastFt > 0.001f)
                {
                    if (EvaluateRoot(lastFt, out var rot0, out var tr0) &&
                        EvaluateRoot(ft, out var rot1, out var tr1))
                    {
                        RootTranslation = (tr1 - tr0);
                        RootRotation = (Quaternion.Inverse(rot0)) * rot1;
                        RootRotationAccumulator = rot1;

                        if (blendWeight > Parent._rootMotionWeight)
                        {
                            Parent._rootMotionInstance = this;
                            Parent._rootMotionWeight = blendWeight;
                        }
                    }

                    lastFt = ft;
                }

                if (Duration > 0)
                {
                    return T < Duration;
                }
                else
                {
                    return running;
                }
            }
        }

        public Dictionary<string, DfmHardpoint> Hardpoints = new(StringComparer.OrdinalIgnoreCase);

        public bool GetAccessoryTransform(RigidModel model, string hpAccessory, string hpSkel, Matrix4x4 world,
            out Matrix4x4 result)
        {
            result = Matrix4x4.Identity;
            // Invert source hardpoint
            Hardpoint? srcHardpoint = null;

            foreach (var part in model.AllParts)
            {
                foreach (var hp in part.Hardpoints)
                {
                    if (hp.Name.Equals(hpAccessory, StringComparison.OrdinalIgnoreCase))
                    {
                        srcHardpoint = hp;
                        break;
                    }
                }
            }

            if (srcHardpoint == null)
            {
                return false;
            }

            var invAccessory = srcHardpoint.Transform.Inverse();

            if (HeadSkinning != null && HeadSkinning.GetHardpoint(hpSkel, out var hpDef, out var boneDef))
            {
                result = (invAccessory * hpDef.Transform * boneDef.LocalTransform * HeadConnection.Transform).Matrix() *
                         world;
                return true;
            }

            if (LeftHandSkinning != null && LeftHandSkinning.GetHardpoint(hpSkel, out hpDef, out boneDef))
            {
                result =
                    (invAccessory * hpDef.Transform * boneDef.LocalTransform * LeftHandConnection.Transform).Matrix() *
                    world;
                return true;
            }

            if (RightHandSkinning != null && RightHandSkinning.GetHardpoint(hpSkel, out hpDef, out boneDef))
            {
                result = (invAccessory * hpDef.Transform * boneDef.LocalTransform * RightHandConnection.Transform)
                    .Matrix() * world;
                return true;
            }

            if (BodySkinning != null && BodySkinning.GetHardpoint(hpSkel, out hpDef, out boneDef))
            {
                result = (invAccessory * hpDef.Transform * boneDef.LocalTransform).Matrix() * world;
                return true;
            }

            return false;
        }

        public IRenderHardpoint? GetBone(string name) =>
            FindBone(name, out var bone, out var connection) ? new DfmBoneHardpoint(connection, bone) : null;

        // Finds a bone of the body, the head or a hand. The body comes first, because every DFM has a "Root" bone.
        // The connection is null for a body bone.
        private bool FindBone(string name, [MaybeNullWhen(false)] out BoneInstance bone, out Connection? connection)
        {
            connection = null;
            if (BodySkinning.Bones.TryGetValue(name, out bone))
            {
                return true;
            }

            connection = HeadConnection;
            if (HeadSkinning != null && HeadSkinning.Bones.TryGetValue(name, out bone))
            {
                return true;
            }

            connection = LeftHandConnection;
            if (LeftHandSkinning != null && LeftHandSkinning.Bones.TryGetValue(name, out bone))
            {
                return true;
            }

            connection = RightHandConnection;
            return RightHandSkinning != null && RightHandSkinning.Bones.TryGetValue(name, out bone);
        }

        private void AddHardpoints(DfmSkinning skinning, Connection? connection)
        {
            foreach (var hp in skinning.GetHardpoints())
            {
                Hardpoints[hp.def.Name] = new DfmHardpoint(connection, hp.bone!, hp.def);
            }
        }

        public DfmSkeletonManager(DfmFile body, DfmFile? head = null, DfmFile? leftHand = null,
            DfmFile? rightHand = null)
        {
            Body = body;
            BodySkinning = new DfmSkinning(body);
            AddHardpoints(BodySkinning, null);

            if (head != null)
            {
                Head = head;
                HeadSkinning = new DfmSkinning(head);
                HeadConnection = new(BodySkinning, HeadSkinning, "hp_head", "hp_head", "hp_neck");
                AddHardpoints(HeadSkinning, HeadConnection);
            }

            if (leftHand != null)
            {
                LeftHand = leftHand;
                LeftHandSkinning = new DfmSkinning(leftHand);
                LeftHandConnection = new(BodySkinning, LeftHandSkinning, "hp_left b", "hp_left b", "hp_left a");
                AddHardpoints(LeftHandSkinning, LeftHandConnection);
            }

            if (rightHand != null)
            {
                RightHand = rightHand;
                RightHandSkinning = new DfmSkinning(rightHand);
                RightHandConnection = new(BodySkinning, RightHandSkinning, "hp_right b", "hp_right b", "hp_right a");
                AddHardpoints(RightHandSkinning, RightHandConnection);
            }

            UpdateBounds();
        }

        public BoundingBox Bounds;

        private void UpdateBounds()
        {
            Bounds = BodySkinning.BoundingBox;
            GetTransforms(Matrix4x4.Identity,
                out var headTr,
                out var lhTr,
                out var rhTr);

            if (HeadSkinning != null)
            {
                Bounds = BoundingBox.CreateMerged(Bounds, BoundingBox.TransformAABB(HeadSkinning.BoundingBox, headTr));
            }

            if (LeftHandSkinning != null)
            {
                Bounds = BoundingBox.CreateMerged(Bounds,
                    BoundingBox.TransformAABB(LeftHandSkinning.BoundingBox, lhTr));
            }

            if (RightHandSkinning != null)
            {
                Bounds = BoundingBox.CreateMerged(Bounds,
                    BoundingBox.TransformAABB(RightHandSkinning.BoundingBox, rhTr));
            }
        }


        public void UpdateScripts(double delta)
        {
            _rootMotionInstance = null;
            _rootMotionWeight = 0f;

            if (RunningScripts.Count == 0)
            {
                _hasStarted = false;
                UpdateBounds();
                return;
            }

            var basePose = new Dictionary<BoneInstance, BonePose>();
            var blendData = new Dictionary<BoneInstance, BoneBlendData>();
            var toRemove = new List<ScriptInstance>();

            foreach (var sc in RunningScripts)
            {
                for (var i = 0; i < sc.Joints.Count; i++)
                {
                    var bone = sc.Joints[i].Bone;
                    if (!basePose.ContainsKey(bone))
                    {
                        basePose[bone] = new BonePose(bone.Rotation, bone.Translation);
                    }
                }
            }

            foreach (var sc in RunningScripts)
            {
                if (!sc.RunScript(delta, blendData))
                {
                    toRemove.Add(sc);
                }
            }

            foreach (var kvp in basePose)
            {
                if (blendData.TryGetValue(kvp.Key, out var data))
                {
                    var rotWeight = MathF.Min(1f, data.RotationWeight);
                    var trWeight = MathF.Min(1f, data.TranslationWeight);

                    var newRot = rotWeight > 0f
                        ? Quaternion.Slerp(kvp.Value.Rotation, data.Rotation, rotWeight)
                        : kvp.Value.Rotation;

                    var newTr = trWeight > 0f
                        ? Vector3.Lerp(kvp.Value.Translation, data.Translation, trWeight)
                        : kvp.Value.Translation;

                    kvp.Key.Rotation = newRot;
                    kvp.Key.Translation = newTr;
                }
                else
                {
                    kvp.Key.Rotation = kvp.Value.Rotation;
                    kvp.Key.Translation = kvp.Value.Translation;
                }
            }

            UpdateBones();
            posed = true;

            foreach (var sc in toRemove) RunningScripts.Remove(sc);
            UpdateBounds();
        }

        private void UpdateBones()
        {
            BodySkinning.UpdateBones();
            HeadSkinning?.UpdateBones();
            LeftHandSkinning?.UpdateBones();
            RightHandSkinning?.UpdateBones();
            UpdateConnections();
        }

        private void UpdateConnections()
        {
            HeadConnection?.Update();
            LeftHandConnection?.Update();
            RightHandConnection?.Update();
        }

        // IK (START_IK point_at)
        public struct IKGoal
        {
            // The effector bone. IK turns it and CountToRoot - 1 of its ancestors.
            public string Bone;
            public int CountToRoot;
            // The axes of the effector bone that point at the target and up.
            public Vector3 Front;
            public Vector3 Up;
            public Vector3 TargetWorld;
            public float Weight;
        }

        private class IKState(object owner, BoneInstance[] chain, Connection? connection, int depth)
        {
            public readonly object Owner = owner;
            // The effector first, then its ancestors.
            public readonly BoneInstance[] Chain = chain;
            // The goal blends from the IK rotations that the chain had when the goal started.
            public readonly Quaternion[] BlendStart = chain.Select(x => x.IKRotation).ToArray();
            public readonly Connection? Connection = connection;
            public readonly int Depth = depth;
            public IKGoal Goal;
        }

        private readonly Dictionary<BoneInstance, IKState> ikGoals = new();
        // The goals that a goal from a newer owner replaced. They stay replaced until their owner calls ClearIK.
        private readonly HashSet<(BoneInstance Bone, object Owner)> ikReplaced = [];
        private readonly List<IKState> ikOrder = [];
        // The bones that IK turned in the last frame.
        private readonly HashSet<BoneInstance> ikBones = [];
        // An animation set the pose. Before that, UpdateBones gives a flat pose.
        private bool posed;

        // Sets the goal of an owner for the next ApplyIK. A goal from a different owner on the same effector bone
        // replaces the old goal. After that, the goals of the old owner on that bone have no effect.
        // Returns false when the bone does not exist.
        public bool SetIK(object owner, in IKGoal goal)
        {
            if (!FindBone(goal.Bone, out var bone, out var connection))
            {
                return false;
            }

            if (ikReplaced.Contains((bone, owner)))
            {
                return true;
            }

            if (!ikGoals.TryGetValue(bone, out var state) || state.Owner != owner)
            {
                if (state != null)
                {
                    ikReplaced.Add((bone, state.Owner));
                }

                var chain = new List<BoneInstance>();
                for (var b = bone; b != null && chain.Count < Math.Max(1, goal.CountToRoot); b = b.Parent)
                {
                    chain.Add(b);
                }

                var depth = 0;
                for (var b = bone.Parent; b != null; b = b.Parent)
                {
                    depth++;
                }

                state = new IKState(owner, chain.ToArray(), connection, depth);
                ikGoals[bone] = state;
            }

            state.Goal = goal;
            return true;
        }

        public void ClearIK(object owner)
        {
            foreach (var (bone, state) in ikGoals)
            {
                if (state.Owner == owner)
                {
                    ikGoals.Remove(bone);
                }
            }

            ikReplaced.RemoveWhere(x => x.Owner == owner);
        }

        // Turns the bones of the goals after the animation. modelToWorld is the world transform of the body.
        public void ApplyIK(Transform3D modelToWorld)
        {
            if (!posed || (ikGoals.Count == 0 && ikBones.Count == 0))
            {
                return;
            }

            // Start again from the animation pose.
            foreach (var bone in ikBones)
            {
                bone.IKRotation = Quaternion.Identity;
            }

            ikBones.Clear();
            UpdateBones();

            // The head and the hands hang on body bones, so the body goals come first. Then a goal on a bone near
            // the root comes before a goal on a bone below it.
            ikOrder.Clear();
            ikOrder.AddRange(ikGoals.Values);
            ikOrder.Sort(CompareIK);
            var i = 0;
            for (; i < ikOrder.Count && ikOrder[i].Connection == null; i++)
            {
                SolveIK(ikOrder[i], modelToWorld);
            }

            UpdateConnections();
            for (; i < ikOrder.Count; i++)
            {
                SolveIK(ikOrder[i], modelToWorld);
            }

            BodySkinning.UpdateBounds();
            HeadSkinning?.UpdateBounds();
            LeftHandSkinning?.UpdateBounds();
            RightHandSkinning?.UpdateBounds();
            UpdateBounds();
        }

        private static int CompareIK(IKState a, IKState b)
        {
            var c = (a.Connection != null).CompareTo(b.Connection != null);
            return c != 0 ? c : a.Depth.CompareTo(b.Depth);
        }

        private void SolveIK(IKState state, Transform3D modelToWorld)
        {
            // The solve uses the space of the DFM that has the chain.
            var partToWorld = (state.Connection?.Transform ?? Transform3D.Identity) * modelToWorld;
            var target = partToWorld.InverseTransform(state.Goal.TargetWorld);
            var up = Vector3.Transform(Vector3.UnitY, Quaternion.Inverse(partToWorld.Orientation));
            var effector = state.Chain[0];
            // The front and up axes are in the frame of the joint without its Cons rotation. Of the retail effectors,
            // only the eyes have a Cons rotation (half a turn about Y). The retail eye events use front = Z for the
            // pupil, which is on the -Z axis of the eye bone.
            var axes = Quaternion.Inverse(effector.OriginalRotation);
            var n = state.Chain.Length;
            // The joint nearest the root turns first. Each joint takes an equal part of the turn that remains.
            // A turn of an ancestor moves the effector, so each joint calculates the turn again.
            for (var k = 0; k < n; k++)
            {
                var j = n - 1 - k;
                var joint = state.Chain[j];
                var pose = effector.LocalTransform;
                var turn = Quaternion.Slerp(Quaternion.Identity, PointAt(pose.Orientation * axes, state.Goal.Front,
                    state.Goal.Up, target - pose.Position, up), 1f / (n - k));
                // The turn is in DFM space. Inverse(P) * turn * P is the same turn in the frame of the parent.
                var parent = joint.Parent?.LocalTransform ?? Transform3D.Identity;
                var ik = Quaternion.Inverse(parent.Orientation) * turn * parent.Orientation * joint.IKRotation;
                ik = joint.LimitIK(Quaternion.Normalize(ik));
                joint.IKRotation = Quaternion.Normalize(Quaternion.Slerp(state.BlendStart[j], ik, state.Goal.Weight));
                joint.Update(parent);
                ikBones.Add(joint);
            }
        }

        // The turn that points the front axis of a bone along dir. It moves the up axis of the bone as near as
        // possible to worldUp.
        private static Quaternion PointAt(Quaternion bone, Vector3 front, Vector3 up, Vector3 dir, Vector3 worldUp)
        {
            if (dir.LengthSquared() < 1e-8f)
            {
                return Quaternion.Identity;
            }

            dir = Vector3.Normalize(dir);
            front = Vector3.Transform(front, bone);
            up = Vector3.Transform(up, bone);
            // When the goal gives no up direction, use the shortest turn.
            if (IsParallel(front, up) || IsParallel(dir, worldUp))
            {
                return Arc(front, dir);
            }

            return QuaternionEx.LookRotation(dir, worldUp) * Quaternion.Inverse(QuaternionEx.LookRotation(front, up));
        }

        private static bool IsParallel(Vector3 a, Vector3 b) => Vector3.Cross(a, b).LengthSquared() < 1e-6f;

        // The shortest turn from one unit vector to another.
        private static Quaternion Arc(Vector3 from, Vector3 to)
        {
            var w = 1 + Vector3.Dot(from, to);
            if (w < 1e-6f)
            {
                // The vectors are opposite. Turn half a circle about an axis at a right angle to them.
                var axis = Vector3.Cross(from, MathF.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
            }

            return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), w));
        }

        public void UploadBoneData(StorageBuffer bonesBuffer, ref int offset, ref int lastSet)
        {
            BodySkinning.SetBoneData(bonesBuffer, ref offset, ref lastSet);
            HeadSkinning?.SetBoneData(bonesBuffer, ref offset, ref lastSet, HeadConnection.Bone);
            LeftHandSkinning?.SetBoneData(bonesBuffer, ref offset, ref lastSet, LeftHandConnection.Bone);
            RightHandSkinning?.SetBoneData(bonesBuffer, ref offset, ref lastSet, RightHandConnection.Bone);
        }

        public void GetTransforms(Matrix4x4 source, out Matrix4x4 head, out Matrix4x4 leftHand, out Matrix4x4 rightHand)
        {
            if (Head != null)
            {
                head = HeadConnection.Transform.Matrix() * source;
            }
            else
            {
                head = source;
            }

            if (LeftHand != null)
            {
                leftHand = LeftHandConnection.Transform.Matrix() * source;
            }
            else
            {
                leftHand = source;
            }

            if (RightHand != null)
            {
                rightHand = RightHandConnection.Transform.Matrix() * source;
            }
            else
            {
                rightHand = source;
            }
        }

        public void StartScript(Script anmScript, float start_time, float time_scale, float duration, bool loop = false,
            float blendIn = 5f, float blendOut = 5f)
        {
            if (anmScript.HasRootHeight)
            {
                RootHeight = anmScript.RootHeight;
            }

            var inst = new ScriptInstance(start_time)
            {
                Script = anmScript,
                TimeScale = time_scale,
                Duration = duration,
                Loop = loop
            };
            var skipBlendIn = IsDefaultPose();
            inst.BlendInDuration = skipBlendIn ? 0f : blendIn;
            inst.BlendOutDuration = blendOut;
            inst.Parent = this;

            for (var i = 0; i < anmScript.JointMaps.Count; i++)
            {
                ref var jm = ref anmScript.JointMaps[i];
                if (BodySkinning.Bones.TryGetValue(jm.ChildName, out var bb))
                {
                    inst.Joints.Add(new ResolvedJoint(bb, i));
                }
                else if (Head != null && HeadSkinning!.Bones.TryGetValue(jm.ChildName, out var bh))
                {
                    inst.Joints.Add(new ResolvedJoint(bh, i));
                }
                else if (LeftHand != null && LeftHandSkinning!.Bones.TryGetValue(jm.ChildName, out var bl))
                {
                    inst.Joints.Add(new ResolvedJoint(bl, i));
                }
                else if (RightHand != null && RightHandSkinning!.Bones.TryGetValue(jm.ChildName, out var br))
                {
                    inst.Joints.Add(new ResolvedJoint(br, i));
                }
            }

            RunningScripts.Add(inst);
            _hasStarted = true;
        }

        private bool IsDefaultPose()
        {
            if (BodySkinning.Bones.Values.Any(bone => !IsDefaultPose(bone)))
            {
                return false;
            }

            if (HeadSkinning != null)
            {
                if (HeadSkinning.Bones.Values.Any(bone => !IsDefaultPose(bone)))
                {
                    return false;
                }
            }

            if (LeftHandSkinning != null)
            {
                if (LeftHandSkinning.Bones.Values.Any(bone => !IsDefaultPose(bone)))
                {
                    return false;
                }
            }

            if (RightHandSkinning != null)
            {
                return RightHandSkinning.Bones.Values.All(bone => IsDefaultPose(bone));
            }

            return true;
        }

        private static bool IsDefaultPose(BoneInstance bone)
        {
            var dot = MathF.Abs(Quaternion.Dot(bone.Rotation, Quaternion.Identity));
            if (dot < 1f - DefaultPoseRotationEpsilon)
            {
                return false;
            }

            if (bone.Translation.LengthSquared() >
                DefaultPoseTranslationEpsilon * DefaultPoseTranslationEpsilon)
            {
                return false;
            }

            return true;
        }

        public void DebugDraw(LineRenderer lines, Matrix4x4 world, DfmDrawMode mode)
        {
            GetTransforms(world, out var h, out var lh, out var rh);
            HeadSkinning?.DebugDraw(lines, h, mode);
            BodySkinning?.DebugDraw(lines, world, mode);
            LeftHandSkinning?.DebugDraw(lines, world, mode);
            RightHandSkinning?.DebugDraw(lines, world, mode);
        }
    }
}
