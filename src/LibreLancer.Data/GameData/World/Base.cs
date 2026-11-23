// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data.GameData.Items;
using LibreLancer.Data.GameData.Market;
using LibreLancer.Data.Schema.Goods;

namespace LibreLancer.Data.GameData.World;

public class Base : NamedItem
{
    //Populated from universe
    public string? System = null;
    public string? TerrainTiny;
    public string? TerrainSml;
    public string? TerrainMdm;
    public string? TerrainLrg;
    public string? TerrainDyna1;
    public string? TerrainDyna2;
    public string? BaseRunBy = null!;
    public bool AutosaveForbidden;

    //Populated from base ini
    public string SourceFile = null!;
    public BaseRoom StartRoom = null!;
    public GameItemCollection<BaseRoom> Rooms = [];

    //Populated from markets
    public List<SoldShip> SoldShips = [];
    public List<BaseSoldGood> SoldGoods = [];

    // The market price of a ship at the base, by the nickname of the ship. It comes from a market line of the hull good
    // of the ship (OL-0080).
    public Dictionary<string, ulong> ShipPrices = new(StringComparer.OrdinalIgnoreCase);

    //Populated from mbases
    public Faction? LocalFaction;
    public int Diff;
    public string? MsgIdPrefix;
    public int MinMissionOffers;
    public int MaxMissionOffers; //not respected by vanilla (?)
    public List<MBaseBaseFaction> BaseFactions = [];

    public ulong GetUnitPrice(Items.Equipment eq)
    {
        var g = SoldGoods.FirstOrDefault(x => x.Good.Equipment.Nickname.Equals(eq.Nickname, StringComparison.OrdinalIgnoreCase));
        return g.Good is null
            ? (ulong) (eq.Good?.Ini?.Price ?? 0)
            : g.Price;
    }

    /// <summary>
    /// The market price of a ship at the base (OL-0080). Retail reads the price of the hull good of the ship in the
    /// market of the base. A market line of the hull good gives the price of the good times the multiplier of the line.
    /// Without such a line, the price is the price of the hull good. The retail ship dealer shows this price for the
    /// hull, and the trade-in value of the ship of the player comes from it.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="hullPrice">The price of the hull good of the ship.</param>
    public long GetShipPrice(Ship ship, long hullPrice) =>
        ShipPrices.TryGetValue(ship.Nickname, out var price) ? (long) price : hullPrice;

    /// <summary>
    /// Adds one <c>marketgood</c> line to the market of the base (OL-0080). Retail keeps one entry for each good at a
    /// base, so a later line of a good replaces the earlier line. The lines come in the order of the market files of
    /// <c>[Data]</c>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Retail skips a line in which each value after the good has its default value (see
    /// <see cref="MarketGood.HasOnlyDefaults"/>, OL-0081). The earlier line of the good then counts.</item>
    /// <item>A line of a ship package sells the ship when its minimum stock or its maximum stock is not 0. A later line
    /// with the minimum stock 0 and the maximum stock 0 stops the sale of the ship.</item>
    /// <item>A line of a hull good sets the market price of its ship at the base (see <see cref="GetShipPrice"/>).</item>
    /// <item>A line of another good sets the price, the rank, the reputation and the sale of the good.</item>
    /// </list>
    /// </remarks>
    /// <param name="line">The line.</param>
    /// <param name="sourceFile">The market file of the line.</param>
    /// <param name="package">The ship package that the line names, or null.</param>
    /// <param name="good">The good that the line names, or null.</param>
    /// <param name="hull">The ship of the hull good that the line names, or null.</param>
    /// <param name="hullPrice">The price of that hull good.</param>
    public void AddMarketLine(MarketGood line, string sourceFile, ShipPackage? package, ResolvedGood? good, Ship? hull,
        long hullPrice)
    {
        if (line.HasOnlyDefaults)
            return;
        if (package != null)
        {
            var index = SoldShips.FindIndex(x => x.Package == package);
            if (line.Min == 0 && line.Max == 0)
            {
                if (index >= 0)
                    SoldShips.RemoveAt(index);
                return;
            }
            var sold = new SoldShip { Package = package, Rank = line.Rank };
            if (index >= 0)
                SoldShips[index] = sold;
            else
                SoldShips.Add(sold);
        }
        else if (good != null)
        {
            var entry = new BaseSoldGood(line.Rank, good, line.Rep, (ulong) ((double) good.Ini.Price * line.Multiplier),
                line.Max > 0, sourceFile);
            var index = SoldGoods.FindIndex(x => x.Good == good);
            if (index >= 0)
                SoldGoods[index] = entry;
            else
                SoldGoods.Add(entry);
        }
        else if (hull != null)
        {
            ShipPrices[hull.Nickname] = (ulong) ((double) hullPrice * line.Multiplier);
        }
    }
}

public class MBaseBaseFaction
{
    public Faction? Faction;
    public float Weight;
    public List<string> Npcs = [];
    public bool OffersMissions;
    public List<BaseMissionOffer> Missions = [];
}
