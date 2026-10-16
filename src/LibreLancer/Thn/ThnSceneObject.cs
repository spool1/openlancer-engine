using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using LibreLancer.Client.Components;
using LibreLancer.Render;
using LibreLancer.World;
using LibreLancer.World.Equipments;

namespace LibreLancer.Thn;

public class ThnSceneObject
{
    public string Name = null!;
    public Vector3 Translate;
    public Quaternion Rotate;
    public string? Actor;
    public string? Voice;
    public GameObject? Object;
    public DynamicLight? Light;
    public ThnEntity Entity = null!;
    public ThnCameraProps? Camera;
    public Vector3 LightDir;
    public Hardpoint? HpMount;
    public ThnSound? Sound;
    public bool Animating = false;
    public bool PosFromObject = false;
    public Engine? Engine;
    // The end of the last START_PSYS on this entity, in whole milliseconds of script time.
    internal long PSysEnd = long.MinValue;
    public int MonitorIndex = 0;

    public List<ThnAttachment> Attachments = new();

    public Transform3D GetTransform()
    {
        var result = GetTransformInternal();
        return result;
    }


    Transform3D GetTransformInternal()
    {
        if (PosFromObject)
        {
            return Object!.Transform;
        }

        Transform3D self = new(Translate, Rotate);
        foreach (var t in Attachments.Where(x => !x.Pending))
        {
            var parent = t.Parent.GetTransform(t.PathLookAt);
            // PARENT_CHILD keeps the object rigid to the parent and reads no offset.
            if (t.Position)
            {
                self.Position = t.ParentChild ? t.Rigid(parent).Position : t.Target(parent);
            }

            if (t.Orientation)
            {
                self.Orientation = t.ParentChild || t.OrientationRelative
                    ? t.Rigid(parent).Orientation
                    : parent.Orientation;
            }
        }

        // Retail runs the path animations of an object before its attachments, so a look aims from the position of
        // this frame. A look applies only when the attachment writes no orientation and is not rigid.
        foreach (var t in Attachments.Where(x => x.LookAt && !x.Orientation && !x.ParentChild))
        {
            var direction = self.Position - t.Target(t.Parent.GetTransform(t.PathLookAt));
            // Retail does not turn an object that is on its target.
            if (direction.LengthSquared() < 1e-8f)
            {
                continue;
            }

            // The front axis points at the target, and the up axis leans toward the scene up.
            self.Orientation = QuaternionEx.LookRotation(Vector3.Normalize(direction), Vector3.UnitY) *
                               Quaternion.Inverse(QuaternionEx.LookRotation(-t.Front, t.Up));
        }

        return self;
    }

    public void Update()
    {
        // An attachment that started before its parent had a pose records its relative pose now.
        foreach (var t in Attachments.Where(x => x.Pending && x.Parent.HasPose))
        {
            t.Record(GetTransform());
        }

        var self = GetTransform();
        Translate = self.Position;
        Rotate = self.Orientation;
        Attachments.RemoveAll(x => x.Expired);
        UpdateEngineObjects();
    }

    // A screen overlay is an effect that an attachment holds at a fixed point in front of the lens of the camera.
    // POSITION with ENTITY_RELATIVE puts the effect at the offset in the axes of the camera. The offset points
    // forward: its forward part is larger than its side part and its up part. Retail uses this for the fades, the TV
    // lines and the news logo, and makes these effects for a 4:3 view. An effect that follows a camera in another way
    // is in the world, for example ceiling dust 2 m below the camera or snow at an offset in the scene axes.
    // The cutscene calls this for each effect in each frame. Thus it does not allocate memory.
    internal bool IsScreenOverlayOf(ThnSceneObject camera)
    {
        foreach (var t in Attachments)
        {
            if (t.Position && t.EntityRelative && !t.ParentChild && t.Parent is ThnObjectParent parent &&
                parent.IsRootOf(camera) && -t.Offset.Z > MathF.Max(MathF.Abs(t.Offset.X), MathF.Abs(t.Offset.Y)))
            {
                return true;
            }
        }

        return false;
    }

    // Gives the effect of this object to the camera as a screen overlay, or puts it in the world.
    internal void UpdateScreenOverlay(ThnCamera camera)
    {
        if (Object?.RenderComponent is ParticleEffectRenderer fx)
        {
            fx.OverlayCamera = camera.Object != null && IsScreenOverlayOf(camera.Object) ? camera : null;
        }
    }


    void UpdateEngineObjects()
    {
        if (!PosFromObject && Object != null)
        {
            if (Object.RenderComponent is CharacterRenderer charRen)
            {
                if (charRen.Skeleton.ApplyRootMotion)
                {
                    var accum = Rotate * charRen.Skeleton.RootRotation;
                    var movement = Quaternion.Inverse(charRen.Skeleton.RootRotationAccumulator) * accum;
                    Translate += Vector3.Transform(charRen.Skeleton.RootTranslation, movement);
                    Rotate = accum;
                }

                Translate.Y = charRen.Skeleton.ModelHeight;
            }

            if (HpMount == null)
            {
                Object.SetTransform(new Transform3D(Translate, Rotate));
            }
            else
            {
                Object.SetTransform(HpMount.Transform.Inverse() * new Transform3D(Translate, Rotate));
            }
        }

        if (Light != null)
        {
            Light.Light.Position = Translate;
            Light.Light.Direction = Vector3.Transform(LightDir.Normalized(), Rotate);
        }
    }
}

public class ThnAttachment(ThnAttachParent parent)
{
    public Vector3 Offset;
    public bool Position;
    public bool Orientation;
    public bool OrientationRelative;
    public bool EntityRelative;
    public bool LookAt;
    public bool PathLookAt;
    public bool ParentChild;
    // The pose of the object relative to the parent when the attachment starts. PARENT_CHILD and
    // ORIENTATION_RELATIVE keep it. It is null until the parent has a pose.
    public Transform3D? Relative;
    // The axes of the object that LOOK_AT aims at the target and turns toward the scene up.
    public Vector3 Front = -Vector3.UnitZ;
    public Vector3 Up = Vector3.UnitY;
    // The event has ended. ThnSceneObject.Update applies the attachment once more, then removes it.
    public bool Expired;

    public ThnAttachParent Parent = parent;

    // The attachment reads the relative pose, but it has not recorded it. It writes nothing until it records it.
    public bool Pending => Relative == null && (ParentChild || (Orientation && OrientationRelative));

    // Records the pose of the object relative to the parent.
    public void Record(Transform3D obj) => Relative = obj * Parent.GetTransform(PathLookAt).Inverse();

    // The pose that keeps the relative pose of the object to the parent.
    public Transform3D Rigid(Transform3D parent) => Relative!.Value * parent;

    // The point that POSITION moves the object to and LOOK_AT aims at. The offset is in the scene axes, or in the
    // axes of the parent with ENTITY_RELATIVE.
    public Vector3 Target(Transform3D parent) =>
        parent.Position + (EntityRelative ? Vector3.Transform(Offset, parent.Orientation) : Offset);
}

public abstract class ThnAttachParent
{
    public abstract Transform3D GetTransform(bool pathLookAt);

    // False while the parent does not have the pose that it is drawn with.
    public virtual bool HasPose => true;
}

public class ThnPathParent(ThnSceneObject Path)
    : ThnAttachParent
{
    public float T = 0;
    public bool Reverse = false;
    public Vector3 Offset;

    public override Transform3D GetTransform(bool pathLookAt)
    {
        var path = Path.Entity.Path!;
        var parent = Path.GetTransform();
        Quaternion orient = Quaternion.Identity;
        // The path points and keys are in the frame of the path entity, as the position below.
        if (pathLookAt)
        {
            // The object aims along the path direction in the scene. Its up axis stays the scene up.
            orient = QuaternionEx.LookRotation(Vector3.Transform(path.GetDirection(T, Reverse), parent.Orientation),
                Vector3.UnitY);
        }
        else if (path.HasOrientation)
        {
            orient = parent.Orientation * path.GetOrientation(T);
        }

        var pos = parent.Transform(path.GetPosition(T) + Offset);
        return new(pos, orient);
    }
}

public class ThnObjectParent(ThnSceneObject obj, IRenderHardpoint? hardpoint, RigidModelPart? part)
    : ThnAttachParent
{
    public Transform3D ChildOffset = Transform3D.Identity;

    // True when the parent is the object itself, not a hardpoint or a part of it.
    internal bool IsRootOf(ThnSceneObject other) => obj == other && hardpoint == null && part == null;

    public override Transform3D GetTransform(bool pathLookAt)
    {
        if (part != null)
        {
            return ChildOffset * part.LocalTransform * GetModelTransform();
        }

        if (hardpoint != null)
        {
            return ChildOffset * hardpoint.Transform * GetModelTransform();
        }

        return ChildOffset * obj.GetTransform();
    }

    // A bone or a hardpoint of a character has no pose before the first animation update of the character. Until
    // then, the bones are in the bind pose, and the first pose can be far from it.
    public override bool HasPose =>
        hardpoint == null || obj.Object?.RenderComponent is not CharacterRenderer ch || ch.Skeleton.Posed;

    // The transform that UpdateEngineObjects gives to the model. A character stands at the model height of its
    // skeleton.
    private Transform3D GetModelTransform()
    {
        var tr = obj.GetTransform();
        if (!obj.PosFromObject && obj.Object?.RenderComponent is CharacterRenderer ch)
        {
            tr.Position = tr.Position with { Y = ch.Skeleton.ModelHeight };
        }

        if (obj.HpMount != null)
        {
            tr = obj.HpMount.Transform.Inverse() * tr;
        }
        return tr;
    }
}
