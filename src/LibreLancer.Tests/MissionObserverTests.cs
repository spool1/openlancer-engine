using System.Collections.Generic;
using System.IO;
using System.Text;
using LibreLancer.Data;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using Xunit;

namespace LibreLancer.Tests;

// The observer of the trigger changes of a mission runtime (OL-0070). The observer changes no rule of the runtime.
[Collection(nameof(LogCapture))]
public class MissionObserverTests
{
    // A start trigger with Cnd_True runs at once and starts a timer trigger.
    private const string Script = """
        [Trigger]
        nickname = sample_start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_ActTrig = sample_timer

        [Trigger]
        nickname = sample_timer
        Cnd_Timer = 100
        """;

    private sealed class Recorder : IMissionObserver
    {
        public List<string> Changes { get; } = [];

        public void MissionLoaded(string mission) => Changes.Add($"loaded {mission}");

        public void TriggerChanged(string trigger, MissionTriggerChange change) => Changes.Add($"{change} {trigger}");
    }

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
    public void TheStartTriggerRunsThenTheTimerStarts()
    {
        using var log = new LogCapture();
        var observer = new Recorder();

        // The runtime reads no player while it starts these triggers.
        _ = new MissionRuntime(Build(Script), null!, [], observer);

        // The instant run of a Cnd_True trigger never joins the active list, so it gives Ran and no Started.
        Assert.Equal(["Ran sample_start", "Started sample_timer"], observer.Changes);
    }

    [Fact]
    public void AStopGivesStoppedOnce()
    {
        using var log = new LogCapture();
        var observer = new Recorder();
        var runtime = new MissionRuntime(Build(Script), null!, [], observer);
        observer.Changes.Clear();

        runtime.DeactivateTrigger("sample_timer");
        // The trigger stays in the list until the next check, so a second stop finds it again. It changes nothing.
        runtime.DeactivateTrigger("sample_timer");
        runtime.DeactivateTrigger("sample_missing");

        Assert.Equal(["Stopped sample_timer"], observer.Changes);
    }

    [Fact]
    public void ASavedTriggerStartsAndReachesTheObserver()
    {
        using var log = new LogCapture();
        var observer = new Recorder();

        _ = new MissionRuntime(Build(Script), null!, [FLHash.CreateID("sample_timer")], observer);

        Assert.Equal(["Started sample_timer"], observer.Changes);
    }

    [Fact]
    public void ARuntimeWithoutAnObserverHasTheSameTriggerStates()
    {
        using var log = new LogCapture();

        Assert.Equal(States(new MissionRuntime(Build(Script), null!, [])),
            States(new MissionRuntime(Build(Script), null!, [], new Recorder())));
    }

    /// <summary>The states of the two triggers after the start, and after a stop of the timer. The check of the script
    /// reads the player, so the states come before it.</summary>
    private static List<TriggerState> States(MissionRuntime runtime)
    {
        var states = new List<TriggerState> { runtime.GetTriggerState("sample_start"), runtime.GetTriggerState("sample_timer") };
        runtime.DeactivateTrigger("sample_timer");
        states.Add(runtime.GetTriggerState("sample_timer"));
        states.Add(runtime.GetTriggerState("sample_missing"));
        return states;
    }
}
