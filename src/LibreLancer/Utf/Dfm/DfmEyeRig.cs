using System;
using System.Linq;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Utf.Cmp;

namespace LibreLancer.Utf.Dfm
{
    // The eyes of a DFM head, as retail (Deformable2.dll) moves them. The pupils are part of the eye texture. The
    // first UV bone of the mesh moves that texture: each frame, the texture of each eye face group moves by an
    // offset that the direction of the bone gives. In the retail heads, Eye IK Left moves both eyes. Retail does not
    // read the UV_vertex_id and UV_default_list nodes, and the other levels of detail have no UV bone.
    internal sealed class DfmEyeRig
    {
        private readonly FaceGroup[] groups;
        private readonly int boneId;
        private readonly float planeDistance;
        private readonly Vector2 scale;
        private readonly Vector2 min;
        private readonly Vector2 max;

        internal DfmEyeRig(FaceGroup[] groups, int boneId, float planeDistance, Vector2 scale, Vector2 min,
            Vector2 max)
        {
            this.groups = groups;
            this.boneId = boneId;
            this.planeDistance = planeDistance;
            this.scale = scale;
            this.min = min;
            this.max = max;
        }

        // Gives null when the mesh has no UV bone or no eye face group.
        public static DfmEyeRig? Create(DfmMesh mesh)
        {
            var eyes = mesh.FaceGroups.Where(x => IsEyeMaterial(x.MaterialName)).ToArray();
            LeafNode?[] nodes =
                [mesh.UVPlaneDistance, mesh.BoneXToUScale, mesh.BoneYToVScale, mesh.MinDU, mesh.MaxDU, mesh.MinDV,
                    mesh.MaxDV];
            if (eyes.Length == 0 || mesh.UVBoneId?.Int32ArrayData is not [var bone, ..] ||
                nodes.Any(x => x?.SingleArrayData is not { Length: > 0 }))
            {
                return null;
            }

            var v = nodes.Select(x => x!.SingleArrayData[0]).ToArray();
            return new DfmEyeRig(eyes, bone, v[0], new Vector2(v[1], v[2]), new Vector2(v[3], v[5]),
                new Vector2(v[4], v[6]));
        }

        // Retail draws a material as an eye when its name in lower case matches "^eye*", thus starts with "ey".
        public static bool IsEyeMaterial(string? name) =>
            name != null && name.StartsWith("ey", StringComparison.OrdinalIgnoreCase);

        public bool Moves(FaceGroup group) => Array.IndexOf(groups, group) >= 0;

        // Sets the texture offset of the eyes of a character from its current pose.
        public MaterialAnim Update(DfmSkinning skinning)
        {
            var offset = Vector2.Zero;
            if (boneId >= 0 && boneId < skinning.Instances.Length && skinning.Instances[boneId] is { } bone)
            {
                // The Z axis of the bone in the frame of its parent bone.
                var axis = Vector3.Transform(Vector3.UnitZ, bone.LocalTransform.Orientation);
                if (bone.Parent != null)
                {
                    axis = Vector3.Transform(axis, Quaternion.Inverse(bone.Parent.LocalTransform.Orientation));
                }

                offset = GetOffset(axis);
            }

            var anim = skinning.EyeOffset ??= new MaterialAnim();
            anim.SetOffset(offset.X, offset.Y);
            return anim;
        }

        // The offset of the texture for the Z axis of the bone. The texture coordinates of the eyes are uv + offset.
        public Vector2 GetOffset(Vector3 axis)
        {
            var offset = new Vector2(axis.X, axis.Y) * scale * planeDistance;
            // Retail does not move the texture when both values are 0.0001 or less.
            if (MathF.Abs(offset.X) <= 1e-4f && MathF.Abs(offset.Y) <= 1e-4f)
            {
                return Vector2.Zero;
            }

            return new Vector2(Limit(offset.X, min.X, max.X), Limit(offset.Y, min.Y, max.Y));
        }

        // Retail compares with the minimum first. Thus a minimum that is more than the maximum does not cause an error.
        private static float Limit(float value, float low, float high) =>
            value < low ? low : value > high ? high : value;
    }
}
