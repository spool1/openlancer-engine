using LibreLancer.Data.Schema;
using Xunit;

namespace LibreLancer.Tests;

// The lowest level of detail of a [Simple] (OL-0051). The retail client caps the level of detail on low detail
// settings with MinSpecLOD. The engine warned "Unknown entry MinSpecLOD". OpenLancer does not need the value.
[Collection(nameof(LogCapture))]
public class SimpleMinSpecLodTests
{
    [Fact]
    public void ASimpleReadsMinSpecLodWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(Simple.TryParse(IniText.Section("""
            [Simple]
            nickname = sample_simple
            DA_archetype = sample.3db
            LODranges = 0, 100, 200, 400
            MinSpecLOD = 2
            """), out var simple));

        Assert.Equal(2, simple.MinSpecLOD);
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void ASimpleWithoutTheKeyHasTheRetailDefault()
    {
        Assert.True(Simple.TryParse(IniText.Section("[Simple]\nnickname = sample_simple\nDA_archetype = sample.3db"),
            out var simple));

        Assert.Equal(1, simple.MinSpecLOD);
    }
}
