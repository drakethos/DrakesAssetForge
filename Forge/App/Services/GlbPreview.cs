using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesForge.Format;
using DrakesForge.Valheim;
using StbImageSharp;

namespace DrakesForge.App.Services;

/// <summary>Turns a pack's .glb into the model the viewport and <c>render</c> draw, like a vanilla prefab's.</summary>
public static class GlbPreview
{
    /// <summary>The model at <paramref name="path"/>, or null with <paramref name="error"/> saying why it can't be shown.</summary>
    public static VanillaModel? Load(string path, out string? error)
    {
        try
        {
            var model = GlbReader.Read(File.ReadAllBytes(path));
            error = model.Warnings.Count > 0 ? string.Join(" ", model.Warnings) : null;
            return FromModel(model);
        }
        catch (Exception ex) when (ex is GlbException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    public static VanillaModel FromModel(GlbModel model)
    {
        var materials = model.Materials.Select((m, i) => new ModelMaterial
        {
            Info = new MaterialInfo { Name = m.Name ?? $"glb material {i}", Shader = "glb", Color = m.BaseColor },
            Albedo = Decode(m.BaseColorImage)
        }).ToList();

        // Submeshes without a material share one default slot at the end.
        var defaultSlot = materials.Count;
        materials.Add(new ModelMaterial { Info = new MaterialInfo { Name = "glb default", Shader = "glb", Color = new float[] { 1, 1, 1, 1 } } });

        var parts = model.Submeshes.Select((sub, i) => new ModelPart
        {
            Name = $"glb{i}_{sub.Name ?? "mesh"}",
            Positions = sub.Positions,
            Normals = sub.Normals.Length == sub.Positions.Length ? sub.Normals : SmoothNormals(sub.Positions, sub.Indices),
            Uvs = sub.Uvs.Length == sub.Positions.Length / 3 * 2 ? sub.Uvs : new float[sub.Positions.Length / 3 * 2],
            Indices = sub.Indices,
            MaterialSlot = sub.Material >= 0 && sub.Material < model.Materials.Count ? sub.Material : defaultSlot
        }).ToList();

        return new VanillaModel
        {
            Parts = parts,
            Materials = materials,
            Problems = model.Warnings.ToList()
        };
    }

    /// <summary>Decodes an embedded PNG/JPG to the BGRA the software renderer uses. Null if there's no image or it can't be read.</summary>
    private static RgbaImage? Decode(byte[]? bytes)
    {
        if (bytes == null)
            return null;
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var bgra = new byte[image.Data.Length];
            for (var i = 0; i < bgra.Length; i += 4)
            {
                bgra[i] = image.Data[i + 2];
                bgra[i + 1] = image.Data[i + 1];
                bgra[i + 2] = image.Data[i];
                bgra[i + 3] = image.Data[i + 3];
            }
            return new RgbaImage { Width = image.Width, Height = image.Height, Bgra = bgra };
        }
        catch (Exception)
        {
            // A texture that won't decode just isn't shown; the model still is.
            return null;
        }
    }

    /// <summary>Per-vertex normals averaged from the faces that use each vertex (for files without normals).</summary>
    private static float[] SmoothNormals(float[] positions, int[] indices)
    {
        var normals = new float[positions.Length];
        for (var t = 0; t + 2 < indices.Length; t += 3)
        {
            var a = indices[t] * 3;
            var b = indices[t + 1] * 3;
            var c = indices[t + 2] * 3;
            var ux = positions[b] - positions[a];
            var uy = positions[b + 1] - positions[a + 1];
            var uz = positions[b + 2] - positions[a + 2];
            var vx = positions[c] - positions[a];
            var vy = positions[c + 1] - positions[a + 1];
            var vz = positions[c + 2] - positions[a + 2];
            var n = new[] { uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx };
            foreach (var v in new[] { a, b, c })
                for (var k = 0; k < 3; k++)
                    normals[v + k] += n[k];
        }
        for (var i = 0; i + 2 < normals.Length; i += 3)
        {
            var length = (float)Math.Sqrt(normals[i] * normals[i] + normals[i + 1] * normals[i + 1] + normals[i + 2] * normals[i + 2]);
            if (length > 0)
            {
                normals[i] /= length;
                normals[i + 1] /= length;
                normals[i + 2] /= length;
            }
        }
        return normals;
    }
}
