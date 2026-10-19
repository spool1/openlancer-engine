using LibreLancer.Data.Schema.Universe;
using Xunit;

namespace LibreLancer.Tests;

// The color curve of a light (OL-0048). Retail reads "color_curve = <graph>, <period>". The color of the graph
// multiplies the light color over time, and the time starts again at the period. The engine warned "Unknown entry
// color_curve". It does not change the color yet.
[Collection(nameof(LogCapture))]
public class LightSourceColorCurveTests
{
    [Fact]
    public void ALightReadsItsColorCurveWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(LightSource.TryParse(IniText.Section("""
            [LightSource]
            nickname = sample_light
            pos = 0, 0, 0
            color = 255, 255, 255
            range = 1000
            color_curve = sample_graph, 2.5
            """), out var light));

        Assert.Equal(("sample_graph", 2.5f), (light.ColorCurve, light.ColorCurvePeriod));
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void AColorCurveWithoutThePeriodGivesAnError()
    {
        using var log = new LogCapture();
        Assert.True(LightSource.TryParse(IniText.Section("""
            [LightSource]
            nickname = sample_light
            color_curve = sample_graph
            """), out var light));

        Assert.Null(light.ColorCurve);
        Assert.Single(log.Lines("Not enough components for color_curve"));
    }
}
