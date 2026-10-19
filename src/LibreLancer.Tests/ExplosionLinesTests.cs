using System.Numerics;
using Xunit;
using EffectExplosion = LibreLancer.Data.Schema.Effects.Explosion;
using EquipmentExplosion = LibreLancer.Data.Schema.Equipment.Explosion;

namespace LibreLancer.Tests;

// The lifetime and effect lines of an [Explosion] (OL-0042). Retail reads "lifetime = min[, max]", and one value sets
// both. Each effect line adds an effect with an optional strength. The engine needed two lifetime values and kept
// only the last effect line, with the warning "Duplicate of effect".
[Collection(nameof(LogCapture))]
public class ExplosionLinesTests
{
    private const string TwoEffects = """
        [Explosion]
        nickname = sample_explosion
        lifetime = 0
        effect = sample_effect_a
        radius = 10
        effect = sample_effect_b, 0.5
        """;

    private static EffectExplosion Effect(string text)
    {
        Assert.True(EffectExplosion.TryParse(IniText.Section(text), out var explosion));
        return explosion;
    }

    private static EquipmentExplosion Equipment(string text)
    {
        Assert.True(EquipmentExplosion.TryParse(IniText.Section(text), out var explosion));
        return explosion;
    }

    [Fact]
    public void AnEffectExplosionReadsOneLifetimeAndEachEffectWithoutAWarning()
    {
        using var log = new LogCapture();
        var explosion = Effect(TwoEffects);

        Assert.Equal(Vector2.Zero, explosion.Lifetime);
        Assert.Equal([("sample_effect_a", null), ("sample_effect_b", 0.5f)], explosion.Effects);
        Assert.Empty(log.Lines(IniText.File));
    }

    // The runtime uses the last effect line, as before.
    [Fact]
    public void TheLastEffectLineStaysTheEffect()
    {
        var explosion = Effect(TwoEffects);

        Assert.Equal(("sample_effect_b", 0.5f), (explosion.Effect, explosion.EffectSParam));
    }

    [Fact]
    public void AnEquipmentExplosionReadsTheSameLines()
    {
        using var log = new LogCapture();
        var explosion = Equipment(TwoEffects);

        Assert.Equal(Vector2.Zero, explosion.Lifetime);
        Assert.Equal([("sample_effect_a", null), ("sample_effect_b", 0.5f)], explosion.Effects);
        Assert.Equal("sample_effect_b", explosion.Effect);
        Assert.Empty(log.Lines(IniText.File));
    }

    [Theory]
    [InlineData("lifetime = 2", 2, 2)]
    [InlineData("lifetime = 1, 3", 1, 3)]
    public void OneLifetimeValueSetsTheMinimumAndTheMaximum(string line, float min, float max)
    {
        var text = $"[Explosion]\nnickname = sample_explosion\n{line}";

        Assert.Equal(new Vector2(min, max), Effect(text).Lifetime);
        Assert.Equal(new Vector2(min, max), Equipment(text).Lifetime);
    }

    [Fact]
    public void ALifetimeWithThreeValuesGivesAWarning()
    {
        using var log = new LogCapture();
        var explosion = Effect("[Explosion]\nnickname = sample_explosion\nlifetime = 1, 3, 5");

        Assert.Equal(new Vector2(1, 3), explosion.Lifetime);
        Assert.Single(log.Lines("Too many components for lifetime"));
    }
}
