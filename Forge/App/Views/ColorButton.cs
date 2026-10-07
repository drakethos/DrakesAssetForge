using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;

namespace DrakesForge.App.Views;

/// <summary>
/// A swatch that opens a full color picker (spectrum, sliders, hex, palette). Two-way bound to a hex
/// string ("#RRGGBB", or "#RRGGBBAA" when <see cref="AllowAlpha"/> and not opaque), which is what recipes store.
/// </summary>
public sealed class ColorButton : UserControl
{
    public static readonly StyledProperty<string?> HexProperty =
        AvaloniaProperty.Register<ColorButton, string?>(nameof(Hex), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> AllowAlphaProperty =
        AvaloniaProperty.Register<ColorButton, bool>(nameof(AllowAlpha));

    private readonly Border _swatch;
    private readonly ColorView _picker;
    private bool _syncing;

    public ColorButton()
    {
        _swatch = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A4048"))
        };
        _picker = new ColorView
        {
            Width = 320,
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsColorModelVisible = false
        };
        _picker.ColorChanged += (_, e) =>
        {
            if (!_syncing)
                Hex = Format(e.NewColor);
        };

        Content = new Button
        {
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = _swatch,
            Flyout = new Flyout { Content = _picker, Placement = PlacementMode.BottomEdgeAlignedLeft },
            [ToolTip.TipProperty] = "Pick a color"
        };
        Sync();
    }

    public string? Hex
    {
        get => GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    public bool AllowAlpha
    {
        get => GetValue(AllowAlphaProperty);
        set => SetValue(AllowAlphaProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HexProperty)
            Sync();
        else if (change.Property == AllowAlphaProperty)
        {
            _picker.IsAlphaEnabled = AllowAlpha;
            _picker.IsAlphaVisible = AllowAlpha;
        }
    }

    private void Sync()
    {
        var ok = Color.TryParse(Hex, out var color);
        _swatch.Background = ok ? new SolidColorBrush(color) : Brushes.Gray;
        if (!ok)
            return;
        _syncing = true;
        _picker.Color = color;
        _syncing = false;
    }

    private string Format(Color c) =>
        AllowAlpha && c.A < 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
