using System.Numerics;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Server;
using LibreLancer.Server.Components;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// OL-0073: FollowPlayer lists for the player, one after the other. OL-0075: a Follow order of a lead.
public class FormationToolsTests
{
    private static readonly FormationDef Shape = new()
    {
        Nickname = "test_shape",
        Positions = [Vector3.Zero, new Vector3(-50, 0, 0), new Vector3(50, 0, 0)],
        PlayerPosition = new Vector3(0, 0, 50),
    };

    private static GameObject Player()
    {
        var player = new GameObject();
        player.AddComponent(new SPlayerComponent(null!, player));
        player.Flags |= GameObjectFlags.Player;
        return player;
    }

    [Fact]
    public void TwoListsForThePlayerKeepThePlayerAsTheLead()
    {
        var player = Player();
        var juni = new GameObject();
        var king = new GameObject();
        // The two ships follow the player, so the player leads a formation before the first list.
        FormationTools.EnterFormation(juni, player, Vector3.Zero);
        FormationTools.EnterFormation(king, player, Vector3.Zero);

        FormationTools.MakeNewFormation(player, Shape, [juni, king]);
        var first = player.Formation;
        FormationTools.MakeNewFormation(player, Shape, [juni, king]);

        var form = player.Formation;
        Assert.NotNull(form);
        Assert.NotSame(first, form);
        Assert.Same(player, form.LeadShip);
        Assert.Equal(new[] { juni, king }, form.Followers);
        Assert.Same(form, juni.Formation);
        Assert.Same(form, king.Formation);
    }

    [Fact]
    public void ANewListLeavesOutAShipThatItDoesNotName()
    {
        var player = Player();
        var juni = new GameObject();
        var king = new GameObject();
        FormationTools.MakeNewFormation(player, null, [juni, king]);

        FormationTools.MakeNewFormation(player, null, [juni]);

        Assert.Same(player, player.Formation!.LeadShip);
        Assert.Equal(new[] { juni }, player.Formation.Followers);
        Assert.DoesNotContain(king, player.Formation.Followers);
    }

    [Fact]
    public void AShipInTheListTwiceJoinsOnce()
    {
        var player = Player();
        var juni = new GameObject();

        FormationTools.MakeNewFormation(player, Shape, [juni, juni, player]);

        Assert.Same(player, player.Formation!.LeadShip);
        Assert.Equal(new[] { juni }, player.Formation.Followers);
    }

    [Fact]
    public void ALeadThatFollowsTakesItsFollowersAlong()
    {
        var player = Player();
        var ozu = new GameObject();
        var juni = new GameObject();
        var wing = new GameObject();
        FormationTools.MakeNewFormation(ozu, Shape, [juni, wing]);

        FormationTools.EnterFormation(ozu, player, new Vector3(-100, 50, -200));

        var form = player.Formation;
        Assert.NotNull(form);
        Assert.Same(player, form.LeadShip);
        Assert.Equal(new[] { ozu, juni, wing }, form.Followers);
        Assert.Same(form, ozu.Formation);
        Assert.Same(form, juni.Formation);
        Assert.Same(form, wing.Formation);
        Assert.Equal(new Vector3(-100, 50, -200), form.GetShipOffset(ozu));
    }

    [Fact]
    public void AFollowerThatFollowsLeavesTheOthersWithTheirLead()
    {
        var player = Player();
        var ozu = new GameObject();
        var juni = new GameObject();
        var wing = new GameObject();
        FormationTools.MakeNewFormation(ozu, Shape, [juni, wing]);

        FormationTools.EnterFormation(juni, player, Vector3.Zero);

        Assert.Equal(new[] { juni }, player.Formation!.Followers);
        Assert.Same(ozu, wing.Formation!.LeadShip);
        Assert.Equal(new[] { wing }, ozu.Formation!.Followers);
    }
}
