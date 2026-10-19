// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    /*
     * NOTES:
     * LOOK_AT aims at the parent plus the offset
     * LOOK_AT rotates directly, object and camera
     * no need for LookAtFunc
     *
     * Attached position is kept after event end, just stops updating
     */
    public class AttachEntityEvent : ThnEvent
    {
        private class AttachEntityProcessor(float duration, ThnAttachment attachment) : ThnEventProcessor
        {
            public float Duration = duration;
            public ThnAttachment Attachment = attachment;

            private double t;

            public override bool Run(double delta)
            {
                t += delta;
                if (t >= Duration)
                {
                    // Retail updates an attach for the last time at its end time, and then stops it. So an attach
                    // of 0 s applies one time, also at 0 s. The scene object applies the attachment that one time,
                    // then removes it.
                    Attachment.Expired = true;
                    return false;
                }
                return true;
            }
        }


        public AttachEntityEvent() { }

        public TargetTypes TargetType;
        public AttachFlags Flags;
        public string? TargetPart;
        public Vector3 Offset;
        public ThnAxis Up = ThnAxis.YAxis;
        public ThnAxis Front = ThnAxis.NegZAxis;

        public AttachEntityEvent(ThornTable table, string source) : base(table)
        {
            if (!GetProps(table, out var props))
            {
                return;
            }

            GetValue(props, "target_type", out TargetType);
            GetValue(props, "flags", out Flags, AttachFlags.Position | AttachFlags.Orientation);
            GetValue(props, "target_part", out TargetPart);
            GetValue(props, "offset", out Offset);

            if (props.TryGetValue("up", out var o))
            {
                Up = ThnTypes.ConvertAxis(o, source);
            }

            if (props.TryGetValue("front", out o))
            {
                Front = ThnTypes.ConvertAxis(o, source);
            }
        }

        public override void Run(ThnScriptInstance instance)
        {
            if(!instance.Objects.TryGetValue(Targets[0], out var objA))
            {
                FLLog.Error("Thn", "Object doesn't exist " + Targets[0]);
                return;
            }

            if(!instance.Objects.TryGetValue(Targets[1], out var objB))
            {
                FLLog.Error("Thn", "Object doesn't exist " + Targets[1]);
                return;
            }

            // Attach GameObjects to eachother
            var tgt = GetAttachParent(objB, TargetType, TargetPart);
            var attachment = new ThnAttachment(tgt)
            {
                Position = ((Flags & AttachFlags.Position) == AttachFlags.Position),
                Orientation = ((Flags & AttachFlags.Orientation) == AttachFlags.Orientation),
                OrientationRelative = ((Flags & AttachFlags.OrientationRelative) == AttachFlags.OrientationRelative),
                EntityRelative = ((Flags & AttachFlags.EntityRelative) == AttachFlags.EntityRelative),
                LookAt = ((Flags & AttachFlags.LookAt) == AttachFlags.LookAt),
                ParentChild = ((Flags & AttachFlags.ParentChild) == AttachFlags.ParentChild),
                Offset = Offset,
                Up = ThnTypes.AxisVector(Up),
                Front = ThnTypes.AxisVector(Front),
            };
            // Retail records the pose of the child relative to the parent when the attach starts. When the parent has
            // no pose yet, the child records it at its first update after the parent has a pose.
            if (tgt.HasPose)
            {
                attachment.Record(objA.GetTransform());
            }
            objA.Attachments.Add(attachment);
            instance.AddProcessor(new AttachEntityProcessor(Duration, attachment));
        }
    }
}
