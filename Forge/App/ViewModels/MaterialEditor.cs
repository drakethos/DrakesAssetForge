using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;
using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

/// <summary>One texture slot of a material (_MainTex, _BumpMap, _ChestTex, …): vanilla thumbnail, replace, export.</summary>
public sealed partial class TextureSlotEditor : ObservableObject
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["_MainTex"] = "Color",
        ["_BumpMap"] = "Normal map",
        ["_MetallicGlossMap"] = "Metal / gloss",
        ["_EmissionMap"] = "Glow (emission)",
        ["_StyleTex"] = "Style variants",
        ["_ChestTex"] = "Chest · upper body",
        ["_ChestBumpMap"] = "Chest normal map",
        ["_ChestMetal"] = "Chest metal",
        ["_LegsTex"] = "Legs · lower body",
        ["_LegsBumpMap"] = "Legs normal map",
        ["_LegsMetal"] = "Legs metal",
        ["_MetalTex"] = "Metal",
        ["_EmissiveTex"] = "Glow (emission)",
        ["_NoiseGlowTex"] = "Glow noise",
        ["_MossTex"] = "Moss"
    };

    private readonly MaterialEditor _owner;

    public TextureSlotEditor(MaterialEditor owner, string slot, string? vanillaName)
    {
        _owner = owner;
        Slot = slot;
        VanillaName = vanillaName;
    }

    public string Slot { get; }
    public string? VanillaName { get; }
    public string Label => Labels.TryGetValue(Slot, out var label) ? label : Slot.TrimStart('_');
    public string Detail => CustomFile.Length > 0 ? $"{Slot} · yours: {CustomFile}" : $"{Slot} · {VanillaName ?? "empty"}";
    public bool IsNormalMap => Slot.Contains("Bump", StringComparison.OrdinalIgnoreCase) || Slot.Contains("Normal", StringComparison.OrdinalIgnoreCase);
    public bool CanExport => VanillaName != null;

    [ObservableProperty] private string _customFile = "";
    [ObservableProperty] private Bitmap? _thumbnail;
    public Bitmap? VanillaThumbnail { get; set; }
    public RgbaImage? CustomImage { get; private set; }
    public bool IsCustom => CustomFile.Length > 0;

    public void SetCustom(string file, PackProject? pack)
    {
        CustomFile = file;
        CustomImage = Images.LoadFile(pack?.FullPath(file));
        Thumbnail = CustomImage != null ? Images.ToBitmap(CustomImage) : VanillaThumbnail;
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsCustom));
    }

    [RelayCommand]
    private async Task Replace()
    {
        var file = await Dialogs.PickFileAsync($"Choose an image for {Label}", "Images", "*.png", "*.jpg", "*.jpeg");
        if (file == null || _owner.Pack == null)
            return;
        SetCustom(_owner.Pack.AddFile(file, "textures"), _owner.Pack);
        _owner.TexturesChanged();
    }

    [RelayCommand]
    private void Clear()
    {
        CustomFile = "";
        CustomImage = null;
        Thumbnail = VanillaThumbnail;
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(IsCustom));
        _owner.TexturesChanged();
    }

    [RelayCommand]
    private Task Export() => _owner.ExportSlotAsync(this);
}

/// <summary>
/// Edits one material of the item (all slots sharing that material change together, which is how
/// Forge Runtime's "target" works). Special rows: "All materials" (untargeted) and the armour body material (@armor).
/// </summary>
public sealed partial class MaterialEditor : ObservableObject
{
    private readonly ItemEditorViewModel _owner;
    // True while loading saved values, so they don't count as edits.
    private bool _suppress;
    private bool _texturesRequested;

    public MaterialEditor(ItemEditorViewModel owner, string? target, MaterialInfo? original, RgbaImage? originalAlbedo, int slots)
    {
        _owner = owner;
        Target = target;
        Original = original;
        OriginalAlbedo = originalAlbedo;
        Slots = slots;

        if (original?.Colors.GetValueOrDefault("_EmissionColor") is { } emission)
            (_emissionColor, _emissionStrength) = SplitEmission(emission);

        if (original != null)
        {
            foreach (var slot in original.TextureSlots.Distinct().OrderBy(SlotOrder))
                if (original.Textures.ContainsKey(slot) || slot is "_MainTex" or "_BumpMap" or "_ChestTex" or "_LegsTex")
                    TextureSlots.Add(new TextureSlotEditor(this, slot, original.Textures.GetValueOrDefault(slot)));
        }
        else if (IsAll)
        {
            TextureSlots.Add(new TextureSlotEditor(this, "_MainTex", null));
        }
    }

    /// <summary>Material name, null for "All materials", or <see cref="MaterialOverride.ArmorTarget"/>.</summary>
    public string? Target { get; }
    public MaterialInfo? Original { get; }
    public RgbaImage? OriginalAlbedo { get; }
    public int Slots { get; }
    public bool IsAll => Target == null;
    public bool IsArmor => Target == MaterialOverride.ArmorTarget;
    public PackProject? Pack => _owner.Pack;

    public string Title => IsArmor ? "Worn on the body" : Target ?? "All materials";
    public string Subtitle => IsAll
        ? "Applies to every mesh slot without its own override"
        : IsArmor
            ? $"{Original?.Name} · {Original?.Shader} · painted on the player when worn"
            : $"{Original!.Shader} · {Original.MainTextureName ?? "no texture"} · {Slots} slot{(Slots == 1 ? "" : "s")}";

    public bool HasGloss => IsAll || Original?.Floats.ContainsKey("_Glossiness") == true;
    public bool HasMetallic => IsAll || Original?.Floats.ContainsKey("_Metallic") == true;
    public bool CanTint => !IsArmor;

    // Emission: a hue plus a strength, since glow is HDR (ward runes run at ~2) and a hex colour stops at 1.
    public bool HasEmission => IsAll || Original?.Colors.ContainsKey("_EmissionColor") == true;
    [ObservableProperty] private bool _emissionOn;
    [ObservableProperty] private string _emissionColor = "#FFFFFF";
    [ObservableProperty] private double _emissionStrength = 1;
    public string EmissionStrengthText => $"× {EmissionStrength:0.##}";
    public string VanillaEmissionText => Original?.Colors.GetValueOrDefault("_EmissionColor") is { } c
        ? $"vanilla: {SplitEmission(c).Hex} × {SplitEmission(c).Strength:0.##}"
        : "";

    partial void OnEmissionOnChanged(bool value) => Changed();
    partial void OnEmissionColorChanged(string value) => Changed();

    partial void OnEmissionStrengthChanged(double value)
    {
        OnPropertyChanged(nameof(EmissionStrengthText));
        Changed();
    }

    /// <summary>HDR colour → brightest channel normalised to 1, and that channel's value as the strength.</summary>
    private static (string Hex, double Strength) SplitEmission(float[] c)
    {
        var max = Math.Max(c[0], Math.Max(c[1], c[2]));
        if (max <= 0)
            return ("#000000", 0);
        static int B(float f) => (int)Math.Clamp(Math.Round(f * 255), 0, 255);
        return ($"#{B(c[0] / max):X2}{B(c[1] / max):X2}{B(c[2] / max):X2}", Math.Round(max, 2));
    }

    public ObservableCollection<TextureSlotEditor> TextureSlots { get; } = new();

    [ObservableProperty] private string _borrowFrom = "";
    /// <summary>Which of <see cref="BorrowFrom"/>'s materials (empty = its first).</summary>
    [ObservableProperty] private string _borrowMaterial = "";
    [ObservableProperty] private bool _tintOn;
    [ObservableProperty] private string _tint = "#FFFFFF";
    [ObservableProperty] private bool _glossOn;
    [ObservableProperty] private double _gloss = 0.5;
    [ObservableProperty] private bool _metallicOn;
    [ObservableProperty] private double _metallic = 0.5;
    [ObservableProperty] private string? _shader;

    /// <summary>Loaded when <see cref="BorrowFrom"/> names a prefab: that prefab's chosen material.</summary>
    public ModelMaterial? Borrowed { get; private set; }

    private TextureSlotEditor? MainSlot => TextureSlots.FirstOrDefault(t => t.Slot == "_MainTex");
    public RgbaImage? CustomAlbedo => MainSlot?.CustomImage;

    public bool IsModified => BorrowFrom.Length > 0 || TintOn || GlossOn || MetallicOn || EmissionOn || Shader != null || TextureSlots.Any(t => t.IsCustom);

    public IBrush Swatch
    {
        get
        {
            if (TintOn && RecipeSerializer.TryParseColor(Tint, out var t))
                return new SolidColorBrush(VanillaPreviewViewModel.ToColor(t));
            var c = Borrowed?.Info.Color ?? Original?.Color ?? new[] { 0.6f, 0.6f, 0.6f, 1f };
            return new SolidColorBrush(VanillaPreviewViewModel.ToColor(c));
        }
    }

    private static int SlotOrder(string slot) => slot switch
    {
        "_MainTex" or "_ChestTex" => 0,
        "_LegsTex" => 1,
        "_BumpMap" or "_ChestBumpMap" => 2,
        "_LegsBumpMap" => 3,
        _ => 10
    };

    public void LoadFrom(MaterialOverride o, PackProject pack)
    {
        _suppress = true;
        try
        {
            TintOn = o.Tint != null;
            Tint = o.Tint ?? "#FFFFFF";
            GlossOn = o.Floats.TryGetValue("_Glossiness", out var g);
            Gloss = GlossOn ? g : 0.5;
            MetallicOn = o.Floats.TryGetValue("_Metallic", out var m);
            Metallic = MetallicOn ? m : 0.5;
            Shader = o.Shader;
            EmissionOn = o.Colors.TryGetValue("_EmissionColor", out var glow) && RecipeSerializer.TryParseColor(glow, out _);
            if (EmissionOn && RecipeSerializer.TryParseColor(glow, out var hdr))
                (EmissionColor, EmissionStrength) = SplitEmission(hdr);
            foreach (var t in o.Textures)
            {
                var editor = TextureSlots.FirstOrDefault(s => s.Slot == t.Key);
                if (editor == null)
                    TextureSlots.Add(editor = new TextureSlotEditor(this, t.Key, null));
                editor.SetCustom(t.Value, pack);
            }

            BorrowMaterial = o.FromMaterial ?? "";
            BorrowFrom = o.FromPrefab ?? "";
        }
        finally
        {
            _suppress = false;
        }

        _ = LoadBorrowedAsync(notify: false);
    }

    public MaterialOverride? ToOverride()
    {
        if (!IsModified)
            return null;
        var o = new MaterialOverride
        {
            Target = Target,
            FromPrefab = BorrowFrom.Length > 0 ? BorrowFrom : null,
            FromMaterial = !IsArmor && BorrowFrom.Length > 0 && BorrowMaterial.Length > 0 ? BorrowMaterial : null,
            Shader = Shader
        };
        if (TintOn && CanTint)
            o.Tint = Tint;
        if (GlossOn)
            o.Floats["_Glossiness"] = (float)Math.Round(Gloss, 2);
        if (MetallicOn)
            o.Floats["_Metallic"] = (float)Math.Round(Metallic, 2);
        if (EmissionOn && RecipeSerializer.TryParseColor(EmissionColor, out _))
            o.Colors["_EmissionColor"] = Math.Abs(EmissionStrength - 1) < 0.005
                ? EmissionColor
                : EmissionColor + "*" + Math.Round(EmissionStrength, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var t in TextureSlots.Where(t => t.IsCustom))
            o.Textures[t.Slot] = t.CustomFile;
        return o;
    }

    /// <summary>How a mesh slot using this material draws in the viewport, given what's underneath.</summary>
    public SlotLook Apply(SlotLook under, bool highlight)
    {
        var albedo = CustomAlbedo ?? Borrowed?.Albedo ?? under.Albedo;
        var color = under.Color;
        if (Borrowed != null)
            color = new Vector4(Borrowed.Info.Color[0], Borrowed.Info.Color[1], Borrowed.Info.Color[2], 1);
        if (TintOn && RecipeSerializer.TryParseColor(Tint, out var t))
            color = new Vector4(t[0], t[1], t[2], t[3]);
        return new SlotLook(albedo, color, highlight);
    }

    /// <summary>Decodes vanilla thumbnails the first time this material is shown.</summary>
    public async Task EnsureThumbnailsAsync()
    {
        if (_texturesRequested || Original == null || _owner.Vanilla == null)
            return;
        _texturesRequested = true;
        try
        {
            var images = await _owner.Vanilla.LoadTexturesAsync(Original, 256);
            foreach (var slot in TextureSlots)
            {
                if (!images.TryGetValue(slot.Slot, out var image))
                    continue;
                slot.VanillaThumbnail = Images.ToBitmap(image);
                if (!slot.IsCustom)
                    slot.Thumbnail = slot.VanillaThumbnail;
            }
        }
        catch (Exception)
        {
            // thumbnails are a nicety
        }
    }

    /// <summary>Saves the vanilla texture at full size so it can be painted over and brought back with Replace.</summary>
    public async Task ExportSlotAsync(TextureSlotEditor slot)
    {
        if (Original == null || _owner.Vanilla == null || slot.VanillaName == null)
            return;
        try
        {
            var images = await _owner.Vanilla.LoadTexturesAsync(Original, 4096);
            if (!images.TryGetValue(slot.Slot, out var image))
            {
                _owner.Status($"Couldn't read {slot.VanillaName}.");
                return;
            }

            var path = Path.Combine(Path.GetDirectoryName(PackProject.DefaultPacksDirectory)!, "Exports", slot.VanillaName + ".png");
            Images.SavePng(image, path);
            _owner.Status($"Exported {slot.VanillaName} ({image.Width}×{image.Height}) to {path}. Edit it, then use Replace.");
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            _owner.Status($"Export failed: {ex.Message}");
        }
    }

    public void TexturesChanged() => Changed();

    partial void OnBorrowFromChanged(string value)
    {
        if (!_suppress)
            _ = LoadBorrowedAsync(notify: true);
    }

    partial void OnTintOnChanged(bool value) => Changed();
    partial void OnTintChanged(string value) => Changed();
    partial void OnGlossOnChanged(bool value) => Changed();
    partial void OnGlossChanged(double value) => Changed();
    partial void OnMetallicOnChanged(bool value) => Changed();
    partial void OnMetallicChanged(double value) => Changed();
    partial void OnShaderChanged(string? value) => Changed();

    private void Changed()
    {
        OnPropertyChanged(nameof(Swatch));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(HasBorrow));
        OnPropertyChanged(nameof(BorrowLabel));
        OnPropertyChanged(nameof(BorrowedAlbedo));
        if (!_suppress)
            _owner.LookChanged();
    }

    /// <param name="notify">False when loading saved state: redraw only, don't save.</param>
    private async Task LoadBorrowedAsync(bool notify)
    {
        Borrowed = null;
        if (BorrowFrom.Length > 0 && _owner.Vanilla?.TryLoadPreviewAsync(BorrowFrom) is { } task)
        {
            try
            {
                var preview = await task;
                if (IsArmor)
                {
                    if (preview.Info.ArmorMaterial is { } armor)
                        Borrowed = new ModelMaterial { Info = armor };
                }
                else
                {
                    var materials = preview.Model?.Materials;
                    Borrowed = materials?.FirstOrDefault(m => m.Info.Name == BorrowMaterial) ?? materials?.FirstOrDefault();
                }
            }
            catch (Exception)
            {
                // unknown prefab: the runtime will warn too
            }
        }

        if (notify)
        {
            Changed();
        }
        else
        {
            OnPropertyChanged(nameof(Swatch));
            OnPropertyChanged(nameof(HasBorrow));
            OnPropertyChanged(nameof(BorrowLabel));
            OnPropertyChanged(nameof(BorrowedAlbedo));
            _owner.Redraw();
        }
    }

    public bool HasBorrow => BorrowFrom.Length > 0;
    public string BorrowLabel => BorrowFrom.Length == 0
        ? (IsArmor ? "Its own body look" : "Own material")
        : IsArmor ? $"{BorrowFrom}'s body look" : BorrowMaterial.Length == 0 ? BorrowFrom : $"{BorrowFrom} › {BorrowMaterial}";
    public Bitmap? BorrowedAlbedo => Borrowed?.Albedo != null ? Images.ToBitmap(Borrowed.Albedo) : null;

    /// <summary>Set by the picker.</summary>
    public void SetBorrow(string prefab, string? material)
    {
        // Same prefab, other material: BorrowFrom won't change, so load explicitly.
        var samePrefab = BorrowFrom == prefab;
        BorrowMaterial = material ?? "";
        BorrowFrom = prefab;
        if (samePrefab)
            _ = LoadBorrowedAsync(notify: true);
    }

    [RelayCommand]
    private void ClearBorrow()
    {
        BorrowMaterial = "";
        BorrowFrom = "";
    }

    [RelayCommand]
    private void ChooseBorrow() => _owner.OpenMaterialPicker(this);

    [RelayCommand]
    private void Reset()
    {
        _suppress = true;
        BorrowMaterial = "";
        BorrowFrom = "";
        TintOn = false;
        GlossOn = false;
        MetallicOn = false;
        Shader = null;
        foreach (var slot in TextureSlots.Where(t => t.IsCustom))
            slot.ClearCommand.Execute(null);
        _suppress = false;
        Borrowed = null;
        Changed();
    }

    [RelayCommand]
    private void SetTint(string hex)
    {
        Tint = hex;
        TintOn = true;
    }
}
