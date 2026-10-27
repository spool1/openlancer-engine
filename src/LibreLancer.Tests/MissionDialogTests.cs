using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LibreLancer.Data;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using LibreLancer.Net;
using LibreLancer.Net.Protocol;
using LibreLancer.Net.Protocol.RpcPackets;
using LibreLancer.Server;
using Xunit;

namespace LibreLancer.Tests;

// Act_StartDialog without a space world (OL-0071). A docked player, or a player before the spawn of a load, has no
// space world. The dialog then goes to the client without a source ship, as Act_SendComm does.
[Collection(nameof(LogCapture))]
public class MissionDialogTests
{
    // A start trigger with Cnd_True starts a dialog of two lines at once, in the constructor of the runtime.
    private const string Script = """
        [Dialog]
        nickname = sample_dialog
        system = sample_system
        line = sample_ship, Player, sample_line_1
        line = Player, sample_ship, sample_line_2

        [Trigger]
        nickname = sample_start
        InitState = ACTIVE
        Cnd_True = no_params
        Act_StartDialog = sample_dialog
        """;

    private sealed class Client : IPacketClient
    {
        public List<IPacket> Sent { get; } = [];

        public int MaxSequencedSize => 1024;

        public bool Connected => true;

        public void SendPacket(IPacket packet, PacketDeliveryMethod method) => Sent.Add(packet);

        public void Disconnect(DisconnectReason reason)
        {
        }
    }

    private static MissionScript Build(string text)
    {
        var ini = new MissionIni();
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        var script = new MissionScript();
        foreach (var dialog in ini.Dialogs)
        {
            script.Dialogs[dialog.Nickname] = ScriptDialog.FromIni(dialog);
        }

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
    public void ADialogStartsWhileThePlayerHasNoSpaceWorld()
    {
        using var log = new LogCapture();
        var client = new Client();
        var player = new Player(client, null!, Guid.Empty, default);

        // Before OL-0071, the dialog line of the ship read the space world of the player and threw.
        _ = new MissionRuntime(Build(Script), player, []);

        Assert.Null(player.Space);
        var packet = Assert.IsType<IClientPlayer_RunMissionDialogPacket>(Assert.Single(client.Sent));
        Assert.Equal(2, packet.lines.Length);
        Assert.All(packet.lines, line => Assert.Equal(0, line.Source));
        Assert.True(packet.lines[0].TargetIsPlayer);
        Assert.False(packet.lines[1].TargetIsPlayer);
        Assert.Equal(FLHash.CreateID("sample_line_1"), packet.lines[0].Hash);
    }
}
