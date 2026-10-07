using Assimp;
using StbImageSharp;

namespace DrakeAssetForge.Services;

public sealed class PreviewMesh
{
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] Uvs { get; init; }
    public required int[] Indices { get; init; }
    public byte[]? DiffuseRgba { get; init; }
    public int DiffuseWidth { get; init; }
    public int DiffuseHeight { get; init; }
    public int VertexCount => Positions.Length / 3;
    public int TriangleCount => Indices.Length / 3;
}

public static class MeshPreviewLoader
{
    public static PreviewMesh Load(string meshPath, string? diffusePath)
    {
        using var ctx = new AssimpContext();
        var scene = ctx.ImportFile(meshPath,
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.PreTransformVertices |
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.FlipUVs);

        if (scene == null || !scene.HasMeshes)
            throw new InvalidOperationException("No mesh data in file.");

        var vertCount = scene.Meshes.Sum(m => m.VertexCount);
        var indexCount = scene.Meshes.Sum(m => m.FaceCount * 3);
        var positions = new float[vertCount * 3];
        var normals = new float[vertCount * 3];
        var uvs = new float[vertCount * 2];
        var indices = new int[indexCount];
        var vBase = 0;
        var iBase = 0;

        foreach (var mesh in scene.Meshes)
        {
            var hasUv = mesh.TextureCoordinateChannelCount > 0 && mesh.TextureCoordinateChannels[0].Count == mesh.VertexCount;
            for (var i = 0; i < mesh.VertexCount; i++)
            {
                var p = mesh.Vertices[i];
                var nx = 0f;
                var ny = 1f;
                var nz = 0f;
                if (mesh.HasNormals)
                {
                    var n = mesh.Normals[i];
                    nx = n.X;
                    ny = n.Y;
                    nz = n.Z;
                }
                positions[(vBase + i) * 3] = p.X;
                positions[(vBase + i) * 3 + 1] = p.Y;
                positions[(vBase + i) * 3 + 2] = p.Z;
                normals[(vBase + i) * 3] = nx;
                normals[(vBase + i) * 3 + 1] = ny;
                normals[(vBase + i) * 3 + 2] = nz;
                if (hasUv)
                {
                    var uv = mesh.TextureCoordinateChannels[0][i];
                    uvs[(vBase + i) * 2] = uv.X;
                    uvs[(vBase + i) * 2 + 1] = uv.Y;
                }
            }

            foreach (var face in mesh.Faces)
            {
                if (face.IndexCount < 3)
                    continue;
                indices[iBase++] = vBase + face.Indices[0];
                indices[iBase++] = vBase + face.Indices[1];
                indices[iBase++] = vBase + face.Indices[2];
            }

            vBase += mesh.VertexCount;
        }

        if (iBase < indices.Length)
            Array.Resize(ref indices, iBase);

        byte[]? rgba = null;
        var dw = 0;
        var dh = 0;
        if (!string.IsNullOrWhiteSpace(diffusePath) && File.Exists(diffusePath))
        {
            using var stream = File.OpenRead(diffusePath);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            rgba = image.Data;
            dw = image.Width;
            dh = image.Height;
        }

        return new PreviewMesh
        {
            Positions = positions,
            Normals = normals,
            Uvs = uvs,
            Indices = indices,
            DiffuseRgba = rgba,
            DiffuseWidth = dw,
            DiffuseHeight = dh,
        };
    }
}
