using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Ships;
using LibreLancer.Data.Schema.Solar;
using Xunit;

namespace LibreLancer.Tests;

// The docking and camera keys of a ship (OL-0050). Retail reads docking_sphere (one sphere for each line, as for a
// solar), docking_camera (a flag), camera_angular_slerp_multiplier and distance_render. The engine warned "Unknown
// entry" for each of them. Ship docking and the camera key wait for the OpenLancer runtime.
[Collection(nameof(LogCapture))]
public class ShipDockingKeysTests
{
    private const string ShipWithKeys = """
        [Ship]
        nickname = sample_ship
        DA_archetype = sample.cmp
        docking_sphere = berth, HpSampleDockA, 2, Sc_sample dock
        docking_sphere = moor_large, HpSampleDockB, 40
        docking_camera = 0
        camera_angular_slerp_multiplier = 0.1
        distance_render = 5000
        """;

    [Fact]
    public void AShipReadsTheFourKeysWithoutAWarning()
    {
        using var log = new LogCapture();
        Assert.True(Ship.TryParse(IniText.Section(ShipWithKeys), out var ship));

        Assert.Equal([
            new DockSphere(DockSphereType.berth, "HpSampleDockA", 2, "Sc_sample dock"),
            new DockSphere(DockSphereType.moor_large, "HpSampleDockB", 40, null),
        ], ship.DockingSpheres);
        Assert.False(ship.DockingCamera);
        Assert.Equal((0.1f, 5000f), (ship.CameraAngularSlerpMultiplier, ship.DistanceRender));
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void AShipWithoutTheKeysHasTheRetailDefaults()
    {
        Assert.True(Ship.TryParse(
            IniText.Section("[Ship]\nnickname = sample_ship\nDA_archetype = sample.cmp"), out var ship));

        Assert.Empty(ship.DockingSpheres);
        Assert.True(ship.DockingCamera);
        Assert.Equal((0.04f, -1f), (ship.CameraAngularSlerpMultiplier, ship.DistanceRender));
    }

    // A solar reads the line with the same reader.
    [Fact]
    public void ASolarReadsItsDockingSpheresAsBefore()
    {
        Assert.True(Archetype.TryParse(IniText.Section("""
            [Solar]
            nickname = sample_solar
            docking_sphere = ring, HpSampleDock, 50, Sc_sample ring
            """), out var solar));

        Assert.Equal([new DockSphere(DockSphereType.ring, "HpSampleDock", 50, "Sc_sample ring")], solar.DockingSpheres);
    }
}
