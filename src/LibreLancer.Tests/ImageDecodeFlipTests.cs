using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibreLancer.ImageLib;
using StbImageSharp;
using Xunit;

namespace LibreLancer.Tests;

// The flip of the image decoder of the engine (OL-0065). StbImageSharp keeps its flip flag in a static field of the
// whole process. Generic set the flag before each decode. So a decode with a flip on one thread could flip the image
// of a decode without a flip on another thread. The OpenLancer editor decodes its thumbnails on worker threads. The
// decoder now never sets the flag and flips the rows itself.
public class ImageDecodeFlipTests
{
    private const int Width = 2;
    private const int Height = 3;

    // The pixels of the test image, row after row. Each pixel has other values, so a row in the wrong place shows.
    private static Bgra8[] Pixels() => Enumerable.Range(0, Width * Height)
        .Select(i => new Bgra8((byte)(10 * i), (byte)(10 * i + 1), (byte)(10 * i + 2), (byte)255)).ToArray();

    private static byte[] Png()
    {
        using var stream = new MemoryStream();
        PNG.Save(stream, Width, Height, Pixels(), false);
        return stream.ToArray();
    }

    // The bytes of the test image in the order of the decoder: B, G, R, A for each pixel, with the rows in reverse
    // order for a flip.
    private static byte[] Expected(bool flip)
    {
        var pixels = Pixels();
        var rows = Enumerable.Range(0, Height).Select(row => flip ? Height - 1 - row : row);
        return rows.SelectMany(row => pixels.Skip(row * Width).Take(Width))
            .SelectMany(p => new[] { p.B, p.G, p.R, p.A }).ToArray();
    }

    private static byte[] Decode(byte[] png, bool flip) => Generic.ImageFromStream(new MemoryStream(png), flip).Data;

    [Fact]
    public void AFlipGivesTheRowsInReverseOrder()
    {
        var png = Png();

        Assert.Equal(Expected(false), Decode(png, false));
        Assert.Equal(Expected(true), Decode(png, true));
    }

    [Fact]
    public void ADecodeDoesNotSetTheFlipFlagOfTheProcess()
    {
        var png = Png();

        Decode(png, true);

        Assert.Equal(0, StbImage.stbi__vertically_flip_on_load_global);
        Assert.Equal(0, StbImage.stbi__vertically_flip_on_load_set);
    }

    [Fact]
    public void DecodesOnManyThreadsDoNotFlipEachOther()
    {
        var png = Png();
        var (straight, flipped) = (Expected(false), Expected(true));

        Parallel.For(0, 400, i =>
        {
            var flip = i % 2 == 0;
            Assert.Equal(flip ? flipped : straight, Decode(png, flip));
        });
    }
}
