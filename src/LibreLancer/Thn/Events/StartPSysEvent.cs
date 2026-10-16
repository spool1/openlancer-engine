// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using LibreLancer.Client.Components;
using LibreLancer.Render;
using LibreLancer.Thorn;

namespace LibreLancer.Thn.Events
{
    public class StartPSysEvent : ThnEvent
    {
        public StartPSysEvent() { }
        public StartPSysEvent(ThornTable table) : base(table) { }

        public override void Run(ThnScriptInstance instance)
        {
            if (!instance.Objects.TryGetValue(Targets[0], out var obj))
            {
                FLLog.Error("Thn", "Entity " + Targets[0] + " does not exist");
                return;
            }

            if (obj.Engine != null)
            {
                obj.Engine.Active = true;
                instance.AddProcessor(new StopEngine(obj.Engine, Duration));
                return;
            }

            if (obj.Object == null)
            {
                FLLog.Error("Thn", "Entity " + Targets[0] + " null renderer");
                return;
            }
            
            var r = (ParticleEffectRenderer)obj.Object.RenderComponent!;
            // START_PSYS starts the effect from its beginning. s069x fires one muzzle-flare effect again for each shot.
            // A start at or after the end of the earlier start also restarts an effect that is still active.
            // The processors run after the events, so the stop of the earlier start can come later in this frame.
            var start = Milliseconds(Time);
            if (!r.Active || obj.PSysEnd <= start)
            {
                r.Restart();
            }
            r.Active = true;
            obj.PSysEnd = Milliseconds((double)Time + Duration);
            instance.AddProcessor(new StopPSys(obj, r, Duration));
        }

        // Retail keeps event times in whole milliseconds.
        private static long Milliseconds(double seconds) => (long)Math.Round(seconds * 1000);

        private class StopEngine : ThnEventProcessor
        {
            private double time;
            public double Duration;
            public CEngineComponent Fx;

            public StopEngine(CEngineComponent fx, double duration)
            {
                Fx = fx;
                Duration = duration;
            }

            public override bool Run(double delta)
            {
                time += delta;

                if(time >= Duration)
                {
                    Fx.Active = false;
                    return false;
                }

                return true;
            }
        }

        private class StopPSys : ThnEventProcessor
        {
            private double time;
            public double Duration;
            public ParticleEffectRenderer Fx;
            private readonly ThnSceneObject obj;
            private readonly long end;

            public StopPSys(ThnSceneObject obj, ParticleEffectRenderer fx, double duration)
            {
                this.obj = obj;
                Fx = fx;
                Duration = duration;
                end = obj.PSysEnd;
            }

            public override bool Run(double delta)
            {
                time += delta;
                if(time >= Duration)
                {
                    // Only the last START_PSYS on the entity stops the effect.
                    if (obj.PSysEnd == end)
                    {
                        Fx.Active = false;
                    }
                    return false;
                }
                return true;
            }

        }
    }
}
