using System.Numerics;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DrakesForge.App.Services;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public sealed class MaterialRow
{
    public required string Name { get; init; }
    public required string Shader { get; init; }
    public required string Texture { get; init; }
    public required int Slots { get; init; }
    public required IBrush Swatch { get; init; }
    public Bitmap? Albedo { get; init; }
}

/// <summary>Read-only preview of one vanilla prefab: model, materials and shaders, icon, facts.</summary>
public sealed partial class VanillaPreviewViewModel : ObservableObject
{
    public VanillaPreviewViewModel(VanillaEntry entry, VanillaService vanilla)
    {
        Entry = entry;
        _ = LoadAsync(vanilla);
    }

    public VanillaEntry Entry { get; }
    public string Name => Entry.Name;
    public string Subtitle => Entry.Category.Length == 0 ? Entry.Kind.ToString() : $"{Entry.Kind} · {Entry.Category}";

    [ObservableProperty] private bool _loading = true;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private IReadOnlyList<ModelPart>? _parts;
    [ObservableProperty] private Func<int, SlotLook>? _look;
    [ObservableProperty] private IReadOnlyList<Marker>? _markers;
    [ObservableProperty] private IReadOnlyList<MaterialRow> _materials = Array.Empty<MaterialRow>();
    [ObservableProperty] private string _scripts = "";
    [ObservableProperty] private string _cost = "";
    [ObservableProperty] private string _stats = "";
    [ObservableProperty] private string _mode = "Model";
    [ObservableProperty] private bool _hasArmor;
    [ObservableProperty] private Bitmap? _armorChest;
    [ObservableProperty] private Bitmap? _armorLegs;
    [ObservableProperty] private IReadOnlyList<ModelPart>? _wornParts;

    public bool ModeModel => Mode == "Model";
    public bool ModeMaterials => Mode == "Materials";
    public bool ModeIcon => Mode == "Icon";

    partial void OnModeChanged(string value)
    {
        OnPropertyChanged(nameof(ModeModel));
        OnPropertyChanged(nameof(ModeMaterials));
        OnPropertyChanged(nameof(ModeIcon));
    }

    public VanillaPreview? Preview { get; private set; }

    private async Task LoadAsync(VanillaService vanilla)
    {
        try
        {
            var preview = await vanilla.LoadPreviewAsync(Entry);
            Preview = preview;
            Icon = preview.Icon != null ? Images.ToBitmap(preview.Icon) : null;
            var model = preview.Model;
            if (model != null)
            {
                Parts = model.Parts;
                Look = slot => SoftwareRenderer.DefaultLook(model, slot);
                // Only what's actually visible (skip snow caps, worn/broken variants, lower LODs).
                var visibleSlots = model.Parts.Select(p => p.MaterialSlot).ToHashSet();
                Materials = model.Materials
                    .Where((_, slot) => visibleSlots.Contains(slot))
                    .GroupBy(m => m.Info.Name)
                    .Select(g => new MaterialRow
                    {
                        Name = g.Key,
                        Shader = g.First().Info.Shader,
                        Texture = g.First().Info.MainTextureName ?? "(no texture)",
                        Slots = g.Count(),
                        Swatch = new SolidColorBrush(ToColor(g.First().Info.Color)),
                        Albedo = g.First().Albedo != null ? Images.ToBitmap(g.First().Albedo!) : null
                    })
                    .ToList();
                Stats = $"{model.TriangleCount:N0} tris · {model.Parts.Count} parts · {Materials.Count} materials";
                if (model.Problems.Count > 0)
                    Error = string.Join("\n", model.Problems);
            }

            if (model?.WornParts.Count > 0)
                WornParts = model.WornParts;
            if (preview.Info.ArmorMaterial is { } armor)
            {
                HasArmor = true;
                var textures = await vanilla.LoadTexturesAsync(armor, 512);
                ArmorChest = textures.TryGetValue("_ChestTex", out var chest) ? Images.ToBitmap(chest) : null;
                ArmorLegs = textures.TryGetValue("_LegsTex", out var legs) ? Images.ToBitmap(legs) : null;
            }

            Markers = preview.Info.SnapPoints.Select(p => new Marker(p, 0xFF7FB2E5)).ToList();
            Scripts = string.Join(" · ", preview.Info.Scripts);
            if (preview.Info.PieceCost is { } cost)
                Cost = $"{string.Join(", ", cost.Resources.Select(r => $"{r.Item} ×{r.Amount}"))}  ·  {cost.Category}  ·  {cost.Station ?? "no station"}";
            else if (Entry.Kind == VanillaKind.Item)
                Cost = "Item recipes aren't read yet; set one in the workspace.";
        }
        catch (Exception ex)
        {
            Error = $"Couldn't read this prefab: {ex.Message}";
        }
        finally
        {
            Loading = false;
        }
    }

    public static Color ToColor(float[] rgba) =>
        Color.FromArgb(255, (byte)Math.Clamp(rgba[0] * 255, 0, 255), (byte)Math.Clamp(rgba[1] * 255, 0, 255), (byte)Math.Clamp(rgba[2] * 255, 0, 255));

    public static Vector4 ToVector(Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}
