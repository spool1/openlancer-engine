// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using LibreLancer.Utf.Anm;

namespace LibreLancer.Utf
{
    public class SphereConstruct : AbstractConstruct
    {
        public Vector3 Offset;
        public Vector3 Min;
        public Vector3 Max;


        public override Transform3D LocalTransform => internalGetTransform(
            new Transform3D(-Offset, Quaternion.Identity) * new Transform3D(Origin,  quatRot * Rotation));

        private Quaternion quatRot = Quaternion.Identity;

        public SphereConstruct()
        {
        }

        public SphereConstruct(BinaryReader reader)
            : base(reader)
        {
            Offset = ConvertData.ToVector3(reader);
            Rotation = ConvertData.ToMatrix3x3(reader).ExtractRotation();

            var min1 = reader.ReadSingle();
            var max1 = reader.ReadSingle();
            var min2 = reader.ReadSingle();
            var max2 = reader.ReadSingle();
            var min3 = reader.ReadSingle();
            var max3 = reader.ReadSingle();

            Min = new(min1, min2, min3);
            Max = new(max1, max2, max3);
        }
		protected SphereConstruct(SphereConstruct cf) : base(cf) { }
		public override AbstractConstruct Clone()
		{
			var newc = new SphereConstruct(this)
            {
                Offset = Offset,
                Min = Min,
                Max = Max
            };
            return newc;
		}
        public override void Reset()
        {
            quatRot = Quaternion.Identity;
        }
        public override void Update(ChannelFloat distance, Quaternion quat)
        {
            quatRot = quat;
        }
    }
}
