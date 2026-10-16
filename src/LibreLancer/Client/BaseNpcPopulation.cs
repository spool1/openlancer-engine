using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.GCS;
using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Thn;
using LibreLancer.Utf.Dfm;
using LibreLancer.World;

namespace LibreLancer.Client;

public readonly record struct BaseNpcAppearance(
    Bodypart? Body,
    Bodypart? Head,
    Bodypart? LeftHand,
    Bodypart? RightHand,
    Accessory? Accessory)
{
    public bool HasBody => Body != null;
}

public readonly record struct BaseNpcAssignment(
    BaseNpc Npc,
    RoomNpcSpot Spot,
    ResolvedThn? FidgetScript,
    bool Fixed,
    BaseNpcRumor? SelectedRumor)
{
    public string SceneSpot => Spot.Nickname;
}

public static class BaseNpcPopulation
{
    public static string? GetServiceAction(BaseNpc npc)
    {
        var action = npc.Placement?.Action;
        if (action?.Equals("trader", StringComparison.OrdinalIgnoreCase) == true)
            return "trader";
        if (action?.Equals("Equipment", StringComparison.OrdinalIgnoreCase) == true)
            return "Equipment";
        if (action?.Equals("ShipDealer", StringComparison.OrdinalIgnoreCase) == true)
            return "ShipDealer";
        return null;
    }

    public static string GetInteractionCursor(BaseNpc npc, bool hasMissionOffer)
    {
        return GetInteractionCursor(
            npc,
            hasMissionOffer,
            npc.Rumors.FirstOrDefault(x => x.Ids != 0),
            GetBribe(npc));
    }

    public static BaseNpcBribe? GetBribe(BaseNpc npc)
    {
        return npc.Bribes.FirstOrDefault(x => x.Faction != null && x.Ids != 0);
    }

    public static string GetInteractionCursor(
        BaseNpc npc,
        bool hasMissionOffer,
        BaseNpcRumor? selectedRumor)
    {
        return GetInteractionCursor(
            npc,
            hasMissionOffer,
            selectedRumor,
            GetBribe(npc));
    }

    public static string GetInteractionCursor(
        BaseNpc npc,
        bool hasMissionOffer,
        BaseNpcRumor? selectedRumor,
        BaseNpcBribe? selectedBribe)
    {
        return GetInteractionCursor(
            npc,
            hasMissionOffer,
            selectedRumor,
            selectedBribe,
            float.MaxValue);
    }

    public static string GetInteractionCursor(
        BaseNpc npc,
        bool hasMissionOffer,
        BaseNpcRumor? selectedRumor,
        BaseNpcBribe? selectedBribe,
        float reputation)
    {
        switch (GetServiceAction(npc))
        {
            case "trader":
                return "talk_commodity_dealer";
            case "Equipment":
                return "talk_equip_dealer";
            case "ShipDealer":
                return "talk_ship_dealer";
        }

        if (hasMissionOffer && npc.Mission != null)
            return "talk_mission";
        if (selectedBribe != null)
            return "talk_bribe";
        if (npc.Know.Any(x =>
                x.Ids1 != 0 &&
                x.Ids2 != 0 &&
                reputation >= x.RepThreshold))
            return "talk_info";
        if (selectedRumor != null)
            return "talk_rumor";

        return "talk_blowoff";
    }

    public static Dictionary<string, BaseNpcBribe> SelectBribes(
        IReadOnlyList<BaseNpcAssignment> assignments,
        ReputationCollection playerReputations,
        Random random)
    {
        var candidates = new List<(BaseNpc Npc, BaseNpcBribe Bribe)>();
        foreach (var assignment in assignments)
        {
            var bribe = assignment.Npc.Bribes.FirstOrDefault(x =>
                BaseNpcRules.IsBribeAvailable(x, playerReputations));
            if (bribe != null)
                candidates.Add((assignment.Npc, bribe));
        }

        Shuffle(candidates, random);

        var selected = new Dictionary<string, BaseNpcBribe>(StringComparer.OrdinalIgnoreCase);
        var factions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var faction = candidate.Bribe.Faction!;
            if (!factions.Add(faction.Nickname))
                continue;

            selected[candidate.Npc.Nickname] = candidate.Bribe;
        }

        return selected;
    }

    public static BaseNpcRumor? SelectRandomRumor(BaseNpc npc, Random random)
    {
        var rumors = npc.Rumors
            .Where(x => x.Ids != 0)
            .ToArray();
        return rumors.Length == 0 ? null : rumors[random.Next(rumors.Length)];
    }

    public static BaseNpcAppearance ResolveAppearance(BaseNpc npc)
    {
        return new BaseNpcAppearance(
            npc.Body ?? npc.BaseAppr?.Body,
            npc.Head ?? npc.BaseAppr?.Head,
            npc.LeftHand ?? npc.BaseAppr?.LeftHand,
            npc.RightHand ?? npc.BaseAppr?.RightHand,
            npc.Accessory ?? npc.BaseAppr?.Accessory);
    }

    public static BaseNpcAssignment[] Select(
        BaseRoom room,
        IReadOnlyList<RoomNpcSpot> spots,
        GameItemDb items,
        Random random,
        IEnumerable<string>? reservedSpots = null,
        IReadOnlySet<BaseNpc>? boundNpcs = null)
    {
        return Select(
            room,
            spots,
            (gender, posture) => items.GetGCSScripts("fidget", gender, posture).FirstOrDefault(),
            random,
            reservedSpots,
            boundNpcs);
    }

    public static BaseNpcAssignment[] Select(
        BaseRoom room,
        IReadOnlyList<RoomNpcSpot> spots,
        Func<FLGender, Posture, ResolvedThn?> fidgetResolver,
        Random random,
        IEnumerable<string>? reservedSpots = null,
        IReadOnlySet<BaseNpc>? boundNpcs = null)
    {
        var assignments = new List<BaseNpcAssignment>();
        var fixedNpcs = new Dictionary<string, BaseNpc>(StringComparer.OrdinalIgnoreCase);
        var usedSpots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var npc in room.Npcs)
        {
            if (npc.Placement == null)
                continue;

            fixedNpcs[npc.Placement.Spot] = npc;
            usedSpots.Add(npc.Placement.Spot);
        }

        foreach (var spot in spots)
        {
            if (!fixedNpcs.TryGetValue(spot.Nickname, out var npc) || npc.Placement == null)
                continue;

            // A fixture that plays a character of the cutscene (see FindFixtureActors) is not at its spot.
            if (boundNpcs?.Contains(npc) == true)
                continue;

            assignments.Add(new BaseNpcAssignment(
                npc,
                spot,
                npc.Placement.FidgetScript,
                true,
                SelectRandomRumor(npc, random)));
        }

        // Ambient NPCs do not get the spots that the RTCs of the room reserve ([Reserve] in the RTC INI).
        // The comparison ignores the letter case, because retail writes both "Sitlow" and "SitLow".
        if (reservedSpots != null)
            usedSpots.UnionWith(reservedSpots);

        var dynamicSpots = spots
            .Where(x => x.Dynamic && !usedSpots.Contains(x.Nickname))
            .ToArray();
        var ambientNpcs = room.Npcs
            .Where(x => x.Placement == null)
            .ToArray();

        Shuffle(dynamicSpots, random);
        Shuffle(ambientNpcs, random);

        var maxCount = Math.Min(Math.Max(room.MaxCharacters, 0),
            Math.Min(dynamicSpots.Length, ambientNpcs.Length));
        var count = maxCount == 0 ? 0 : random.Next(maxCount + 1);

        for (var i = 0; i < count; i++)
        {
            var npc = ambientNpcs[i];
            var spot = dynamicSpots[i];
            var appearance = ResolveAppearance(npc);
            var gender = appearance.Body?.Sex ?? FLGender.male;
            var fidget = fidgetResolver(gender, spot.Posture);

            assignments.Add(new BaseNpcAssignment(
                npc,
                spot,
                fidget,
                false,
                SelectRandomRumor(npc, random)));
        }

        return assignments.ToArray();
    }

    // Finds the ambient NPCs that stand on one of the spots. A fixture keeps its spot, as in Select. The comparison
    // ignores the letter case, as in Select.
    public static BaseNpcAssignment[] AmbientNpcsOnSpots(
        IEnumerable<BaseNpcAssignment> assignments,
        IEnumerable<string> spots)
    {
        var spotSet = new HashSet<string>(spots, StringComparer.OrdinalIgnoreCase);
        return assignments.Where(x => !x.Fixed && spotSet.Contains(x.SceneSpot)).ToArray();
    }

    // Finds the room fixtures that play characters of a cutscene. The rule comes from the retail data. The retail
    // binary does not confirm it. A DEFORMABLE whose actor is the role of a fixture (the 4th field of the mbases
    // fixture: bartender, trader, Equipment or ShipDealer) is that room NPC. The data shows this in three ways: some
    // of these DEFORMABLEs have a placeholder template (benchmark_male in s037a), many are offscreen from the start,
    // and s003x and s009b move theirs back to the stand spot of the room fixture at the end. First, a [Char] of the
    // RTC INI with an actor and a spot, but no npc, binds the fixture at that spot. Then each fixture binds the first
    // free DEFORMABLE with its role. An NPC or an entity is bound one time at most. The comparisons ignore the letter
    // case.
    public static IReadOnlyList<(BaseNpc Npc, ThnEntity Entity)> FindFixtureActors(
        BaseRoom room,
        ThnScript script,
        IEnumerable<StoryChar>? chars = null)
    {
        var result = new List<(BaseNpc Npc, ThnEntity Entity)>();
        var free = script.Entities.Values.Where(x => x.Type == EntityTypes.Deformable).ToList();

        void Bind(BaseNpc? npc, string? actor)
        {
            if (npc == null || string.IsNullOrEmpty(actor) || result.Exists(x => x.Npc == npc))
                return;
            var entity = free.Find(x => string.Equals(x.Actor, actor, StringComparison.OrdinalIgnoreCase));
            if (entity == null)
                return;
            free.Remove(entity);
            result.Add((npc, entity));
        }

        foreach (var character in chars ?? [])
        {
            if (string.IsNullOrEmpty(character.Npc) && !string.IsNullOrEmpty(character.Spot))
                Bind(room.Npcs.Find(x => string.Equals(x.Placement?.Spot, character.Spot,
                    StringComparison.OrdinalIgnoreCase)), character.Actor);
        }

        foreach (var npc in room.Npcs)
            Bind(npc, npc.Placement?.Action);

        return result;
    }

    private static void Shuffle<T>(T[] values, Random random)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static void Shuffle<T>(List<T> values, Random random)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
