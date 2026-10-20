using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LibreLancer.Graphics.Backends.OpenGL;
using Xunit;

namespace LibreLancer.Tests;

// The pixel buffer of Texture2D.GetDataAsync (OL-0064). The read copies the pixels of the buffer when its fence
// signals. No code deleted the buffer, so each read kept its pixels in the memory of the driver. The editor reads each
// thumbnail in this way. The suite has no GL context, so the tests load a fake GL with the four functions of the copy.
// No other test of the suite calls GL. The tests unload the fake GL at their end.
public class TextureReadBackTests
{
    private delegate void BindBufferFunction(int target, uint buffer);

    private delegate IntPtr MapBufferRangeFunction(uint target, IntPtr offset, IntPtr length, uint access);

    private delegate int UnmapBufferFunction(uint target);

    private delegate void DeleteBuffersFunction(int count, IntPtr buffers);

    private const uint Buffer = 7;

    private readonly List<string> calls = [];

    [Fact]
    public void TheCopyDeletesThePixelBuffer()
    {
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var copy = WithFakeGL(pinned.AddrOfPinnedObject(), () => GLTexture2D.ReadPixelBuffer(Buffer, pixels.Length));

            Assert.Equal(pixels, copy);
            Assert.Equal(["bind 7", "map 8", "unmap", "bind 0", "delete 7"], calls);
        }
        finally
        {
            pinned.Free();
        }
    }

    [Fact]
    public void ACopyThatFailsAlsoDeletesThePixelBuffer()
    {
        // A map that fails gives a null pointer, and the copy throws.
        Assert.Throws<ArgumentNullException>(() =>
            WithFakeGL(IntPtr.Zero, () => GLTexture2D.ReadPixelBuffer(Buffer, 8)));

        Assert.Equal(["bind 7", "map 8", "bind 0", "delete 7"], calls);
    }

    /// <summary>Runs the read with a fake GL. The map of the fake GL gives <paramref name="mapped"/>.</summary>
    private T WithFakeGL<T>(IntPtr mapped, Func<T> read)
    {
        BindBufferFunction bind = (_, buffer) => calls.Add($"bind {buffer}");
        MapBufferRangeFunction map = (_, _, length, _) =>
        {
            calls.Add($"map {length}");
            return mapped;
        };
        UnmapBufferFunction unmap = _ =>
        {
            calls.Add("unmap");
            return 1;
        };
        DeleteBuffersFunction delete = (count, buffers) =>
        {
            for (var i = 0; i < count; i++)
                calls.Add($"delete {(uint)Marshal.ReadInt32(buffers, 4 * i)}");
        };
        var functions = new Dictionary<string, Delegate>
        {
            ["glBindBuffer"] = bind,
            ["glMapBufferRange"] = map,
            ["glUnmapBuffer"] = unmap,
            ["glDeleteBuffers"] = delete,
        };
        try
        {
            GL.Load(name => functions.TryGetValue(name, out var function)
                ? Marshal.GetFunctionPointerForDelegate(function)
                : IntPtr.Zero, false);
            return read();
        }
        finally
        {
            GL.Load(_ => IntPtr.Zero, false);
            GC.KeepAlive(functions);
        }
    }
}
