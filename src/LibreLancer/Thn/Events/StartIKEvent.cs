// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using LibreLancer.Render;
using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    public class StartIKEvent : ThnEvent
    {
        public string? EndEffector;
        public int CountToRoot;
        public TargetTypes TargetType;
        public string? TargetPart;
        // Only move_to uses the damping.
        public float Damping;
        public ThnAxis Up;
        public ThnAxis Front;
        public bool PointAt;
        public bool MoveTo;
        public float TransitionDuration;
        public int Flags;

        // Retail event_flags 128. The game exe changes the THN flags into the flags of the IK code, and that part of
        // the exe is encrypted. Thus we infer the meaning from the motion flags. An overlay keeps the animation under
        // the goal and blends back to it when the goal ends.
        public bool Overlay => (Flags & 128) != 0;

        public StartIKEvent() { }

        public StartIKEvent(ThornTable table, string source) : base(table)
        {
            if (!GetProps(table, out var props))
            {
                return;
            }

            GetValue(props, "end_effector", out EndEffector);
            GetValue(props, "count_to_root", out CountToRoot);
            GetValue(props, "target_type", out TargetType);
            GetValue(props, "target_part", out TargetPart);
            GetValue(props, "damping", out Damping);
            GetValue(props, "point_at", out PointAt);
            GetValue(props, "move_to", out MoveTo);
            GetValue(props, "transition_duration", out TransitionDuration);
            GetValue(props, "event_flags", out Flags);

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
            if (!PointAt)
            {
                if (MoveTo)
                {
                    FLLog.Warning("Thn", $"({Time}) START_IK move_to is not implemented, ignoring");
                }

                return;
            }

            if (!instance.Objects.TryGetValue(Targets[0], out var obj))
            {
                FLLog.Error("Thn", $"Entity {Targets[0]} does not exist");
                return;
            }

            if (obj.Object is not { RenderComponent: CharacterRenderer ch })
            {
                FLLog.Error("Thn", $"StartIK: Entity {Targets[0]} is not deformable");
                return;
            }

            if (Targets.Length < 2 || !instance.Objects.TryGetValue(Targets[1], out var target))
            {
                FLLog.Error("Thn", $"({Time}) StartIK: The target of {Targets[0]} does not exist");
                return;
            }

            instance.AddProcessor(new IKProcessor(this, instance, ch.Skeleton,
                GetAttachParent(target, TargetType, TargetPart)));
        }

        // The goal blends in over the transition with the cosine ease of retail. Retail does not step back to the
        // animation when the goal ends. An overlay goal with a transition blends back to the animation over the
        // transition. The retail motion rules free the motions under a motion that is not an overlay or that has no
        // transition. We infer that the other goals do the same, so their joints hold the pose until a motion or a
        // new goal takes them. A motion that started on a joint during the goal is over the goal, so the joint blends
        // back to it (see DfmSkeletonManager.EndIK). When the script ends, an active goal or a blend-back ends at once.
        private class IKProcessor(StartIKEvent ev, ThnScriptInstance instance, DfmSkeletonManager skeleton,
            ThnObjectParent target) : ThnEventProcessor
        {
            private double time;
            // The goal reached its duration.
            private bool ended;

            public override bool Run(double delta)
            {
                time += delta;
                // The script runs no processor after the update in which it ends. Thus the goal also ends there.
                if (instance.CurrentTime > instance.Duration)
                {
                    skeleton.ClearIK(this);
                    return false;
                }

                if (time >= ev.Duration)
                {
                    return Release(time - ev.Duration);
                }

                var goal = new DfmSkeletonManager.IKGoal()
                {
                    Bone = ev.EndEffector ?? "",
                    CountToRoot = ev.CountToRoot,
                    Front = ThnTypes.AxisVector(ev.Front),
                    Up = ThnTypes.AxisVector(ev.Up),
                    TargetWorld = target.GetTransform(false).Position,
                    Weight = DfmSkeletonManager.TransitionWeight(time, ev.TransitionDuration)
                };

                if (!skeleton.SetIK(this, goal))
                {
                    FLLog.Error("Thn", $"({ev.Time}) Could not find bone {ev.EndEffector} on {ev.Targets[0]}");
                    return false;
                }

                return true;
            }

            // Ends the goal. release is the time since the end of the goal.
            private bool Release(double release)
            {
                if (!ended)
                {
                    ended = true;
                    skeleton.EndIK(this, ev.TransitionDuration, ev.Overlay);
                }

                if (skeleton.ReleaseIK(this, release))
                {
                    return true;
                }

                skeleton.ClearIK(this);
                return false;
            }
        }
    }
}