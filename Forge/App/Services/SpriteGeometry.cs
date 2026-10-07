using System.Numerics;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>A sprite's quad for the software renderer, built the way the runtime builds it.</summary>
public static class SpriteGeometry
{
    /// <summary>
    /// Pivot at the bottom centre, facing +Z, Unity's Z-X-Y rotation order (degrees), converted to the viewport's
    /// axes (X mirrored). The image reads correctly from the front.
    /// </summary>
    public static ModelPart Part(string name, double width, double height, Vector3 position, Vector3 rotationDegrees, int slot)
    {
        var w = (float)Math.Max(width, 0.001) / 2f;
        var h = (float)Math.Max(height, 0.001);
        const float deg = MathF.PI / 180f;
        var rotation = Matrix4x4.CreateRotationZ(rotationDegrees.Z * deg) * Matrix4x4.CreateRotationX(rotationDegrees.X * deg) *
                       Matrix4x4.CreateRotationY(rotationDegrees.Y * deg);
        var corners = new[] { new Vector3(-w, 0, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, 0, 0) };
        var normal = Vector3.TransformNormal(new Vector3(0, 0, 1), rotation);

        var positions = new float[12];
        var normals = new float[12];
        for (var i = 0; i < 4; i++)
        {
            var p = Vector3.Transform(corners[i], rotation) + position;
            positions[i * 3] = -p.X;
            positions[i * 3 + 1] = p.Y;
            positions[i * 3 + 2] = p.Z;
            normals[i * 3] = -normal.X;
            normals[i * 3 + 1] = normal.Y;
            normals[i * 3 + 2] = normal.Z;
        }

        return new ModelPart
        {
            Name = "sprite " + name,
            Positions = positions,
            Normals = normals,
            Uvs = new float[] { 1, 0, 1, 1, 0, 1, 0, 0 },
            Indices = new[] { 0, 1, 2, 0, 2, 3 },
            MaterialSlot = slot
        };
    }
}
