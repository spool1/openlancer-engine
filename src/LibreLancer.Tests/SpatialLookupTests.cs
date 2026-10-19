using System.Collections.Generic;
using System.Numerics;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The lookup forgets a removed object (OL-0057). Before, the entry of a removed object stayed: the lookup kept the
// object alive, and a second removal of it could remove a new cell with the same key.
public class SpatialLookupTests
{
    private static readonly Vector3 Point = new(100, 0, 100);

    [Fact]
    public void ASecondRemovalThrows()
    {
        var lookup = new SpatialLookup();
        var obj = new GameObject();
        lookup.AddObject(obj, Point);

        lookup.RemoveObject(obj);

        Assert.Throws<KeyNotFoundException>(() => lookup.RemoveObject(obj));
    }

    // The removal empties the cell, so the lookup drops it. A new object then makes a new cell with the same key.
    [Fact]
    public void ASecondRemovalKeepsTheObjectsOfANewCellWithTheSameKey()
    {
        var lookup = new SpatialLookup();
        var removed = new GameObject();
        var kept = new GameObject();
        lookup.AddObject(removed, Point);
        lookup.RemoveObject(removed);
        lookup.AddObject(kept, Point);

        Assert.Throws<KeyNotFoundException>(() => lookup.RemoveObject(removed));

        Assert.Equal(new[] { kept }, lookup.GetNearbyObjects(new GameObject(), Point));
    }
}
