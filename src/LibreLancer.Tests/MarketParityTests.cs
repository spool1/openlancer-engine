using System.Linq;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.Items;
using LibreLancer.Data.GameData.Market;
using LibreLancer.Data.GameData.World;
using LibreLancer.Data.Schema.Goods;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The market of a base keeps the last line of each good, as retail does (OL-0080). Before, the engine added each line
// of a good as one more entry, added a ship only when a line sold it and never removed the ship. So a later line could
// not change or remove an earlier line. The trade-in value of a ship comes from the market price of its hull good at the
// base, as retail gives it. Before, the engine did not read a market line of a hull good. Retail skips a line in which
// each value after the good has its default value (OL-0081). Before, the engine read such a line, so it stopped the
// sale of the good.
public class MarketParityTests
{
    private const string FirstFile = "market_commodities.ini";
    private const string LaterFile = "market_misc.ini";

    private static MarketGood Line(string values) =>
        new(IniText.Section($"[BaseGood]\nbase = sample_base\nMarketGood = {values}").Single(e => e.Name == "MarketGood"));

    private static ResolvedGood Commodity(string nickname, int price) => new()
    {
        Nickname = nickname,
        Equipment = new CommodityEquipment { Nickname = nickname },
        Ini = new Good { Nickname = nickname, Price = price },
    };

    private static readonly Ship Fighter = new() { Nickname = "sample_fighter" };

    private static ShipPackage Package(string nickname) => new(nickname, Fighter);

    private static Base NewBase() => new() { Nickname = "sample_base" };

    [Fact]
    public void ALaterLineChangesTheCommodity()
    {
        var b = NewBase();
        var water = Commodity("sample_water", 100);

        b.AddMarketLine(Line("sample_water, 0, -1, 10, 10, 0, 1"), FirstFile, null, water, null, 0);
        b.AddMarketLine(Line("sample_water, 2, 0.3, 20, 20, 0, 1.5"), LaterFile, null, water, null, 0);

        var sold = Assert.Single(b.SoldGoods);
        Assert.Equal(150UL, sold.Price);
        Assert.Equal(2, sold.Rank);
        Assert.Equal(0.3f, sold.Rep);
        Assert.True(sold.ForSale);
        Assert.Equal(LaterFile, sold.SourceFile);
        Assert.Equal(150UL, b.GetUnitPrice(water.Equipment));
    }

    [Fact]
    public void ALaterBuyOnlyLineStopsTheSaleOfTheCommodity()
    {
        var b = NewBase();
        var water = Commodity("sample_water", 100);
        var food = Commodity("sample_food", 40);

        b.AddMarketLine(Line("sample_water, 0, -1, 10, 10, 0, 1"), FirstFile, null, water, null, 0);
        b.AddMarketLine(Line("sample_food, 0, -1, 10, 10, 0, 1"), FirstFile, null, food, null, 0);
        b.AddMarketLine(Line("sample_water, 0, -1, 0, 0, 1, 0.8"), LaterFile, null, water, null, 0);

        Assert.Equal(["sample_water", "sample_food"], b.SoldGoods.Select(g => g.Good.Nickname));
        var water2 = b.SoldGoods[0];
        Assert.False(water2.ForSale);
        Assert.Equal(80UL, water2.Price);
        Assert.True(b.SoldGoods[1].ForSale);
    }

    [Fact]
    public void ALaterLineWithoutStockStopsTheSaleOfTheShip()
    {
        var b = NewBase();
        var package = Package("sample_fighter_package");
        var other = Package("sample_other_package");

        b.AddMarketLine(Line("sample_fighter_package, 3, -1, 1, 1, 0, 1"), FirstFile, package, null, null, 0);
        b.AddMarketLine(Line("sample_other_package, 0, -1, 1, 1, 0, 1"), FirstFile, other, null, null, 0);
        b.AddMarketLine(Line("sample_fighter_package, 0, -1, 0, 0, 1, 1"), LaterFile, package, null, null, 0);

        var sold = Assert.Single(b.SoldShips);
        Assert.Same(other, sold.Package);
    }

    [Fact]
    public void ALaterLineChangesTheShipAndCanSellItAgain()
    {
        var b = NewBase();
        var package = Package("sample_fighter_package");

        b.AddMarketLine(Line("sample_fighter_package, 3, -1, 1, 1, 0, 1"), FirstFile, package, null, null, 0);
        b.AddMarketLine(Line("sample_fighter_package, 5, -1, 1, 1, 0, 1"), LaterFile, package, null, null, 0);

        var sold = Assert.Single(b.SoldShips);
        Assert.Equal(5, sold.Rank);

        b.AddMarketLine(Line("sample_fighter_package, 0, -1, 0, 0, 1, 1"), LaterFile, package, null, null, 0);
        Assert.Empty(b.SoldShips);
        b.AddMarketLine(Line("sample_fighter_package, 7, -1, 1, 1, 0, 1"), LaterFile, package, null, null, 0);
        Assert.Equal(7, Assert.Single(b.SoldShips).Rank);
    }

    [Fact]
    public void ABuyOnlyLineOfAShipThatTheBaseDoesNotSellAddsNoShip()
    {
        var b = NewBase();

        b.AddMarketLine(Line("sample_fighter_package, 0, -1, 0, 0, 1, 1"), FirstFile, Package("sample_fighter_package"),
            null, null, 0);

        Assert.Empty(b.SoldShips);
    }

    [Fact]
    public void ALineOfTheHullGoodGivesTheTradeInValueOfTheShip()
    {
        var b = NewBase();

        b.AddMarketLine(Line("sample_fighter_hull, 0, -1, 0, 0, 1, 2"), FirstFile, null, null, Fighter, 6000);
        b.AddMarketLine(Line("sample_fighter_hull, 0, -1, 0, 0, 1, 0.5"), LaterFile, null, null, Fighter, 6000);

        Assert.Equal(3000, b.GetShipPrice(Fighter, 6000));
        Assert.Equal(2400UL, TradeConstants.ShipResale(b.GetShipPrice(Fighter, 6000)));
        Assert.Empty(b.SoldGoods);
        Assert.Empty(b.SoldShips);
    }

    [Fact]
    public void WithoutALineOfTheHullGoodTheTradeInValueComesFromThePriceOfTheHull()
    {
        var b = NewBase();

        // A line of the ship package does not change the price of the ship. Retail reads the hull good.
        b.AddMarketLine(Line("sample_fighter_package, 0, -1, 0, 0, 1, 2"), FirstFile, Package("sample_fighter_package"),
            null, null, 0);

        Assert.Equal(6000, b.GetShipPrice(Fighter, 6000));
        Assert.Equal(4800UL, TradeConstants.ShipResale(b.GetShipPrice(Fighter, 6000)));
    }

    [Fact]
    public void ALaterLineWithOnlyDefaultValuesKeepsTheEarlierLineOfTheCommodity()
    {
        var b = NewBase();
        var water = Commodity("sample_water", 100);
        var food = Commodity("sample_food", 40);

        b.AddMarketLine(Line("sample_water, 0, -1, 150, 500, 0, 1.2"), FirstFile, null, water, null, 0);
        b.AddMarketLine(Line("sample_water, 0, -1, 0, 0, 0, 1"), LaterFile, null, water, null, 0);
        b.AddMarketLine(Line("sample_food, 0, -1, 0, 0, 0, 1"), LaterFile, null, food, null, 0);

        var sold = Assert.Single(b.SoldGoods);
        Assert.Same(water, sold.Good);
        Assert.True(sold.ForSale);
        Assert.Equal(120UL, sold.Price);
        Assert.Equal(FirstFile, sold.SourceFile);
    }

    [Fact]
    public void ALaterLineWithOnlyDefaultValuesKeepsTheShipForSale()
    {
        var b = NewBase();
        var package = Package("sample_fighter_package");

        b.AddMarketLine(Line("sample_fighter_package, 3, -1, 1, 1, 0, 1"), FirstFile, package, null, null, 0);
        b.AddMarketLine(Line("sample_fighter_package, 0, -1, 0, 0, 0, 1"), LaterFile, package, null, null, 0);

        Assert.Equal(3, Assert.Single(b.SoldShips).Rank);
    }

    [Fact]
    public void ALaterLineOfTheHullGoodWithOnlyDefaultValuesKeepsThePriceOfTheShip()
    {
        var b = NewBase();

        b.AddMarketLine(Line("sample_fighter_hull, 0, -1, 0, 0, 1, 0.5"), FirstFile, null, null, Fighter, 6000);
        b.AddMarketLine(Line("sample_fighter_hull, 0, -1, 0, 0, 0, 1"), LaterFile, null, null, Fighter, 6000);

        Assert.Equal(3000, b.GetShipPrice(Fighter, 6000));
    }
}
