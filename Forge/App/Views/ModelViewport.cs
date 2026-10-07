using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DrakesForge.App.Services;
using DrakesForge.Valheim;
using Vector3 = System.Numerics.Vector3;

namespace DrakesForge.App.Views;

/// <summary>
/// Orbit viewport over <see cref="SoftwareRenderer"/>: drag to orbit, wheel to zoom, double-click to reset.
/// With <see cref="MarkerEditing"/> on, markers with an id can be picked and dragged (locked to an axis or the floor),
/// and Delete removes the selected one.
/// </summary>
public sealed class ModelViewport : Control
{
    public static readonly StyledProperty<IReadOnlyList<ModelPart>?> PartsProperty =
        AvaloniaProperty.Register<ModelViewport, IReadOnlyList<ModelPart>?>(nameof(Parts));

    public static readonly StyledProperty<Func<int, SlotLook>?> LookProperty =
        AvaloniaProperty.Register<ModelViewport, Func<int, SlotLook>?>(nameof(Look));

    public static readonly StyledProperty<IReadOnlyList<Marker>?> MarkersProperty =
        AvaloniaProperty.Register<ModelViewport, IReadOnlyList<Marker>?>(nameof(Markers));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ModelViewport, string?>(nameof(Message));

    public static readonly StyledProperty<bool> MarkerEditingProperty =
        AvaloniaProperty.Register<ModelViewport, bool>(nameof(MarkerEditing));

    /// <summary>"free" (in the plane facing the camera), "x", "y", "z" (along that axis), or "floor" (level, X and Z).</summary>
    public static readonly StyledProperty<string> ConstraintProperty =
        AvaloniaProperty.Register<ModelViewport, string>(nameof(Constraint), "free");

    public static readonly StyledProperty<IMarkerEditor?> EditorProperty =
        AvaloniaProperty.Register<ModelViewport, IMarkerEditor?>(nameof(Editor));

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0F1113"));
    private static readonly IBrush MessageBrush = new SolidColorBrush(Color.Parse("#A3A9B0"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#F6C08F"));
    private static readonly IBrush LabelBack = new SolidColorBrush(Color.Parse("#C0121417"));
    private static readonly IPen AxisX = new Pen(new SolidColorBrush(Color.Parse("#E0605A")), 2);
    private static readonly IPen AxisY = new Pen(new SolidColorBrush(Color.Parse("#7CC48A")), 2);
    private static readonly IPen AxisZ = new Pen(new SolidColorBrush(Color.Parse("#6FA8E8")), 2);

    private WriteableBitmap? _bitmap;
    private bool _dirty = true;
    private Point? _orbit;
    private Projection? _projection;

    // Marker drag: which marker, where it started, where the pointer first hit the drag plane/axis.
    private int _dragId = -1;
    private Vector3 _dragStart;
    private Vector3 _dragHit;

    static ModelViewport()
    {
        AffectsRender<ModelViewport>(PartsProperty, LookProperty, MarkersProperty, MessageProperty, ConstraintProperty);
        PartsProperty.Changed.AddClassHandler<ModelViewport>((v, _) => v._dirty = true);
        LookProperty.Changed.AddClassHandler<ModelViewport>((v, _) => v._dirty = true);
        MarkersProperty.Changed.AddClassHandler<ModelViewport>((v, _) => v._dirty = true);
    }

    public ModelViewport()
    {
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    public IReadOnlyList<ModelPart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public Func<int, SlotLook>? Look
    {
        get => GetValue(LookProperty);
        set => SetValue(LookProperty, value);
    }

    public IReadOnlyList<Marker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool MarkerEditing
    {
        get => GetValue(MarkerEditingProperty);
        set => SetValue(MarkerEditingProperty, value);
    }

    public string Constraint
    {
        get => GetValue(ConstraintProperty);
        set => SetValue(ConstraintProperty, value);
    }

    public IMarkerEditor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Current view angle; "Render icon" uses it so the icon matches what you see.</summary>
    public OrbitCamera Camera { get; } = new();

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);

        var parts = Parts;
        if (parts == null || parts.Count == 0 || bounds.Width < 8 || bounds.Height < 8)
        {
            _projection = null;
            DrawMessage(context, Message ?? (parts == null ? "Loading…" : "Nothing to show"));
            return;
        }

        var w = (int)bounds.Width;
        var h = (int)bounds.Height;
        if (_dirty || _bitmap == null || _bitmap.PixelSize.Width != w || _bitmap.PixelSize.Height != h)
        {
            var look = Look ?? (_ => new SlotLook(null, new System.Numerics.Vector4(0.75f, 0.75f, 0.75f, 1)));
            var image = SoftwareRenderer.Render(parts, look, Camera, w, h, Markers);
            _bitmap = Images.Update(_bitmap, image);
            _projection = SoftwareRenderer.Project(parts, Camera, w, h);
            _dirty = false;
        }

        context.DrawImage(_bitmap, new Rect(0, 0, w, h));
        if (_projection is { } projection && Markers is { Count: > 0 } markers)
        {
            if (_dragId >= 0 && Constraint is "x" or "y" or "z")
                DrawAxisGuide(context, projection, _dragStart);
            foreach (var m in markers)
                if (m.Label != null && projection.ToScreen(m.UnityPosition) is { } s)
                    DrawLabel(context, m.Label, new Point(s.X + 9, s.Y - 20));
            DrawCompass(context, projection, parts);
        }

        if (Message != null)
            DrawMessage(context, Message, overModel: true);
    }

    private static void DrawLabel(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        context.DrawRectangle(LabelBack, null, new Rect(at.X - 3, at.Y - 1, formatted.Width + 6, formatted.Height + 2), 3, 3);
        context.DrawText(formatted, at);
    }

    /// <summary>A long line through the dragged point along the locked axis, in that axis' colour.</summary>
    private void DrawAxisGuide(DrawingContext context, Projection projection, Vector3 through)
    {
        var (axis, pen) = Constraint switch
        {
            "x" => (Vector3.UnitX, AxisX),
            "y" => (Vector3.UnitY, AxisY),
            _ => (Vector3.UnitZ, AxisZ)
        };
        var len = Math.Max(Vector3.Distance(projection.Eye, through), 1f);
        if (projection.ToScreen(through - axis * len) is { } a && projection.ToScreen(through + axis * len) is { } b)
            context.DrawLine(pen, new Point(a.X, a.Y), new Point(b.X, b.Y));
    }

    /// <summary>Bottom-left compass: which way is up, front (+Z) and right (-X), matching the point labels.</summary>
    private void DrawCompass(DrawingContext context, Projection projection, IReadOnlyList<ModelPart> parts)
    {
        var center = SnapGeometry.Bounds(parts).Center;
        if (projection.ToScreen(center) is not { } c)
            return;
        var origin = new Point(64, Bounds.Height - 52);
        var step = Math.Max(Vector3.Distance(projection.Eye, center) * 0.05f, 0.01f);
        foreach (var (dir, pen, name) in new[] { (-Vector3.UnitX, AxisX, "right"), (Vector3.UnitY, AxisY, "up"), (Vector3.UnitZ, AxisZ, "front") })
        {
            if (projection.ToScreen(center + dir * step) is not { } tip)
                continue;
            var d = new Vector(tip.X - c.X, tip.Y - c.Y);
            var length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (length < 0.01)
                continue;
            var end = origin + d / length * 26;
            context.DrawLine(pen, origin, end);
            var text = new FormattedText(name, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 10, pen.Brush!);
            var at = end + d / length * (text.Width / 2 + 4) - new Vector(text.Width / 2, text.Height / 2);
            context.DrawRectangle(LabelBack, null, new Rect(at.X - 2, at.Y, text.Width + 4, text.Height), 3, 3);
            context.DrawText(text, at);
        }
    }

    private static readonly IBrush NoteBackground = new SolidColorBrush(Color.Parse("#D01A1D21"));

    /// <summary>Centered when there's nothing to show; a banner along the top when it would cover the model.</summary>
    private void DrawMessage(DrawingContext context, string text, bool overModel = false)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 13, MessageBrush)
        {
            MaxTextWidth = Math.Max(100, Bounds.Width - 48)
        };
        if (!overModel)
        {
            context.DrawText(formatted, new Point((Bounds.Width - formatted.Width) / 2, (Bounds.Height - formatted.Height) / 2));
            return;
        }

        var box = new Rect((Bounds.Width - formatted.Width) / 2 - 12, 10, formatted.Width + 24, formatted.Height + 12);
        context.DrawRectangle(NoteBackground, null, box, 6, 6);
        context.DrawText(formatted, new Point(box.X + 12, box.Y + 6));
    }

    // ---- input ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var at = e.GetPosition(this);
        if (MarkerEditing && HitMarker(at) is { } marker && _projection is { } projection)
        {
            Editor?.SelectMarker(marker.Id);
            _dragId = marker.Id;
            _dragStart = marker.UnityPosition;
            _dragHit = Intersect(projection, at, _dragStart) ?? _dragStart;
            e.Pointer.Capture(this);
            return;
        }

        if (e.ClickCount == 2)
        {
            Camera.Yaw = 0.7f;
            Camera.Pitch = 0.3f;
            Camera.Zoom = 1f;
            Redraw();
            return;
        }

        _orbit = at;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var now = e.GetPosition(this);
        if (_dragId >= 0)
        {
            if (_projection is { } projection && Intersect(projection, now, _dragStart) is { } hit)
                Editor?.MoveMarker(_dragId, _dragStart + (hit - _dragHit), Constraint);
            return;
        }

        if (_orbit is not { } last)
        {
            Cursor = MarkerEditing && HitMarker(now) != null ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.SizeAll);
            return;
        }

        Camera.Yaw -= (float)(now.X - last.X) * 0.01f;
        Camera.Pitch = Math.Clamp(Camera.Pitch + (float)(now.Y - last.Y) * 0.01f, -1.45f, 1.45f);
        _orbit = now;
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragId >= 0)
            Editor?.EndMarkerDrag(_dragId);
        _dragId = -1;
        _orbit = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!MarkerEditing || e.Key is not (Key.Delete or Key.Back))
            return;
        if (Markers?.FirstOrDefault(m => m.Selected && m.Id >= 0) is { Id: >= 0 } selected)
        {
            Editor?.DeleteMarker(selected.Id);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom = Math.Clamp(Camera.Zoom * (e.Delta.Y > 0 ? 1.12f : 1 / 1.12f), 0.3f, 8f);
        Redraw();
        e.Handled = true;
    }

    /// <summary>The editable marker nearest the pointer, within a few pixels.</summary>
    private Marker? HitMarker(Point at)
    {
        if (_projection is not { } projection || Markers == null)
            return null;
        Marker? best = null;
        var bestDistance = 12.0;
        foreach (var m in Markers)
        {
            if (m.Id < 0 || projection.ToScreen(m.UnityPosition) is not { } s)
                continue;
            var d = Math.Sqrt((s.X - at.X) * (s.X - at.X) + (s.Y - at.Y) * (s.Y - at.Y));
            if (d < bestDistance)
            {
                best = m;
                bestDistance = d;
            }
        }

        return best;
    }

    /// <summary>Where the pointer ray meets the drag constraint through <paramref name="through"/>: an axis line or a plane.</summary>
    private Vector3? Intersect(Projection projection, Point at, Vector3 through)
    {
        var (origin, dir) = projection.Ray((float)at.X, (float)at.Y);
        switch (Constraint)
        {
            case "x":
            case "y":
            case "z":
            {
                var axis = Constraint == "x" ? Vector3.UnitX : Constraint == "y" ? Vector3.UnitY : Vector3.UnitZ;
                // Closest point on the axis line to the pointer ray.
                var w0 = through - origin;
                var b = Vector3.Dot(axis, dir);
                var d = Vector3.Dot(axis, w0);
                var e = Vector3.Dot(dir, w0);
                var denom = 1f - b * b;
                if (Math.Abs(denom) < 1e-4f)
                    return null; // looking straight down the axis
                var t = (b * e - d) / denom;
                return through + axis * t;
            }
            default:
            {
                var normal = Constraint == "floor" ? Vector3.UnitY : Vector3.Normalize(projection.Eye - through);
                var facing = Vector3.Dot(normal, dir);
                if (Math.Abs(facing) < 1e-4f)
                    return null;
                var s = Vector3.Dot(through - origin, normal) / facing;
                return s < 0 ? null : origin + dir * s;
            }
        }
    }

    private void Redraw()
    {
        _dirty = true;
        InvalidateVisual();
    }
}
