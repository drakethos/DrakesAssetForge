using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DrakeAssetForge.Services;

namespace DrakeAssetForge.Views;

/// <summary>
/// Drag-to-orbit preview of an attached FBX. No Unity — Assimp loads the mesh and a software raster draws it.
/// </summary>
public sealed class MeshOrbitView : UserControl
{
    public static readonly StyledProperty<string?> MeshPathProperty =
        AvaloniaProperty.Register<MeshOrbitView, string?>(nameof(MeshPath));

    public static readonly StyledProperty<string?> DiffusePathProperty =
        AvaloniaProperty.Register<MeshOrbitView, string?>(nameof(DiffusePath));

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _hint;

    private PreviewMesh? _mesh;
    private int _request;
    private float _yaw = 0.8f;
    private float _pitch = 0.35f;
    private float _distance = 2.6f;
    private Point? _last;

    public string? MeshPath
    {
        get => GetValue(MeshPathProperty);
        set => SetValue(MeshPathProperty, value);
    }

    public string? DiffusePath
    {
        get => GetValue(DiffusePathProperty);
        set => SetValue(DiffusePathProperty, value);
    }

    public MeshOrbitView()
    {
        _hint = new TextBlock
        {
            Text = "Drag to orbit · wheel to zoom",
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(8),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
        };
        Content = new Panel
        {
            Children = { _image, _hint },
        };
        ClipToBounds = true;
    }

    static MeshOrbitView()
    {
        MeshPathProperty.Changed.AddClassHandler<MeshOrbitView>((view, _) => view.Reload());
        DiffusePathProperty.Changed.AddClassHandler<MeshOrbitView>((view, _) => view.Reload());
    }

    private void Reload()
    {
        var path = MeshPath;
        var diffuse = DiffusePath;
        var ticket = ++_request;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _mesh = null;
            _image.Source = null;
            _hint.Text = "";
            return;
        }

        _hint.Text = "Loading mesh…";
        Task.Run(() =>
        {
            try
            {
                return (MeshPreviewLoader.Load(path, diffuse), (string?)null);
            }
            catch (Exception ex)
            {
                return ((PreviewMesh?)null, ex.Message);
            }
        }).ContinueWith(t =>
        {
            if (ticket != _request)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (ticket != _request)
                    return;
                var (mesh, error) = t.Result;
                _mesh = mesh;
                if (mesh == null)
                {
                    _hint.Text = error ?? "Mesh failed to load.";
                    _image.Source = null;
                    return;
                }

                _hint.Text = $"{mesh.VertexCount:N0} verts · {mesh.TriangleCount:N0} tris · drag to orbit";
                Render();
            });
        });
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _last = e.GetPosition(this);
        e.Pointer.Capture(this);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _last = null;
        e.Pointer.Capture(null);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_last == null || _mesh == null)
            return;
        var pos = e.GetPosition(this);
        var dx = (float)(pos.X - _last.Value.X);
        var dy = (float)(pos.Y - _last.Value.Y);
        _last = pos;
        _yaw += dx * 0.01f;
        _pitch = Math.Clamp(_pitch + dy * 0.01f, -1.2f, 1.2f);
        Render();
        base.OnPointerMoved(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance - (float)e.Delta.Y * 0.15f, 1.2f, 8f);
        Render();
        base.OnPointerWheelChanged(e);
    }

    private void Render()
    {
        var mesh = _mesh;
        if (mesh == null)
            return;

        var w = Math.Clamp((int)Bounds.Width, 64, 900);
        var h = Math.Clamp((int)Bounds.Height, 64, 700);
        if (Bounds.Width < 8 || Bounds.Height < 8)
        {
            w = 480;
            h = 320;
        }

        var yaw = _yaw;
        var pitch = _pitch;
        var distance = _distance;
        var ticket = _request;
        Task.Run(() => Raster(mesh, w, h, yaw, pitch, distance)).ContinueWith(t =>
        {
            if (t.IsFaulted || ticket != _request)
                return;
            var pixels = t.Result;
            Dispatcher.UIThread.Post(() =>
            {
                if (ticket != _request)
                    return;
                var bmp = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                using (var fb = bmp.Lock())
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
                }

                var old = _image.Source;
                _image.Source = bmp;
                (old as IDisposable)?.Dispose();
            });
        });
    }

    private static byte[] Raster(PreviewMesh mesh, int width, int height, float yaw, float pitch, float distance)
    {
        var pos = mesh.Positions;
        var nrm = mesh.Normals;
        var uvs = mesh.Uvs;
        var idx = mesh.Indices;
        var count = pos.Length / 3;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < count; i++)
        {
            var p = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        var center = (min + max) * 0.5f;
        var radius = Math.Max(0.001f, (max - min).Length() * 0.5f);
        var eyeDist = radius * distance;
        var cp = MathF.Cos(pitch);
        var eye = center + new Vector3(MathF.Sin(yaw) * cp, MathF.Sin(pitch), MathF.Cos(yaw) * cp) * eyeDist;
        var forward = Vector3.Normalize(center - eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        if (right.LengthSquared() < 1e-6f)
            right = Vector3.UnitX;
        var up = Vector3.Cross(right, forward);
        var light = Vector3.Normalize(eye - center);

        var pixels = new byte[width * height * 4];
        var depth = new float[width * height];
        Array.Fill(depth, float.MaxValue);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 28;
            pixels[i + 1] = 30;
            pixels[i + 2] = 34;
            pixels[i + 3] = 255;
        }

        var focal = height * 0.9f;
        Span<Vector3> view = stackalloc Vector3[3];
        Span<Vector2> screen = stackalloc Vector2[3];
        Span<float> zc = stackalloc float[3];

        for (var t = 0; t + 2 < idx.Length; t += 3)
        {
            var ia = idx[t];
            var ib = idx[t + 1];
            var ic = idx[t + 2];
            if ((uint)ia >= (uint)count || (uint)ib >= (uint)count || (uint)ic >= (uint)count)
                continue;

            Project(ia, pos, center, eye, right, up, forward, focal, width, height, out view[0], out screen[0], out zc[0]);
            Project(ib, pos, center, eye, right, up, forward, focal, width, height, out view[1], out screen[1], out zc[1]);
            Project(ic, pos, center, eye, right, up, forward, focal, width, height, out view[2], out screen[2], out zc[2]);
            if (zc[0] <= 0.05f || zc[1] <= 0.05f || zc[2] <= 0.05f)
                continue;

            var n = new Vector3(
                nrm[ia * 3] + nrm[ib * 3] + nrm[ic * 3],
                nrm[ia * 3 + 1] + nrm[ib * 3 + 1] + nrm[ic * 3 + 1],
                nrm[ia * 3 + 2] + nrm[ib * 3 + 2] + nrm[ic * 3 + 2]);
            if (n.LengthSquared() < 1e-8f)
                n = Vector3.UnitY;
            else
                n = Vector3.Normalize(n);
            var shade = 0.28f + 0.72f * MathF.Abs(Vector3.Dot(n, light));

            var minX = (int)MathF.Floor(MathF.Min(screen[0].X, MathF.Min(screen[1].X, screen[2].X)));
            var maxX = (int)MathF.Ceiling(MathF.Max(screen[0].X, MathF.Max(screen[1].X, screen[2].X)));
            var minY = (int)MathF.Floor(MathF.Min(screen[0].Y, MathF.Min(screen[1].Y, screen[2].Y)));
            var maxY = (int)MathF.Ceiling(MathF.Max(screen[0].Y, MathF.Max(screen[1].Y, screen[2].Y)));
            if (maxX < 0 || maxY < 0 || minX >= width || minY >= height)
                continue;
            minX = Math.Clamp(minX, 0, width - 1);
            maxX = Math.Clamp(maxX, 0, width - 1);
            minY = Math.Clamp(minY, 0, height - 1);
            maxY = Math.Clamp(maxY, 0, height - 1);

            var area = Edge(screen[0], screen[1], screen[2]);
            if (MathF.Abs(area) < 0.5f)
                continue;

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var w0 = Edge(screen[1], screen[2], p) / area;
                    var w1 = Edge(screen[2], screen[0], p) / area;
                    var w2 = 1f - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0)
                        continue;
                    var z = zc[0] * w0 + zc[1] * w1 + zc[2] * w2;
                    var di = y * width + x;
                    if (z >= depth[di])
                        continue;
                    depth[di] = z;

                    byte r = (byte)(170 * shade);
                    byte g = (byte)(174 * shade);
                    byte b = (byte)(180 * shade);
                    if (mesh.DiffuseRgba != null && mesh.DiffuseWidth > 0)
                    {
                        var u = uvs[ia * 2] * w0 + uvs[ib * 2] * w1 + uvs[ic * 2] * w2;
                        var v = uvs[ia * 2 + 1] * w0 + uvs[ib * 2 + 1] * w1 + uvs[ic * 2 + 1] * w2;
                        Sample(mesh, u, v, shade, out b, out g, out r);
                    }

                    var o = di * 4;
                    pixels[o] = b;
                    pixels[o + 1] = g;
                    pixels[o + 2] = r;
                    pixels[o + 3] = 255;
                }
            }
        }

        return pixels;
    }

    private static void Project(
        int index,
        float[] pos,
        Vector3 center,
        Vector3 eye,
        Vector3 right,
        Vector3 up,
        Vector3 forward,
        float focal,
        int width,
        int height,
        out Vector3 view,
        out Vector2 screen,
        out float z)
    {
        var p = new Vector3(pos[index * 3], pos[index * 3 + 1], pos[index * 3 + 2]) - eye;
        view = new Vector3(Vector3.Dot(p, right), Vector3.Dot(p, up), Vector3.Dot(p, forward));
        z = view.Z;
        var inv = focal / Math.Max(0.05f, z);
        screen = new Vector2(width * 0.5f + view.X * inv, height * 0.5f - view.Y * inv);
        _ = center;
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

    private static void Sample(PreviewMesh mesh, float u, float v, float shade, out byte b, out byte g, out byte r)
    {
        var x = (int)(Math.Clamp(u - MathF.Floor(u), 0, 0.999f) * (mesh.DiffuseWidth - 1));
        var y = (int)(Math.Clamp(v - MathF.Floor(v), 0, 0.999f) * (mesh.DiffuseHeight - 1));
        var o = (y * mesh.DiffuseWidth + x) * 4;
        var data = mesh.DiffuseRgba!;
        r = (byte)(data[o] * shade);
        g = (byte)(data[o + 1] * shade);
        b = (byte)(data[o + 2] * shade);
    }
}
