using System;
using System.Linq;
using System.Numerics;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// START_SPATIAL_PROP_ANIM without a renderer. Each frame uses the order of Cutscene._Update: the events and
// processors run, then each scene object updates.
public class ThnSpatialAnimTests
{
    private const float Tolerance = 1e-4f;
    // A frame of 1/64 s adds up to exact times, for example 16 frames give 0.25 s.
    private const double Frame = 1.0 / 64;
    private static readonly Vector3 Start = new(1, 2, 3);
    private static readonly Vector3 End = new(11, 2, 3);

    // The FreeForm smoothstep curve. Most retail spatial anims use it.
    private static readonly ParameterCurve Smoothstep = FreeForm(new(0, 0, 0, 0), new(1, 1, 0, 0));

    // Retail head shakes and nods use a curve that goes to 1 at 0.5 and back to 0.
    private static readonly ParameterCurve Returning = FreeForm(new(0, 0, 0, 0), new(0.5f, 1, 0, 0), new(1, 0, 0, 0));

    private static ParameterCurve FreeForm(params Vector4[] points) => new(PCurveType.FreeForm, points);

    private static ThnSceneObject Prop(string name, Vector3 position, Quaternion rotation) =>
        new() { Name = name, Translate = position, Rotate = rotation };

    // Moves "prop" from Start to End over 1 s. Without a curve, the move is linear.
    private static StartSpatialPropAnimEvent Move(ParameterCurve? curve) => new()
    {
        Duration = 1,
        Targets = ["prop"],
        SetFlags = StartSpatialPropAnimEvent.AnimVars.Pos,
        Pos = End,
        ParamCurve = curve
    };

    // Moves "camera" onto "lead" over 1 s, linear.
    private static StartSpatialPropAnimEvent Follow() => new()
    {
        Duration = 1,
        Targets = ["camera", "lead"],
        SetFlags = StartSpatialPropAnimEvent.AnimVars.Pos
    };

    private static StartSpatialPropAnimEvent AxisRot(ThnAxis axis) => new()
    {
        Duration = 1,
        Targets = ["prop"],
        SetFlags = StartSpatialPropAnimEvent.AnimVars.AxisRot,
        AxisRot = new() { Axis = axis, Degrees = 90 },
        ParamCurve = Smoothstep
    };

    private static ThnScriptInstance Scene(ThnSceneObject[] objects, params ThnEvent[] events)
    {
        var script = new ThnScript() { Duration = 100 };
        script.Events.AddRange(events);
        var instance = new ThnScriptInstance(null!, script);
        foreach (var obj in objects)
        {
            instance.Objects[obj.Name] = obj;
        }

        // Cutscene.SceneSetup runs the events at T=0.
        instance.Update(0);
        return instance;
    }

    private static void Frames(ThnScriptInstance instance, int count, double frame = Frame)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(frame);
            foreach (var obj in instance.Objects.Values)
            {
                obj.Update();
            }
        }
    }

    // The part of the move from Start to End.
    private static float Progress(ThnSceneObject prop) => (prop.Translate.X - Start.X) / (End.X - Start.X);

    // The angle of the turn from a to b. Atan2 keeps small angles exact.
    private static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Normalize(Quaternion.Conjugate(a) * b);
        return 2 * MathF.Atan2(new Vector3(d.X, d.Y, d.Z).Length(), MathF.Abs(d.W));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");

    private static void AssertNear(Quaternion expected, Quaternion actual) =>
        Assert.True(Angle(expected, actual) < 1e-3f, $"Expected {expected}, got {actual}");

    [Fact]
    public void ASmoothstepCurveEasesTheMove()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var instance = Scene([prop], Move(Smoothstep));

        Frames(instance, 16);
        // 3 * 0.25^2 - 2 * 0.25^3 = 0.15625. A linear move gives 0.25.
        Assert.Equal(0.15625f, Progress(prop), Tolerance);
        Frames(instance, 16);
        Assert.Equal(0.5f, Progress(prop), Tolerance);
        Frames(instance, 40);
        AssertNear(End, prop.Translate);
    }

    // The object must end where it started. A frame of 0.03 s does not land on the peak of the curve.
    [Fact]
    public void ACurveThatReturnsEndsAtTheStart()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var instance = Scene([prop], Move(Returning));

        Frames(instance, 17, 0.03);
        // At 0.51 s the curve is 0.9988.
        Assert.Equal(0.9988f, Progress(prop), 1e-3f);
        Frames(instance, 20, 0.03);
        AssertNear(Start, prop.Translate);
    }

    // When u is 1 or more, retail moves the object onto the target and keeps the last u below 1. After that, each
    // frame moves the object a part of a remaining way of 0. A frame of 1/64 s lands on the peak of the curve.
    [Fact]
    public void AFrameOnThePeakHoldsTheObjectOnTheTarget()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var instance = Scene([prop], Move(Returning));

        Frames(instance, 32);
        AssertNear(End, prop.Translate);
        Frames(instance, 40);
        AssertNear(End, prop.Translate);
    }

    // The start tangent 8 makes the curve go past 1. Retail curves go to 1.003 at most. The end tangent 2.14 is from
    // the m01a_fp7_03 look target.
    [Fact]
    public void ACurveAboveOneHoldsTheObjectOnTheTarget()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var ev = Move(FreeForm(new(0, 0, 0, 8), new(1, 1, 2.14f, 0)));
        var instance = Scene([prop], ev);

        var held = false;
        for (int i = 1; i <= 72; i++)
        {
            Frames(instance, 1);
            held |= ev.GetT((float) (i * Frame)) >= 1;
            if (held)
            {
                AssertNear(End, prop.Translate);
            }
            else
            {
                Assert.True(Progress(prop) < 1, $"Progress {Progress(prop)} at frame {i}");
            }
        }

        // The test is valid only if the curve goes past 1. The Hermite value at 0.5 is 1.2325.
        Assert.True(held);
    }

    // Each copy of an event moves the object from the pose that the copy before it gave. In each frame, one copy of
    // a linear move leaves 1 - w of the remaining way, so two copies give 1 - (1 - u)^2 of the move.
    [Fact]
    public void CopiesOfOneEventCompound()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var instance = Scene([prop], Move(null), Move(null));

        Frames(instance, 16);
        Assert.Equal(0.4375f, Progress(prop), Tolerance);
        Frames(instance, 16);
        Assert.Equal(0.75f, Progress(prop), Tolerance);
        Frames(instance, 40);
        AssertNear(End, prop.Translate);
    }

    // A move onto a second target goes from the current pose toward the pose of the target in that frame.
    [Fact]
    public void AMoveOntoASecondTargetPursuesTheTarget()
    {
        var camera = Prop("camera", Vector3.Zero, Quaternion.Identity);
        var lead = Prop("lead", new Vector3(10, 0, 0), Quaternion.Identity);
        var instance = Scene([camera, lead], Follow());

        Frames(instance, 32);
        Assert.Equal(5, camera.Translate.X, Tolerance);
        lead.Translate = new Vector3(20, 0, 0);
        Frames(instance, 16);
        // w = (0.75 - 0.5) / (1 - 0.5) = 0.5 of the way from 5 to 20. A lerp from the start pose gives 15.
        Assert.Equal(12.5f, camera.Translate.X, Tolerance);
    }

    // Another move of the object during the anim stays, for example the root motion of a character.
    [Fact]
    public void AnotherMoveOfTheObjectStays()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var instance = Scene([prop], Move(null));

        Frames(instance, 32);
        Assert.Equal(0.5f, Progress(prop), Tolerance);
        prop.Translate = Start;
        Frames(instance, 16);
        // w = 0.5 of the way from Start. A lerp from the start pose gives 0.75.
        Assert.Equal(0.5f, Progress(prop), Tolerance);
    }

    [Fact]
    public void TheMoveStopsAfterItsDuration()
    {
        var camera = Prop("camera", Vector3.Zero, Quaternion.Identity);
        var lead = Prop("lead", new Vector3(10, 0, 0), Quaternion.Identity);
        var instance = Scene([camera, lead], Follow());

        Frames(instance, 64);
        AssertNear(lead.Translate, camera.Translate);
        lead.Translate = new Vector3(20, 0, 0);
        Frames(instance, 8);
        AssertNear(new Vector3(10, 0, 0), camera.Translate);
    }

    // q_orient turns the object along the curve. It does not move the object.
    [Fact]
    public void QOrientTurnsTheObjectAlongTheCurve()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f);
        var to = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 2);
        var prop = Prop("prop", Start, start);
        var ev = new StartSpatialPropAnimEvent()
        {
            Duration = 1,
            Targets = ["prop"],
            SetFlags = StartSpatialPropAnimEvent.AnimVars.QOrient,
            Q_Orient = to,
            ParamCurve = Smoothstep
        };
        var instance = Scene([prop], ev);

        Frames(instance, 16);
        AssertNear(Quaternion.Slerp(start, to, 0.15625f), prop.Rotate);
        Assert.Equal(Start, prop.Translate);
        Frames(instance, 56);
        AssertNear(to, prop.Rotate);
    }

    // The axis is an axis of the object. A turn about X makes the Y axis of the object point along Z of the world,
    // so a turn about the world Y axis gives a different result.
    [Fact]
    public void AxisRotFollowsTheCurveAboutTheAxisOfTheObject()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var prop = Prop("prop", Start, start);
        var ev = AxisRot(ThnAxis.YAxis);
        var instance = Scene([prop], ev);

        Frames(instance, 16);
        AssertNear(start * Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(0.15625f)), prop.Rotate);
        Frames(instance, 60);
        AssertNear(start * Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(1)), prop.Rotate);
    }

    // Each copy of an axisrot event turns the object from the pose that the copy before it gave.
    [Fact]
    public void CopiesOfAnAxisRotCompound()
    {
        var prop = Prop("prop", Start, Quaternion.Identity);
        var ev = AxisRot(ThnAxis.YAxis);
        Frames(Scene([prop], ev, AxisRot(ThnAxis.YAxis)), 72);

        AssertNear(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(2)), prop.Rotate);
    }

    // Retail turns about the positive axis also for a NEG axis, but about the axis of the parent. Thus NEG_Y_AXIS
    // gives the same turn as Y_AXIS when the start rotation is the identity.
    [Fact]
    public void ANegativeAxisTurnsAboutThePositiveAxisOfTheParent()
    {
        var turns = new Quaternion[2];
        for (int i = 0; i < 2; i++)
        {
            var prop = Prop("prop", Start, Quaternion.Identity);
            Frames(Scene([prop], AxisRot(i == 0 ? ThnAxis.YAxis : ThnAxis.NegYAxis)), 72);
            turns[i] = prop.Rotate;
        }

        AssertNear(turns[0], turns[1]);
        // The test is valid only if the object turns.
        Assert.True(Angle(Quaternion.Identity, turns[1]) > 1);

        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var turned = Prop("prop", Start, start);
        var ev = AxisRot(ThnAxis.NegYAxis);
        var instance = Scene([turned], ev);
        Frames(instance, 16);
        AssertNear(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(0.15625f)) * start, turned.Rotate);
        Frames(instance, 56);
        AssertNear(Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(1)) * start, turned.Rotate);
    }

    // A zero-duration anim sets the pose at the end of the anim at once.
    [Fact]
    public void AZeroDurationAxisRotTurnsAboutTheAxisOfTheObject()
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        var prop = Prop("prop", Start, start);
        var ev = new StartSpatialPropAnimEvent()
        {
            Targets = ["prop"],
            SetFlags = StartSpatialPropAnimEvent.AnimVars.AxisRot,
            AxisRot = new() { Axis = ThnAxis.YAxis, Degrees = 30 }
        };
        Scene([prop], ev);

        AssertNear(start * Quaternion.CreateFromAxisAngle(Vector3.UnitY, ev.AxisRot.GetRads(1)), prop.Rotate);
    }

    // Retail writes the position only for pos and the orientation only for q_orient or orient, also for a
    // zero-duration move onto a second target.
    [Theory]
    [InlineData(StartSpatialPropAnimEvent.AnimVars.Pos)]
    [InlineData(StartSpatialPropAnimEvent.AnimVars.QOrient)]
    public void AZeroDurationMoveOntoATargetSetsOnlyItsValues(StartSpatialPropAnimEvent.AnimVars flags)
    {
        var start = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f);
        var prop = Prop("prop", Start, start);
        var lead = Prop("lead", End, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1));
        Scene([prop, lead], new StartSpatialPropAnimEvent() { Targets = ["prop", "lead"], SetFlags = flags });

        var pos = flags == StartSpatialPropAnimEvent.AnimVars.Pos;
        AssertNear(pos ? End : Start, prop.Translate);
        AssertNear(pos ? start : lead.Rotate, prop.Rotate);
    }

    // s004x at 16.302 s: the eye marker of the diplomat goes to hp_head of Trent. Without the target type, the marker
    // goes to the root of Trent, which is at the hip height of about 0.96 m.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIKMarkerGoesToTheHardpointOfACharacter(bool zeroDuration)
    {
        const string script = @"SCRIPTS\STORY\s004x_nrml_juni_li_01_Bar_01.thn";
        var data = RetailData.Open();
        var output = new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(data.ReadDataFile(script), script);
        var table = ((ThornTable)output["events"]).Values.Cast<ThornTable>().Single(x =>
            ThnTypes.Convert<EventTypes>(x[2]) == EventTypes.StartSpatialPropAnim &&
            MathF.Abs((float)x[1] - 16.302f) < 1e-3f && (string)((ThornTable)x[3])[1] == "Char_li_dip_Mk_IKeyes");
        var ev = new StartSpatialPropAnimEvent(table, script) { Time = 0 };
        Assert.Equal(["Char_li_dip_Mk_IKeyes", "Char_Trent"], ev.Targets);
        if (zeroDuration)
        {
            ev.Duration = 0;
        }

        var skeleton = data.LoadPosedCostume("trent", DfmMotionStackTests.GenericMale,
            "Sc_MLBODY_STND_IDLE_000LV_xa_04");
        // A zero time step keeps the pose and stops the root motion. Thus Trent stays on his spot.
        skeleton.UpdateScripts(0);
        var trent = new ThnSceneObject()
        {
            Name = "Char_Trent",
            Translate = new Vector3(-3.665f, 0, 3.133f),
            Rotate = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3),
            Object = new GameObject() { RenderComponent = new CharacterRenderer(skeleton) }
        };
        // The update puts Trent at his floor height plus his root height.
        trent.Update();
        var marker = Prop("Char_li_dip_Mk_IKeyes", Vector3.Zero, Quaternion.Identity);
        var instance = Scene([marker, trent], ev);
        Frames(instance, 40, 1.0 / 60);

        var head = (skeleton.Hardpoints["hp_head"].Transform * trent.Object!.WorldTransform).Position;
        AssertNear(head, marker.Translate);
        Assert.InRange(marker.Translate.Y, 1.6f, 1.85f);
        Assert.InRange(trent.Translate.Y, 0.85f, 1.1f);
    }
}
