using System.Numerics;
using LibreLancer.Utf.Cmp;

namespace LibreLancer.Render;

public class DfmHardpoint : IRenderHardpoint
{
    public DfmSkeletonManager.Connection? Connection;
    public BoneInstance Bone;
    public HardpointDefinition Definition;

    public DfmHardpoint(DfmSkeletonManager.Connection? connection, BoneInstance bone, HardpointDefinition definition)
    {
        Connection = connection;
        Bone = bone;
        Definition = definition;
    }

    public Transform3D Transform => Definition.Transform *
                                    (Bone?.LocalTransform ?? Transform3D.Identity) *
                                    (Connection?.Transform ?? Transform3D.Identity);
}

// A bone as an attach target. The transform is in the space of the body model.
public sealed class DfmBoneHardpoint(DfmSkeletonManager.Connection? connection, BoneInstance bone) : IRenderHardpoint
{
    public Transform3D Transform => bone.LocalTransform * (connection?.Transform ?? Transform3D.Identity);
}
