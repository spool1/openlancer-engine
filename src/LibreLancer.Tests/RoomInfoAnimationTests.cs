using LibreLancer.Data.Schema.Universe.Rooms;
using Xunit;

namespace LibreLancer.Tests;

// The animations of a room (OL-0049). Each animation line adds a looping animation, and each animation_oneshot line
// one that plays one time. The engine kept one animation line, with the warning "Duplicate of animation", and dropped
// animation_oneshot. It does not play room animations yet.
[Collection(nameof(LogCapture))]
public class RoomInfoAnimationTests
{
    [Fact]
    public void ARoomKeepsEachAnimationLineWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(RoomInfo.TryParse(IniText.Section("""
            [Room_Info]
            set_script = sample.thn
            animation = Sc_sample loop
            animation = Sc_sample fan
            animation_oneshot = Sc_sample door
            """), out var room));

        Assert.Equal(["Sc_sample loop", "Sc_sample fan"], room.Animations);
        Assert.Equal(["Sc_sample door"], room.OneShotAnimations);
        Assert.Empty(log.Lines(IniText.File));
    }
}
