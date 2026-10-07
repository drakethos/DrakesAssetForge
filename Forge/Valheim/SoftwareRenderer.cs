using System.Numerics;

namespace DrakesForge.Valheim;

/// <summary>How one material slot draws. Lets the editor preview tints and swapped materials without touching the model.</summary>
public readonly record struct SlotLook(RgbaImage? Albedo, Vector4 Color, bool Highlight = false);

/// <summary>
/// A dot drawn over the model (snap points). Position is in Unity's axes, local to the prefab root.
/// <paramref name="Id"/> ≥ 0 makes it draggable in the viewport; <paramref name="Label"/> is drawn beside it.
/// </summary>
public readonly record struct Marker(Vector3 UnityPosition, uint Argb, int Size = 9, int Id = -1, string? Label = null, bool Selected = false);

public sealed class OrbitCamera
{
    public float Yaw { get; set; } = 0.7f;
    public float Pitch { get; set; } = 0.3f;
    public float Zoom { get; set; } = 1f;
}

/// <summary>
/// The camera of one rendered frame: Unity-space points to screen pixels and back. The viewport mirrors X
/// (Unity is left-handed), which this hides from callers.
/// </summary>
public readonly struct Projection
{
    private readonly Matrix4x4 _viewProj;
    private readonly Matrix4x4 _inverse;
    private readonly int _width;
    private readonly int _height;

    public Projection(Matrix4x4 viewProj, Vector3 eye, int width, int height)
    {
        _viewProj = viewProj;
        Matrix4x4.Invert(viewProj, out _inverse);
        Eye = new Vector3(-eye.X, eye.Y, eye.Z);
        _width = width;
        _height = height;
    }

    /// <summary>Camera position, Unity space.</summary>
    public Vector3 Eye { get; }

    /// <summary>Screen pixel of a Unity-space point, or null when it's behind the camera.</summary>
    public Vector2? ToScreen(Vector3 unity)
    {
        var clip = Vector4.Transform(new Vector4(-unity.X, unity.Y, unity.Z, 1f), _viewProj);
        if (clip.W <= 1e-5f)
            return null;
        return new Vector2((clip.X / clip.W * 0.5f + 0.5f) * _width, (0.5f - clip.Y / clip.W * 0.5f) * _height);
    }

    /// <summary>The ray under a screen pixel: origin and unit direction, Unity space.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(float x, float y)
    {
        var nx = x / _width * 2f - 1f;
        var ny = 1f - y / _height * 2f;
        var near = Unproject(new Vector4(nx, ny, 0f, 1f));
        var far = Unproject(new Vector4(nx, ny, 1f, 1f));
        return (near, Vector3.Normalize(far - near));
    }

    private Vector3 Unproject(Vector4 ndc)
    {
        var p = Vector4.Transform(ndc, _inverse);
        return new Vector3(-p.X / p.W, p.Y / p.W, p.Z / p.W);
    }
}

/// <summary>
/// Small CPU rasterizer for previews: perspective, depth buffer, textured, lambert + ambient.
/// Approximate on purpose: the real Valheim shaders are only seen in game (Push to game).
/// </summary>
public static class SoftwareRenderer
{
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(-0.4f, 0.8f, 0.45f));

    public static RgbaImage Render(IReadOnlyList<ModelPart> parts, Func<int, SlotLook> look, OrbitCamera camera, int width, int height,
        IReadOnlyList<Marker>? markers = null, uint background = 0xFF0F1113)
    {
        var color = new byte[width * height * 4];
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);
        Clear(color, background);
        if (parts.Count == 0)
            return new RgbaImage { Width = width, Height = height, Bgra = color };

        var viewProj = ViewProjection(parts, camera, width, height, out _);

        foreach (var part in parts)
        {
            var slot = look(part.MaterialSlot);
            var vertCount = part.Positions.Length / 3;
            var screen = new Vector4[vertCount];
            var shade = new float[vertCount];
            for (var i = 0; i < vertCount; i++)
            {
                var p = new Vector3(part.Positions[i * 3], part.Positions[i * 3 + 1], part.Positions[i * 3 + 2]);
                var clip = Vector4.Transform(new Vector4(p, 1f), viewProj);
                var w = clip.W <= 1e-5f ? 1e-5f : clip.W;
                screen[i] = new Vector4((clip.X / w * 0.5f + 0.5f) * width, (0.5f - clip.Y / w * 0.5f) * height, clip.Z / w, 1f / w);
                var n = new Vector3(part.Normals[i * 3], part.Normals[i * 3 + 1], part.Normals[i * 3 + 2]);
                // Two-sided: many Valheim meshes (bars, leaves, cloth) are thin and drawn without culling.
                shade[i] = 0.38f + 0.72f * MathF.Abs(Vector3.Dot(n, LightDir));
            }

            for (var t = 0; t + 2 < part.Indices.Length; t += 3)
                Triangle(part, slot, screen, shade, part.Indices[t], part.Indices[t + 1], part.Indices[t + 2], color, depth, width, height);
        }

        // Markers draw on top so snap points behind the model stay visible.
        foreach (var marker in markers ?? Array.Empty<Marker>())
        {
            var p = new Vector3(-marker.UnityPosition.X, marker.UnityPosition.Y, marker.UnityPosition.Z);
            var clip = Vector4.Transform(new Vector4(p, 1f), viewProj);
            if (clip.W <= 0)
                continue;
            var sx = (int)((clip.X / clip.W * 0.5f + 0.5f) * width);
            var sy = (int)((0.5f - clip.Y / clip.W * 0.5f) * height);
            Dot(color, width, height, sx, sy, marker.Size, marker.Argb, marker.Selected);
        }

        return new RgbaImage { Width = width, Height = height, Bgra = color };
    }

    /// <summary>The frame's camera, for hit-testing and dragging markers over a rendered image.</summary>
    public static Projection Project(IReadOnlyList<ModelPart> parts, OrbitCamera camera, int width, int height)
    {
        var viewProj = ViewProjection(parts, camera, width, height, out var eye);
        return new Projection(viewProj, eye, width, height);
    }

    // Framed on the model only, so dragging a snap point never moves the camera.
    private static Matrix4x4 ViewProjection(IReadOnlyList<ModelPart> parts, OrbitCamera camera, int width, int height, out Vector3 eye)
    {
        var (center, radius) = Bounds(parts);
        // A little room around the model, so points on its outline stay grabbable.
        var distance = radius * 3.6f / Math.Max(camera.Zoom, 0.05f);
        eye = center + distance * new Vector3(
            MathF.Cos(camera.Pitch) * MathF.Sin(camera.Yaw),
            MathF.Sin(camera.Pitch),
            MathF.Cos(camera.Pitch) * MathF.Cos(camera.Yaw));
        var view = Matrix4x4.CreateLookAt(eye, center, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 5.5f, width / (float)height, radius * 0.05f, distance + radius * 4);
        return view * proj;
    }

    private static void Dot(byte[] color, int width, int height, int cx, int cy, int size, uint argb, bool selected = false)
    {
        if (selected)
            Dot(color, width, height, cx, cy, size + 6, 0xFFFFFFFF);
        var r = size / 2;
        for (var y = cy - r - 1; y <= cy + r + 1; y++)
        for (var x = cx - r - 1; x <= cx + r + 1; x++)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
                continue;
            var d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d2 > (r + 1) * (r + 1))
                continue;
            var edge = d2 > r * r;
            var o = (y * width + x) * 4;
            color[o] = edge ? (byte)0x10 : (byte)argb;
            color[o + 1] = edge ? (byte)0x10 : (byte)(argb >> 8);
            color[o + 2] = edge ? (byte)0x10 : (byte)(argb >> 16);
            color[o + 3] = 255;
        }
    }

    private static void Triangle(ModelPart part, SlotLook slot, Vector4[] s, float[] shade, int a, int b, int c, byte[] color, float[] depth, int width, int height)
    {
        var p0 = s[a];
        var p1 = s[b];
        var p2 = s[c];
        if (p0.W <= 0 || p1.W <= 0 || p2.W <= 0)
            return; // behind the camera

        var area = Edge(p0, p1, p2.X, p2.Y);
        if (MathF.Abs(area) < 1e-6f)
            return;

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        if (minX > maxX || minY > maxY)
            return;

        var uv = part.Uvs;
        var tex = slot.Albedo;
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var px = x + 0.5f;
            var py = y + 0.5f;
            var w0 = Edge(p1, p2, px, py) / area;
            var w1 = Edge(p2, p0, px, py) / area;
            var w2 = 1f - w0 - w1;
            if (w0 < 0 || w1 < 0 || w2 < 0)
                continue;

            var z = w0 * p0.Z + w1 * p1.Z + w2 * p2.Z;
            var idx = y * width + x;
            if (z >= depth[idx])
                continue;

            // Perspective-correct interpolation via 1/w.
            var iw = w0 * p0.W + w1 * p1.W + w2 * p2.W;
            var c0 = w0 * p0.W / iw;
            var c1 = w1 * p1.W / iw;
            var c2 = w2 * p2.W / iw;

            var r = slot.Color.X;
            var g = slot.Color.Y;
            var bl = slot.Color.Z;
            if (tex != null && uv.Length > 0)
            {
                var u = c0 * uv[a * 2] + c1 * uv[b * 2] + c2 * uv[c * 2];
                var v = c0 * uv[a * 2 + 1] + c1 * uv[b * 2 + 1] + c2 * uv[c * 2 + 1];
                var tx = (int)((u - MathF.Floor(u)) * tex.Width) % tex.Width;
                var ty = (int)((1f - (v - MathF.Floor(v))) * tex.Height) % tex.Height;
                var ti = (Math.Max(ty, 0) * tex.Width + Math.Max(tx, 0)) * 4;
                if (tex.Bgra[ti + 3] < 64)
                    continue; // alpha-cut foliage, chains, etc.
                bl *= tex.Bgra[ti] / 255f;
                g *= tex.Bgra[ti + 1] / 255f;
                r *= tex.Bgra[ti + 2] / 255f;
            }

            var light = c0 * shade[a] + c1 * shade[b] + c2 * shade[c];
            if (slot.Highlight)
            {
                r = r * 0.6f + 0.35f;
                g = g * 0.6f + 0.22f;
                bl *= 0.6f;
            }

            depth[idx] = z;
            var o = idx * 4;
            color[o] = ToByte(bl * light);
            color[o + 1] = ToByte(g * light);
            color[o + 2] = ToByte(r * light);
            color[o + 3] = 255;
        }
    }

    private static float Edge(Vector4 a, Vector4 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f), 0, 255);

    private static void Clear(byte[] color, uint argb)
    {
        for (var i = 0; i < color.Length; i += 4)
        {
            color[i] = (byte)argb;
            color[i + 1] = (byte)(argb >> 8);
            color[i + 2] = (byte)(argb >> 16);
            color[i + 3] = (byte)(argb >> 24);
        }
    }

    /// <summary>Framing box: the model plus any markers, so snap points just outside the mesh stay on screen.</summary>
    public static (Vector3 Center, float Radius) Bounds(IReadOnlyList<ModelPart> parts, IReadOnlyList<Marker>? markers = null)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in parts)
            for (var i = 0; i + 2 < part.Positions.Length; i += 3)
            {
                var p = new Vector3(part.Positions[i], part.Positions[i + 1], part.Positions[i + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

        foreach (var m in markers ?? Array.Empty<Marker>())
        {
            var p = new Vector3(-m.UnityPosition.X, m.UnityPosition.Y, m.UnityPosition.Z);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        if (min.X > max.X)
            return (Vector3.Zero, 1f);
        return ((min + max) * 0.5f, Math.Max(Vector3.Distance(min, max) * 0.5f, 0.01f));
    }

    /// <summary>The look a slot has with no overrides: its texture tinted by _Color.</summary>
    public static SlotLook DefaultLook(VanillaModel model, int slot)
    {
        if (slot < 0 || slot >= model.Materials.Count)
            return new SlotLook(null, new Vector4(0.8f, 0.8f, 0.8f, 1f));
        var m = model.Materials[slot];
        var c = m.Info.Color;
        return new SlotLook(m.Albedo, new Vector4(c[0], c[1], c[2], c[3]));
    }
}
