using System;
using System.Linq;
using DrakesForge.Format;
using Xunit;

namespace DrakesForge.Format.Tests;

public class GlbWriterTests
{
    private static GlbModel Sample() => new()
    {
        Submeshes = new[]
        {
            new GlbSubmesh
            {
                Name = "tri",
                Positions = new float[] { 0, 0, 1, 1, 0, 1, 0, 1, 1 },
                Normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 },
                Uvs = new float[] { 0, 0, 1, 0, 0, 1 },
                Indices = new[] { 0, 1, 2 },
                Material = 0
            },
            new GlbSubmesh
            {
                Name = "quad",
                Positions = new float[] { 0, 0, 0, 2, 0, 0, 2, 2, 0, 0, 2, 0 },
                Indices = new[] { 0, 1, 2, 0, 2, 3 },
                Material = -1
            }
        },
        Materials = new[] { new GlbMaterial { Name = "stone", BaseColor = new[] { 0.5f, 0.25f, 0.125f, 1f }, Blend = true } }
    };

    [Fact]
    public void ReadsBackWhatWasWritten()
    {
        var read = GlbReader.Read(GlbWriter.Write(Sample()));

        Assert.Equal(2, read.Submeshes.Count);
        var tri = read.Submeshes[0];
        Assert.Equal("tri", tri.Name);
        Assert.Equal(new float[] { 0, 0, 1, 1, 0, 1, 0, 1, 1 }, tri.Positions);
        Assert.Equal(new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }, tri.Normals);
        Assert.Equal(new float[] { 0, 0, 1, 0, 0, 1 }, tri.Uvs);
        Assert.Equal(new[] { 0, 1, 2 }, tri.Indices);
        Assert.Equal(0, tri.Material);
        Assert.Equal(new[] { 0, 1, 2, 0, 2, 3 }, read.Submeshes[1].Indices);
        Assert.Equal(-1, read.Submeshes[1].Material);
        Assert.Empty(read.Warnings);
    }

    [Fact]
    public void KeepsMaterialColourBlendAndTexture()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var model = Sample();
        model.Materials[0].BaseColorImage = png;
        model.Materials[0].BaseColorMimeType = "image/png";

        var material = GlbReader.Read(GlbWriter.Write(model)).Materials.Single();

        Assert.Equal("stone", material.Name);
        Assert.Equal(new[] { 0.5f, 0.25f, 0.125f, 1f }, material.BaseColor);
        Assert.True(material.Blend);
        Assert.Equal(png, material.BaseColorImage);
    }

    [Fact]
    public void FlipsUnityIntoGltfSpace()
    {
        // Unity (0,0,1) is glTF (0,0,-1); V runs bottom-up in Unity and top-down in glTF.
        var data = GlbWriter.Write(Sample());
        var json = System.Text.Encoding.UTF8.GetString(data, 20, (int)BitConverter.ToUInt32(data, 12));
        Assert.Contains("\"min\":[0,0,-1]", json);
        Assert.Contains("\"max\":[1,1,-1]", json);
    }

    [Fact]
    public void RefusesAModelWithNoGeometry() =>
        Assert.Throws<GlbException>(() => GlbWriter.Write(new GlbModel()));

    [Fact]
    public void RefusesFacesOutsideTheVertices()
    {
        var model = new GlbModel
        {
            Submeshes = new[] { new GlbSubmesh { Positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, Indices = new[] { 0, 1, 3 } } }
        };
        Assert.Throws<GlbException>(() => GlbWriter.Write(model));
    }
}
