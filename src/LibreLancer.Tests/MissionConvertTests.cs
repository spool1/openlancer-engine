using System.IO;
using System.Linq;
using System.Text;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using LibreLancer.Missions.Actions;
using LibreLancer.Missions.Conditions;
using Xunit;

namespace LibreLancer.Tests;

// The build of mission triggers (OL-0061). The engine cannot build some names. It skips such a line with a warning
// that names the trigger and the line, so the mission still loads.
[Collection(nameof(LogCapture))]
public class MissionConvertTests
{
    private static MissionTrigger Trigger(string text)
    {
        var ini = new MissionIni();
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        return Assert.Single(ini.Triggers);
    }

    [Fact]
    public void ALineThatTheEngineCannotBuildIsSkippedWithAWarning()
    {
        using var log = new LogCapture();

        var trigger = ScriptedTrigger.FromIni(Trigger("""
            [Trigger]
            nickname = sample_trigger
            Cnd_CmpToPlane = sample_plane, sample_ship, 1, 0, 0, 0
            Cnd_True = no_params
            Act_GiveMB = Player, sample_behavior
            Act_ActTrig = sample_next
            """));

        Assert.IsType<Cnd_True>(Assert.Single(trigger.Conditions));
        Assert.IsType<Act_ActTrig>(Assert.Single(trigger.Actions));
        var condition = Assert.Single(log.Lines("cannot run Cnd_CmpToPlane in trigger 'sample_trigger'"));
        var action = Assert.Single(log.Lines("cannot run Act_GiveMB in trigger 'sample_trigger'"));
        Assert.Contains($"{IniText.File}, line 3", condition);
        Assert.Contains($"{IniText.File}, line 5", action);
        Assert.Empty(log.Lines("never runs"));
    }

    [Fact]
    public void ATriggerWhoseOnlyConditionIsSkippedNeverRuns()
    {
        using var log = new LogCapture();

        var trigger = ScriptedTrigger.FromIni(Trigger("""
            [Trigger]
            nickname = sample_trigger
            Cnd_JumpgateAct = sample_gate
            Act_ActTrig = sample_next
            """));

        var condition = Assert.IsType<NeverCondition>(Assert.Single(trigger.Conditions));
        Assert.False(condition.CheckCondition(null!, null!, 1));
        Assert.Single(log.Lines("cannot run Cnd_JumpgateAct"));
        Assert.Single(log.Lines("Trigger 'sample_trigger' has no condition that the engine can run"));
    }

    [Fact]
    public void ATriggerWithoutConditionsKeepsNoCondition()
    {
        using var log = new LogCapture();

        var trigger = ScriptedTrigger.FromIni(Trigger("[Trigger]\nnickname = sample_trigger\nAct_ActTrig = sample_next\n"));

        Assert.Empty(trigger.Conditions);
        Assert.Empty(log.Lines("sample_trigger"));
    }

    [Fact]
    public void EachUnsupportedNameIsSkipped()
    {
        using var log = new LogCapture();
        var actions = string.Join("\n", ScriptedAction.Unsupported.Select(a => $"{a} = sample_value"));
        var conditions = string.Join("\n", ScriptedCondition.Unsupported.Select(c => $"{c} = sample_value"));

        var trigger = ScriptedTrigger.FromIni(Trigger($"[Trigger]\nnickname = sample_trigger\n{conditions}\n{actions}\n"));

        Assert.IsType<NeverCondition>(Assert.Single(trigger.Conditions));
        Assert.Empty(trigger.Actions);
        Assert.Equal(ScriptedAction.Unsupported.Length + ScriptedCondition.Unsupported.Length,
            log.Lines("in trigger 'sample_trigger' and skips the line").Count);
    }
}
