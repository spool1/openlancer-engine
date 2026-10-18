using System;
using System.Runtime.InteropServices;
using LibreLancer.Platforms;
using Xunit;

namespace LibreLancer.Tests;

// The colors of the window icon (OL-0038). SetWindowIcon gets Bgra8 pixels, whose bytes are in the order B, G, R, A.
// SDL3 read them as R, G, B, A, and the SDL2 masks put the red channel on the alpha byte. So red and blue changed
// places.
public class WindowIconTests
{
    private static readonly Bgra8 Pixel = new((byte)0x11, (byte)0x22, (byte)0x33, (byte)0x44);

    [Fact]
    public void ABgra8PixelHasTheBytesBlueGreenRedAlpha()
    {
        var bytes = MemoryMarshal.AsBytes(new[] { Pixel }.AsSpan()).ToArray();

        Assert.Equal(new byte[] { 0x33, 0x22, 0x11, 0x44 }, bytes);
    }

    [Fact]
    public void TheSdl3IconFormatIsTheByteOrderOfBgra8()
    {
        Assert.Equal(SDL3.SDL_PixelFormat.SDL_PIXELFORMAT_BGRA32, SDL3Game.IconFormat);
        // SDL defines BGRA32 by the order of the bytes. On a little-endian CPU it is the packed format ARGB8888.
        if (BitConverter.IsLittleEndian)
        {
            Assert.Equal(SDL3.SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888, SDL3Game.IconFormat);
            Assert.Equal(0x44112233u, Pixel.Pixel);
        }
    }

    [Fact]
    public void EachSdl2IconMaskGetsItsChannelFromAPixel()
    {
        Assert.SkipUnless(BitConverter.IsLittleEndian, "The SDL2 masks are for a little-endian CPU.");

        Assert.Equal(0x11u, (Pixel.Pixel & SDL2Game.IconRedMask) >> 16);
        Assert.Equal(0x22u, (Pixel.Pixel & SDL2Game.IconGreenMask) >> 8);
        Assert.Equal(0x33u, Pixel.Pixel & SDL2Game.IconBlueMask);
        Assert.Equal(0x44u, (Pixel.Pixel & SDL2Game.IconAlphaMask) >> 24);
    }
}
