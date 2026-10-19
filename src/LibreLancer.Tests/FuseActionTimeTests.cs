using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data.Schema.Fuses;
using LibreLancer.Server.Components;
using Xunit;

namespace LibreLancer.Tests;

// The time of a fuse action (OL-0044). Retail reads "at_t = a[, b]". With b, the server picks a random time between a
// and b at each ignition of the fuse. The engine read one value, warned "Too many components for at_t" and always
// used a.
[Collection(nameof(LogCapture))]
public class FuseActionTimeTests
{
    private static FuseDestroyGroup Action(string atT)
    {
        Assert.True(FuseDestroyGroup.TryParse(IniText.Section($"[destroy_group]\ngroup_name = sample_part\n{atT}"),
            out var action));
        return action;
    }

    [Fact]
    public void TwoValuesGiveTheRangeWithoutAWarning()
    {
        using var log = new LogCapture();
        var action = Action("at_t = 0.2, 0.6");

        Assert.Equal((0.2f, 0.6f), (action.AtT, action.AtTMax));
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void OneValueGivesOneTime()
    {
        var action = Action("at_t = 0.4");

        Assert.Equal((0.4f, 0.4f), (action.AtT, action.AtTMax));
    }

    [Fact]
    public void EachIgnitionPicksATimeInTheRange()
    {
        var ranged = Action("at_t = 0.2, 0.6");
        var random = new Random(1);
        var times = Enumerable.Range(0, 200)
            .Select(_ => SFuseRunnerComponent.Schedule([ranged], random).Single().Time).ToList();

        Assert.All(times, t => Assert.InRange(t, 0.2f, 0.6f));
        Assert.True(times.Distinct().Count() > 100);
    }

    // The order of the actions follows the times of the ignition, so an action with a range can come before or after
    // an action with one time in that range.
    [Fact]
    public void TheOrderOfTheActionsFollowsThePickedTimes()
    {
        var ranged = Action("at_t = 0.2, 0.6");
        var fixedTime = Action("at_t = 0.4");
        var random = new Random(2);
        var firsts = new HashSet<FuseAction>();
        for (var i = 0; i < 100; i++)
        {
            var schedule = SFuseRunnerComponent.Schedule([ranged, fixedTime], random).ToList();
            Assert.True(schedule[0].Time <= schedule[1].Time);
            firsts.Add(schedule[0].Action);
        }

        Assert.Equal(2, firsts.Count);
    }

    // [destroy_root] has no key of its own, so at_t is its only key. The parser generator now also checks a second line
    // of a handler in a section without entries.
    [Fact]
    public void ASecondTimeLineOfADestroyRootGivesAWarning()
    {
        using var log = new LogCapture();
        Assert.True(FuseDestroyRoot.TryParse(IniText.Section("[destroy_root]\nat_t = 0.1, 0.3\nat_t = 0.5"),
            out var action));

        Assert.Equal((0.5f, 0.5f), (action.AtT, action.AtTMax));
        Assert.Single(log.Lines("Duplicate of at_t"));
    }

    [Fact]
    public void ActionsWithTheSameTimeKeepTheOrderOfTheFile()
    {
        var first = Action("at_t = 0.5");
        var second = Action("at_t = 0.5");

        Assert.Equal([first, second], SFuseRunnerComponent.Schedule([first, second], new Random(3)).Select(x => x.Action));
    }
}
