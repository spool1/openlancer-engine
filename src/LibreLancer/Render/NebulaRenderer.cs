// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Graphics;
using LibreLancer.Render.Materials;
using LibreLancer.Resources;
using ZoneShape = LibreLancer.Data.Schema.Universe.ZoneShape;

namespace LibreLancer.Render
{
	public class NebulaRenderer
	{
		public Nebula Nebula;
        private Random rand;
        private Billboards billboards;
        private List<ExteriorPuff> Exterior = [];
        private SystemRenderer sysr;
        private TexturePanelCollection sprites;

        private int puffId = 0;
        private int puffsIdx = -1;
        private VertexBillboardColor2[] puffVertices = [];
        private InteriorPuff[] interiorPuffs = null!;

        private double dynLightningTimer = 0.0;
        private bool dynLightningActive = false;
        private Vector3 dynamicLightningPos = Vector3.Zero;

        private double cldLightningTimer = 0.0;
        private bool cldLightningActive = false;


		public NebulaRenderer(Nebula n, Game g, SystemRenderer sysr)
		{
			Nebula = n;
            billboards = g.GetService<Billboards>()!;
            this.sysr = sysr;
            sprites = new TexturePanelCollection();
            foreach (var f in n.TexturePanels)
            {
                f.Load(sysr.ResourceManager);
                sprites.AddFile(f);
            }

            rand = new Random();
			if (n.HasInteriorClouds)
			{
				interiorPuffs = new InteriorPuff[n.InteriorCloudCount];
				for (var i = 0; i < n.InteriorCloudCount; i++)
					interiorPuffs[i].Spawned = false;
			}

			GenerateExteriorPuffs();
			// Set Timers
			dynLightningTimer = Nebula.DynamicLightningGap;
			bckLightningTimer = Nebula.BackgroundLightningGap;
        }

		public bool FogTransitioned()
		{
			return FogTransitionOpacity() >= 1f;
		}

        public float FogTransitionOpacity()
        {
            if (Math.Abs(Nebula.Zone!.EdgeFraction) < 0.000000001) // Instant transition
                return 1f;
            return FogTransition(Nebula.Zone, sysr.Camera.Position);
        }

        /// <summary>
        /// The fog opacity at a position: 0 at the edge of the zone or outside it, and 1 at the depth
        /// <c>edge_fraction</c> and deeper. <see cref="Zone.ScaledDistance"/> is 0 at the center and 1 at the edge.
        /// </summary>
        public static float FogTransition(Zone zone, Vector3 position)
		{
			var sd = 1 - MathHelper.Clamp(zone.ScaledDistance(position), 0f, 1f);
			return MathHelper.Clamp(sd / zone.EdgeFraction, 0, 1);
		}

        /// <summary>
        /// The semi-axes of the volume that a nebula draws for a zone: the radius on each axis for a sphere, and the
        /// size for an ellipsoid. <see cref="Zone.ContainsPoint"/>, the fog and the asteroid band read the size in the
        /// same way. Null for a box, a cylinder or a ring, which draw no exterior and no shell.
        /// </summary>
        public static Vector3? ZoneRadii(Zone zone) => zone.Shape switch
        {
            ShapeKind.Sphere => new Vector3(zone.Size.X),
            ShapeKind.Ellipsoid => zone.Size,
            _ => null
        };

        /// <summary>
        /// The semi-axes of the shell of an exclusion zone: the zone radii multiplied by <c>shell_scalar</c>. The
        /// retail scalars are 1, 1.1 and 1.2, so the shell is the wall of the hole. Null when the zone has no radii.
        /// </summary>
        public static Vector3? ShellRadii(NebulaExclusionZone ex) => ZoneRadii(ex.Zone!) * ex.ShellScalar;

        /// <summary>
        /// The position of an exterior puff relative to the zone center, on the surface of the ellipsoid with the
        /// semi-axes <paramref name="radii"/>, at the height <c>y - radii.Y / 2</c> above the zone center.
        /// </summary>
        internal static Vector3 PuffPosition(Vector3 radii, float y, float angle)
        {
            var pos = PrimitiveMath.GetPointOnRadius(radii, y, angle);
            return new Vector3(pos.X, pos.Y - (radii.Y / 2), pos.Z);
        }

		public void RenderFogTransition(RenderContext ren)
		{
			var c = GetFogColor();
			c.A = FogTransitionOpacity();
            ren.TintViewport(c);
        }

        private Color4 GetFogColor()
        {
            return bckLightningActive ? Nebula.BackgroundLightningColor : Nebula.FogColor;
        }

		public void Update(double elapsed)
        {
            if (!Nebula.Zone!.ContainsPoint(sysr.Camera.Position))
            {
                return;
            }

            UpdateBackgroundLightning(elapsed);
            UpdateDynamicLightning(elapsed);
            UpdateCloudLightning(elapsed);

            if (!Nebula.HasInteriorClouds)
            {
                return;
            }

            for (var i = 0; i < Nebula.InteriorCloudCount; i++)
            {
                if (!interiorPuffs[i].Spawned ||
                    Vector3.Distance(interiorPuffs[i].Position, sysr.Camera.Position) > Nebula.InteriorCloudMaxDistance)
                {
                    interiorPuffs[i].Color = GetPuffColor();
                    interiorPuffs[i].Shape = sprites.GetShape(Nebula.InteriorCloudShapes!.GetNext(rand));
                    interiorPuffs[i].Position = sysr.Camera.Position + RandomPointSphere(Nebula.InteriorCloudMaxDistance);
                    interiorPuffs[i].Spawned = true;
                    interiorPuffs[i].Velocity = rand.NextUnitVector() * Nebula.InteriorCloudDrift;
                }
                interiorPuffs[i].Position += interiorPuffs[i].Velocity * (float)elapsed;
            }
        }

        private double bckLightningTimer = 0.0;
        private bool bckLightningActive = false;

        private void UpdateBackgroundLightning(double elapsed)
		{
            // Vanilla St03 has lightning but with 0 duration
			if (!Nebula.BackgroundLightning || Nebula.BackgroundLightningDuration == 0)
				return;
			bckLightningTimer -= elapsed;
			if (!bckLightningActive)
			{
				if (bckLightningTimer <= 0.0)
				{
					bckLightningActive = true;
					bckLightningTimer = Nebula.BackgroundLightningDuration;
				}
			}
			else
			{
				if (bckLightningTimer <= 0.0)
				{
					bckLightningActive = false;
					bckLightningTimer = Nebula.BackgroundLightningGap;
				}
			}
		}

        private void UpdateDynamicLightning(double elapsed)
		{
			if (!Nebula.DynamicLightning)
				return;
			dynLightningTimer -= elapsed;
			if (!dynLightningActive)
			{
				if (dynLightningTimer <= 0.0)
				{
					dynLightningActive = true;
					dynLightningTimer = Nebula.DynamicLightningDuration;
					// spawn dynamic lightning
					dynamicLightningPos = sysr.Camera.Position + RandomPointSphere(Nebula.FogRange.Y);
				}
			}
			else
			{
				if (dynLightningTimer <= 0.0)
				{
					dynLightningActive = false;
					dynLightningTimer = Nebula.DynamicLightningGap;
				}
			}
		}

        private void UpdateCloudLightning(double elapsed)
		{
			if (!Nebula.CloudLightning)
            {
                return;
            }

            cldLightningTimer -= elapsed;
			if (!cldLightningActive)
			{
				if (cldLightningTimer <= 0.0)
				{
					cldLightningActive = true;
					cldLightningTimer = Nebula.CloudLightningDuration;
				}
			}
			else
			{
				if (cldLightningTimer <= 0.0)
				{
					cldLightningActive = false;
					cldLightningTimer = Nebula.CloudLightningGap;
				}
			}
		}

		public void GetLighting(out bool FogEnabled, out Color4? Ambient, out Vector2 FogRange, out Color4 FogColor, out RenderLight? lightning)
		{
			Ambient = Nebula.AmbientColor;
			FogEnabled = Nebula.FogEnabled;
			FogColor = GetFogColor();
			FogRange = Nebula.FogRange;
			var ex = GetExclusion(sysr.Camera.Position);
			if (ex != null)
			{
				var factor = FogTransition(ex.Zone!, sysr.Camera.Position);
				FogRange.Y = MathHelper.Lerp(FogRange.Y, ex.FogFar, factor);
			}

			lightning = null;
			if (dynLightningActive)
			{
				var rl = new RenderLight
                {
                    Kind = LightKind.Point,
                    Color = new Color3f(Nebula.DynamicLightningColor.R,
                        Nebula.DynamicLightningColor.G,
                        Nebula.DynamicLightningColor.B),
                    Position = dynamicLightningPos,
                    Attenuation = new Vector3(1, 0, 0.0000055f),
                    Range = (int)Nebula.FogRange.Y
                };
                lightning = rl;
			}
		}

		public bool DoLightning(out PointLight lt)
		{
			lt = new PointLight();
			if (dynLightningActive)
			{
				lt.Position = new Vector4(dynamicLightningPos, 1);
				lt.ColorRange = new Vector4(
					Nebula.DynamicLightningColor.R,
					Nebula.DynamicLightningColor.G,
					Nebula.DynamicLightningColor.B,
					Nebula.FogRange.Y
				);
				lt.Attenuation = new Vector4(1, 0, 0.0000055f, 0);
				return true;
			}
			return false;
		}

        private NebulaExclusionZone? GetExclusion(Vector3 position)
		{
            return Nebula.ExclusionZones?.FirstOrDefault(zone => zone.Zone!.ContainsPoint(position));
        }

		public void Draw(CommandBuffer buffer)
		{
			var inside = Nebula.Zone!.ContainsPoint(sysr.Camera.Position);
            var z = RenderHelpers.GetZ(Matrix4x4.Identity, sysr.Camera.Position, Nebula.Zone!.Position);
			if (!inside || !FogTransitioned())
				RenderFill(buffer, inside, z);
			DrawPuffRing(inside, buffer, z);
			if (inside)
			{
				var ex = GetExclusion(sysr.Camera.Position);
				if (ex != null)
				{
					RenderExclusionZone(buffer, ex);
				}
				else
					RenderInteriorPuffs();
			}
		}

        private Dictionary<string, RigidModel> shellModels = new();

        private void RenderExclusionZone(CommandBuffer buffer, NebulaExclusionZone ex)
		{
			if (ex.Shell == null)
				return;
            if (!shellModels.TryGetValue(ex.Shell.ModelFile!, out var shellModel))
            {
                var file = (IRigidModelFile) (ex.Shell.LoadFile(sysr.ResourceManager)!.Drawable);
                shellModel = file.CreateRigidModel(true, sysr.ResourceManager);
                shellModels[ex.Shell.ModelFile!] = shellModel;
            }

			// The shell takes the size of the exclusion zone, not the size of the nebula zone.
			if (ShellRadii(ex) is not { } radii)
				return;
			var world = Matrix4x4.CreateScale(radii / shellModel.GetRadius()) * ex.Zone!.RotationMatrix * Matrix4x4.CreateTranslation(ex.Zone.Position);
			// var shell = (ModelFile)ex.Shell;
			// Calculate Alpha
			var alpha = ex.ShellMaxAlpha * FogTransition(ex.Zone, sysr.Camera.Position);
			// Set all render materials. We don't want LOD for this Mesh.
            foreach (var pt in shellModel.AllParts)
            {
                foreach (var dc in pt.Mesh?.Levels?[0]?.Drawcalls ?? [])
                {
                    var mat = dc.GetMaterial(sysr.ResourceManager)?.Render;

                    if (mat is not BasicMaterial basic)
                    {
                        continue;
                    }

                    basic.Oc = alpha;
                    basic.OcEnabled = true;
                    basic.AlphaEnabled = true;
                    basic.Dc = new Color4(ex.ShellTint, alpha) * (Nebula.AmbientColor ?? Color4.White);
                }
            }

            shellModel.Update(0.0);
            shellModel.DrawBuffer(0, buffer, sysr.ResourceManager, world, ref Lighting.Empty);
        }

        private void AddPuffQuad(List<VertexBillboardColor2> vx, Vector3 pos, Vector2 size, Color4 c1, Color4 c2, float angle,
            Vector2 tl,Vector2 tr, Vector2 bl, Vector2 br)
        {
            vx.Add(new VertexBillboardColor2(
                pos, -0.5f * size.X, -0.5f * size.Y, angle,
                c1, c2,
                tl
            ));
            vx.Add(new VertexBillboardColor2(
                pos, 0.5f * size.X, -0.5f * size.Y, angle,
                c1, c2,
                tr
            ));
            vx.Add(new VertexBillboardColor2(
                pos, -0.5f * size.X, 0.5f * size.Y, angle,
                c1, c2,
                bl
            ));
            vx.Add(new VertexBillboardColor2(
                pos, 0.5f * size.X, 0.5f * size.Y, angle,
                c1, c2,
                br
            ));
        }

        private unsafe void DrawPuffRing(bool inside, CommandBuffer buffer, float z)
		{
            /* Skip rendering puff rings */
            if(!inside) {
                if (ZoneRadii(Nebula.Zone!) is not { } sz)
                    return;
                var bitRadius = Nebula.ExteriorBitRadius * (1 + Nebula.ExteriorBitRandomVariation);
                var szR = Math.Max(sz.X, Math.Max(sz.Y, sz.Z));
                var sph = new BoundingSphere(Nebula.Zone!.Position, (szR + bitRadius) * 1.2f);
                if (!sysr.Camera.FrustumCheck(sph))
                    return;
            }

            if (puffsIdx == -1)
                return;
            var world = Nebula.Zone!.RotationMatrix * Matrix4x4.CreateTranslation(Nebula.Zone.Position);
            var wh = buffer.WorldBuffer.SubmitMatrix(ref world);
            /* Actually Render */
			var factor = FogTransition(Nebula.Zone, sysr.Camera.Position);
            var idx = puffsIdx;
            for (var i = 0; i < Exterior.Count; i++)
			{
				var p = Exterior[i];
                buffer.AddCommand(p.Material, null, wh, Lighting.Empty
                    , sysr.QuadBuffer.VertexBuffer, 1.0f,
                    PrimitiveTypes.TriangleList, 0, idx, 2, inside ? SortLayers.NEBULA_INSIDE : SortLayers.NEBULA_NORMAL,
                    z, null, i, *(int*)&factor);
                idx += 6;
			}
		}

        private void GenerateExteriorPuffs()
		{
			var rn = new Random((int)CrcTool.FLModelCrc(Nebula.Zone!.Nickname));
            var verts = new List<VertexBillboardColor2>();
            GeneratePuffRing(0.25f, rn, verts);
            GeneratePuffRing(0.5f, rn, verts);
			GeneratePuffRing(0.75f, rn, verts);
            puffVertices = verts.ToArray();
		}

        public void UploadPuffs() => puffsIdx =  sysr.QuadBuffer.DoVertices(puffVertices);

        private void GeneratePuffRing(float ypct, Random rn, List<VertexBillboardColor2> verts)
		{
            // Only render ellipsoid and sphere exteriors
            if (ZoneRadii(Nebula.Zone!) is not { } sz)
                return;

			var yval = ypct * sz.Y;
			var puffcount = rn.Next(Nebula.ExteriorMinBits, Nebula.ExteriorMaxBits + 1);
			double currentAngle = 0;
			var deltaAngle = (2 * Math.PI) / puffcount;
			for (var i = 0; i < puffcount; i++)
			{
				var y = rn.NextFloat(
					yval - (sz.Y * Nebula.ExteriorMoveBitPercent),
					yval + (sz.Y * Nebula.ExteriorMoveBitPercent)
				);
				var puffPos = PuffPosition(sz, y, (float)currentAngle);
				var radius = rn.NextFloat(
					Nebula.ExteriorBitRadius * (1 - Nebula.ExteriorBitRandomVariation),
					Nebula.ExteriorBitRadius * (1 + Nebula.ExteriorBitRandomVariation)
				);
                var puffSize = new Vector2(radius * 2);
                var shape = sprites.GetShape(Nebula.ExteriorCloudShapes?.GetNext(rn));
                var angle = rn.NextFloat(-MathF.PI, MathF.PI);
                AddPuffQuad(verts, puffPos, puffSize, Nebula.ExteriorColor, Nebula.FogColor, angle,
                    new Vector2(shape.Dimensions.X, shape.Dimensions.Y),
                    new Vector2(shape.Dimensions.X + shape.Dimensions.Width, shape.Dimensions.Y),
                    new Vector2(shape.Dimensions.X, shape.Dimensions.Y + shape.Dimensions.Height),
                    new Vector2(shape.Dimensions.X + shape.Dimensions.Width, shape.Dimensions.Y + shape.Dimensions.Height)
                );

                Exterior.Add(new ExteriorPuff()
                    { Position = puffPos, Material = new NebulaPuffMaterial(sysr.ResourceManager, shape.Texture) });
				currentAngle += deltaAngle;
			}
		}

        private struct ExteriorPuff
        {
            public Vector3 Position;
            public NebulaPuffMaterial Material;
        }

        private struct InteriorPuff
		{
			public bool Spawned;
			public Vector3 Position;
			public Vector3 Velocity;
			public Color3f Color;
			public TextureShape Shape;
		}

        private void RenderInteriorPuffs()
		{
			if (Nebula.HasInteriorClouds)
			{
				for (var i = 0; i < Nebula.InteriorCloudCount; i++)
				{
					if (!interiorPuffs[i].Spawned)
						continue;
					var distance = Vector3.Distance(interiorPuffs[i].Position, sysr.Camera.Position);
					var alpha = Nebula.InteriorCloudMaxAlpha;
					if (distance > Nebula.InteriorCloudFadeDistance.X && distance < Nebula.InteriorCloudFadeDistance.Y)
					{
						var distanceDiff = Nebula.InteriorCloudFadeDistance.Y - Nebula.InteriorCloudFadeDistance.X;
						var current = distance - Nebula.InteriorCloudFadeDistance.X;
						alpha -= (alpha * (distanceDiff - current) / distanceDiff);
					}
					if (distance < Nebula.InteriorCloudFadeDistance.X)
						alpha = 0;
					var shape = interiorPuffs[i].Shape;
					Color4 c = new Color4(interiorPuffs[i].Color, alpha);
					if (cldLightningActive)
					{
						c *= Nebula.CloudLightningColor;
					}
					billboards.Draw(
						(Texture2D)sysr.ResourceManager.FindTexture(shape.Texture)!,
						interiorPuffs[i].Position,
						new Vector2(Nebula.InteriorCloudRadius),
						c,
						new Vector2(shape.Dimensions.X, shape.Dimensions.Y),
						new Vector2(shape.Dimensions.X + shape.Dimensions.Width, shape.Dimensions.Y),
						new Vector2(shape.Dimensions.X, shape.Dimensions.Y + shape.Dimensions.Height),
						new Vector2(shape.Dimensions.X + shape.Dimensions.Width, shape.Dimensions.Y + shape.Dimensions.Height),
						0,
						SortLayers.OBJECT
					);
				}
			}
		}

        private Vector3 RandomPointSphere(float radius)
		{
			var phi = (rand.NextDouble() * (2 * Math.PI));
			var costheta = (-1 + (rand.NextDouble() * 2));
			var u = rand.NextDouble();

			var theta = Math.Acos(costheta);
			var r = radius * Math.Pow(u, 1.0 / 3.0);

			var x = r * Math.Sin(theta) * Math.Cos(phi);
			var y = r * Math.Sin(theta) * Math.Sin(phi);
			var z = r * Math.Cos(theta);
			return new Vector3((float)x, (float)y, (float)z);
		}

        private Color3f GetPuffColor()
		{
			var lerpval = rand.NextDouble();
			var c = Easing.EaseColorRGB(
				EasingTypes.Linear,
				(float)lerpval,
				0,
				1,
				Nebula.InteriorCloudColorA,
				Nebula.InteriorCloudColorB
			);
			return new Color3f(c.R, c.G, c.B);
		}

        private NebulaInteriorMaterial? fillMaterial;

        private void RenderFill(CommandBuffer buffer, bool inside, float z)
		{
			if (Nebula.ExteriorFill == null) return;
			// Only render ellipsoid and sphere exteriors. The fill quads go from -1 to 1 on each axis
			// (QuadBuffer.NebulaFill), so the scale is the radii.
			if (ZoneRadii(Nebula.Zone!) is not { } sz)
				return;
			var p = Nebula.Zone!.Position;
            var sph = new BoundingSphere(p, Math.Max(sz.X, Math.Max(sz.Y, sz.Z)) * 1.2f);
            if (!sysr.Camera.FrustumCheck(sph))
                return;

            fillMaterial ??= new NebulaInteriorMaterial(sysr.ResourceManager, Nebula.ExteriorFill, Nebula.FogColor);
            var transform = Matrix4x4.CreateScale(sz) * Nebula.Zone.RotationMatrix * Matrix4x4.CreateTranslation(p);
            var world = buffer.WorldBuffer.SubmitMatrix(ref transform);

            buffer.AddCommand(
                fillMaterial,
                null,
                world,
                Lighting.Empty,
                sysr.QuadBuffer.VertexBuffer,
                1.0f,
                PrimitiveTypes.TriangleList,
                0,
                0,
                QuadBuffer.NebulaFillPrimCount,
                inside ? SortLayers.NEBULA_INSIDE : SortLayers.NEBULA_NORMAL,
                z, null,
                Exterior?.Count ?? 0
            );
        }
	}
}
