using System.IO;
using System.Linq;
using System.Text;
using LibreLancer.Data.Schema.Missions;
using Xunit;

namespace LibreLancer.Tests;

// The mission sections [MsnRandEnc] and [MetaBehavior] and the labels of [MsnFormation] (OL-0040). Retail reads them.
// The engine dropped them with the warnings "Unknown section" and "Unknown entry label".
[Collection(nameof(LogCapture))]
public class MissionIniSectionTests
{
    private const string Mission = """
        [Mission]
        mission_title = 1

        [MsnFormation]
        nickname = sample_formation
        formation = sample_wing
        label = sample_label
        label = sample_group

        [MsnRandEnc]
        nickname = sample_encounter
        encounter_type = TL_Attack
        sample_key = 1, 2
        other_key = text

        [MetaBehavior]
        nickname = sample_guides
        system = sample_system
        MB_GotoGuide = sample_target, 0, 0, 0, 100, 0, 0, 500
        MB_DockGuide = sample_dock
        """;

    private static MissionIni Parse(string text)
    {
        var ini = new MissionIni();
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        return ini;
    }

    [Fact]
    public void TheMissionReadsEachSectionWithoutAWarning()
    {
        using var log = new LogCapture();
        Parse(Mission);

        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void AFormationKeepsEachLabel()
    {
        var formation = Assert.Single(Parse(Mission).Formations);

        Assert.Equal(["sample_label", "sample_group"], formation.Labels);
    }

    [Fact]
    public void ARandomEncounterKeepsItsTypeAndTheOtherKeysAsRawEntries()
    {
        var encounter = Assert.Single(Parse(Mission).RandomEncounters);

        Assert.Equal(("sample_encounter", "TL_Attack"), (encounter.Nickname, encounter.EncounterType));
        Assert.Equal(["sample_key", "other_key"], encounter.Entries.Select(x => x.Name));
        Assert.Equal(2, encounter.Entries[0].Count);
    }

    [Fact]
    public void AMetaBehaviorKeepsEachGuideLine()
    {
        var behavior = Assert.Single(Parse(Mission).MetaBehaviors);

        Assert.Equal(("sample_guides", "sample_system"), (behavior.Nickname, behavior.System));
        Assert.Equal(["MB_GotoGuide", "MB_DockGuide"], behavior.Guides.Select(x => x.Name));
        Assert.Equal(8, behavior.Guides[0].Count);
    }

    // The game ignores a key of a [MetaBehavior] that is not a guide line, so the engine warns about it.
    [Fact]
    public void AnotherKeyOfAMetaBehaviorGivesAWarning()
    {
        using var log = new LogCapture();
        var behavior = Assert.Single(Parse("""
            [MetaBehavior]
            nickname = sample_guides
            sample_key = 1
            """).MetaBehaviors);

        Assert.Empty(behavior.Guides);
        Assert.Single(log.Lines("Unknown entry sample_key"));
    }
}
