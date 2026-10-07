using System.Diagnostics;
using AssetsTools.NET.Texture;
using DrakesForge.Valheim;

/// <summary>Writes icon + model render PNGs to %TEMP%/forge-probe for eyeballing the decoders.</summary>
internal static class Render
{
    public static void Save(AssetSession session, VanillaEntry entry)
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-probe");
        Directory.CreateDirectory(dir);
        var sw = Stopwatch.StartNew();
        var preview = PreviewLoader.Load(session, entry);
        Console.WriteLine($"  preview decoded in {sw.ElapsedMilliseconds} ms: {preview.Model?.Parts.Count} parts, {preview.Model?.TriangleCount} tris, icon {(preview.Icon == null ? "none" : $"{preview.Icon.Width}x{preview.Icon.Height}")}");
        foreach (var p in preview.Model?.Problems ?? Array.Empty<string>())
            Console.WriteLine($"  problem: {p}");

        if (preview.Icon != null)
            Write(preview.Icon, Path.Combine(dir, entry.Name + "_icon.png"));
        if (preview.Model != null)
        {
            sw.Restart();
            var image = SoftwareRenderer.Render(preview.Model.Parts, slot => SoftwareRenderer.DefaultLook(preview.Model, slot), new OrbitCamera(), 640, 480);
            Console.WriteLine($"  rendered in {sw.ElapsedMilliseconds} ms");
            Write(image, Path.Combine(dir, entry.Name + "_model.png"));
            if (preview.Model.WornParts.Count > 0)
            {
                var worn = SoftwareRenderer.Render(preview.Model.WornParts, slot => SoftwareRenderer.DefaultLook(preview.Model, slot), new OrbitCamera { Yaw = 0.4f, Pitch = 0.1f }, 480, 640);
                Write(worn, Path.Combine(dir, entry.Name + "_worn.png"));
                Console.WriteLine($"  worn: {preview.Model.WornParts.Count} parts");
            }
        }

        if (preview.Info.ArmorMaterial is { } armor)
            foreach (var t in PreviewLoader.LoadTextures(session, armor, 512))
                Write(t.Value, Path.Combine(dir, $"{entry.Name}_armor{t.Key}.png"));

        Console.WriteLine($"  wrote {dir}");
    }

    private static void Write(RgbaImage image, string path)
    {
        // WriteRawImage takes RGBA; Forge images are BGRA.
        var rgba = (byte[])image.Bgra.Clone();
        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        using var file = File.Create(path);
        TextureOperations.WriteRawImage(rgba, image.Width, image.Height, file, ImageExportType.Png, 100);
    }
}
