// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Numerics;
using LibreLancer.Render;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibreLancer.Thn
{
	public class ThnCamera : ICamera
	{
		public long frameNo = 0;

        public ThnSceneObject? Object = new()
        {
            Camera = new ThnCameraProps(),
            Translate = Vector3.Zero,
            Rotate = Quaternion.Identity,
            Name = "DEFAULT_OBJECT_UNINITED"
        };

        private Matrix4x4 view;
        private Matrix4x4 projection;
        private Matrix4x4 ogProjection;
        private Matrix4x4 viewProjection;
        private BoundingFrustum frustum;
        private Rectangle viewport;

        private float screenAspect = 1f;

		public ThnCamera(Rectangle vp)
		{
			viewport = vp;
			Update();
		}

        public void SetViewport(Rectangle vp, float screenAspect)
        {
            viewport = vp;
            this.screenAspect = screenAspect;
        }

        public void DefaultZ()
        {
            CalcCameraProps(out float fovv, out float aspectRatio);
            projection = Matrix4x4.CreatePerspectiveFieldOfView(fovv, aspectRatio,
                2.5f, 1000000f);
            viewProjection = view * projection;
            frameNo++;
        }
        public void CameraZ()
        {
            projection = ogProjection;
            viewProjection = view * projection;
            frameNo++;
        }

        // Retail cutscenes are made for a 4:3 screen. On another screen, CalcCameraProps multiplies the tangent of the
        // horizontal half-angle by this factor, and the vertical view stays as on 4:3 (FOVUtil.CalcFovx). A screen
        // overlay is made for 4:3, so it stretches by the same factor to fill the width of the view.
        internal float OverlayStretch => screenAspect / (4f / 3f);

        public bool FrustumCheck(BoundingSphere sphere) => frustum.Intersects(sphere);
        public bool FrustumCheck(BoundingBox box) => frustum.Intersects(box);

        private void CalcCameraProps(out float fovV, out float aspectRatio)
        {
            float viewportRatio = (float) viewport.Width / (float) viewport.Height;
            float fovh = Object!.Camera!.FovH;
            aspectRatio = viewportRatio;
            fovh = FOVUtil.CalcFovx(fovh, screenAspect);
            fovV = FOVUtil.FovVRad(fovh * 2f, viewportRatio);
        }

		public void Update()
        {
            CalcCameraProps(out float fovv, out float aspectRatio);
            // TODO: Tweak clip plane some more - isn't quite right
			// NOTE: near clip plane can't be too small or it causes z-fighting
			projection = Matrix4x4.CreatePerspectiveFieldOfView(fovv, aspectRatio, Object!.Camera!.Znear, Object.Camera.Zfar);
            ogProjection = projection;
            view = new Transform3D(Object.Translate, Object.Rotate).Inverse().Matrix();
            // var transform = Object.Rotate * Matrix4x4.CreateTranslation(Object.Translate);
            // Matrix4x4.Invert(transform, out view);
			viewProjection = view * projection;
			frustum = new BoundingFrustum(viewProjection);
		}

		public Vector3 Position
		{
			get
			{
				return Object!.Translate;
			}
		}

		public Matrix4x4 Projection
		{
			get
			{
				return projection;
			}
		}

		public Matrix4x4 View
		{
			get
			{
				return view;
			}
		}

		public Matrix4x4 ViewProjection
		{
			get
			{
				return viewProjection;
			}
		}
	}
}
