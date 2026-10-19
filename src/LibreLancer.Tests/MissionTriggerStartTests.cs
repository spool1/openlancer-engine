using System.IO;
using System.Text;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using Xunit;

namespace LibreLancer.Tests;

// A start of a trigger that the script does not have (OL-0062). Retail ignores the name. Before, the engine stopped
// with an exception, so the mission ended.
[Collection(nameof(LogCapture))]
public class MissionTriggerStartTests
{
    private const string Script = """
        [Trigger]
        nickname = sample_start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = sample_missing
        Act_ActTrig = sample_next

        [Trigger]
        nickname = sample_next
        Cnd_Timer = 100
        """;

    private static MissionScript Build(string text)
    {
        var ini = new MissionIni();
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        var script = new MissionScript();
        foreach (var trigger in ini.Triggers)
        {
            script.AvailableTriggers[trigger.Nickname] = ScriptedTrigger.FromIni(trigger);
            if (trigger.InitState == TriggerInitState.ACTIVE)
            {
                script.InitTriggers.Add(trigger.Nickname);
            }
        }

        return script;
    }

    [Fact]
    public void AStartOfAMissingTriggerIsIgnoredWithAWarning()
    {
        using var log = new LogCapture();

        // The runtime reads no player while it starts these triggers.
        var runtime = new MissionRuntime(Build(Script), null!, []);

        Assert.Single(log.Lines("The script has no trigger 'sample_missing'"));
        Assert.Equal(TriggerState.ON, runtime.GetTriggerState("sample_next"));
    }
}
