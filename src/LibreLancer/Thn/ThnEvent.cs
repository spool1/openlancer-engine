// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Diagnostics.CodeAnalysis;
using LibreLancer.Render;
using LibreLancer.Thorn;
using LibreLancer.World;

namespace LibreLancer.Thn
{
	public abstract class ThnEvent
    {
        public float Duration;
		public float Time;
        public string[] Targets = [];
        public EventTypes Type;
		public ParameterCurve? ParamCurve;

        protected ThnEvent()
        {
        }

        public virtual void Run(ThnScriptInstance instance)
        {
            FLLog.Error("Thn", $"({Time}): Unimplemented event: {Type}");
        }

        public float GetT(float intime)
        {
            if (Duration <= 0) return 1;
            if (intime > Duration) intime = Duration;
            if (intime < 0) intime = 0;
            if (ParamCurve != null) {
                return ParamCurve.GetValue(intime, Duration);
            }
            else
            {
                return intime / Duration;
            }
        }

        protected ThnEvent(ThornTable table)
        {
            Time = (float) table[1];
            Type = ThnTypes.Convert<EventTypes>(table[2]);
            if (GetProps(table, out var props))
            {
                if (GetValue(props!, "param_curve", out ThornTable? pcurve))
                {
                    ParamCurve = new ParameterCurve(pcurve!);
                    GetValue(props!, "pcurve_period", out ParamCurve.Period);
                }

                GetValue(props!, "duration", out Duration);
            }

            var targetTable = (ThornTable) table[3];
            Targets = new string[targetTable.Length];

            for (int i = 1; i <= Targets.Length; i++)
            {
                Targets[i-1] = (string)targetTable[i];
            }
        }

        protected static bool GetValue<T>(ThornTable table, string key, [MaybeNullWhen(false)] out T result, T def = default!)
        {
            result = def;
            if (!table.TryGetValue(key, out var tmp))
            {
                return false;
            }

            result = ThnTypes.Convert<T>(tmp);
            return true;
        }

        protected static bool GetProps(ThornTable table, [MaybeNullWhen(false)] out ThornTable props)
        {
            props = null;

            if (table.Length < 4)
            {
                return false;
            }

            props = (ThornTable) table[4];
            return true;
        }

        protected static IRenderHardpoint? GetHardpoint(GameObject obj, string hp)
        {
            if (obj.RenderComponent is not CharacterRenderer ch)
            {
                return obj.GetHardpoint(hp);
            }

            ch.Skeleton.Hardpoints.TryGetValue(hp, out var h);
            return h;
        }

        // ROOT ignores the part name. A part or hardpoint that is not found gives the root of the object.
        protected static ThnObjectParent GetAttachParent(ThnSceneObject obj, TargetTypes type, string? part)
        {
            IRenderHardpoint? hardpoint = null;
            RigidModelPart? modelPart = null;
            switch (type)
            {
                case TargetTypes.Hardpoint when !string.IsNullOrEmpty(part):
                {
                    if (obj.Object == null)
                    {
                        FLLog.Error("Thn", "Could not get hardpoints on " + obj.Name);
                    }
                    else
                    {
                        hardpoint = GetHardpoint(obj.Object, part);
                        if (hardpoint == null)
                        {
                            FLLog.Error("Thn", $"Could not find hardpoint {part} on " + obj.Name);
                        }
                    }

                    break;
                }

                case TargetTypes.Part when !string.IsNullOrEmpty(part):
                {
                    // A character has no rigid model. Its parts are the bones of the skeleton.
                    if (obj.Object?.RenderComponent is CharacterRenderer ch)
                    {
                        hardpoint = ch.Skeleton.GetBone(part);
                        if (hardpoint == null)
                        {
                            FLLog.Error("Thn", $"Could not find bone {part} on " + obj.Name);
                        }
                    }
                    else if (obj.Object?.Model?.RigidModel.Parts == null)
                    {
                        FLLog.Error("Thn", "Could not get parts on " + obj.Name);
                    }
                    else if (!obj.Object.Model.RigidModel.Parts.TryGetPart(part, out modelPart))
                    {
                        FLLog.Error("Thn", $"Could not find part {part} on " + obj.Name);
                    }

                    break;
                }
            }

            return new ThnObjectParent(obj, hardpoint, modelPart);
        }

    }
}

