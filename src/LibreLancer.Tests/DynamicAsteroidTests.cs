using LibreLancer.Data.Schema.Solar;
using Xunit;

namespace LibreLancer.Tests;

// The particle effect of a dynamic asteroid (OL-0047). The retail client attaches the effect to the asteroid. The
// engine warned "Unknown entry particle_effect". It does not show the effect yet.
[Collection(nameof(LogCapture))]
public class DynamicAsteroidTests
{
    [Fact]
    public void ADynamicAsteroidReadsItsParticleEffectWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(DynamicAsteroid.TryParse(IniText.Section("""
            [DynamicAsteroid]
            nickname = sample_asteroid
            DA_archetype = sample.3db
            particle_effect = sample_trail
            """), out var asteroid));

        Assert.Equal("sample_trail", asteroid.ParticleEffect);
        Assert.Empty(log.Lines(IniText.File));
    }
}
