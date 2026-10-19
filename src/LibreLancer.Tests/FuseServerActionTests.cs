using System.IO;
using System.Numerics;
using System.Text;
using LibreLancer.Data.Schema.Fuses;
using Xunit;

namespace LibreLancer.Tests;

// The server fuse actions of retail (OL-0046): [tumble], [damage_root], [damage_group], [damage_hp_attachment],
// [make_invincible] and [dump_cargo]. The engine dropped them with the warning "Unknown section". The fuse runner
// does not run them yet.
[Collection(nameof(LogCapture))]
public class FuseServerActionTests
{
    private const string Fuses = """
        [fuse]
        name = sample_fuse
        lifetime = 2

        [tumble]
        at_t = 0.1
        ang_drag_scale = 0.5
        turn_throttle_x = -1, 1
        turn_throttle_y = -0.5, 0.5
        turn_throttle_z = 0, 0.2
        throttle = 0.2, 0.8
        engine_kill = true

        [damage_root]
        at_t = 0.2
        damage_type = absolute
        hitpoints = 100

        [damage_group]
        at_t = 0.3
        group_name = sample_part
        damage_type = normal
        hitpoints = 50

        [damage_hp_attachment]
        at_t = 0.4
        hardpoint = HpSample01
        damage_type = absolute
        hitpoints = 25

        [make_invincible]
        turn_on = false

        [dump_cargo]
        at_t = 0.9
        origin_hardpoint = HpSample02
        """;

    private static Fuse Parse(string text)
    {
        var ini = new FuseIni();
        ini.ParseIni(new MemoryStream(Encoding.UTF8.GetBytes(text)), IniText.File);
        return Assert.Single(ini.Fuses);
    }

    [Fact]
    public void TheFuseReadsEachServerActionWithoutAWarning()
    {
        using var log = new LogCapture();
        var fuse = Parse(Fuses);

        Assert.Collection(fuse.Actions,
            x => Assert.IsType<FuseTumble>(x),
            x => Assert.IsType<FuseDamageRoot>(x),
            x => Assert.IsType<FuseDamageGroup>(x),
            x => Assert.IsType<FuseDamageHpAttachment>(x),
            x => Assert.IsType<FuseMakeInvincible>(x),
            x => Assert.IsType<FuseDumpCargo>(x));
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void TumbleKeepsItsRanges()
    {
        var tumble = Assert.IsType<FuseTumble>(Parse(Fuses).Actions[0]);

        Assert.Equal(0.1f, tumble.AtT);
        Assert.Equal(0.5f, tumble.AngularDragScale);
        Assert.Equal(new Vector2(-1, 1), tumble.TurnThrottleX);
        Assert.Equal(new Vector2(-0.5f, 0.5f), tumble.TurnThrottleY);
        Assert.Equal(new Vector2(0, 0.2f), tumble.TurnThrottleZ);
        Assert.Equal(new Vector2(0.2f, 0.8f), tumble.Throttle);
        Assert.True(tumble.EngineKill);
    }

    [Fact]
    public void TheDamageActionsKeepTheirTargetTypeAndHitPoints()
    {
        var actions = Parse(Fuses).Actions;
        var root = Assert.IsType<FuseDamageRoot>(actions[1]);
        var group = Assert.IsType<FuseDamageGroup>(actions[2]);
        var attachment = Assert.IsType<FuseDamageHpAttachment>(actions[3]);

        Assert.Equal(("absolute", 100), (root.DamageType, root.Hitpoints));
        Assert.Equal(("sample_part", "normal", 50), (group.GroupName, group.DamageType, group.Hitpoints));
        Assert.Equal(("HpSample01", "absolute", 25), (attachment.Hardpoint, attachment.DamageType, attachment.Hitpoints));
    }

    [Fact]
    public void MakeInvincibleAndDumpCargoKeepTheirValues()
    {
        var actions = Parse(Fuses).Actions;

        Assert.False(Assert.IsType<FuseMakeInvincible>(actions[4]).TurnOn);
        Assert.Equal("HpSample02", Assert.IsType<FuseDumpCargo>(actions[5]).OriginHardpoint);
    }

    // The defaults are the retail defaults: a full angular drag, a turn input from 0 to 1 and invincibility on.
    [Fact]
    public void AnActionWithoutKeysHasTheRetailDefaults()
    {
        var actions = Parse("[fuse]\nname = sample_fuse\n[tumble]\n[make_invincible]").Actions;
        var tumble = Assert.IsType<FuseTumble>(actions[0]);

        Assert.Equal(1f, tumble.AngularDragScale);
        Assert.Equal(new Vector2(0, 1), tumble.TurnThrottleX);
        Assert.Equal(Vector2.Zero, tumble.Throttle);
        Assert.True(Assert.IsType<FuseMakeInvincible>(actions[1]).TurnOn);
    }
}
