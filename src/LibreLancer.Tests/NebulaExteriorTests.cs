using System;
using System.Numerics;
using LibreLancer.Data.GameData.World;
using LibreLancer.Render;
using Xunit;

namespace LibreLancer.Tests;

// The size of what a nebula draws (OL-0028). The zone size of an ellipsoid holds its semi-axes, so the exterior, the
// puff ring and the exclusion shells use the size of their zone as the radii. The values are from Li01: the Badlands
// nebula and Buffalo Base (Li01_12).
public class NebulaExteriorTests
{
    private static readonly Vector3 BadlandsCenter = new(-7460, 0, 63000);
    private static readonly Vector3 BadlandsSize = new(55000, 20000, 35000);
    private static readonly Vector3 Buffalo = new(25390, 0, 74163);

    private static Zone MakeZone(ShapeKind shape, Vector3 position, Vector3 size, float edgeFraction = 0.25f) => new()
    {
        Nickname = "test_zone",
        Shape = shape,
        Position = position,
        Size = size,
        RotationMatrix = Matrix4x4.Identity,
        EdgeFraction = edgeFraction,
    };

    private static Zone Badlands() => MakeZone(ShapeKind.Ellipsoid, BadlandsCenter, BadlandsSize);

    private static void AssertNear(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 2);
        Assert.Equal(expected.Y, actual.Y, 2);
        Assert.Equal(expected.Z, actual.Z, 2);
    }

    [Fact]
    public void EllipsoidRadiiAreTheZoneSize()
    {
        Assert.Equal(BadlandsSize, NebulaRenderer.ZoneRadii(Badlands()));
    }

    [Fact]
    public void SphereRadiiAreTheRadiusOnEachAxis()
    {
        var sphere = MakeZone(ShapeKind.Sphere, Vector3.Zero, new Vector3(3000, 1, 1));
        Assert.Equal(new Vector3(3000), NebulaRenderer.ZoneRadii(sphere));
    }

    [Theory]
    [InlineData(ShapeKind.Box)]
    [InlineData(ShapeKind.Cylinder)]
    [InlineData(ShapeKind.Ring)]
    public void OtherShapesHaveNoRadii(ShapeKind shape)
    {
        Assert.Null(NebulaRenderer.ZoneRadii(MakeZone(shape, Vector3.Zero, new Vector3(1000, 500, 200))));
    }

    [Fact]
    public void TheDrawnVolumeHoldsThePointsThatTheZoneHolds()
    {
        var zone = Badlands();
        var radii = NebulaRenderer.ZoneRadii(zone)!.Value;
        // Buffalo Base is at 0.458 of the zone. With half the size as the radii, it was at 1.834: outside the cloud.
        Assert.True(zone.ContainsPoint(Buffalo));
        Assert.Equal(0.458f, PrimitiveMath.EllipsoidFunction(BadlandsCenter, radii, Buffalo), 3);
        Vector3[] points =
        [
            Buffalo,
            BadlandsCenter + new Vector3(0.99f * BadlandsSize.X, 0, 0),
            BadlandsCenter + new Vector3(1.01f * BadlandsSize.X, 0, 0),
            BadlandsCenter + new Vector3(0, -0.99f * BadlandsSize.Y, 0),
            BadlandsCenter + new Vector3(0, -1.01f * BadlandsSize.Y, 0),
            BadlandsCenter + new Vector3(0, 0, 0.99f * BadlandsSize.Z),
            BadlandsCenter + new Vector3(0, 0, 1.01f * BadlandsSize.Z),
            BadlandsCenter + new Vector3(0.6f * BadlandsSize.X, 0.5f * BadlandsSize.Y, 0.5f * BadlandsSize.Z),
            BadlandsCenter + new Vector3(0.5f * BadlandsSize.X, 0.5f * BadlandsSize.Y, 0.5f * BadlandsSize.Z),
        ];
        foreach (var point in points)
        {
            Assert.Equal(zone.ContainsPoint(point), PrimitiveMath.EllipsoidContains(BadlandsCenter, radii, point));
        }
    }

    [Fact]
    public void ThePuffRingsLieOnTheZoneSurface()
    {
        var zone = Badlands();
        var radii = NebulaRenderer.ZoneRadii(zone)!.Value;
        // The three rings are at 0.25, 0.5 and 0.75 of radii.Y, and each puff moves by up to move_bit_percent (0.5 in
        // the Badlands file) of radii.Y.
        foreach (var y in new[] { -0.25f, 0f, 0.25f, 0.5f, 0.75f, 1f, 1.25f })
        {
            for (var angle = 0f; angle < 2 * MathF.PI; angle += MathF.PI / 7)
            {
                var puff = BadlandsCenter + NebulaRenderer.PuffPosition(radii, y * radii.Y, angle);
                Assert.Equal(1f, zone.ScaledDistance(puff), 4);
            }
        }

        // The middle ring reaches the edge of the zone: 55 km on X, not 27.5 km.
        Assert.Equal(new Vector3(BadlandsSize.X, 0, 0), NebulaRenderer.PuffPosition(radii, 0.5f * radii.Y, 0));
    }

    [Fact]
    public void TheShellTakesTheSizeOfTheExclusionZone()
    {
        // Zone_Li01_012_Station_Li01_04: a sphere of radius 3000 in the Badlands nebula, with shell_scalar 1.1.
        var hole = new NebulaExclusionZone
        {
            Zone = MakeZone(ShapeKind.Sphere, new Vector3(-10844, 0, 78091), new Vector3(3000)),
            ShellScalar = 1.1f,
        };
        AssertNear(new Vector3(3300), NebulaRenderer.ShellRadii(hole)!.Value);

        var ellipsoid = new NebulaExclusionZone
        {
            Zone = MakeZone(ShapeKind.Ellipsoid, Vector3.Zero, new Vector3(2000, 1000, 3000)),
            ShellScalar = 1.2f,
        };
        AssertNear(new Vector3(2400, 1200, 3600), NebulaRenderer.ShellRadii(ellipsoid)!.Value);

        var box = new NebulaExclusionZone
        {
            Zone = MakeZone(ShapeKind.Box, Vector3.Zero, new Vector3(2000, 1000, 3000)),
            ShellScalar = 1,
        };
        Assert.Null(NebulaRenderer.ShellRadii(box));
    }

    [Fact]
    public void TheFogIsFullFromTheDepthOfTheEdgeFraction()
    {
        var zone = Badlands();
        Assert.Equal(1f, NebulaRenderer.FogTransition(zone, BadlandsCenter));
        Assert.Equal(1f, NebulaRenderer.FogTransition(zone, Buffalo));
        // ScaledDistance of an ellipsoid is the square of the radius fraction. With edge_fraction 0.25, the fog is half
        // at the scaled distance 0.875.
        var half = BadlandsCenter + new Vector3(MathF.Sqrt(0.875f) * BadlandsSize.X, 0, 0);
        Assert.Equal(0.5f, NebulaRenderer.FogTransition(zone, half), 3);
        Assert.Equal(0f, NebulaRenderer.FogTransition(zone, BadlandsCenter + new Vector3(BadlandsSize.X, 0, 0)), 4);
        Assert.Equal(0f, NebulaRenderer.FogTransition(zone, BadlandsCenter + new Vector3(2 * BadlandsSize.X, 0, 0)));
    }
}
