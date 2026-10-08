using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DrakesForge.Format;
using Xunit;

namespace DrakesForge.Format.Tests;

public class GlbReaderTests
{
    /// <summary>Packs a glTF JSON document and a binary buffer into a .glb file.</summary>
    private static byte[] Glb(string json, byte[] bin)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonPadded = jsonBytes.Concat(Enumerable.Repeat((byte)' ', (4 - jsonBytes.Length % 4) % 4)).ToArray();
        var binPadded = bin.Concat(Enumerable.Repeat((byte)0, (4 - bin.Length % 4) % 4)).ToArray();

        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + jsonPadded.Length + 8 + binPadded.Length));
        writer.Write((uint)jsonPadded.Length);
        writer.Write(0x4E4F534Au);
        writer.Write(jsonPadded);
        writer.Write((uint)binPadded.Length);
        writer.Write(0x004E4942u);
        writer.Write(binPadded);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    /// <summary>A triangle at z = 1 with normals, UVs and uint16 indices. Positions are (0,0,1) (1,0,1) (0,1,1).</summary>
    private static (string json, byte[] bin) Triangle()
    {
        var positions = Floats(0, 0, 1, 1, 0, 1, 0, 1, 1);
        var normals = Floats(0, 0, 1, 0, 0, 1, 0, 0, 1);
        var uvs = Floats(0, 0, 1, 0, 0, 1);
        var indices = new byte[] { 0, 0, 1, 0, 2, 0 };
        var bin = positions.Concat(normals).Concat(uvs).Concat(indices).ToArray();
        var json = """
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [0] } ],
          "nodes": [ { "mesh": 0, "name": "tri" } ],
          "meshes": [ { "name": "tri", "primitives": [ { "attributes": { "POSITION": 0, "NORMAL": 1, "TEXCOORD_0": 2 }, "indices": 3, "material": 0 } ] } ],
          "materials": [ { "name": "plain", "pbrMetallicRoughness": { "baseColorFactor": [0.5, 0.25, 1, 1] } } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" },
            { "bufferView": 3, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 72, "byteLength": 24 },
            { "buffer": 0, "byteOffset": 96, "byteLength": 6 }
          ],
          "buffers": [ { "byteLength": 102 } ]
        }
        """;
        return (json, bin);
    }

    [Fact]
    public void Reads_a_triangle_into_unity_space()
    {
        var (json, bin) = Triangle();
        var model = GlbReader.Read(Glb(json, bin));

        var sub = Assert.Single(model.Submeshes);
        // Z flipped into Unity's left-handed space.
        Assert.Equal(new float[] { 0, 0, -1, 1, 0, -1, 0, 1, -1 }, sub.Positions);
        Assert.Equal(new float[] { 0, 0, -1, 0, 0, -1, 0, 0, -1 }, sub.Normals);
        // V flipped to Unity's bottom-left origin.
        Assert.Equal(new float[] { 0, 1, 1, 1, 0, 0 }, sub.Uvs);
        // Winding reversed for the handedness change: the triangle is (0, 2, 1).
        Assert.Equal(new[] { 0, 2, 1 }, sub.Indices);
        Assert.Equal(0, sub.Material);
        Assert.Equal("tri", sub.Name);
        Assert.Equal(new float[] { 0.5f, 0.25f, 1, 1 }, model.Materials[0].BaseColor);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void Applies_node_translation()
    {
        var (json, bin) = Triangle();
        json = json.Replace("\"mesh\": 0, \"name\": \"tri\"", "\"mesh\": 0, \"name\": \"tri\", \"translation\": [5, 0, 0]");
        var sub = Assert.Single(GlbReader.Read(Glb(json, bin)).Submeshes);
        Assert.Equal(5f, sub.Positions[0]);
        Assert.Equal(6f, sub.Positions[3]);
    }

    [Fact]
    public void Applies_node_rotation_about_y()
    {
        // 90 degrees about Y (right-handed): glTF (1, 0, 1) becomes (1, 0, -1), and the Z flip makes it (1, 0, 1).
        var (json, bin) = Triangle();
        json = json.Replace("\"mesh\": 0, \"name\": \"tri\"", "\"mesh\": 0, \"name\": \"tri\", \"rotation\": [0, 0.70710678, 0, 0.70710678]");
        var sub = Assert.Single(GlbReader.Read(Glb(json, bin)).Submeshes);
        Assert.InRange(sub.Positions[3], 1 - 1e-4f, 1 + 1e-4f);
        Assert.InRange(sub.Positions[5], 1 - 1e-4f, 1 + 1e-4f);
    }

    [Fact]
    public void Rejects_a_file_that_is_not_glb()
    {
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
        Assert.Contains("wrong header", ex.Message);
    }

    [Fact]
    public void Rejects_a_file_shorter_than_its_header()
    {
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(new byte[] { 1, 2, 3 }));
        Assert.Contains("too short", ex.Message);
    }

    [Fact]
    public void Rejects_truncated_files()
    {
        var (json, bin) = Triangle();
        var bytes = Glb(json, bin);
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(bytes.Take(bytes.Length - 8).ToArray()));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void Rejects_external_buffers()
    {
        var (json, bin) = Triangle();
        json = json.Replace("\"buffers\": [ { \"byteLength\": 102 } ]", "\"buffers\": [ { \"byteLength\": 102, \"uri\": \"other.bin\" } ]");
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(Glb(json, bin)));
        Assert.Contains("another file", ex.Message);
    }

    [Fact]
    public void Rejects_accessors_that_run_past_the_buffer()
    {
        var (json, bin) = Triangle();
        json = json.Replace("{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 3", "{ \"bufferView\": 0, \"componentType\": 5126, \"count\": 30");
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(Glb(json, bin)));
        Assert.Contains("past the end", ex.Message);
    }

    [Fact]
    public void Rejects_indices_that_point_past_the_vertices()
    {
        var (json, _) = Triangle();
        var positions = Floats(0, 0, 1, 1, 0, 1, 0, 1, 1);
        var normals = Floats(0, 0, 1, 0, 0, 1, 0, 0, 1);
        var uvs = Floats(0, 0, 1, 0, 0, 1);
        var indices = new byte[] { 0, 0, 9, 0, 2, 0 };
        var bin = positions.Concat(normals).Concat(uvs).Concat(indices).ToArray();
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(Glb(json, bin)));
        Assert.Contains("refers to vertex 9", ex.Message);
    }

    [Fact]
    public void Skips_triangle_strips_with_a_warning()
    {
        var (json, bin) = Triangle();
        json = json.Replace("\"material\": 0 } ]", "\"material\": 0, \"mode\": 5 } ]");
        var model = GlbReader.Read(Glb(json, bin));
        Assert.Empty(model.Submeshes);
        Assert.Contains(model.Warnings, w => w.Contains("only triangles"));
    }

    [Fact]
    public void Rejects_node_cycles()
    {
        var (json, bin) = Triangle();
        json = json.Replace("\"nodes\": [ { \"mesh\": 0, \"name\": \"tri\" } ]", "\"nodes\": [ { \"mesh\": 0, \"name\": \"tri\", \"children\": [0] } ]");
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(Glb(json, bin)));
        Assert.Contains("loops back", ex.Message);
    }

    [Fact]
    public void Rejects_required_extensions()
    {
        var (json, bin) = Triangle();
        json = json.Replace("\"asset\": { \"version\": \"2.0\" },", "\"asset\": { \"version\": \"2.0\" }, \"extensionsRequired\": [\"KHR_draco_mesh_compression\"],");
        var ex = Assert.Throws<GlbException>(() => GlbReader.Read(Glb(json, bin)));
        Assert.Contains("KHR_draco_mesh_compression", ex.Message);
    }

    [Fact]
    public void Returns_embedded_base_colour_image()
    {
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        var (json, bin) = Triangle();
        bin = bin.Concat(png).ToArray();
        json = json.Replace("\"materials\": [ { \"name\": \"plain\", \"pbrMetallicRoughness\": { \"baseColorFactor\": [0.5, 0.25, 1, 1] } } ],",
            "\"materials\": [ { \"name\": \"plain\", \"pbrMetallicRoughness\": { \"baseColorFactor\": [0.5, 0.25, 1, 1], \"baseColorTexture\": { \"index\": 0 } } } ],");
        json = json.Replace("\"accessors\": [", "\"textures\": [ { \"source\": 0 } ],\n          \"images\": [ { \"bufferView\": 4, \"mimeType\": \"image/png\" } ],\n          \"accessors\": [");
        json = json.Replace("{ \"buffer\": 0, \"byteOffset\": 96, \"byteLength\": 6 }", "{ \"buffer\": 0, \"byteOffset\": 96, \"byteLength\": 6 },\n            { \"buffer\": 0, \"byteOffset\": 102, \"byteLength\": 11 }");
        json = json.Replace("\"byteLength\": 102 } ]", "\"byteLength\": 113 } ]");

        var model = GlbReader.Read(Glb(json, bin));
        Assert.Equal(png, model.Materials[0].BaseColorImage);
        Assert.Equal("image/png", model.Materials[0].BaseColorMimeType);
    }
}
