using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DrakesForge.Format.Json;

namespace DrakesForge.Format;

/// <summary>
/// Writes a <see cref="GlbModel"/> as a binary glTF 2.0 (.glb): the inverse of <see cref="GlbReader"/>, so a model
/// read back comes out the same. Input is in Unity's space (left-handed, Y up, V from the bottom); the file gets
/// Z flipped, the winding fixed and V flipped. Static meshes only: one node and mesh per submesh, named after it.
/// </summary>
public static class GlbWriter
{
    private const uint Magic = 0x46546C67;
    private const uint ChunkJson = 0x4E4F534A;
    private const uint ChunkBin = 0x004E4942;

    private const int Float = 5126;
    private const int UnsignedInt = 5125;
    private const int ArrayBuffer = 34962;
    private const int ElementArrayBuffer = 34963;

    public static byte[] Write(GlbModel model)
    {
        var bin = new MemoryStream();
        var views = JsonValue.NewArray();
        var accessors = JsonValue.NewArray();
        var meshes = JsonValue.NewArray();
        var nodes = JsonValue.NewArray();
        var sceneNodes = JsonValue.NewArray();

        for (var i = 0; i < model.Submeshes.Count; i++)
        {
            var sub = model.Submeshes[i];
            var vertexCount = sub.Positions.Length / 3;
            if (vertexCount == 0 || sub.Indices.Length < 3)
                continue;

            var attributes = JsonValue.NewObject();
            attributes.Set("POSITION", AddFloats(bin, views, accessors, FlipZ(sub.Positions), 3, "VEC3", withBounds: true));
            if (sub.Normals.Length == sub.Positions.Length)
                attributes.Set("NORMAL", AddFloats(bin, views, accessors, FlipZ(sub.Normals), 3, "VEC3", withBounds: false));
            if (sub.Uvs.Length == vertexCount * 2)
                attributes.Set("TEXCOORD_0", AddFloats(bin, views, accessors, FlipV(sub.Uvs), 2, "VEC2", withBounds: false));

            var primitive = JsonValue.NewObject()
                .Set("attributes", attributes)
                .Set("indices", AddIndices(bin, views, accessors, ReverseWinding(sub.Indices), vertexCount))
                .Set("mode", 4);
            if (sub.Material >= 0 && sub.Material < model.Materials.Count)
                primitive.Set("material", sub.Material);

            var mesh = JsonValue.NewObject().Set("primitives", JsonValue.NewArray().Add(primitive));
            if (sub.Name != null)
                mesh.Set("name", sub.Name);
            var node = JsonValue.NewObject().Set("mesh", meshes.Count);
            if (sub.Name != null)
                node.Set("name", sub.Name);
            sceneNodes.Add(nodes.Count);
            nodes.Add(node);
            meshes.Add(mesh);
        }

        if (meshes.Count == 0)
            throw new GlbException("The model has no geometry to write.");

        var materials = JsonValue.NewArray();
        var textures = JsonValue.NewArray();
        var images = JsonValue.NewArray();
        foreach (var m in model.Materials)
        {
            var pbr = JsonValue.NewObject()
                .Set("baseColorFactor", Floats(m.BaseColor.Length == 4 ? m.BaseColor : new[] { 1f, 1f, 1f, 1f }))
                .Set("metallicFactor", 0)
                .Set("roughnessFactor", 1);
            if (m.BaseColorImage is { Length: > 0 } image)
            {
                var view = AddView(bin, views, image, null);
                images.Add(JsonValue.NewObject().Set("bufferView", view).Set("mimeType", m.BaseColorMimeType ?? "image/png"));
                textures.Add(JsonValue.NewObject().Set("source", images.Count - 1));
                pbr.Set("baseColorTexture", JsonValue.NewObject().Set("index", textures.Count - 1));
            }

            var material = JsonValue.NewObject().Set("pbrMetallicRoughness", pbr);
            if (m.Name != null)
                material.Set("name", m.Name);
            if (m.Blend)
                material.Set("alphaMode", "BLEND");
            materials.Add(material);
        }

        var root = JsonValue.NewObject()
            .Set("asset", JsonValue.NewObject().Set("version", "2.0").Set("generator", "Drakes Asset Forge"))
            .Set("scene", 0)
            .Set("scenes", JsonValue.NewArray().Add(JsonValue.NewObject().Set("nodes", sceneNodes)))
            .Set("nodes", nodes)
            .Set("meshes", meshes)
            .Set("accessors", accessors)
            .Set("bufferViews", views)
            .Set("buffers", JsonValue.NewArray().Add(JsonValue.NewObject().Set("byteLength", (int)bin.Length)));
        if (materials.Count > 0)
            root.Set("materials", materials);
        if (images.Count > 0)
            root.Set("images", images).Set("textures", textures);

        var json = Pad(Encoding.UTF8.GetBytes(root.ToJson(false)), (byte)' ');
        var binary = Pad(bin.ToArray(), 0);
        using var file = new MemoryStream();
        using var w = new BinaryWriter(file);
        w.Write(Magic);
        w.Write(2u);
        w.Write((uint)(12 + 8 + json.Length + 8 + binary.Length));
        w.Write((uint)json.Length);
        w.Write(ChunkJson);
        w.Write(json);
        w.Write((uint)binary.Length);
        w.Write(ChunkBin);
        w.Write(binary);
        w.Flush();
        return file.ToArray();
    }

    private static int AddFloats(MemoryStream bin, JsonValue views, JsonValue accessors, float[] data, int components, string type, bool withBounds)
    {
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        var accessor = JsonValue.NewObject()
            .Set("bufferView", AddView(bin, views, bytes, ArrayBuffer))
            .Set("componentType", Float)
            .Set("count", data.Length / components)
            .Set("type", type);
        if (withBounds)
        {
            var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (var i = 0; i < data.Length; i++)
            {
                min[i % 3] = Math.Min(min[i % 3], data[i]);
                max[i % 3] = Math.Max(max[i % 3], data[i]);
            }

            accessor.Set("min", Floats(min)).Set("max", Floats(max));
        }

        accessors.Add(accessor);
        return accessors.Count - 1;
    }

    private static int AddIndices(MemoryStream bin, JsonValue views, JsonValue accessors, int[] indices, int vertexCount)
    {
        foreach (var index in indices)
            if (index < 0 || index >= vertexCount)
                throw new GlbException($"A face refers to vertex {index}, but there are {vertexCount}.");
        var bytes = new byte[indices.Length * 4];
        Buffer.BlockCopy(indices, 0, bytes, 0, bytes.Length);
        accessors.Add(JsonValue.NewObject()
            .Set("bufferView", AddView(bin, views, bytes, ElementArrayBuffer))
            .Set("componentType", UnsignedInt)
            .Set("count", indices.Length)
            .Set("type", "SCALAR"));
        return accessors.Count - 1;
    }

    /// <summary>Appends data on a 4-byte boundary and returns the buffer view's index.</summary>
    private static int AddView(MemoryStream bin, JsonValue views, byte[] data, int? target)
    {
        while (bin.Length % 4 != 0)
            bin.WriteByte(0);
        var view = JsonValue.NewObject()
            .Set("buffer", 0)
            .Set("byteOffset", (int)bin.Length)
            .Set("byteLength", data.Length);
        if (target != null)
            view.Set("target", target.Value);
        bin.Write(data, 0, data.Length);
        views.Add(view);
        return views.Count - 1;
    }

    private static JsonValue Floats(float[] values)
    {
        var a = JsonValue.NewArray();
        foreach (var v in values)
            a.Add((double)v);
        return a;
    }

    private static byte[] Pad(byte[] data, byte fill)
    {
        var padded = new byte[(data.Length + 3) / 4 * 4];
        Array.Copy(data, padded, data.Length);
        for (var i = data.Length; i < padded.Length; i++)
            padded[i] = fill;
        return padded;
    }

    private static float[] FlipZ(float[] xyz)
    {
        var r = (float[])xyz.Clone();
        for (var i = 2; i < r.Length; i += 3)
            r[i] = -r[i];
        return r;
    }

    private static float[] FlipV(float[] uv)
    {
        var r = (float[])uv.Clone();
        for (var i = 1; i < r.Length; i += 2)
            r[i] = 1f - r[i];
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
