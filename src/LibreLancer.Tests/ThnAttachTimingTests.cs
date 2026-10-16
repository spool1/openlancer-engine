using System.Numerics;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using Xunit;

namespace LibreLancer.Tests;

// Each frame uses the order of Cutscene._Update: the events and processors run, then each scene object updates.
public class ThnAttachTimingTests
{
    private static readonly Vector3 MarkerPosition = new(10, 0, 0);

    private static (ThnScriptInstance Instance, ThnSceneObject Actor, ThnSceneObject Marker) Scene(float time,
        float duration)
    {
        var script = new ThnScript() { Duration = 100 };
        script.Events.Add(new AttachEntityEvent()
        {
            Time = time,
            Duration = duration,
            Targets = ["actor", "marker"],
            TargetType = TargetTypes.Root,
            Flags = AttachFlags.Position | AttachFlags.Orientation
        });
        var instance = new ThnScriptInstance(null!, script);
        var actor = new ThnSceneObject() { Name = "actor", Rotate = Quaternion.Identity };
        var marker = new ThnSceneObject() { Name = "marker", Translate = MarkerPosition, Rotate = Quaternion.Identity };
        instance.Objects[actor.Name] = actor;
        instance.Objects[marker.Name] = marker;
        // Cutscene.SceneSetup runs the events at T=0.
        instance.Update(0);
        return (instance, actor, marker);
    }

    private static void Frames(ThnScriptInstance instance, int count)
    {
        for (int i = 0; i < count; i++)
        {
            instance.Update(1);
            foreach (var obj in instance.Objects.Values)
            {
                obj.Update();
            }
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(5f)]
    public void AZeroDurationAttachMovesTheActorOnce(float time)
    {
        var (instance, actor, marker) = Scene(time, 0);

        Frames(instance, 10);
        Assert.Equal(MarkerPosition, actor.Translate);
        Assert.Empty(actor.Attachments);

        marker.Translate = new Vector3(20, 0, 0);
        Frames(instance, 1);
        Assert.Equal(MarkerPosition, actor.Translate);
    }

    [Fact]
    public void ALongAttachFollowsTheMarker()
    {
        var (instance, actor, marker) = Scene(5, 50);

        Frames(instance, 10);
        marker.Translate = new Vector3(20, 0, 0);
        Frames(instance, 1);

        Assert.Equal(new Vector3(20, 0, 0), actor.Translate);
        Assert.Single(actor.Attachments);
    }
}
