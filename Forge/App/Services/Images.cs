using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

public static class Images
{
    public static WriteableBitmap ToBitmap(RgbaImage image)
    {
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        CopyInto(bitmap, image);
        return bitmap;
    }

    /// <summary>Reuses <paramref name="bitmap"/> when the size matches (viewport redraws every drag).</summary>
    public static WriteableBitmap Update(WriteableBitmap? bitmap, RgbaImage image)
    {
        if (bitmap == null || bitmap.PixelSize.Width != image.Width || bitmap.PixelSize.Height != image.Height)
            return ToBitmap(image);
        CopyInto(bitmap, image);
        return bitmap;
    }

    /// <summary>Pixels of a BGRA bitmap (headless screenshots).</summary>
    public static RgbaImage FromBitmap(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        var w = fb.Size.Width;
        var h = fb.Size.Height;
        var bgra = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Marshal.Copy(fb.Address + y * fb.RowBytes, bgra, y * w * 4, w * 4);
        return new RgbaImage { Width = w, Height = h, Bgra = bgra };
    }

    /// <summary>A PNG/JPG from disk as BGRA, or null if missing/unreadable.</summary>
    public static RgbaImage? LoadFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            var img = StbImageSharp.ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var bgra = img.Data;
            for (var i = 0; i < bgra.Length; i += 4)
                (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            return new RgbaImage { Width = img.Width, Height = img.Height, Bgra = bgra };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Bilinear resize (Thunderstore wants exactly 256×256 icons).</summary>
    public static RgbaImage Resize(RgbaImage src, int w, int h)
    {
        var dst = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var fx = (x + 0.5f) * src.Width / w - 0.5f;
            var fy = (y + 0.5f) * src.Height / h - 0.5f;
            int x0 = Math.Clamp((int)MathF.Floor(fx), 0, src.Width - 1), y0 = Math.Clamp((int)MathF.Floor(fy), 0, src.Height - 1);
            int x1 = Math.Min(x0 + 1, src.Width - 1), y1 = Math.Min(y0 + 1, src.Height - 1);
            float tx = Math.Clamp(fx - x0, 0, 1), ty = Math.Clamp(fy - y0, 0, 1);
            for (var c = 0; c < 4; c++)
            {
                float a = src.Bgra[(y0 * src.Width + x0) * 4 + c], b = src.Bgra[(y0 * src.Width + x1) * 4 + c];
                float d = src.Bgra[(y1 * src.Width + x0) * 4 + c], e = src.Bgra[(y1 * src.Width + x1) * 4 + c];
                dst[(y * w + x) * 4 + c] = (byte)Math.Clamp((a + (b - a) * tx) * (1 - ty) + (d + (e - d) * tx) * ty, 0, 255);
            }
        }

        return new RgbaImage { Width = w, Height = h, Bgra = dst };
    }

    /// <summary>A Thunderstore icon from item icons: up to four on a dark tile, alpha-blended.</summary>
    public static RgbaImage IconMontage(IReadOnlyList<RgbaImage> icons, int size = 256)
    {
        var px = new byte[size * size * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = 0x21;
            px[i + 1] = 0x1D;
            px[i + 2] = 0x1A;
            px[i + 3] = 255;
        }

        var cells = icons.Count <= 1 ? 1 : 2;
        var cell = size / cells;
        var pad = cells == 1 ? size / 10 : size / 16;
        for (var n = 0; n < Math.Min(icons.Count, cells * cells); n++)
        {
            var icon = Resize(icons[n], cell - pad * 2, cell - pad * 2);
            var ox = n % cells * cell + pad;
            var oy = n / cells * cell + pad;
            for (var y = 0; y < icon.Height; y++)
            for (var x = 0; x < icon.Width; x++)
            {
                var s = (y * icon.Width + x) * 4;
                var d = ((oy + y) * size + ox + x) * 4;
                var a = icon.Bgra[s + 3] / 255f;
                for (var c = 0; c < 3; c++)
                    px[d + c] = (byte)(icon.Bgra[s + c] * a + px[d + c] * (1 - a));
            }
        }

        return new RgbaImage { Width = size, Height = size, Bgra = px };
    }

    /// <summary>Writes a BGRA image as PNG (icons rendered from the viewport).</summary>
    public static void SavePng(RgbaImage image, string path)
    {
        var rgba = (byte[])image.Bgra.Clone();
        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        new StbImageWriteSharp.ImageWriter().WritePng(rgba, image.Width, image.Height, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, file);
    }

    private static void CopyInto(WriteableBitmap bitmap, RgbaImage image)
    {
        using var fb = bitmap.Lock();
        var row = image.Width * 4;
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(image.Bgra, y * row, fb.Address + y * fb.RowBytes, row);
    }
}
