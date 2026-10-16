// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    public class StartCameraPropAnimEvent : ThnEvent
    {
        [Flags]
        public enum AnimFlags
        {
            Nothing = 0,
            FovH = 1 << 0,
            HVAspect = 1 << 1,
            NearPlane = 1 << 2,
            FarPlane = 1 << 3
        }
        public StartCameraPropAnimEvent() { }

        public AnimFlags SetFlags;
        public float FovH;
        public float HVAspect;
        public float NearPlane;
        public float FarPlane;

        public StartCameraPropAnimEvent(ThornTable table) : base(table)
        {
            if (GetProps(table, out var props))
            {
                if (!GetValue<ThornTable>(props, "cameraprops", out var cameraprops))
                {
                    return;
                }

                // Retail reads the keys of the event by position, not by name. The keys fovh, aspect, near and far go
                // to the camera values in the order fovh, near plane, far plane and aspect. Thus aspect sets the near
                // plane, near sets the far plane and far sets the aspect. The retail values agree: all aspect values
                // are near planes. The names of a CAMERA entity stay as a fallback, with their own meaning.
                if (GetValue(cameraprops, "fovh", out FovH)) SetFlags |= AnimFlags.FovH;
                if (GetValue(cameraprops, "aspect", out NearPlane) || GetValue(cameraprops, "nearplane", out NearPlane))
                    SetFlags |= AnimFlags.NearPlane;
                if (GetValue(cameraprops, "near", out FarPlane) || GetValue(cameraprops, "farplane", out FarPlane))
                    SetFlags |= AnimFlags.FarPlane;
                if (GetValue(cameraprops, "far", out HVAspect) || GetValue(cameraprops, "hvaspect", out HVAspect))
                    SetFlags |= AnimFlags.HVAspect;
            }
        }

        public override void Run(ThnScriptInstance instance)
        {
            if (SetFlags == AnimFlags.Nothing) return;
            if (!instance.Objects.TryGetValue(Targets[0], out var obj))
            {
                FLLog.Error("Thn", $"Entity {Targets[0]} does not exist");
                return;
            }
            if (obj.Camera == null)
            {
                FLLog.Error("Thn", $"Entity {Targets[0]} is not a camera");
                return;
            }
            // When the second target is a camera, retail takes the end value of each key from that camera, not from
            // the event. When the second target is missing or is not a camera, the end values come from the event.
            ThnCameraProps? target = null;
            if (Targets.Length > 1 && instance.Objects.TryGetValue(Targets[1], out var objB))
            {
                target = objB.Camera;
            }

            var anim = new CameraPropAnim()
            {
                Event = this,
                Camera = obj.Camera,
                Target = target,
                OrigFovH = obj.Camera.FovH,
                OrigHVAspect = obj.Camera.AspectRatio,
                OrigNearPlane = obj.Camera.Znear,
                OrigFarPlane = obj.Camera.Zfar
            };
            if (Duration > 0)
            {
                instance.AddProcessor(anim);
            }
            else
            {
                // A zero-duration anim sets the end values at once.
                anim.Run(0);
            }
        }

        // Retail writes from + (to - from) * u for each key that the event sets. from is the value of the camera when
        // the anim starts. u is the value of the param_curve, or the linear time without a curve. Retail reads the end
        // values of a second camera in each frame.
        private class CameraPropAnim : ThnEventProcessor
        {
            public StartCameraPropAnimEvent Event = null!;
            public ThnCameraProps Camera = null!;
            public ThnCameraProps? Target;
            public float OrigFovH;
            public float OrigHVAspect;
            public float OrigNearPlane;
            public float OrigFarPlane;

            private double time;
            public override bool Run(double delta)
            {
                time += delta;
                float t = Event.GetT((float) time);
                if ((Event.SetFlags & AnimFlags.FovH) == AnimFlags.FovH)
                    Camera.FovH = MathHelper.Lerp(OrigFovH, Target?.FovH ?? Event.FovH, t);
                if ((Event.SetFlags & AnimFlags.HVAspect) == AnimFlags.HVAspect)
                    Camera.AspectRatio = MathHelper.Lerp(OrigHVAspect, Target?.AspectRatio ?? Event.HVAspect, t);
                if ((Event.SetFlags & AnimFlags.NearPlane) == AnimFlags.NearPlane)
                    Camera.Znear = MathHelper.Lerp(OrigNearPlane, Target?.Znear ?? Event.NearPlane, t);
                if ((Event.SetFlags & AnimFlags.FarPlane) == AnimFlags.FarPlane)
                    Camera.Zfar = MathHelper.Lerp(OrigFarPlane, Target?.Zfar ?? Event.FarPlane, t);
                return time < Event.Duration;
            }
        }
    }
}
