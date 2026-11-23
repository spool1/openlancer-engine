using System;
using System.Collections.Generic;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.Goods;
using LibreLancer.Net;
using LibreLancer.Net.Protocol;
using LibreLancer.Net.Protocol.RpcPackets;
using LibreLancer.Server;
using Xunit;

namespace LibreLancer.Tests;

// The price of a ship at a base and the trade-in value through the loaded game data and the player (OL-0080 and
// OL-0081). GameItemDb.InitMarkets gives a market line of a hull good to the base. Player.GetShipTradeInValue reads the
// price of the ship at the base where the player is docked, and the inventory sends that value to the client as the ship
// worth. The net worth keeps the price of the hull good.
public class ShipMarketPriceTests
{
    private const string MarketBase = "sample_market_base";
    private const string PlainBase = "sample_plain_base";

    private const string GoodsText = """
        [Good]
        nickname = sample_fighter_hull
        category = shiphull
        ship = sample_fighter
        price = 6000

        [Good]
        nickname = sample_fighter_package
        category = ship
        hull = sample_fighter_hull
        """;

    // The hull line of the later file has only default values, so retail skips it (OL-0081).
    private static readonly (string File, string Text)[] Markets =
    [
        ("market_ships.ini", """
            [BaseGood]
            base = sample_market_base
            MarketGood = sample_fighter_package, 0, -1, 1, 1, 0, 1, 1
            MarketGood = sample_fighter_hull, 0, -1, 0, 0, 1, 0.5

            [BaseGood]
            base = sample_plain_base
            MarketGood = sample_fighter_package, 0, -1, 1, 1, 0, 1, 1
            """),
        ("market_misc.ini", """
            [BaseGood]
            base = sample_market_base
            MarketGood = sample_fighter_hull, 0, -1, 0, 0, 0, 1
            """),
    ];

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

    private sealed record Loaded(MockData Data, Ship Fighter, Base MarketBase, Base PlainBase);

    // The goods and the markets of the test go through InitGoods and InitMarkets, as LoadData reads them.
    private static Loaded Load()
    {
        var data = new MockData();
        var items = data.GameData.Items;
        var fighter = new Ship { Nickname = "sample_fighter" };
        data.HashAndAdd(fighter, items.Ships);
        var market = new Base { Nickname = MarketBase, System = "sample_system" };
        var plain = new Base { Nickname = PlainBase, System = "sample_system" };
        data.HashAndAdd(market, items.Bases);
        data.HashAndAdd(plain, items.Bases);

        items.Ini.Goods = new GoodsIni();
        foreach (var section in IniText.Sections(GoodsText))
        {
            Assert.True(Good.TryParse(section, out var good));
            items.Ini.Goods.Goods.Add(good);
        }

        items.Ini.Markets = new MarketsIni();
        foreach (var (file, text) in Markets)
        {
            foreach (var section in IniText.Sections(text))
            {
                Assert.True(BaseGood.TryParse(section, out var baseGood));
                baseGood.SourceFile = file;
                items.Ini.Markets.BaseGoods.Add(baseGood);
            }
        }

        items.InitGoods();
        items.InitMarkets();
        return new Loaded(data, fighter, market, plain);
    }

    private static (Player Player, Client Client) Pilot(Loaded loaded)
    {
        var client = new Client();
        var server = new GameServer(loaded.Data.GameData, null!);
        var player = new Player(client, server, Guid.Empty, default);
        var character = new NetCharacter();
        using (var transaction = character.BeginTransaction())
        {
            transaction.UpdateShip(loaded.Fighter);
        }

        player.Character = character;
        return (player, client);
    }

    [Fact]
    public void TheLineOfTheHullGoodSetsThePriceOfTheShipAtItsBase()
    {
        var loaded = Load();
        var items = loaded.Data.GameData.Items;

        Assert.Equal(3000, items.GetShipPrice(loaded.Fighter, loaded.MarketBase));
        Assert.Equal(6000, items.GetShipPrice(loaded.Fighter, loaded.PlainBase));
        Assert.Equal(6000, items.GetShipPrice(loaded.Fighter));
        Assert.Equal("sample_fighter_package", Assert.Single(loaded.MarketBase.SoldShips).Package.Nickname);
        Assert.Single(loaded.PlainBase.SoldShips);
        Assert.Empty(loaded.MarketBase.SoldGoods);
    }

    [Fact]
    public void TheTradeInValueIsTheMarketPriceOfTheShipAtTheBaseOfThePlayer()
    {
        var loaded = Load();
        var (player, _) = Pilot(loaded);

        // In space, the value is the price of the hull good times 0.8.
        Assert.Equal(4800UL, player.GetShipTradeInValue());

        player.Baseside = new BasesidePlayer(player, loaded.MarketBase);
        Assert.Equal(2400UL, player.GetShipTradeInValue());

        player.Baseside = new BasesidePlayer(player, loaded.PlainBase);
        Assert.Equal(4800UL, player.GetShipTradeInValue());
    }

    [Fact]
    public void TheInventoryGivesTheTradeInValueAsTheShipWorth()
    {
        var loaded = Load();
        var (player, client) = Pilot(loaded);
        player.Baseside = new BasesidePlayer(player, loaded.MarketBase);

        player.UpdateCurrentInventory();

        var packet = Assert.IsType<IClientPlayer_UpdateInventoryPacket>(Assert.Single(client.Sent));
        var inventory = packet.diff.Apply(new PlayerInventory());
        Assert.Equal(2400UL, inventory.ShipWorth);
        // The net worth keeps the price of the hull good.
        Assert.Equal(4800UL, inventory.NetWorth);
    }
}
