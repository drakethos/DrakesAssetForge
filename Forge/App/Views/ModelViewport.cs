using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DrakesForge.App.Services;
using DrakesForge.Valheim;

namespace DrakesForge.App.Views;

/// <summary>Orbit viewport over <see cref="SoftwareRenderer"/>: drag to orbit, wheel to zoom, double-click to reset.</summary>
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

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0F1113"));
    private static readonly IBrush MessageBrush = new SolidColorBrush(Color.Parse("#A3A9B0"));

    private WriteableBitmap? _bitmap;
    private bool _dirty = true;
    private Point? _drag;

    static ModelViewport()
    {
        AffectsRender<ModelViewport>(PartsProperty, LookProperty, MarkersProperty, MessageProperty);
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

    /// <summary>Current view angle; "Render icon" uses it so the icon matches what you see.</summary>
    public OrbitCamera Camera { get; } = new();

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);

        var parts = Parts;
        if (parts == null || parts.Count == 0 || bounds.Width < 8 || bounds.Height < 8)
        {
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
            _dirty = false;
        }

        context.DrawImage(_bitmap, new Rect(0, 0, w, h));
        if (Message != null)
            DrawMessage(context, Message, overModel: true);
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.ClickCount == 2)
        {
            Camera.Yaw = 0.7f;
            Camera.Pitch = 0.3f;
            Camera.Zoom = 1f;
            Redraw();
            return;
        }

        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } last)
            return;
        var now = e.GetPosition(this);
        Camera.Yaw -= (float)(now.X - last.X) * 0.01f;
        Camera.Pitch = Math.Clamp(Camera.Pitch + (float)(now.Y - last.Y) * 0.01f, -1.45f, 1.45f);
        _drag = now;
        Redraw();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera.Zoom = Math.Clamp(Camera.Zoom * (e.Delta.Y > 0 ? 1.12f : 1 / 1.12f), 0.3f, 8f);
        Redraw();
        e.Handled = true;
    }

    private void Redraw()
    {
        _dirty = true;
        InvalidateVisual();
    }
}
