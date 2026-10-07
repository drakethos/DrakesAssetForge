using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DrakesForge.App.Views;

public static class Converters
{
    /// <summary>Extra space above category group headers.</summary>
    public static readonly IValueConverter HeaderMargin =
        new FuncValueConverter<bool, Thickness>(header => header ? new Thickness(0, 10, 0, 0) : default);

    /// <summary>"#RRGGBB" → brush (falls back to grey while typing).</summary>
    public static readonly IValueConverter HexBrush =
        new FuncValueConverter<string?, IBrush>(hex => Color.TryParse(hex, out var c) ? new SolidColorBrush(c) : Brushes.Gray);

    /// <summary>Snap grid step: 0 → "Off", 0.25 → "0.25 m".</summary>
    public static readonly IValueConverter GridStep =
        new FuncValueConverter<double, string>(step => step <= 0 ? "Off" : $"{step:0.##} m");

    public static readonly IValueConverter CheckBrush =
        new FuncValueConverter<bool, IBrush>(ok => new SolidColorBrush(Color.Parse(ok ? "#7CC48A" : "#F1CF6B")));
}
