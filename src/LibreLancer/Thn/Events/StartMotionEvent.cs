// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    public class StartMotionEvent : ThnEvent
    {
        public string Animation = null!;
        public float StartTime;
        public float TimeScale;
        public int Flags;
        // The time in seconds that the motion takes to blend in. Retail uses 0 when the event has no value.
        public float TransitionTime;

        // Retail event_flags: 2 loops the motion, and 128 makes it an overlay. An overlay keeps the motions under it
        // and blends back to them when it ends. Retail uses 130 for a looping overlay. The engine does not play the
        // flag 1 (backwards).
        public bool Loop => (Flags & 2) != 0;
        public bool Overlay => (Flags & 128) != 0;

        public StartMotionEvent()
        {
        }



        public StartMotionEvent(ThornTable table) : base(table)
        {
            if (!GetProps(table, out var props)) return;

            GetValue(props, "animation", out Animation!);
            GetValue(props, "start_time", out StartTime);
            GetValue(props, "time_scale", out TimeScale, 1f);
            GetValue(props, "event_flags", out Flags);
            GetValue(props, "trans_time", out TransitionTime);
        }

        public StartMotionEvent Clone(string target) => new()
        {
            Targets = [target],
            Time = Time,
            Duration = Duration,
            ParamCurve = ParamCurve,
            Type = Type,
            Animation = Animation,
            StartTime = StartTime,
            TimeScale = TimeScale,
            Flags = Flags,
            TransitionTime = TransitionTime
        };

        public override void Run(ThnScriptInstance instance)
        {
            if (!instance.Objects.TryGetValue(Targets[0], out var obj))
            {
                FLLog.Error("Thn", $"${Targets[0]} does not exist");
                return;
            }

            if (obj.Object is { AnimationComponent: not null }) // Check if object has Cmp animation
            {
                obj.Object.AnimationComponent.StartAnimation(Animation, Loop, StartTime, TimeScale, Duration,
                    transition: TransitionTime, overlay: Overlay);
            }
            else
            {
                FLLog.Error("Thn", $"Unable to start animation on {Targets[0]}");
            }
        }
    }
}
