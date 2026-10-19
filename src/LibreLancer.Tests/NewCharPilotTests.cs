using LibreLancer.Data.Schema.NewCharDB;
using Xunit;

namespace LibreLancer.Tests;

// The picture of a pilot of the new character screen (OL-0052). Retail reads thumb in a [Pilot] of the new character
// data. The engine warned "Unknown entry thumb". OpenLancer does not need the picture.
[Collection(nameof(LogCapture))]
public class NewCharPilotTests
{
    [Fact]
    public void APilotReadsItsPictureWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(NewCharPilot.TryParse(IniText.Section("""
            [Pilot]
            nickname = sample_pilot
            body = sample_body
            comm = sample_head
            voice = sample_voice
            thumb = sample_picture
            """), out var pilot));

        Assert.Equal("sample_picture", pilot.Thumb);
        Assert.Empty(log.Lines(IniText.File));
    }
}
