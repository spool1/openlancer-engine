// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Linq;
namespace LibreLancer.Utf.Cmp
{
    public class CmpCameraInfo
    {
        // Some defaults just in case.
        public float Fovx = 0.6451778f;
        public float Fovy = 0.514412f;
        public float Znear = 0.1f;
        public float Zfar = 6f;

        public CmpCameraInfo(IntermediateNode node)
        {
            var cameraNode = (node.Children.FirstOrDefault((x) => x.Name.Equals("camera", StringComparison.OrdinalIgnoreCase)) as IntermediateNode);
            if(cameraNode == null) {
                FLLog.Error("Cmp", "Camera does not contain valid camera node"); // This won't be thrown in normal loading
                return;
            }
            foreach(var child in cameraNode.Children)
            {
                var leaf = (child as LeafNode);
                if(leaf == null)
                {
                    FLLog.Error("Cmp", "Invalid node in camera " + child.Name);
                    continue;
                }
                switch (child.Name.ToLowerInvariant())
                {
                    case "znear":
                        Znear = leaf.SingleData!.Value;
                        break;
                    case "zfar":
                        Zfar = leaf.SingleData!.Value;
                        break;
                    case "fovx":
                        Fovx = leaf.SingleData!.Value;
                        break;
                    case "fovy":
                        Fovy = leaf.SingleData!.Value;
                        break;
                    default:
                        FLLog.Error("Cmp", "Invalid node in camera " + child.Name);
                        break;
                }
            }
        }
    }
}
