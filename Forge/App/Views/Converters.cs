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

    public static readonly IValueConverter CheckBrush =
        new FuncValueConverter<bool, IBrush>(ok => new SolidColorBrush(Color.Parse(ok ? "#7CC48A" : "#F1CF6B")));
}
