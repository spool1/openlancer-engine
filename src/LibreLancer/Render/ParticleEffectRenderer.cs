// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Fx;
using LibreLancer.Thn;
using LibreLancer.World;

namespace LibreLancer.Render
{
	public class ParticleEffectRenderer : ObjectRenderer
	{
		public float SParam = 0f;
		public bool Active = true;
        private SystemRenderer sys = null!;
        private ParticleEffectInstance? fx;
        public bool Finished = false;
        public int Index; // needed to fix fuses spawning multiple fx on top of each-other
        public Hardpoint? Attachment;
        // A cutscene camera that holds this effect in front of its lens as a screen overlay, or null for an effect
        // in the world. The cutscene sets it in each frame.
        internal ThnCamera? OverlayCamera;

        private Matrix4x4 tr;
        private Vector3 pos;
        private float dist = float.MaxValue;
        private const float CULL_DISTANCE = 20000;
        private const float CULL = CULL_DISTANCE * CULL_DISTANCE;

        private Vector3 cameraPos;

		public ParticleEffectRenderer(ParticleEffect? effect)
		{
            if (effect == null)
            {
                return;
            }

            fx = new ParticleEffectInstance(effect);
		}

        public void Restart()
        {
            fx?.Reset();
        }

        // For tests.
        internal ParticleEffectInstance? Instance => fx;

        // The horizontal stretch of the effect when the camera draws it. Only the camera of a screen overlay
        // stretches the effect. Another view of the same effect, for example a free camera in an editor, shows the
        // effect in the world.
        internal float ScreenStretch(ICamera camera) =>
            OverlayCamera != null && camera == OverlayCamera ? OverlayCamera.OverlayStretch : 1;

        public override bool PrepareRender(ICamera camera, NebulaRenderer? nr, SystemRenderer sys, bool forceCull)
        {
            if (fx == null)
            {
                return false;
            }

            this.sys = sys;
            cameraPos = camera.Position;
            dist = Vector3.DistanceSquared(pos, camera.Position);
            fx.Resources = sys.ResourceManager;
            if (Active && dist < Math.Pow(20000, 2) && !forceCull)
            {
                sys.AddObject(this);
                fx.Pool = sys.FxPool;
                fx.UpdateCull(camera);
                return true;
            }

            fx.Pool = null!;
            return false;
        }

		public override void Update(double time, Vector3 position, Matrix4x4 transform)
		{
            if (fx == null)
            {
                return;
            }

            if (Attachment != null) {
                transform = Attachment.Transform.Matrix() * transform;
                position = Vector3.Transform(Vector3.Zero, transform);
            }
			pos = position;
            dist = Vector3.DistanceSquared(position, cameraPos);

            if (!Active || !(dist < CULL))
            {
                return;
            }

            tr = transform;
            fx.Update(time, transform, SParam);
            fx.DrawIndex = Index;
            if (fx.IsFinished())
            {
                Finished = true;
            }
        }

		public override void Draw(ICamera camera, CommandBuffer commands, SystemLighting lights, NebulaRenderer nr)
		{
            if (fx == null)
            {
                return;
            }

            if(!fx.Culled)
            {
                fx.ScreenStretch = ScreenStretch(camera);
                fx.Draw(tr,SParam);
            }
        }

        // nice name in debugger window
        public override string ToString()
        {
            return fx == null ? "Null ParticleFx" : $"[{this.GetType().Name}] {fx.Effect.Nickname}";
        }
    }
}
