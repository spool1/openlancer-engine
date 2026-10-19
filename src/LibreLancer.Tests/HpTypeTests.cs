using System.Collections.Generic;
using System.Linq;
using LibreLancer.Data.GameData;
using LibreLancer.Data.Schema.Equipment;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The generic hardpoint types of retail (OL-0041). The retail table has 63 names. The engine did not know the five
// generic names, so it logged "Unrecognised hp_type" for each ship line with one of them and skipped the line.
public class HpTypeTests
{
    private static HpTypesIni Defaults()
    {
        var types = new HpTypesIni();
        types.LoadDefault();
        return types;
    }

    [Fact]
    public void TheDefaultTableHasTheSixtyThreeRetailNames()
    {
        Assert.Equal(63, Defaults().Types.Count);
    }

    [Theory]
    [InlineData("hp_gun", HpCategory.Weapon)]
    [InlineData("hp_turret", HpCategory.Weapon)]
    [InlineData("hp_torpedo", HpCategory.Weapon)]
    [InlineData("hp_shield_generator", HpCategory.External)]
    [InlineData("hp_cargo_pod", HpCategory.External)]
    public void AGenericTypeHasClassZeroAndNoNameText(string name, HpCategory category)
    {
        Assert.True(Defaults().Types.TryGetValue(name, out var type));

        Assert.Equal((category, 0, 0, 0), (type.Category, type.Class, type.IdsName, type.IdsHpDescription));
    }

    // The generic types come after the other types, so the sort order of the other types does not change.
    [Fact]
    public void TheGenericTypesComeLast()
    {
        var types = Defaults().Types.Values.OrderBy(x => x.SortIndex).Select(x => x.Type).ToList();

        Assert.Equal("hp_countermeasure_dropper", types[57]);
        Assert.Equal(["hp_gun", "hp_turret", "hp_torpedo", "hp_shield_generator", "hp_cargo_pod"], types[58..]);
    }

    // A ship with a generic line, with the hardpoint lists that GameItemDb makes from the line.
    private static Ship ShipWith(HpTypesIni types, string type, string hardpoint) => new()
    {
        Nickname = "sample_ship",
        HardpointTypes = new() { [hardpoint] = [types.Types[type]] },
        PossibleHardpoints = new() { [type] = [hardpoint] },
    };

    [Fact]
    public void EquipmentOfAGenericTypeMountsOnItsHardpoint()
    {
        var types = Defaults();
        var ship = ShipWith(types, "hp_turret", "HpTurret01");

        Assert.Equal(["HpTurret01"], CargoUtilities.CompatibleHardpoints(ship, types, "hp_turret"));
    }

    [Fact]
    public void EquipmentOfAClassTypeDoesNotMountOnAGenericHardpoint()
    {
        var types = Defaults();
        var ship = ShipWith(types, "hp_turret", "HpTurret01");

        Assert.Empty(CargoUtilities.CompatibleHardpoints(ship, types, "hp_turret_special_1"));
    }
}
