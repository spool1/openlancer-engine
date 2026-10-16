// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Graphics;
using LibreLancer.Render.Materials;
using LibreLancer.Utf.Ale;

namespace LibreLancer.Fx
{
    public class FxBasicAppearance : FxAppearance
    {
        public bool QuadTexture = true;
        public bool MotionBlur;
        public AlchemyColorAnimation Color;
        public AlchemyFloatAnimation Alpha;
        public AlchemyFloatAnimation? HToVAspect;
        public AlchemyFloatAnimation? Rotate;
        public AlchemyFloatAnimation Size;
        public ushort BlendInfo = BlendMode.Normal;
        public string Texture = "";
        public bool UseCommonTexFrame = false;
        public AlchemyFloatAnimation? TexFrame;
        public AlchemyCurveAnimation? CommonTexFrame;
        public bool FlipHorizontal = false;
        public bool FlipVertical = false;

        public FxBasicAppearance(AlchemyNode ale) : base(ale)
        {
            var tt = ale.GetBoolean(AleProperty.BasicApp_TriTexture);

            if (tt)
            {
                QuadTexture = false;
            }

            MotionBlur = ale.GetBoolean(AleProperty.BasicApp_MotionBlur);
            Color = ale.GetColorAnimation(AleProperty.BasicApp_Color)!;
            Alpha = ale.GetFloatAnimation(AleProperty.BasicApp_Alpha)!;
            HToVAspect = ale.GetFloatAnimation(AleProperty.BasicApp_HToVAspect, false);
            Rotate = ale.GetFloatAnimation(AleProperty.BasicApp_Rotate, false);
            Texture = ale.GetString(AleProperty.BasicApp_TexName) ?? "";
            UseCommonTexFrame = ale.GetBoolean(AleProperty.BasicApp_UseCommonTexFrame);
            TexFrame = ale.GetFloatAnimation(AleProperty.BasicApp_TexFrame, false);
            CommonTexFrame = ale.GetCurveAnimation(AleProperty.BasicApp_CommonTexFrame, false);
            FlipHorizontal = ale.GetBoolean(AleProperty.BasicApp_FlipTexU);
            FlipVertical = ale.GetBoolean(AleProperty.BasicApp_FlipTexV);
            Size = ale.GetFloatAnimation(AleProperty.BasicApp_Size)!;

            if (ale.TryGetParameter(AleProperty.BasicApp_BlendInfo, out var temp))
            {
                BlendInfo = BlendMap.Map((Tuple<uint, uint>) temp.Value);
            }
        }

        public FxBasicAppearance(string name) : base(name)
        {
            Size = new(1);
            Color = new(Color3f.White);
            Alpha = new(1);
        }

        protected void SerializeColorTextureParameters(AlchemyNode n)
        {
            n.Parameters.Add(new(AleProperty.BasicApp_QuadTexture, QuadTexture));
            n.Parameters.Add(new(AleProperty.BasicApp_TriTexture, !QuadTexture));
            n.Parameters.Add(new(AleProperty.BasicApp_Color, Color));
            n.Parameters.Add(new(AleProperty.BasicApp_Alpha, Alpha));
            if (Rotate != null)
            {
                n.Parameters.Add(new(AleProperty.BasicApp_Rotate, Rotate));
            }
            n.Parameters.Add(new(AleProperty.BasicApp_TexName, Texture));
            n.Parameters.Add(new(AleProperty.BasicApp_UseCommonTexFrame, UseCommonTexFrame));
            if (TexFrame != null)
            {
                n.Parameters.Add(new(AleProperty.BasicApp_TexFrame, TexFrame));
            }
            if (CommonTexFrame != null)
            {
                n.Parameters.Add(new(AleProperty.BasicApp_CommonTexFrame, CommonTexFrame));
            }
            n.Parameters.Add(new(AleProperty.BasicApp_FlipTexU, FlipHorizontal));
            n.Parameters.Add(new(AleProperty.BasicApp_FlipTexV, FlipVertical));
            var (src, dst) = BlendMode.Deconstruct(BlendInfo);
            n.Parameters.Add(new(AleProperty.BasicApp_BlendInfo, new Tuple<uint, uint>((uint) src, (uint) dst)));
        }

        public override AlchemyNode SerializeNode()
        {
            var n = base.SerializeNode();
            n.Parameters.Add(new(AleProperty.BasicApp_MotionBlur, MotionBlur));
            if (HToVAspect != null)
            {
                n.Parameters.Add(new(AleProperty.BasicApp_HToVAspect, HToVAspect));
            }
            n.Parameters.Add(new(AleProperty.BasicApp_Size, Size));
            SerializeColorTextureParameters(n);
            return n;
        }

        public override void Draw(ParticleEffectInstance instance, AppearanceReference node, int nodeIdx,
            Matrix4x4 transform, float sparam)
        {
            var count = instance.Buffer.GetCount(nodeIdx);
            TextureHandler.Update(Texture, instance.Resources!);
            var nodeTr = instance.NodeWorldTransforms[node.NodeIdx];

            for (var i = 0; i < count; i++)
            {
                ref var particle = ref instance.Buffer[nodeIdx, i];
                var time = particle.TimeAlive / particle.LifeSpan;
                var p = Vector3.Transform(Vector3.Transform(particle.Position, particle.Orientation), nodeTr);
                var v = Vector3.TransformNormal(particle.Velocity, nodeTr);
                var c = Color.GetValue(sparam, time);
                var a = Alpha.GetValue(sparam, time);
                instance.Pool?.AddParticle(
                    TextureHandler,
                    p,
                    ParticleSize(sparam, time),
                    new Color4(c, a),
                    GetFrame((float) instance.GlobalTime, sparam, ref particle),
                    v, //For motion blur pass not normalized velocity as normal (needed)
                    ParticleRotation(sparam, time),
                    FlipHorizontal, FlipVertical,
                    MotionBlur
                );
            }

            instance.Pool?.DrawBuffer(ParticleDrawKind.Basic, this, instance.Resources!, transform,
                (instance.DrawIndex << 11) + nodeIdx, instance.ScreenStretch);
        }

        // The width and the height of a particle. Size is the half-height, and HToVAspect is the ratio of the width
        // to the height. The retail TV lines need this width to cover a 4:3 view.
        internal Vector2 ParticleSize(float sparam, float time)
        {
            var size = Size?.GetValue(sparam, time) ?? 1f;
            var aspect = HToVAspect?.GetValue(sparam, time) ?? 1f;
            return new Vector2(size * aspect, size) * 2;
        }

        // The angle of a particle on the screen, in radians. Retail gives the BasicApp_Rotate value to the sine and the
        // cosine without a conversion. For example, the retail base glows have an angle of about pi/2 and a small
        // aspect, so they are horizontal streaks.
        internal float ParticleRotation(float sparam, float time) => Rotate?.GetValue(sparam, time) ?? 0f;

        public readonly ParticleTexture TextureHandler = new();

        protected float GetFrame(float globaltime, float sparam, ref Particle particle)
        {
            float frame = 0;

            if (UseCommonTexFrame)
            {
                frame = CommonTexFrame?.GetValue(sparam, globaltime) ?? 0;
            }
            else
            {
                frame = TexFrame?.GetValue(sparam, particle.TimeAlive / particle.LifeSpan) ?? 0;
            }

            return MathHelper.Clamp(frame, 0, 1);
        }
    }
}
