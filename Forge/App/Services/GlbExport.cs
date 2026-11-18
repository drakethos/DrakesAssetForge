using DrakesForge.Format;
using DrakesForge.Valheim;
using StbImageWriteSharp;

namespace DrakesForge.App.Services;

/// <summary>Writes a vanilla prefab's model as a .glb (meshes, material colours and albedo textures) for Blender and friends.</summary>
public static class GlbExport
{
    /// <summary>Full-size albedo textures; the preview's 512 px copies are too soft to edit.</summary>
    public const int TextureSize = 4096;

    public sealed record Result(int Parts, int Triangles, int Materials, int Textures, IReadOnlyList<string> Problems);

    /// <summary>
    /// <paramref name="worn"/> exports armour and capes as worn (bind pose) instead of as dropped.
    /// Only the materials the written parts use are included.
    /// </summary>
    public static Result Write(VanillaModel model, string path, bool worn = false)
    {
        var parts = worn && model.WornParts.Count > 0 ? model.WornParts : model.Parts;
        if (parts.Count == 0)
            throw new InvalidOperationException("This prefab has no model to export.");

        // Re-index materials to the ones in use so the file isn't full of unused slots.
        var slotToMaterial = new Dictionary<int, int>();
        var materials = new List<GlbMaterial>();
        var textureCount = 0;
        foreach (var slot in parts.Select(p => p.MaterialSlot).Distinct().OrderBy(s => s))
        {
            slotToMaterial[slot] = materials.Count;
            if (slot < 0 || slot >= model.Materials.Count)
            {
                materials.Add(new GlbMaterial { Name = "default" });
                continue;
            }

            var m = model.Materials[slot];
            var color = m.Info.Color.Length >= 4 ? m.Info.Color : new float[] { 1, 1, 1, 1 };
            materials.Add(new GlbMaterial
            {
                Name = m.Info.Name,
                BaseColor = new[] { color[0], color[1], color[2], color[3] },
                BaseColorImage = m.Albedo != null ? EncodePng(m.Albedo) : null,
                BaseColorMimeType = "image/png",
                Blend = m.Info.Translucent
            });
            if (m.Albedo != null)
                textureCount++;
        }

        var submeshes = parts.Select(p => new GlbSubmesh
        {
            Name = p.Name.Replace('#', '_').Replace('/', '.'),
            // Back to Unity's space: the viewer mirrors X and reverses the winding.
            Positions = MirrorX(p.Positions),
            Normals = MirrorX(p.Normals),
            Uvs = p.Uvs,
            Indices = ReverseWinding(p.Indices),
            Material = slotToMaterial[p.MaterialSlot]
        }).ToList();

        var bytes = GlbWriter.Write(new GlbModel { Submeshes = submeshes, Materials = materials });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
        return new Result(parts.Count, parts.Sum(p => p.Indices.Length / 3), materials.Count, textureCount, model.Problems);
    }

    private static byte[] EncodePng(RgbaImage image)
    {
        var rgba = (byte[])image.Bgra.Clone();
        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        using var stream = new MemoryStream();
        new ImageWriter().WritePng(rgba, image.Width, image.Height, ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }

    private static float[] MirrorX(float[] xyz)
    {
        var r = (float[])xyz.Clone();
        for (var i = 0; i < r.Length; i += 3)
            r[i] = -r[i];
        return r;
    }

    private static int[] ReverseWinding(int[] indices)
    {
        var r = (int[])indices.Clone();
        for (var t = 0; t + 2 < r.Length; t += 3)
            (r[t + 1], r[t + 2]) = (r[t + 2], r[t + 1]);
        return r;
    }
}
