using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using LibreLancer.Fx;
using LibreLancer.Graphics;
using LibreLancer.Render;
using LibreLancer.Render.Cameras;
using LibreLancer.Render.Materials;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Utf.Ale;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// Screen overlays of cutscenes: the width and the angle of a basic particle, the overlay rule and the stretch on a wide
// screen.
public class ThnScreenOverlayTests
{
    private const string S002xe = @"SCRIPTS\STORY\s002xe_nrml_sinclair_li_01_Cityscape_01.thn";
    private const string NewsCamera = "Cam_01_VideoCam_A";
    private const double Frame = 1.0 / 60;

    private static FxBasicAppearance LoadAppearance(RetailData data, string file, string node)
    {
        using var stream = new MemoryStream(data.ReadDataFile(@"FX\" + file));
        var effects = new ParticleLibrary(null!, new AleFile(file, stream)).Effects;
        return (FxBasicAppearance)effects.SelectMany(x => x.Appearances)
            .First(x => x.Appearance.NodeName == node).Appearance;
    }

    // TV_Lines.app has BasicApp_Size 0.1127 and BasicApp_HToVAspect 1.2114. With this width, the lines cover a 4:3
    // view at their distance from the news camera.
    [Fact]
    public void TheTvLinesAreWiderThanHigh()
    {
        var lines = LoadAppearance(RetailData.Open(), @"misc\rtc_Tv_Lines.ale", "TV_Lines.app");

        var half = lines.ParticleSize(0, 0) / 2;

        Assert.Equal(0.1365f, half.X, 1e-4);
        Assert.Equal(0.1127f, half.Y, 1e-4);
    }

    // The aspect of the Liberty thruster flame goes from 2.0178 at its first key to 0 at the end of the life of the
    // particle. The aspect uses the time of the particle, as the size does.
    [Fact]
    public void TheAspectFollowsTheLifeOfTheParticle()
    {
        var flame = LoadAppearance(RetailData.Open(), @"equipment\gf_li_thruster01.ale", "gf_li_thruster01.app");

        var start = flame.ParticleSize(0, 0.0129f);
        var end = flame.ParticleSize(0, 1);

        Assert.Equal(2.0178f, start.X / start.Y, 1e-4);
        Assert.Equal(0f, end.X, 1e-6);
        Assert.True(end.Y > 0);
    }

    [Fact]
    public void AnAppearanceWithoutAnAspectIsSquare()
    {
        var appearance = new FxBasicAppearance("square") { Size = new(0.25f) };

        Assert.Equal(new Vector2(0.5f, 0.5f), appearance.ParticleSize(0, 0.5f));
    }

    // rtc_base_smoke02_glow.app (Bw_01_Cityscape, the Pl_01 bar and pad, the Pl_07 pad) has BasicApp_HToVAspect 0.0567
    // and BasicApp_Rotate 1.5484. Retail gives the rotate value to the sine and the cosine as radians. Thus the turned
    // glow is a horizontal streak, about 37 m wide, and not a vertical bar.
    [Fact]
    public void ABaseGlowIsAHorizontalStreak()
    {
        var glow = LoadAppearance(RetailData.Open(), @"misc\rtc_smokestacks.ale", "rtc_base_smoke02_glow.app");

        var angle = glow.ParticleRotation(0, 0.5f);
        var half = glow.ParticleSize(0, 0.5f) / 2;
        // The extent of the turned quad on the screen. Particle.vert.hlsl turns the width axis by the angle.
        var width = MathF.Abs(MathF.Cos(angle)) * half.X + MathF.Abs(MathF.Sin(angle)) * half.Y;
        var height = MathF.Abs(MathF.Sin(angle)) * half.X + MathF.Abs(MathF.Cos(angle)) * half.Y;

        Assert.Equal(1.5484f, angle, 1e-4);
        Assert.True(width > 10 * height, $"{width} x {height}");
    }

    // CalcCameraProps widens the horizontal view with FOVUtil.CalcFovx. The overlay stretch is the same widening, so
    // an overlay has the same width on the screen as on a 4:3 screen.
    [Theory]
    [InlineData(4f / 3f)]
    [InlineData(16f / 9f)]
    [InlineData(21f / 9f)]
    [InlineData(5f / 4f)]
    public void TheOverlayStretchIsTheWideningOfTheView(float aspect)
    {
        const float fovh = 25;
        var camera = new ThnCamera(new Rectangle(0, 0, 1280, 720));
        camera.SetViewport(new Rectangle(0, 0, 1280, 720), aspect);

        var widening = MathF.Tan(MathHelper.DegreesToRadians(FOVUtil.CalcFovx(fovh, aspect))) /
                       MathF.Tan(MathHelper.DegreesToRadians(fovh));

        Assert.Equal(widening, camera.OverlayStretch, 1e-5);
    }

    // A script with the camera "cam", the camera "other" and the effect "fx". At T=0, an ATTACH_ENTITY puts the
    // effect on the parent with the flags and the offset.
    private static ThnScriptInstance AttachEffect(AttachFlags flags, Vector3 offset, string parent = "cam")
    {
        var script = new ThnScript() { Duration = 10 };
        script.Events.Add(new AttachEntityEvent()
        {
            Duration = 10, Targets = ["fx", parent], Flags = flags, Offset = offset
        });
        var instance = new ThnScriptInstance(null!, script);
        foreach (var name in new[] { "cam", "other", "fx" })
        {
            instance.Objects[name] = new ThnSceneObject()
            {
                Name = name,
                Rotate = Quaternion.Identity,
                Camera = name == "fx" ? null : new ThnCameraProps() { FovH = 25, Znear = 0.25f, Zfar = 7000 }
            };
        }

        instance.Update(0);
        return instance;
    }

    // The retail attaches of effects to cameras. The fades, the TV lines and the news logo are in front of the lens
    // in the axes of the camera. Dust below the camera and snow at an offset in the scene axes are in the world.
    [Theory]
    [InlineData(38, 0f, 0f, -0.2877f, true)] // s002xe TV lines
    [InlineData(38, 0.0844f, -0.0483f, -0.2783f, true)] // s002xe news logo
    [InlineData(34, 0f, 0f, -0.4f, true)] // s069x fade without ORIENTATION
    [InlineData(166, 0f, 0f, -0.5f, true)] // s003x fade with ORIENTATION_RELATIVE
    [InlineData(34, 0f, -2f, 0f, false)] // s027xb ceiling dust
    [InlineData(34, -0.758234f, -2.942155f, -0.070217f, false)] // s027xc ceiling dust
    [InlineData(2, 0f, 30f, -80f, false)] // Rh_01_Cityscape snow
    [InlineData(2, 0f, 0f, 0f, false)] // Intro_planetchunks tourist dust
    [InlineData(102, 0f, 0f, -0.3f, false)] // PARENT_CHILD reads no offset
    [InlineData(36, 0f, 0f, -0.3f, false)] // ORIENTATION without POSITION does not move the effect
    [InlineData(38, 0.4f, 0f, -0.3f, false)] // the side part of the offset is larger than its forward part
    public void AScreenOverlayIsInFrontOfTheLensInTheAxesOfTheCamera(int flags, float x, float y, float z,
        bool overlay)
    {
        var instance = AttachEffect((AttachFlags)flags, new Vector3(x, y, z));

        Assert.Equal(overlay, instance.Objects["fx"].IsScreenOverlayOf(instance.Objects["cam"]));
    }

    private sealed class FixedHardpoint : IRenderHardpoint
    {
        public Transform3D Transform => Transform3D.Identity;
    }

    // An attachment to a hardpoint or to a part of the camera holds the effect in the axes of that hardpoint or part,
    // not in the axes of the lens.
    [Fact]
    public void AnAttachmentToAHardpointOrAPartOfTheCameraIsNotAnOverlay()
    {
        var cam = new ThnSceneObject() { Name = "cam", Rotate = Quaternion.Identity };

        bool IsOverlay(IRenderHardpoint? hardpoint, RigidModelPart? part)
        {
            var fx = new ThnSceneObject() { Name = "fx", Rotate = Quaternion.Identity };
            fx.Attachments.Add(new ThnAttachment(new ThnObjectParent(cam, hardpoint, part))
            {
                Position = true, EntityRelative = true, Offset = new Vector3(0, 0, -0.3f)
            });
            return fx.IsScreenOverlayOf(cam);
        }

        Assert.True(IsOverlay(null, null));
        Assert.False(IsOverlay(new FixedHardpoint(), null));
        Assert.False(IsOverlay(null, new RigidModelPart()));
    }

    private static ThnCamera Camera(ThnSceneObject obj, float aspect)
    {
        var viewport = new Rectangle(0, 0, 1280, 720);
        var camera = new ThnCamera(viewport) { Object = obj };
        camera.SetViewport(viewport, aspect);
        camera.Update();
        return camera;
    }

    [Fact]
    public void OnlyTheCameraOfAnOverlayStretchesIt()
    {
        var instance = AttachEffect(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative,
            new Vector3(0, 0, -0.2877f));
        var fx = instance.Objects["fx"];
        var renderer = new ParticleEffectRenderer(null);
        fx.Object = new GameObject() { RenderComponent = renderer };
        var camera = Camera(instance.Objects["cam"], 16f / 9f);

        fx.UpdateScreenOverlay(camera);
        Assert.Same(camera, renderer.OverlayCamera);
        Assert.Equal(4f / 3f, renderer.ScreenStretch(camera), 1e-5);
        // Another view of the effect, for example the free camera of an editor, shows the effect in the world.
        Assert.Equal(1f, renderer.ScreenStretch(new LookAtCamera()));
        Assert.Equal(1f, renderer.ScreenStretch(Camera(instance.Objects["cam"], 16f / 9f)));

        // After a cut to another camera, the effect stays in front of the old camera, in the world.
        camera.Object = instance.Objects["other"];
        fx.UpdateScreenOverlay(camera);
        Assert.Null(renderer.OverlayCamera);
        Assert.Equal(1f, renderer.ScreenStretch(camera));
    }

    // In each frame, the cutscene gives its camera to the effects that this camera holds as screen overlays.
    [Fact]
    public void TheCutsceneGivesItsCameraToTheOverlaysInEachFrame()
    {
        var instance = AttachEffect(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative,
            new Vector3(0, 0, -0.2877f));
        // A cutscene without a game or a world. The scene objects have no engine object when the cutscene adds them,
        // so the cutscene puts nothing into a world.
        var viewport = new Rectangle(0, 0, 1280, 720);
        var cutscene = new Cutscene(new ThnScriptContext(null), null!, null!, null, viewport, null!);
        cutscene.UpdateViewport(viewport, 16f / 9f);
        foreach (var obj in instance.Objects.Values)
        {
            cutscene.AddObject(obj);
        }

        var renderer = new ParticleEffectRenderer(null);
        instance.Objects["fx"].Object = new GameObject() { RenderComponent = renderer };

        cutscene.SetCamera("cam");
        cutscene._Update(0);
        Assert.Same(cutscene.CameraHandle, renderer.OverlayCamera);
        Assert.Equal(4f / 3f, renderer.ScreenStretch(cutscene.CameraHandle), 1e-5);

        // After a cut to another camera, the effect is in the world.
        cutscene.SetCamera("other");
        cutscene._Update(0);
        Assert.Null(renderer.OverlayCamera);
    }

    // The renderer gives the stretch of the camera that draws the effect to the effect. The appearances of the effect
    // then draw with this stretch.
    [Fact]
    public void TheRendererDrawsTheEffectWithTheStretchOfTheCamera()
    {
        var instance = AttachEffect(AttachFlags.Position | AttachFlags.Orientation | AttachFlags.EntityRelative,
            new Vector3(0, 0, -0.2877f));
        var renderer = new ParticleEffectRenderer(new ParticleEffect(0, "empty", [], [], []));
        var camera = Camera(instance.Objects["cam"], 16f / 9f);
        instance.Objects["fx"].Object = new GameObject() { RenderComponent = renderer };
        instance.Objects["fx"].UpdateScreenOverlay(camera);

        renderer.Draw(camera, null!, null!, null!);
        Assert.Equal(4f / 3f, renderer.Instance!.ScreenStretch, 1e-5);
        renderer.Draw(new LookAtCamera(), null!, null!, null!);
        Assert.Equal(1f, renderer.Instance.ScreenStretch);
    }

    // The particle pool records one material parameter set for each draw. Each set keeps the stretch of its effect.
    [Fact]
    public void EachParticleDrawKeepsTheStretchOfItsEffect()
    {
        var material = new ParticleMaterial(null!);

        var overlay = material.AddParameters(null!, BlendMode.Normal, ParticleDrawKind.Basic, 4f / 3f, 0, 4);
        var world = material.AddParameters(null!, BlendMode.Normal, ParticleDrawKind.Rect, 1f, 4, 2);

        Assert.Equal(4f / 3f, material.Parameters[overlay].screenStretch);
        Assert.Equal(1f, material.Parameters[world].screenStretch);
    }

    // The news overlays of s002xe: the effect file, the START_PSYS time and the time of the check.
    private static readonly (string Entity, string File, double Start, double Check)[] NewsOverlays =
    [
        ("vfx_rtc_fadeblack_variable_1", @"misc\rtc_fadeblack_variable.ale", 0, 3.5),
        ("vfx_TV_Lines_2", @"misc\rtc_Tv_Lines.ale", 3, 4.8),
        ("vfx_TV_Distortion_2", @"misc\rtc_Tv_Distortion.ale", 4.684, 4.8),
        ("vfx_rtc_libertynewsicon_1", @"misc\rtc_libertynewsicon.ale", 3.592, 6.0),
    ];

    // The rectangle of a particle quad on the screen in normalized device coordinates, and the depth of its center.
    private record struct ScreenQuad(float Left, float Right, float Bottom, float Top, float Depth);

    // Replays the attaches of the s002xe news overlays and draws each overlay at its check time with the news camera
    // on a screen of the aspect. The view has the cutscene letterbox of RoomGameplay. Each quad expands as
    // Particle.vert.hlsl expands a basic particle without rotation, and stretches as the shader does.
    private static Dictionary<string, ScreenQuad> DrawNewsOverlays(RetailData data, float aspect)
    {
        var replay = new RetailThnReplay(S002xe, [], ev =>
            ev is AttachEntityEvent && NewsOverlays.Any(x => x.Entity == ev.Targets[0]));
        var renderers = NewsOverlays.Select(overlay =>
        {
            using var stream = new MemoryStream(data.ReadDataFile(@"FX\" + overlay.File));
            var renderer = new ParticleEffectRenderer(
                new ParticleLibrary(null!, new AleFile(overlay.File, stream)).Effects.Single());
            renderer.Instance!.Pool =
                (ParticleEffectPool)RuntimeHelpers.GetUninitializedObject(typeof(ParticleEffectPool));
            replay[overlay.Entity].Object = new GameObject() { RenderComponent = renderer };
            return renderer;
        }).ToArray();

        // RoomGameplay gives the letterbox the aspect of the screen times 1.39. The screen aspect comes from the whole
        // pixels of the viewport, so the vertical view is the same on each screen.
        var viewport = new Rectangle(0, 0, (int)MathF.Round(1000 * aspect * 1.39f), 1000);
        var camera = new ThnCamera(viewport) { Object = replay[NewsCamera] };
        camera.SetViewport(viewport, viewport.Width / (viewport.Height * 1.39f));

        var quads = new Dictionary<string, ScreenQuad>();
        for (var frame = 1; frame * Frame <= NewsOverlays.Max(x => x.Check) + 1e-9; frame++)
        {
            var time = frame * Frame;
            replay.RunTo(time);
            camera.Update();
            for (var i = 0; i < NewsOverlays.Length; i++)
            {
                var (entity, _, start, check) = NewsOverlays[i];
                if (time < start)
                {
                    continue;
                }

                var obj = replay[entity];
                var transform = obj.Object!.WorldTransform.Matrix();
                renderers[i].Update(Frame, transform.Translation, transform);
                if (Math.Abs(time - check) < Frame / 2)
                {
                    obj.UpdateScreenOverlay(camera);
                    quads[entity] = Draw(renderers[i], camera);
                }
            }
        }

        return quads;
    }

    private static ScreenQuad Draw(ParticleEffectRenderer renderer, ThnCamera camera)
    {
        var instance = renderer.Instance!;
        var reference = instance.Effect.Appearances.Single();
        var appearance = (FxBasicAppearance)reference.Appearance;
        var node = instance.Effect.Appearances.IndexOf(reference);
        Assert.Equal(1, instance.Buffer.GetCount(node));

        ref var particle = ref instance.Buffer[node, 0];
        var center = Vector3.Transform(Vector3.Transform(particle.Position, particle.Orientation),
            instance.NodeWorldTransforms[reference.NodeIdx]);
        var half = appearance.ParticleSize(renderer.SParam, particle.TimeAlive / particle.LifeSpan) / 2;
        var view = camera.View;
        var right = new Vector3(view.M11, view.M21, view.M31) * half.X;
        var up = new Vector3(view.M12, view.M22, view.M32) * half.Y;
        var stretch = renderer.ScreenStretch(camera);

        Vector3 Project(Vector3 point)
        {
            var clip = Vector4.Transform(new Vector4(point, 1), camera.ViewProjection);
            return new Vector3(clip.X * stretch, clip.Y, clip.Z) / clip.W;
        }

        var corners = new[] { center - right - up, center + right - up, center - right + up, center + right + up }
            .Select(Project).ToArray();
        return new ScreenQuad(corners.Min(c => c.X), corners.Max(c => c.X), corners.Min(c => c.Y),
            corners.Max(c => c.Y), Project(center).Z);
    }

    // The TV lines, the distortion and the fade cover the whole view, and the news logo has the same place and size
    // on the screen as on a 4:3 screen. The overlays are in front of the near plane of the news camera (0.25).
    [Theory]
    [InlineData(4f / 3f)]
    [InlineData(16f / 9f)]
    [InlineData(21f / 9f)]
    [InlineData(5f / 4f)]
    public void TheS002xeNewsOverlaysFillTheViewOnEveryScreen(float aspect)
    {
        var data = RetailData.Open();
        var reference = DrawNewsOverlays(data, 4f / 3f);
        var quads = DrawNewsOverlays(data, aspect);

        foreach (var entity in new[] { "vfx_TV_Lines_2", "vfx_TV_Distortion_2", "vfx_rtc_fadeblack_variable_1" })
        {
            var quad = quads[entity];
            Assert.True(quad.Left <= -1 && quad.Right >= 1 && quad.Bottom <= -1 && quad.Top >= 1,
                $"{entity} covers only {quad}");
        }

        foreach (var (entity, quad) in quads)
        {
            Assert.InRange(quad.Depth, 0f, 1f);
            var expected = reference[entity];
            Assert.Equal(expected.Left, quad.Left, 1e-3);
            Assert.Equal(expected.Right, quad.Right, 1e-3);
            Assert.Equal(expected.Bottom, quad.Bottom, 1e-3);
            Assert.Equal(expected.Top, quad.Top, 1e-3);
        }

        // The logo sits in the lower right of the frame.
        var logo = quads["vfx_rtc_libertynewsicon_1"];
        Assert.InRange((logo.Left + logo.Right) / 2, 0.6f, 0.7f);
        Assert.InRange((logo.Bottom + logo.Top) / 2, -0.75f, -0.6f);
    }
}
