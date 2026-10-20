// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.IO;
using LibreLancer.Graphics;
using StbImageSharp;

namespace LibreLancer.ImageLib;

public static class Generic
{
    public static Image ImageFromStream(Stream stream, bool flip = false)
    {
        var image = Decode(stream, flip);
        return new Image
        {
            Width = image.Width, Height = image.Height, Data = image.Data
        };
    }

    public static Texture TextureFromStream(RenderContext context, Stream stream, bool flip = true)
    {
        if (DDS.StreamIsDDS(stream))
        {
            return DDS.FromStream(context, stream);
        }

        var image = Decode(stream, flip);
        var texture = new Texture2D(context, image.Width, image.Height, false, SurfaceFormat.Bgra8);
        texture.SetData(image.Data);
        return texture;
    }

    // OL-0065: Decodes the whole stream to Bgra8 pixels. With flip, the last row of the image comes first. The flip flag
    // of StbImageSharp is a static field of the whole process. This decoder set it before each decode, so a decode on
    // another thread could change it during this decode and flip the wrong image. The decoder now never sets the flag
    // and flips the rows itself.
    private static ImageResult Decode(Stream stream, bool flip)
    {
        /* Read full stream */
        var len = (int)stream.Length;
        var b = new byte[len];
        var pos = 0;
        int r;

        while ((r = stream.Read(b, pos, len - pos)) > 0)
        {
            pos += r;
        }

        /* stb_image it */
        ImageResult image = ImageResult.FromMemory(b, ColorComponents.RedGreenBlueAlpha);
        for (var i = 0; i < image.Data.Length; i += 4)
        {
            (image.Data[i + 2], image.Data[i]) = (image.Data[i], image.Data[i + 2]);
        }

        if (flip)
        {
            FlipRows(image.Data, image.Width * 4, image.Height);
        }

        return image;
    }

    private static void FlipRows(byte[] data, int stride, int height)
    {
        var row = new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            var upper = data.AsSpan(top * stride, stride);
            var lower = data.AsSpan(bottom * stride, stride);
            upper.CopyTo(row);
            lower.CopyTo(upper);
            row.CopyTo(lower);
        }
    }
}
