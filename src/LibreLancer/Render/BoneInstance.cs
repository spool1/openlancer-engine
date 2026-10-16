// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Numerics;

namespace LibreLancer.Render
{
    public class BoneInstance
    {
        public string Name;
        public BoneInstance? Parent;
        public Transform3D InvBindPose;
        public Matrix4x4 BoneMatrix = Matrix4x4.Identity;
        public Quaternion OriginalRotation = Quaternion.Identity;
        public Vector3 Origin = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Translation = Vector3.Zero;
        // IK applies this rotation after the animation, in the frame of the parent.
        public Quaternion IKRotation = Quaternion.Identity;
        // IK limits in radians about the X (pitch), Y (yaw) and Z (roll) axes of the Cons frame.
        public bool HasIKLimits;
        public Vector3 IKMin;
        public Vector3 IKMax;
        public List<BoneInstance> Children = [];
        public Transform3D LocalTransform;
        public BoundingBox BoundingBox;

        private Vector3 bMin;
        private Vector3 bMax;

        public BoneInstance(string name, Transform3D boneToRoot, Vector3 bMin, Vector3 bMax)
        {
            Name = name;
            LocalTransform = boneToRoot;
            InvBindPose = boneToRoot.Inverse();
            this.bMin = InvBindPose.Transform(bMin);
            this.bMax = InvBindPose.Transform(bMax);
            BoundingBox = BoundingBox.TransformAABB(new BoundingBox(bMin, bMax), LocalTransform);
        }

        public void Update(Transform3D parentMatrix)
        {
            var r = Quaternion.Concatenate(Quaternion.Concatenate(OriginalRotation, Rotation), IKRotation);
            LocalTransform = new Transform3D(Origin + Translation, r) * parentMatrix;
            BoneMatrix = (InvBindPose * LocalTransform).Matrix();
            foreach (var b in Children)
                b.Update(LocalTransform);
            BoundingBox = BoundingBox.TransformAABB(new BoundingBox(bMin, bMax), LocalTransform);
        }

        // Applies the IK limits to an IK rotation. The limits are for the rotation of the joint from its Cons frame,
        // with the animation included.
        public Quaternion LimitIK(Quaternion ik)
        {
            if (!HasIKLimits)
            {
                return ik;
            }

            var joint = Quaternion.Inverse(OriginalRotation) * ik * Rotation * OriginalRotation;
            // The angles are yaw, then pitch, then roll. Thus the roll does not move the Z axis.
            var front = Vector3.Transform(Vector3.UnitZ, joint);
            var pitch = MathF.Asin(Math.Clamp(-front.Y, -1f, 1f));
            var yaw = MathF.Atan2(front.X, front.Z);
            var roll = MathF.Atan2(Vector3.Transform(Vector3.UnitX, joint).Y, Vector3.Transform(Vector3.UnitY, joint).Y);
            joint = Quaternion.CreateFromYawPitchRoll(Math.Clamp(yaw, IKMin.Y, IKMax.Y),
                Math.Clamp(pitch, IKMin.X, IKMax.X), Math.Clamp(roll, IKMin.Z, IKMax.Z));
            return OriginalRotation * joint * Quaternion.Inverse(OriginalRotation) * Quaternion.Inverse(Rotation);
        }
    }
}
