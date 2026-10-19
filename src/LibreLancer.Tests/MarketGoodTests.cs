using System.Linq;
using LibreLancer.Data.Schema.Goods;
using Xunit;

namespace LibreLancer.Tests;

// The transaction type of a market line (OL-0045). Retail reads the sixth value of a MarketGood line as 0 (the base
// buys and sells the good), 1 (the base only buys it) or 2 (the base only sells it). The engine kept only the flag
// Preserve = value > 0, so the types 1 and 2 looked the same.
public class MarketGoodTests
{
    private static MarketGood Parse(string line) =>
        new(IniText.Section($"[BaseGood]\nbase = sample_base\n{line}").Single(e => e.Name == "MarketGood"));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void TheMarketLineKeepsTheTransactionType(int type, bool preserve)
    {
        var good = Parse($"MarketGood = sample_good, 0, -1, 0, 0, {type}, 1");

        Assert.Equal(type, good.TransactionType);
        Assert.Equal(preserve, good.Preserve);
    }
}
