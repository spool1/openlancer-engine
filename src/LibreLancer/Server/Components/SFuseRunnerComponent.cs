// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data;
using LibreLancer.Data.Schema.Fuses;
using LibreLancer.Data.GameData;
using LibreLancer.Net.Protocol;
using LibreLancer.World;

namespace LibreLancer.Server.Components
{
    public class SFuseRunnerComponent : GameComponent
    {
        public List<SpawnedEffect> Effects = [];

        public SFuseRunnerComponent(GameObject parent) : base(parent)
        {
        }

        private class FuseInstance
        {
            public Queue<(float Time, FuseAction Action)> Actions;
            public FuseResources Fuse;
            public double T;

            public FuseInstance(Queue<(float Time, FuseAction Action)> actions, FuseResources fuse, double t)
            {
                Actions = actions;
                Fuse = fuse;
                T = t;
            }
        }

        private List<FuseInstance> instances = [];

        public void Run(FuseResources fuse)
        {
            var instance = new FuseInstance(new(Schedule(fuse.Fuse.Actions, Random.Shared)), fuse, 0.0);
            instances.Add(instance);
        }

        // The actions of one ignition of a fuse, in the order of their times. An action with two at_t values gets a
        // new random time between them at each ignition. Actions with the same time keep the order of the file.
        internal static IEnumerable<(float Time, FuseAction Action)> Schedule(IEnumerable<FuseAction> actions,
            Random random) =>
            actions.Select(x => (x.AtTMax > x.AtT ? x.AtT + (x.AtTMax - x.AtT) * random.NextSingle() : x.AtT, x))
                .OrderBy(x => x.Item1);

        private BitArray128 runningHealthFuses = new();
        public List<DamageFuse> DamageFuses = [];

        public bool RunningDeathFuse => instances.Any(x => x.Fuse.Fuse.DeathFuse);

        public void RunAtHealth(float t)
        {
            for (int i = 0; i < DamageFuses.Count; i++)
            {
                if (t < DamageFuses[i].Threshold && !runningHealthFuses[i])
                {
                    runningHealthFuses[i] = true;
                    Run(DamageFuses[i].Fuse!);
                    FLLog.Debug("Server", $"Running fuse {DamageFuses[i].Fuse!.Fuse.Name}");
                }
            }
        }

        private uint fxID = 1;

        private void Update(double time, GameWorld world, FuseInstance instance)
        {
            instance.T += time / instance.Fuse.Fuse.Lifetime;
            while (instance.Actions.Count > 0 && instance.Actions.Peek().Time <= instance.T)
            {
                var act = instance.Actions.Dequeue().Action;

                if (act is FuseStartEffect fxact)
                {
                    Effects.Add(new SpawnedEffect()
                    {
                        ID = fxID++, Effect = fxact.Effect,
                        Hardpoints = fxact.Hardpoints.Select(FLHash.CreateID).ToArray(),
                    });
                    world.Server!.EffectSpawned(Parent);
                }
                else if (act is FuseDestroyGroup dst)
                {
                    if (dst.Fate == FusePartFate.disappear)
                    {
                        Parent.DisableCmpPart(dst.GroupName!, world, GetResourceManager(world)!, out _);
                    }
                    else if (dst.Fate == FusePartFate.debris)
                    {
                        Parent.SpawnDebris(dst.GroupName!, world, GetResourceManager(world)!);
                    }
                }
                else if (act is FuseDestroyHpAttachment)
                {

                }
                else if (act is FuseImpulse)
                {

                }
                else if (act is FuseIgniteFuse ig)
                {
                    var start = true;

                    foreach (var fuseInstance in instances)
                    {
                        if (fuseInstance.Fuse.Fuse.Name.Equals(ig.Fuse, StringComparison.OrdinalIgnoreCase))
                        {
                            FLLog.Debug("Fuse", $"Fuse already running {ig.Fuse}");
                            start = false;
                            break;
                        }
                    }

                    if (start)
                    {
                        FLLog.Debug("Fuse", $"Igniting {ig.Fuse}");
                        Run(GetGameData(world)!.Items.Fuses.Get(ig.Fuse)!);
                    }
                }
                else if (act is FuseDestroyRoot)
                {
                    FLLog.Debug("Fuse", $"Killing {Parent}");

                    if (Parent.TryGetComponent<SDestroyableComponent>(out var destroy))
                    {
                        destroy.Destroy(true);
                    }
                }
            }
        }

        public override void Update(double time, GameWorld world)

        {
            for (int i = 0; i < instances.Count; i++)
            {
                Update(time, world, instances[i]);
            }
        }
    }
}
