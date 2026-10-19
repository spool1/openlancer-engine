using System;
using System.IO;
using System.Linq;
using LibreLancer.Data;
using LibreLancer.Data.IO;
using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Data.Schema.Storyline;
using LibreLancer.Missions;
using Xunit;

namespace LibreLancer.Tests;

// Each trigger of the 14 story scripts builds (OL-0061). Before, a line that the engine cannot build stopped the load
// of the whole mission. The retail data has one such line: an action of a trigger that nothing starts.
[Collection(nameof(LogCapture))]
public class MissionScriptRetailTests
{
    private const string Skipped = "and skips the line";

    // Builds each trigger of each story script, and returns the number of triggers.
    private static int BuildTriggers(FileSystem vfs)
    {
        var freelancer = new FreelancerIni(vfs);
        var storyline = new StorylineIni();
        storyline.AddDefault();
        var count = 0;
        foreach (var mission in storyline.Missions)
        {
            var script = new MissionIni(freelancer.DataPath + mission.File, vfs);
            foreach (var trigger in script.Triggers)
            {
                ScriptedTrigger.FromIni(trigger);
                count++;
            }
        }

        return count;
    }

    [Fact]
    public void EachTriggerOfTheRetailStoryBuilds()
    {
        var data = RetailData.Open();
        using var log = new LogCapture();

        Assert.Equal(2984, BuildTriggers(data.VFS));
        Assert.Single(log.Lines(Skipped));
    }

    // The whole script of each of the 14 story missions loads with the loaded game data (OL-0063): the solars, the
    // ships, the formations, the order lists, the loot, the ship builds and the triggers. Before OL-0061, the load of
    // one mission stopped at a line that the engine cannot build.
    [Fact]
    public void EachRetailStoryMissionLoads()
    {
        var data = RetailData.Open();
        var db = new GameItemDb(data.VFS);
        db.LoadData();
        var freelancer = new FreelancerIni(data.VFS);
        var storyline = new StorylineIni();
        storyline.AddDefault();
        using var log = new LogCapture();

        foreach (var mission in storyline.Missions)
        {
            var ini = new MissionIni(freelancer.DataPath + mission.File, data.VFS);
            var script = new MissionScript(ini, db);
            Assert.NotEmpty(script.InitTriggers);
            // The script has one trigger for each name of a trigger section.
            Assert.Equal(ini.Triggers.Select(t => t.Nickname).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                script.AvailableTriggers.Count);
        }

        Assert.Equal(14, storyline.Missions.Count);
        Assert.Single(log.Lines(Skipped));
    }

    // OPENLANCER_HDE names a copy of an HD Edition 0.7.1 install. The test only reads it, and it skips without it.
    [Fact]
    public void EachTriggerOfTheHdEditionStoryBuilds()
    {
        var folder = Environment.GetEnvironmentVariable("OPENLANCER_HDE");
        Assert.SkipUnless(!string.IsNullOrEmpty(folder) && Directory.Exists(folder),
            "No HD Edition copy. Set OPENLANCER_HDE to a copy of an HD Edition 0.7.1 install.");
        using var log = new LogCapture();

        Assert.Equal(2649, BuildTriggers(FileSystem.FromPath(folder!)));
        Assert.Empty(log.Lines(Skipped));
    }
}
