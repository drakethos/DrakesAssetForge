using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public sealed partial class FlagOption : ObservableObject
{
    private readonly FieldEditor _owner;

    public FlagOption(FieldEditor owner, EnumOption option)
    {
        _owner = owner;
        Option = option;
    }

    public EnumOption Option { get; }
    public string Name => Option.Name;
    [ObservableProperty] private bool _isChecked;
    partial void OnIsCheckedChanged(bool value) => _owner.FlagsChanged();
}

/// <summary>One editable setting (a leaf of a component), with its vanilla value and reset.</summary>
public sealed partial class FieldEditor : ObservableObject
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly ComponentsPanel _owner;
    private bool _loading = true;

    public FieldEditor(ComponentsPanel owner, string component, FieldNode node, string breadcrumb)
    {
        _owner = owner;
        Component = component;
        Node = node;
        Breadcrumb = breadcrumb;
        if (node.IsFlags && node.EnumOptions != null)
            foreach (var o in node.EnumOptions.Where(o => o.Value != 0))
                Flags.Add(new FlagOption(this, o));
        SetFromVanilla();
        _loading = false;
    }

    public string Component { get; }
    public FieldNode Node { get; }
    public string Key => Component + "|" + Node.Path;
    public string Label => Node.Label;
    public string Breadcrumb { get; }
    public string Tooltip => $"{Component}.{Node.Path}  ({Node.TypeName})";

    public bool IsNumber => Node.Kind is FieldKind.Integer or FieldKind.Number;
    public bool IsText => Node.Kind == FieldKind.Text;
    public bool IsBool => Node.Kind == FieldKind.Bool;
    public bool IsEnum => Node.Kind == FieldKind.Enum && !Node.IsFlags;
    public bool IsFlags => Node.Kind == FieldKind.Enum && Node.IsFlags;
    public bool IsColor => Node.Kind == FieldKind.Color;
    public bool IsVector => Node.Kind is FieldKind.Vector2 or FieldKind.Vector3;
    public bool IsVector3 => Node.Kind == FieldKind.Vector3;
    public bool IsReadOnly => Node.Kind is FieldKind.List or FieldKind.Reference;
    public string ReadOnlyText => Node.Kind == FieldKind.List ? $"{Node.Count} entr{(Node.Count == 1 ? "y" : "ies")} · not editable yet" : $"{Node.Value} asset · not editable";

    public IReadOnlyList<EnumOption> EnumOptions => Node.EnumOptions ?? Array.Empty<EnumOption>();
    public ObservableCollection<FlagOption> Flags { get; } = new();

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _boolValue;
    [ObservableProperty] private EnumOption? _enumValue;
    [ObservableProperty] private string _colorHex = "#FFFFFF";
    [ObservableProperty] private string _x = "";
    [ObservableProperty] private string _y = "";
    [ObservableProperty] private string _z = "";
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private bool _hasError;

    public string FlagsSummary
    {
        get
        {
            var on = Flags.Where(f => f.IsChecked).Select(f => f.Name).ToList();
            return on.Count == 0 ? "None" : string.Join(", ", on);
        }
    }

    public string VanillaText => Node.Kind switch
    {
        FieldKind.Enum when Node.IsFlags => "vanilla: " + FlagsText((long)(double)Node.Value!),
        FieldKind.Enum => "vanilla: " + (EnumOptions.FirstOrDefault(o => o.Value == (long)(double)Node.Value!)?.Name ?? Node.Value),
        FieldKind.Color => "vanilla: " + Hex((float[])Node.Value!),
        FieldKind.Vector2 or FieldKind.Vector3 => "vanilla: " + string.Join(", ", ((float[])Node.Value!).Select(v => v.ToString("0.###", Inv))),
        FieldKind.Bool => "vanilla: " + ((bool)Node.Value! ? "on" : "off"),
        FieldKind.Number or FieldKind.Integer => "vanilla: " + Num((double)Node.Value!),
        _ => "vanilla: " + Node.Value
    };

    partial void OnTextChanged(string value) => Edited();
    partial void OnBoolValueChanged(bool value) => Edited();
    partial void OnEnumValueChanged(EnumOption? value) => Edited();
    partial void OnColorHexChanged(string value) => Edited();
    partial void OnXChanged(string value) => Edited();
    partial void OnYChanged(string value) => Edited();
    partial void OnZChanged(string value) => Edited();

    public void FlagsChanged()
    {
        OnPropertyChanged(nameof(FlagsSummary));
        Edited();
    }

    private void Edited()
    {
        if (_loading)
            return;
        HasError = ToJson() == null && !IsReadOnly;
        IsModified = !HasError && !SameAsVanilla();
        _owner.Changed(this);
    }

    [RelayCommand]
    private void Reset()
    {
        _loading = true;
        SetFromVanilla();
        _loading = false;
        HasError = false;
        IsModified = false;
        _owner.Changed(this);
    }

    private void SetFromVanilla() => SetValue(Node.Value);

    private void SetValue(object? value)
    {
        switch (Node.Kind)
        {
            case FieldKind.Number:
            case FieldKind.Integer:
                Text = Num((double)value!);
                break;
            case FieldKind.Text:
                Text = (string?)value ?? "";
                break;
            case FieldKind.Bool:
                BoolValue = (bool)value!;
                break;
            case FieldKind.Enum when Node.IsFlags:
                var bits = (long)(double)value!;
                foreach (var f in Flags)
                    f.IsChecked = (bits & f.Option.Value) == f.Option.Value;
                OnPropertyChanged(nameof(FlagsSummary));
                break;
            case FieldKind.Enum:
                EnumValue = EnumOptions.FirstOrDefault(o => o.Value == (long)(double)value!);
                break;
            case FieldKind.Color:
                ColorHex = Hex((float[])value!);
                break;
            case FieldKind.Vector2:
            case FieldKind.Vector3:
                var v = (float[])value!;
                X = v[0].ToString("0.###", Inv);
                Y = v[1].ToString("0.###", Inv);
                Z = v.Length > 2 ? v[2].ToString("0.###", Inv) : "";
                break;
        }
    }

    /// <summary>Applies a saved override from the recipe.</summary>
    public void Load(JsonValue json)
    {
        _loading = true;
        switch (Node.Kind)
        {
            case FieldKind.Number:
            case FieldKind.Integer:
                if (json.AsNumber() is { } n)
                    Text = Num(n);
                break;
            case FieldKind.Text:
                Text = json.AsString() ?? "";
                break;
            case FieldKind.Bool:
                BoolValue = json.AsBool() ?? BoolValue;
                break;
            case FieldKind.Enum:
                var names = (json.AsString() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                long value = json.AsNumber() is { } num ? (long)num : names.Sum(name => EnumOptions.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? 0);
                SetValue((double)value);
                break;
            case FieldKind.Color:
                if (json.AsString() is { } hex)
                    ColorHex = hex;
                break;
            case FieldKind.Vector2:
            case FieldKind.Vector3:
                if (json.IsArray)
                    SetValue(json.Items.Select(i => (float)(i.AsNumber() ?? 0)).ToArray());
                break;
        }

        _loading = false;
        HasError = ToJson() == null && !IsReadOnly;
        IsModified = !HasError && !SameAsVanilla();
    }

    /// <summary>The value as a recipe stores it, or null if what's typed isn't valid.</summary>
    public JsonValue? ToJson()
    {
        switch (Node.Kind)
        {
            case FieldKind.Integer:
                return long.TryParse(Text.Trim(), NumberStyles.Integer, Inv, out var i) ? (JsonValue)(double)i : null;
            case FieldKind.Number:
                return double.TryParse(Text.Trim(), NumberStyles.Float, Inv, out var d) ? (JsonValue)d : null;
            case FieldKind.Text:
                return Text;
            case FieldKind.Bool:
                return BoolValue;
            case FieldKind.Enum when Node.IsFlags:
                var on = Flags.Where(f => f.IsChecked).Select(f => f.Name).ToList();
                return on.Count == 0 ? (JsonValue)0 : string.Join(", ", on);
            case FieldKind.Enum:
                return EnumValue == null ? null : (JsonValue)EnumValue.Name;
            case FieldKind.Color:
                return RecipeSerializer.TryParseColor(ColorHex, out _) ? (JsonValue)ColorHex : null;
            case FieldKind.Vector2:
            case FieldKind.Vector3:
                var parts = (IsVector3 ? new[] { X, Y, Z } : new[] { X, Y }).Select(s => double.TryParse(s.Trim(), NumberStyles.Float, Inv, out var p) ? p : double.NaN).ToList();
                if (parts.Any(double.IsNaN))
                    return null;
                var arr = JsonValue.NewArray();
                foreach (var p in parts)
                    arr.Add(p);
                return arr;
            default:
                return null;
        }
    }

    private bool SameAsVanilla()
    {
        var json = ToJson();
        if (json == null)
            return true;
        switch (Node.Kind)
        {
            case FieldKind.Number:
            case FieldKind.Integer:
                return Math.Abs(json.NumberValue - (double)Node.Value!) < 1e-6;
            case FieldKind.Text:
                return json.StringValue == ((string?)Node.Value ?? "");
            case FieldKind.Bool:
                return json.BoolValue == (bool)Node.Value!;
            case FieldKind.Enum when Node.IsFlags:
                return Flags.Where(f => f.IsChecked).Sum(f => f.Option.Value) == (long)(double)Node.Value!;
            case FieldKind.Enum:
                return EnumValue?.Value == (long)(double)Node.Value!;
            case FieldKind.Color:
                return string.Equals(Normalize(ColorHex), Hex((float[])Node.Value!), StringComparison.OrdinalIgnoreCase);
            case FieldKind.Vector2:
            case FieldKind.Vector3:
                var v = (float[])Node.Value!;
                return json.Items.Select((p, idx) => Math.Abs(p.NumberValue - v[idx]) < 1e-4).All(b => b);
            default:
                return true;
        }
    }

    private string FlagsText(long bits)
    {
        var on = EnumOptions.Where(o => o.Value != 0 && (bits & o.Value) == o.Value).Select(o => o.Name).ToList();
        return on.Count == 0 ? "None" : string.Join(", ", on);
    }

    private static string Num(double v) => v.ToString("0.######", Inv);

    private static string Hex(float[] c)
    {
        static int B(float f) => (int)Math.Clamp(Math.Round(f * 255), 0, 255);
        var hex = $"#{B(c[0]):X2}{B(c[1]):X2}{B(c[2]):X2}";
        return c.Length > 3 && B(c[3]) < 255 ? hex + $"{B(c[3]):X2}" : hex;
    }

    private static string Normalize(string hex) =>
        RecipeSerializer.TryParseColor(hex, out var c) ? Hex(c) : hex;
}

/// <summary>A component or a nested group of settings (collapsible).</summary>
public partial class FieldGroup : ObservableObject
{
    public FieldGroup(string label, string? subtitle, bool expanded)
    {
        Label = label;
        Subtitle = subtitle;
        _isExpanded = expanded;
    }

    public string Label { get; }
    public string? Subtitle { get; }
    public ObservableCollection<object> Children { get; } = new();
    public List<FieldEditor> AllEditors { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private int _changedCount;

    public bool HasChanges => ChangedCount > 0;
    public string ChangedText => ChangedCount == 1 ? "1 changed" : $"{ChangedCount} changed";
    public string CountText => $"{AllEditors.Count(e => !e.IsReadOnly)} settings";

    partial void OnChangedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(ChangedText));
    }

    public void Recount()
    {
        ChangedCount = AllEditors.Count(e => e.IsModified);
        foreach (var g in Children.OfType<FieldGroup>())
            g.Recount();
    }
}
