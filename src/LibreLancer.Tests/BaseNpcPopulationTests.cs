using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Client;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.GCS;
using LibreLancer.Data.Schema.MBases;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Data.Schema.Save;
using LibreLancer.Thn;
using LibreLancer.Thorn;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

public class BaseNpcPopulationTests
{
    [Fact]
    public void FixedFixturesKeepTheirConfiguredMarkerAndIgnoreDensity()
    {
        var fixedNpc = new BaseNpc("bartender")
        {
            Placement = new BaseNpcPlacement("Zs/NPC/01/01/A/Stand", Script("fixed"), "bartender")
        };
        var ambient = new BaseNpc("ambient");
        var room = Room(0, fixedNpc, ambient);
        var spots = new[]
        {
            new RoomNpcSpot("Zs/NPC/01/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/02/A/Stand", true, Posture.Stand)
        };

        var result = BaseNpcPopulation.Select(room, spots, (_, _) => null, new Random(42));

        var assignment = Assert.Single(result);
        Assert.True(assignment.Fixed);
        Assert.Equal("bartender", assignment.Npc.Nickname);
        Assert.Equal("Zs/NPC/01/01/A/Stand", assignment.SceneSpot);
    }

    [Fact]
    public void AmbientAssignmentsRerollWithRoomEntrySeed()
    {
        var room = Room(3, new BaseNpc("one"), new BaseNpc("two"), new BaseNpc("three"));
        var spots = new[]
        {
            new RoomNpcSpot("Zg/NPC/01/01/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/02/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/03/A/Stand", true, Posture.Stand)
        };

        var signatures = Enumerable.Range(1, 8)
            .Select(seed => string.Join(",", BaseNpcPopulation
                .Select(room, spots, (_, _) => null, new Random(seed))
                .Select(x => $"{x.Npc.Nickname}@{x.SceneSpot}")))
            .Distinct()
            .ToArray();

        Assert.True(signatures.Length > 1);
    }

    [Fact]
    public void DensityLimitsOnlyAmbientAndDoesNotDuplicateSpotsOrNpcs()
    {
        var fixedNpc = new BaseNpc("vendor")
        {
            Placement = new BaseNpcPlacement("Zs/NPC/01/01/A/Stand", Script("fixed"), "trader")
        };
        var room = Room(1, fixedNpc, new BaseNpc("one"), new BaseNpc("two"));
        var spots = new[]
        {
            new RoomNpcSpot("Zs/NPC/01/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/02/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/03/A/Stand", true, Posture.Stand)
        };

        var result = BaseNpcPopulation.Select(room, spots, (_, _) => null, new Random(7));

        Assert.InRange(result.Length, 1, 2);
        Assert.Equal(result.Length, result.Select(x => x.Npc.Nickname).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(result.Length, result.Select(x => x.SceneSpot).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(result, x => x.Fixed && x.Npc.Nickname == "vendor");
    }

    [Fact]
    public void AmbientSlotsCanBeEmptyWhileFixturesRemainPresent()
    {
        var fixedNpc = new BaseNpc("vendor")
        {
            Placement = new BaseNpcPlacement("Zs/NPC/01/01/A/Stand", Script("fixed"), "trader")
        };
        var room = Room(3, fixedNpc, new BaseNpc("one"), new BaseNpc("two"), new BaseNpc("three"));
        var spots = new[]
        {
            new RoomNpcSpot("Zs/NPC/01/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/02/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/03/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/01/04/A/Stand", true, Posture.Stand)
        };

        var counts = Enumerable.Range(1, 32)
            .Select(seed => BaseNpcPopulation
                .Select(room, spots, (_, _) => null, new Random(seed))
                .Count(x => !x.Fixed))
            .ToArray();

        Assert.Contains(0, counts);
        Assert.Contains(3, counts);
    }

    [Fact]
    public void ReservedSpotsAreNeverGivenToAmbientNpcs()
    {
        var fixedNpc = Fixture("bartender", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var ambient = Enumerable.Range(0, 10).Select(i => new BaseNpc($"ambient{i}"));
        var room = Room(7, [fixedNpc, ..ambient]);
        var spots = new[]
        {
            new RoomNpcSpot("Zs/NPC/Bartender/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/mTable3/01/A/SitLow", true, Posture.SitLow),
            new RoomNpcSpot("Zg/NPC/mFloor3/01/A/Stand", true, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/mFloor2/01/A/Stand", true, Posture.Stand)
        };
        // The first spot has a different letter case than its marker, as in the s003x RTC INI.
        string[] reserved = ["Zg/NPC/mTable3/01/A/Sitlow", "Zg/NPC/mFloor3/01/A/Stand"];

        var freeSpotUsed = false;
        for (var seed = 0; seed < 200; seed++)
        {
            var result = BaseNpcPopulation.Select(room, spots, (_, _) => null, new Random(seed), reserved);

            Assert.Contains(result, x => x.Fixed && x.Npc == fixedNpc && x.SceneSpot == "Zs/NPC/Bartender/01/A/Stand");
            Assert.DoesNotContain(result, x => reserved.Contains(x.SceneSpot, StringComparer.OrdinalIgnoreCase));
            freeSpotUsed |= result.Any(x => x.SceneSpot == "Zg/NPC/mFloor2/01/A/Stand");
        }

        Assert.True(freeSpotUsed);
    }

    [Fact]
    public void FindFixtureActorsBindsTheRole()
    {
        var bartender = Fixture("li0101_fix_bartender", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var dealer = Fixture("li0101_fix_weaponsdealer", "Zs/NPC/Equipment/01/A/Stand", "Equipment");
        var trader = Fixture("li0101_fix_trader", "Zs/NPC/Trader/01/A/Stand", "trader");
        var room = Room(0, bartender, new BaseNpc("ambient"), dealer, trader);
        var script = Characters(
            // Only the actor binds. The name and the template of this entity are the role of the trader.
            Character("trader", "trader", "story_trader"),
            Character("Char_bartender", "li_manhattan_bartender", "story_bartender"),
            Character("Fixture_bartender", "benchmark_male", "BARTENDER"),
            Character("Char_equipment_dealer", "li_manhattan_weaponstrader", "equipment"),
            Character("Fixture_equipment_dealer", "li_manhattan_weaponstrader", "equipment"));

        var bound = BaseNpcPopulation.FindFixtureActors(room, script);

        // The match ignores the letter case. The first DEFORMABLE with the role is bound, and the second one stays
        // free. The trader has no DEFORMABLE with its role.
        Assert.Equal(
            new[] { (bartender, "Fixture_bartender"), (dealer, "Char_equipment_dealer") },
            bound.Select(x => (x.Npc, x.Entity.Name)));
    }

    [Fact]
    public void AnRtcCharWithASpotBindsTheFixtureAtThatSpotFirst()
    {
        var first = Fixture("first", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var second = Fixture("second", "Zs/NPC/Bartender/02/A/Stand", "bartender");
        var room = Room(0, first, second);
        var script = Characters(Character("Char_li_bartender_Fixture", "li_bartender_1", "bartender"));
        // An actor and a spot without an npc, as in the s003x RTC INI. The letter case is different.
        var key = new StoryChar { Actor = "Bartender", Spot = "zs/npc/bartender/02/a/stand" };
        // A [Char] with an npc is a story character. Its spot does not bind a fixture.
        var story = new StoryChar { Npc = "manhtn_bar", Actor = "bartender", Spot = "Zs/NPC/Bartender/02/A/Stand" };

        Assert.Same(second, Assert.Single(BaseNpcPopulation.FindFixtureActors(room, script, [key])).Npc);
        Assert.Same(first, Assert.Single(BaseNpcPopulation.FindFixtureActors(room, script, [story])).Npc);
        Assert.Same(first, Assert.Single(BaseNpcPopulation.FindFixtureActors(room, script)).Npc);
    }

    [Fact]
    public void AFixtureIsBoundOneTimeAtMost()
    {
        var bartender = Fixture("bartender", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var room = Room(0, bartender);
        var script = Characters(
            Character("Char_li_bartender_Fixture", "li_bartender_1", "bartender"),
            Character("Fixture_bartender", "li_bartender_1", "bartender"));
        var key = new StoryChar { Actor = "bartender", Spot = "Zs/NPC/Bartender/01/A/Stand" };

        // The [Char] key binds the first DEFORMABLE. The role match then does not bind the same fixture to the
        // second DEFORMABLE, so the second one stays free.
        var bound = Assert.Single(BaseNpcPopulation.FindFixtureActors(room, script, [key]));
        Assert.Equal((bartender, "Char_li_bartender_Fixture"), (bound.Npc, bound.Entity.Name));
    }

    [Fact]
    public void ABoundFixtureIsNotPlacedAtItsSpot()
    {
        var bartender = Fixture("bartender", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var trader = Fixture("trader", "Zs/NPC/Trader/01/A/Stand", "trader");
        var room = Room(1, bartender, trader, new BaseNpc("ambient"));
        var spots = new[]
        {
            new RoomNpcSpot("Zs/NPC/Bartender/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zs/NPC/Trader/01/A/Stand", false, Posture.Stand),
            new RoomNpcSpot("Zg/NPC/mFloor2/01/A/Stand", true, Posture.Stand)
        };

        var result = BaseNpcPopulation.Select(room, spots, (_, _) => null, new Random(0),
            boundNpcs: new HashSet<BaseNpc> { bartender });

        // The bartender plays a character of the cutscene, and nobody stands at his spot. The trader is not bound,
        // so he stays at his spot.
        Assert.DoesNotContain(result, x => x.Npc == bartender || x.SceneSpot == "Zs/NPC/Bartender/01/A/Stand");
        Assert.Contains(result, x => x.Fixed && x.Npc == trader);
    }

    [Fact]
    public void AmbientNpcsOnSpotsFindsAmbientNpcsButNotFixtures()
    {
        var bartender = Fixture("bartender", "Zs/NPC/Bartender/01/A/Stand", "bartender");
        var onReserve = new BaseNpc("on_reserve");
        BaseNpcAssignment[] assignments =
        [
            new(bartender, new RoomNpcSpot("Zs/NPC/Bartender/01/A/Stand", false, Posture.Stand), null, true, null),
            new(onReserve, new RoomNpcSpot("Zg/NPC/mTable3/01/A/SitLow", true, Posture.SitLow), null, false, null),
            new(new BaseNpc("free"), new RoomNpcSpot("Zg/NPC/mFloor2/01/A/Stand", true, Posture.Stand), null, false,
                null)
        ];
        // The first spot has a different letter case than its marker. The fixture keeps its spot, also when an RTC
        // reserves it.
        string[] reserved = ["Zg/NPC/mTable3/01/A/Sitlow", "Zs/NPC/Bartender/01/A/Stand", "Zg/NPC/mFloor3/01/A/Stand"];

        var found = BaseNpcPopulation.AmbientNpcsOnSpots(assignments, reserved);

        Assert.Same(onReserve, Assert.Single(found).Npc);
    }

    [Fact]
    public void OnlyOneSpawnedNpcCanOfferABribeForEachFaction()
    {
        var liberty = MakeFaction("li_police");
        var corsairs = MakeFaction("corsairs");
        var first = Briber("first", liberty);
        var second = Briber("second", liberty);
        var third = Briber("third", corsairs);
        var assignments = FixedAssignments(first, second, third);

        var selected = BaseNpcPopulation.SelectBribes(
            assignments,
            new ReputationCollection(),
            new Random(42));

        Assert.Equal(2, selected.Count);
        Assert.Single(selected.Values.Where(x => x.Faction == liberty));
        Assert.Single(selected.Values.Where(x => x.Faction == corsairs));
    }

    [Fact]
    public void AlliedFactionBribesAreNotSelected()
    {
        var liberty = MakeFaction("li_police");
        var corsairs = MakeFaction("corsairs");
        var assignments = FixedAssignments(
            Briber("police_contact", liberty),
            Briber("corsair_contact", corsairs));
        var reputations = new ReputationCollection();
        reputations.Reputations[liberty] = Faction.FriendlyThreshold;

        var selected = BaseNpcPopulation.SelectBribes(assignments, reputations, new Random(42));

        Assert.DoesNotContain(selected.Values, x => x.Faction == liberty);
        Assert.Single(selected.Values, x => x.Faction == corsairs);
    }

    [Theory]
    [InlineData("trader", "trader")]
    [InlineData("Equipment", "Equipment")]
    [InlineData("ShipDealer", "ShipDealer")]
    [InlineData("bartender", null)]
    [InlineData(null, null)]
    public void ServiceActionsDispatchOnlyConfiguredDealerActions(string? configured, string? expected)
    {
        var npc = new BaseNpc("npc");
        if (configured != null)
            npc.Placement = new BaseNpcPlacement("spot", Script("fixed"), configured);

        Assert.Equal(expected, BaseNpcPopulation.GetServiceAction(npc));
    }

    [Theory]
    [InlineData("trader", false, "talk_commodity_dealer")]
    [InlineData("Equipment", false, "talk_equip_dealer")]
    [InlineData("ShipDealer", false, "talk_ship_dealer")]
    [InlineData(null, true, "talk_mission")]
    [InlineData(null, false, "talk_blowoff")]
    public void InteractionCursorMatchesConfiguredService(string? action, bool hasMissionOffer, string expected)
    {
        var npc = new BaseNpc("npc");
        if (action != null)
            npc.Placement = new BaseNpcPlacement("spot", Script("fixed"), action);
        if (hasMissionOffer)
            npc.Mission = new NpcMission("DestroyMission", 0, 1);

        Assert.Equal(expected, BaseNpcPopulation.GetInteractionCursor(npc, hasMissionOffer));
    }

    [Fact]
    public void InteractionCursorPrioritizesBribesOverKnowledgeAndRumors()
    {
        var npc = new BaseNpc("npc")
        {
            Know = { new NpcKnow(1, 2, 0, 0) },
            Rumors = { new BaseNpcRumor { Ids = 3 } },
            Bribes =
            {
                new BaseNpcBribe
                {
                    Faction = new Faction { Nickname = "faction", Properties = null },
                    Ids = 4
                }
            }
        };

        Assert.Equal("talk_bribe", BaseNpcPopulation.GetInteractionCursor(npc, false));
    }

    [Fact]
    public void InteractionCursorPrioritizesKnowledgeOverSelectedRumors()
    {
        var npc = new BaseNpc("npc")
        {
            Know = { new NpcKnow(1, 2, 100, 0) },
            Rumors = { new BaseNpcRumor { Ids = 3 } }
        };

        Assert.Equal(
            "talk_info",
            BaseNpcPopulation.GetInteractionCursor(npc, false, npc.Rumors[0]));
    }

    [Fact]
    public void SelectRandomRumorReturnsOnlyRumorsFromTheNpc()
    {
        var expected = new BaseNpcRumor { Ids = 101 };
        var npc = new BaseNpc("npc")
        {
            Rumors = { new BaseNpcRumor { Ids = 0 }, expected, new BaseNpcRumor { Ids = 202 } }
        };

        var selected = BaseNpcPopulation.SelectRandomRumor(npc, new Random(42));

        Assert.NotNull(selected);
        Assert.Contains(selected, npc.Rumors);
        Assert.NotEqual(0, selected!.Ids);
    }

    [Fact]
    public void RoomPopulationSelectsOneRumorForEachNpcWhenItLoads()
    {
        var npc = new BaseNpc("npc")
        {
            Placement = new BaseNpcPlacement("spot", Script("fixed"), "bartender"),
            Rumors = { new BaseNpcRumor { Ids = 101 }, new BaseNpcRumor { Ids = 202 } }
        };
        var spots = new[] { new RoomNpcSpot("spot", false, Posture.Stand) };

        var assignment = Assert.Single(
            BaseNpcPopulation.Select(Room(0, npc), spots, (_, _) => null, new Random(42)));

        Assert.NotNull(assignment.SelectedRumor);
        Assert.Contains(assignment.SelectedRumor, npc.Rumors);
    }

    [Fact]
    public void RumorCursorUsesTheRumorSelectedForTheLoadedNpc()
    {
        var npc = new BaseNpc("npc")
        {
            Rumors = { new BaseNpcRumor { Ids = 101 } }
        };

        Assert.Equal(
            "talk_rumor",
            BaseNpcPopulation.GetInteractionCursor(npc, false, npc.Rumors[0]));
        Assert.Equal(
            "talk_blowoff",
            BaseNpcPopulation.GetInteractionCursor(npc, false, null));
    }

    [Fact]
    public void LegacySaveWithoutNpcStateStillLoadsAndInteractionStateRoundTrips()
    {
        var legacy = SaveGame.FromString("legacy", "[player]\n");
        Assert.Null(legacy.MPlayer);

        var save = SaveGame.FromString(
            "npc-state", "[mplayer]\nrumor = 123, 1\nvnpc = 456, 789, 2, 0\n");

        Assert.NotNull(save.MPlayer);
        Assert.Single(save.MPlayer!.Rumors);
        Assert.Single(save.MPlayer.VNPCs);
        Assert.Equal(new HashValue(123), save.MPlayer.Rumors[0].Item);
        Assert.Equal(new HashValue(456), save.MPlayer.VNPCs[0].ItemA);
    }

    private static BaseRoom Room(int density, params BaseNpc[] npcs) => new()
    {
        SourceFile = "test.ini",
        MaxCharacters = density,
        Npcs = npcs.ToList()
    };

    private static ResolvedThn Script(string name) => new()
    {
        DataPath = name,
        SourcePath = name,
        ReadCallback = null!,
        VFS = null!
    };

    private static BaseNpc Fixture(string nickname, string spot, string role) => new(nickname)
    {
        Placement = new BaseNpcPlacement(spot, Script("fixed"), role)
    };

    private static ThnScript Characters(params ThnEntity[] entities)
    {
        var script = new ThnScript();
        foreach (var entity in entities)
            script.Entities.Add(entity.Name, entity);
        return script;
    }

    private static ThnEntity Character(string name, string template, string actor) =>
        new(new ThornTable { ["entity_name"] = name, ["type"] = EntityTypes.Deformable })
        {
            Template = template,
            Actor = actor
        };

    private static BaseNpcAssignment[] FixedAssignments(params BaseNpc[] npcs)
    {
        return npcs.Select((npc, index) => new BaseNpcAssignment(
            npc,
            new RoomNpcSpot($"spot{index}", false, Posture.Stand),
            null,
            true,
            null)).ToArray();
    }

    private static BaseNpc Briber(string nickname, Faction faction) => new(nickname)
    {
        Bribes = { new BaseNpcBribe { Faction = faction, Ids = 1, Price = 1000 } }
    };

    private static Faction MakeFaction(string nickname) => new()
    {
        Nickname = nickname,
        Properties = null
    };
}
