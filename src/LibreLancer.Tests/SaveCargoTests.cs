using LibreLancer.Data;
using LibreLancer.Data.Ini;
using LibreLancer.Data.Schema.Save;
using Xunit;

namespace LibreLancer.Tests;

// The cargo lines of a save (OL-0043). Retail writes "cargo = item, count, hardpoint, health, mission" and leaves a
// value empty for its default. The engine read the health from value 2 (the hardpoint) and wrote it there. So an empty
// hardpoint gave the error "Failed to parse float ''" and the health 0, for example in the cargo of a new character.
[Collection(nameof(LogCapture))]
public class SaveCargoTests
{
    private static SavePlayer Parse(string cargo)
    {
        Assert.True(SavePlayer.TryParse(IniText.Section($"[Player]\n{cargo}"), out var player));
        return player;
    }

    [Fact]
    public void EmptyValuesKeepTheFullHealthAndNoMissionFlag()
    {
        using var log = new LogCapture();
        var cargo = Assert.Single(Parse("cargo = sample_good, 5, , , 0").Cargo);

        Assert.Equal((5, 1f, false), (cargo.Count, cargo.PercentageHealth, cargo.IsMissionCargo));
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void TheHealthIsTheFourthValueAndTheMissionFlagTheFifth()
    {
        var cargo = Assert.Single(Parse("cargo = sample_good, 2, , 0.5, 1").Cargo);

        Assert.Equal((2, 0.5f, true), (cargo.Count, cargo.PercentageHealth, cargo.IsMissionCargo));
    }

    // GameServer writes the cargo of a new character with PlayerCargo.ToString and reads the text again.
    [Fact]
    public void TheTextOfACargoLineReadsAgain()
    {
        var line = new PlayerCargo { Item = new HashValue("sample_good"), Count = 3, PercentageHealth = 0.25f }.ToString();
        var cargo = Assert.Single(Parse(line).Cargo);

        Assert.Equal((3, 0.25f, false), (cargo.Count, cargo.PercentageHealth, cargo.IsMissionCargo));
    }

    [Fact]
    public void ASavedCargoLineReadsAgain()
    {
        var player = new SavePlayer();
        player.Cargo.Add(new PlayerCargo { Item = new HashValue("sample_good"), Count = 4, IsMissionCargo = true });
        player.Cargo.Add(new PlayerCargo { Item = new HashValue("other_good"), Count = 1, PercentageHealth = 0.75f });
        var builder = new IniBuilder();
        player.WriteTo(builder);

        Assert.True(SavePlayer.TryParse(Assert.Single(builder.Sections), out var copy));
        Assert.Equal((4, 1f, true), (copy.Cargo[0].Count, copy.Cargo[0].PercentageHealth, copy.Cargo[0].IsMissionCargo));
        Assert.Equal((1, 0.75f, false), (copy.Cargo[1].Count, copy.Cargo[1].PercentageHealth, copy.Cargo[1].IsMissionCargo));
    }
}
