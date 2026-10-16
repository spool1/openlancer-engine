// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    public class StartSpatialPropAnimEvent : ThnEvent
    {
        [Flags]
        public enum AnimVars
        {
            Nothing = 0,
            QOrient = 1 << 1,
            Orient = 1 << 2,
            AxisRot = 1 << 3,
            Pos = 1 << 4
        }

        public AnimVars SetFlags;
        public Quaternion Q_Orient;
        public Matrix4x4 Orient;
        public AxisRotation AxisRot;
        public Vector3 Pos;
        public TargetTypes TargetType;
        public string? TargetPart;

        public struct AxisRotation
        {
            public ThnAxis Axis;
            public float Degrees;

            public float GetRads(float pct)
            {
                var degs = MathHelper.Lerp(0, Degrees, pct);
                return MathHelper.DegreesToRadians(-degs);
            }

            // Retail turns about the positive axis also for a NEG axis. X_AXIS, Y_AXIS and Z_AXIS turn the object
            // about its own axis. NEG_X_AXIS, NEG_Y_AXIS and NEG_Z_AXIS turn it about the axis of its parent.
            // The result is normalized, because an anim adds a turn in each frame.
            internal Quaternion Turn(Quaternion rotation, float pct)
            {
                var positive = (ThnAxis) ((int) Axis % 3);
                var turn = Quaternion.CreateFromAxisAngle(ThnTypes.AxisVector(positive), GetRads(pct));
                return Quaternion.Normalize(Axis == positive ? rotation * turn : turn * rotation);
            }
        }

        public StartSpatialPropAnimEvent()
        {
        }

        public StartSpatialPropAnimEvent(ThornTable table, string source) : base(table)
        {
            if (!GetProps(table, out var props))
            {
                return;
            }

            GetValue(props!, "target_type", out TargetType);
            GetValue(props!, "target_part", out TargetPart);

            if (!GetValue(props!, "spatialprops", out ThornTable? sp))
            {
                return;
            }

            if (GetValue(sp!, "q_orient", out Q_Orient))
            {
                SetFlags |= AnimVars.QOrient;
            }

            if (GetValue(sp!, "orient", out Orient))
            {
                SetFlags |= AnimVars.Orient;
            }

            if (GetValue(sp!, "axisrot", out ThornTable? axisrot))
            {
                SetFlags |= AnimVars.AxisRot;

                if (!axisrot!.TryGetValue(2, out var o))
                {
                    FLLog.Error("Thn", "START_SPATIAL_PROP_ANIM axisrot missing axis");
                }
                else
                {
                    AxisRot.Axis = ThnTypes.ConvertAxis(o, source);
                }

                AxisRot.Degrees = (float) axisrot[1];
            }

            if (GetValue(sp!, "pos", out Pos))
            {
                SetFlags |= AnimVars.Pos;
            }
        }

        public override void Run(ThnScriptInstance instance)
        {
            if (Targets.Length == 0)
            {
                return;
            }

            var hasPos = (SetFlags & AnimVars.Pos) == AnimVars.Pos;
            var hasQuat = (SetFlags & AnimVars.Orient) == AnimVars.Orient ||
                          (SetFlags & AnimVars.QOrient) == AnimVars.QOrient;

            Quaternion quat = Q_Orient;
            if ((SetFlags & AnimVars.Orient) == AnimVars.Orient)
            {
                quat = Orient.ExtractRotation();
            }

            if (!instance.Objects.TryGetValue(Targets[0], out var objA))
            {
                FLLog.Error("Thn", $"Object does not exist {Targets[0]}");
                return;
            }

            ThnObjectParent? target = null;
            if (Targets.Length > 1)
            {
                if (!instance.Objects.TryGetValue(Targets[1], out var objB))
                {
                    FLLog.Error("Thn", $"Object does not exist {Targets[1]}");
                    return;
                }

                // target_type and target_part can select a hardpoint or a part, for example the head of a character
                // for an IK look marker.
                target = GetAttachParent(objB, TargetType, TargetPart);
            }

            var routine = new SpatialAnimRoutine(this, objA, target, new Transform3D(Pos, quat), hasPos, hasQuat);
            if (Duration < float.Epsilon)
            {
                // A zero-duration anim runs the routine one time, at its end.
                routine.Run(0);
            }
            else
            {
                instance.AddProcessor(routine);
            }
        }

        // In each frame, retail moves the object from its current pose a part w of the remaining way to the target.
        // u is the value of the param_curve (linear without a curve). While u is below 1,
        // w = (u - uPrev) / (1 - uPrev), where uPrev is the last u below 1. When u is 1 or more, w is 1 and uPrev does
        // not change. Thus, for a target that does not move, the object is at from + (to - from) * u until u gets to 1.
        // After that, it stays on the target. Copies of one event compound, the object pursues a second target that
        // moves, and other moves of the object stay.
        private sealed class SpatialAnimRoutine(StartSpatialPropAnimEvent ev, ThnSceneObject obj,
            ThnObjectParent? target, Transform3D end, bool hasPos, bool hasQuat) : ThnEventProcessor
        {
            private double time;
            private float uPrev;
            // The part of the axisrot turn that the routine applied.
            private float turned;

            public override bool Run(double delta)
            {
                time = Math.Min(time + delta, ev.Duration);
                var u = ev.GetT((float) time);
                var w = 1f;
                if (u < 1)
                {
                    w = (u - uPrev) / (1 - uPrev);
                    uPrev = u;
                }

                var (toPos, toRot) = target?.GetTransform(false) ?? end;
                var rot = obj.Rotate;
                if (hasPos)
                {
                    obj.Translate += (toPos - obj.Translate) * w;
                }

                if (hasQuat)
                {
                    obj.Rotate = Quaternion.Slerp(rot, toRot, w);
                }

                if ((ev.SetFlags & AnimVars.AxisRot) == AnimVars.AxisRot)
                {
                    // Retail turns the pose from the start of the frame by the change of u since the last frame. The
                    // turn replaces the q_orient or orient result of the frame.
                    obj.Rotate = ev.AxisRot.Turn(rot, u - turned);
                    turned = u;
                }

                return time < ev.Duration;
            }
        }
    }
}
