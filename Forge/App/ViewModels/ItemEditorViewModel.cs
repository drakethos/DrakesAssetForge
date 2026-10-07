using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public sealed partial class RequirementRow : ObservableObject
{
    [ObservableProperty] private string _item = "";
    [ObservableProperty] private int _amount = 1;
    [ObservableProperty] private bool _recover = true;
    public IRelayCommand? RemoveCommand { get; set; }
}

public sealed partial class SnapRow : ObservableObject
{
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _z;
    public IRelayCommand? RemoveCommand { get; set; }
}

/// <summary>
/// Edits one recipe. Every change rebuilds the recipe, re-renders the viewport, and saves after a short
/// pause (and pushes to the game when live push is on).
/// </summary>
public sealed partial class ItemEditorViewModel : ObservableObject
{
    public static readonly string[] Tools = { "Hammer", "Hoe", "Cultivator" };
    public static readonly string[] PieceCategories = { "Misc", "Crafting", "Building", "HeavyBuild", "Furniture" };
    public static readonly string[] Stations = { "none", "workbench", "forge", "stonecutter", "artisan", "blackforge", "galdr", "cauldron" };
    public static readonly string[] SnapModes = { "keep", "add", "replace" };
    public static readonly string[] TintPresets = { "#C48A48", "#8A5A28", "#5A4632", "#B0B8C0", "#3A3F46", "#7FB2E5", "#7CC48A", "#C04A3A" };

    private readonly MainViewModel _main;
    private readonly DispatcherTimer _saveTimer;
    private bool _loading = true;

    public ItemEditorViewModel(ItemRecipe recipe, MainViewModel main)
    {
        Recipe = recipe;
        _main = main;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) => SaveNow();
        // Hook rows before loading them so edits to existing rows save too.
        Requirements.CollectionChanged += OnRowsChanged;
        SnapPoints.CollectionChanged += OnRowsChanged;

        _name = recipe.Name ?? "";
        _description = recipe.Description ?? "";
        _meshFrom = recipe.Look.Mesh?.Prefab ?? "";
        _iconFile = recipe.Look.Icon ?? "";

        var glow = recipe.Behaviours.FirstOrDefault(b => b.Type.Equals("glow", StringComparison.OrdinalIgnoreCase));
        _glowOn = glow != null;
        _glowColor = glow?.Settings.GetValueOrDefault("color")?.AsString() ?? "#FFB066";
        _glowIntensity = glow?.Settings.GetValueOrDefault("intensity")?.AsNumber() ?? 1;
        _glowRange = glow?.Settings.GetValueOrDefault("range")?.AsNumber() ?? 4;
        _glowNightOnly = glow?.Settings.GetValueOrDefault("nightOnly")?.AsBool() ?? false;

        var craft = recipe.Craft;
        _tool = craft?.Tool ?? "Hammer";
        _category = craft?.Category ?? "Misc";
        _station = craft?.Station ?? "none";
        _stationLevel = craft?.StationLevel ?? 1;
        _craftOn = craft != null;
        foreach (var r in craft?.Requirements ?? new List<Requirement>())
            AddRequirementRow(r.Item, r.Amount, r.Recover);

        Components = new ComponentsPanel(ScheduleSave);

        _snapMode = (recipe.Snap?.Mode ?? Format.SnapMode.Keep).ToString().ToLowerInvariant();
        foreach (var p in recipe.Snap?.Points ?? new List<Vec3>())
            AddSnapRow(p.X, p.Y, p.Z);

        _ = LoadBaseAsync();
    }

    public ItemRecipe Recipe { get; }
    public VanillaService? Vanilla => _main.Vanilla;
    public PackProject? Pack => _main.Pack;
    public string Id => Recipe.Id;
    public string Base => Recipe.Base;
    public string KindLabel => Recipe.Kind switch { RecipeKind.Piece => "Piece", RecipeKind.Item => "Item", _ => "Reskin" };
    public bool IsPiece => Recipe.Kind == RecipeKind.Piece;
    public bool IsItem => Recipe.Kind == RecipeKind.Item;
    public bool IsReskin => Recipe.Kind == RecipeKind.Reskin;
    public bool CanCraft => !IsReskin;
    /// <summary>Every vanilla prefab name (mesh / borrow-from autocomplete).</summary>
    public IReadOnlyList<string> PrefabNames => _main.Vanilla?.PrefabNames ?? Array.Empty<string>();

    /// <summary>Item prefab names (cost autocomplete).</summary>
    public IReadOnlyList<string> ItemNames => _main.Vanilla?.ItemNames ?? Array.Empty<string>();

    // Identity
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;

    // Inspector
    [ObservableProperty] private string _tab = "Look";
    public bool TabLook => Tab == "Look";
    public bool TabComponents => Tab == "Components";
    public bool TabRecipe => Tab == "Recipe";
    public bool TabSnap => Tab == "Snap";

    // Viewport
    [ObservableProperty] private IReadOnlyList<ModelPart>? _parts;
    [ObservableProperty] private Func<int, SlotLook>? _look;
    [ObservableProperty] private IReadOnlyList<Marker>? _markers;
    [ObservableProperty] private string? _viewportMessage = "Loading…";
    [ObservableProperty] private bool _showSnapPoints;
    [ObservableProperty] private bool _compareToBase;
    /// <summary>Armour/capes: show the mesh as worn instead of the dropped model.</summary>
    [ObservableProperty] private bool _showWorn;
    public bool HasWornLook => (_meshSource?.Model ?? _base?.Model)?.WornParts.Count > 0;

    // Look
    public ObservableCollection<MaterialEditor> Materials { get; } = new();
    [ObservableProperty] private MaterialEditor? _selectedMaterial;
    [ObservableProperty] private string _meshFrom;
    [ObservableProperty] private string _iconFile;
    [ObservableProperty] private Bitmap? _iconImage;
    [ObservableProperty] private string _scripts = "";

    // Components
    public ComponentsPanel Components { get; }
    [ObservableProperty] private bool _glowOn;
    [ObservableProperty] private string _glowColor;
    [ObservableProperty] private double _glowIntensity;
    [ObservableProperty] private double _glowRange;
    [ObservableProperty] private bool _glowNightOnly;

    // Recipe
    [ObservableProperty] private bool _craftOn;
    [ObservableProperty] private string _tool;
    [ObservableProperty] private string _category;
    [ObservableProperty] private string _station;
    [ObservableProperty] private int _stationLevel;
    public ObservableCollection<RequirementRow> Requirements { get; } = new();

    // Snap
    [ObservableProperty] private string _snapMode;
    public ObservableCollection<SnapRow> SnapPoints { get; } = new();
    [ObservableProperty] private string _baseSnapText = "";

    private VanillaPreview? _base;
    private VanillaPreview? _meshSource;

    private async Task LoadBaseAsync()
    {
        if (Vanilla?.TryLoadPreviewAsync(Recipe.Base) is not { } task)
        {
            ViewportMessage = $"'{Recipe.Base}' isn't in this Valheim install.";
            _loading = false;
            return;
        }

        try
        {
            _base = await task;
        }
        catch (Exception ex)
        {
            ViewportMessage = $"Couldn't read '{Recipe.Base}': {ex.Message}";
            _loading = false;
            return;
        }

        Scripts = string.Join(" · ", _base.Info.Scripts);
        Components.Build(_base.Info, Recipe.Fields);
        BaseSnapText = _base.Info.SnapPoints.Count == 0
            ? "The base has no snap points."
            : $"The base has {_base.Info.SnapPoints.Count}: {string.Join("  ", _base.Info.SnapPoints.Select(p => $"({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})"))}";
        await LoadMeshSourceAsync();
        BuildMaterialEditors();
        LoadIcon();
        _loading = false;
        Refresh();
    }

    private async Task LoadMeshSourceAsync()
    {
        _meshSource = null;
        if (MeshFrom.Length > 0 && Vanilla?.TryLoadPreviewAsync(MeshFrom) is { } task)
        {
            try
            {
                _meshSource = await task;
            }
            catch (Exception)
            {
                // runtime will warn about the missing prefab
            }
        }
    }

    /// <summary>The model on screen: the base, or the borrowed mesh with its own materials.</summary>
    private VanillaModel? ShownModel => CompareToBase ? _base?.Model : (_meshSource?.Model ?? _base?.Model);

    private void BuildMaterialEditors()
    {
        Materials.Clear();
        var model = _meshSource?.Model ?? _base?.Model;
        var overrides = Recipe.Look.Materials;

        var all = new MaterialEditor(this, null, null, null, 0);
        if (overrides.FirstOrDefault(o => o.Target == null && o.Slot == null) is { } untargeted && Pack != null)
            all.LoadFrom(untargeted, Pack);
        Materials.Add(all);

        foreach (var group in (model?.Materials ?? Array.Empty<ModelMaterial>()).GroupBy(m => m.Info.Name))
        {
            var first = group.First();
            var editor = new MaterialEditor(this, group.Key, first.Info, first.Albedo, group.Count());
            if (overrides.FirstOrDefault(o => o.Target == group.Key) is { } o && Pack != null)
                editor.LoadFrom(o, Pack);
            Materials.Add(editor);
        }

        // Chest/legs armour: the material Valheim paints onto the player's body.
        if (_base?.Info.ArmorMaterial is { } armor)
        {
            var body = new MaterialEditor(this, MaterialOverride.ArmorTarget, armor, null, 0);
            if (overrides.FirstOrDefault(o => o.Target == MaterialOverride.ArmorTarget) is { } o && Pack != null)
                body.LoadFrom(o, Pack);
            Materials.Add(body);
        }

        SelectedMaterial = Materials.Skip(1).FirstOrDefault(m => m.IsModified) ?? Materials.ElementAtOrDefault(1) ?? all;
        OnPropertyChanged(nameof(HasWornLook));
        if (HasWornLook && !_wornDefaulted)
        {
            _wornDefaulted = true;
            ShowWorn = true;
        }
    }

    private void LoadIcon()
    {
        if (IconFile.Length > 0 && Images.LoadFile(Pack?.FullPath(IconFile)) is { } custom)
            IconImage = Images.ToBitmap(custom);
        else
            IconImage = _base?.Icon != null ? Images.ToBitmap(_base.Icon) : null;
    }

    /// <summary>Re-render with current edits.</summary>
    private void Refresh()
    {
        var model = ShownModel;
        if (model == null)
        {
            Parts = null;
            ViewportMessage ??= "No model";
            return;
        }

        var parts = ShowWorn && model.WornParts.Count > 0 ? model.WornParts : model.Parts;
        ViewportMessage = parts.Count == 0
            ? "This prefab has no visible mesh."
            : SelectedMaterial is { IsArmor: true }
                ? "The body look is painted on the player, so it isn't drawn here. See its textures in the inspector."
                : null;
        Parts = parts;
        var editors = Materials.ToList();
        var all = editors.FirstOrDefault(e => e.IsAll);
        var selected = SelectedMaterial;
        var compare = CompareToBase;
        Look = slot =>
        {
            var look = SoftwareRenderer.DefaultLook(model, slot);
            if (compare || slot >= model.Materials.Count)
                return look;
            // Same order as Forge Runtime: the all-materials override first, then this material's own on top.
            var name = model.Materials[slot].Info.Name;
            var own = editors.FirstOrDefault(e => e.Target == name);
            if (all is { IsModified: true })
                look = all.Apply(look, false);
            if (own is { IsModified: true })
                look = own.Apply(look, false);
            return own == selected && selected != null ? look with { Highlight = true } : look;
        };

        var markers = new List<Marker>();
        if (ShowSnapPoints || TabSnap)
        {
            var mode = SnapMode;
            if (mode != "replace")
                markers.AddRange((_base?.Info.SnapPoints ?? Array.Empty<Vector3>()).Select(p => new Marker(p, 0xFF7FB2E5)));
            if (mode != "keep")
                markers.AddRange(SnapPoints.Select(p => new Marker(new Vector3((float)p.X, (float)p.Y, (float)p.Z), 0xFFE8893C, 11)));
        }

        Markers = markers;
    }

    // Change plumbing ---------------------------------------------------------------------------

    /// <summary>Re-render without saving (e.g. a borrowed material finished loading).</summary>
    public void Redraw() => Refresh();

    public void LookChanged()
    {
        Refresh();
        ScheduleSave();
    }

    partial void OnTabChanged(string value)
    {
        OnPropertyChanged(nameof(TabLook));
        OnPropertyChanged(nameof(TabComponents));
        OnPropertyChanged(nameof(TabRecipe));
        OnPropertyChanged(nameof(TabSnap));
        Refresh();
    }

    partial void OnSelectedMaterialChanged(MaterialEditor? value)
    {
        _ = value?.EnsureThumbnailsAsync();
        Refresh();
    }

    partial void OnShowWornChanged(bool value) => Refresh();
    partial void OnShowSnapPointsChanged(bool value) => Refresh();
    partial void OnCompareToBaseChanged(bool value) => Refresh();
    partial void OnNameChanged(string value) => ScheduleSave();
    partial void OnDescriptionChanged(string value) => ScheduleSave();
    partial void OnGlowOnChanged(bool value) => ScheduleSave();
    partial void OnGlowColorChanged(string value) => ScheduleSave();
    partial void OnGlowIntensityChanged(double value) => ScheduleSave();
    partial void OnGlowRangeChanged(double value) => ScheduleSave();
    partial void OnGlowNightOnlyChanged(bool value) => ScheduleSave();
    partial void OnCraftOnChanged(bool value) => ScheduleSave();
    partial void OnToolChanged(string value) => ScheduleSave();
    partial void OnCategoryChanged(string value) => ScheduleSave();
    partial void OnStationChanged(string value) => ScheduleSave();
    partial void OnStationLevelChanged(int value) => ScheduleSave();

    partial void OnSnapModeChanged(string value)
    {
        Refresh();
        ScheduleSave();
    }

    partial void OnMeshFromChanged(string value)
    {
        OnPropertyChanged(nameof(MeshLabel));
        OnPropertyChanged(nameof(HasMeshSwap));
        _ = MeshFromChangedAsync();
    }

    private async Task MeshFromChangedAsync()
    {
        await LoadMeshSourceAsync();
        BuildMaterialEditors();
        Refresh();
        ScheduleSave();
    }

    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (INotifyPropertyChanged row in e.NewItems ?? Array.Empty<object>())
            row.PropertyChanged += (_, _) => RowEdited(sender);
        RowEdited(sender);
    }

    private void RowEdited(object? collection)
    {
        if (collection == SnapPoints)
            Refresh();
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_loading)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes the editor state into the recipe and saves it.</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        if (Pack == null || _loading)
            return;

        Recipe.Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        Recipe.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        Recipe.Look.Mesh = MeshFrom.Length > 0 ? new MeshSource { Prefab = MeshFrom } : null;
        Recipe.Look.Icon = IconFile.Length > 0 ? IconFile : null;
        Recipe.Look.Materials = Materials.Select(m => m.ToOverride()).Where(o => o != null).Select(o => o!).ToList();

        Recipe.Fields = Components.Collect();

        Recipe.Behaviours.RemoveAll(b => b.Type.Equals("glow", StringComparison.OrdinalIgnoreCase));
        if (GlowOn)
        {
            Recipe.Behaviours.Add(new BehaviourRecipe
            {
                Type = "glow",
                Settings =
                {
                    ["color"] = GlowColor,
                    ["intensity"] = Math.Round(GlowIntensity, 2),
                    ["range"] = Math.Round(GlowRange, 1),
                    ["nightOnly"] = GlowNightOnly
                }
            });
        }

        Recipe.Craft = CanCraft && CraftOn
            ? new CraftRecipe
            {
                Tool = IsPiece ? Tool : null,
                Category = IsPiece ? Category : null,
                Station = Station == "none" ? null : Station,
                StationLevel = Math.Max(1, StationLevel),
                Requirements = Requirements.Where(r => r.Item.Trim().Length > 0)
                    .Select(r => new Requirement { Item = r.Item.Trim(), Amount = r.Amount, Recover = r.Recover }).ToList()
            }
            : null;

        Recipe.Snap = IsPiece && SnapMode != "keep"
            ? new SnapRecipe
            {
                Mode = Enum.Parse<Format.SnapMode>(SnapMode, true),
                Points = SnapPoints.Select(p => new Vec3((float)p.X, (float)p.Y, (float)p.Z)).ToList()
            }
            : null;

        try
        {
            Pack.SaveRecipe(Recipe);
            _main.RecipeSaved(Recipe.Id);
            _main.Workspace.RecipeRenamed(Recipe);
        }
        catch (IOException ex)
        {
            _main.Status = $"Couldn't save {Recipe.Id}: {ex.Message}";
        }
    }

    // Commands ----------------------------------------------------------------------------------

    [RelayCommand]
    private void SetTab(string tab) => Tab = tab;

    [RelayCommand]
    private async Task PickIcon()
    {
        var file = await Dialogs.PickFileAsync("Choose an icon (PNG, square)", "Images", "*.png");
        if (file == null || Pack == null)
            return;
        IconFile = Pack.AddFile(file, "textures");
        LoadIcon();
        ScheduleSave();
    }

    [RelayCommand]
    private void RenderIcon(OrbitCamera? camera)
    {
        var model = ShownModel;
        if (model == null || Pack == null || Look == null)
            return;
        var image = SoftwareRenderer.Render(model.Parts, Look, camera ?? new OrbitCamera(), 128, 128, background: 0x00000000);
        var relative = $"textures/{Recipe.Id}_icon.png";
        Images.SavePng(image, Pack.FullPath(relative)!);
        IconFile = relative;
        LoadIcon();
        ScheduleSave();
    }

    [RelayCommand]
    private void ResetIcon()
    {
        IconFile = "";
        LoadIcon();
        ScheduleSave();
    }

    private bool _wornDefaulted;

    public void Status(string text) => _main.Status = text;

    public void OpenMaterialPicker(MaterialEditor editor) =>
        _main.Workspace.Picker = editor.IsArmor
            ? new AssetPickerViewModel(_main, PickerMode.ArmorLook, "Borrow another armour's body look", (prefab, _) => editor.SetBorrow(prefab, null))
            :
            new AssetPickerViewModel(_main, PickerMode.Material,
            editor.IsAll ? "Borrow a material for every slot" : $"Borrow a material for {editor.Title}",
            (prefab, material) => editor.SetBorrow(prefab, material));

    [RelayCommand]
    private void ChooseMesh() =>
        _main.Workspace.Picker = new AssetPickerViewModel(_main, PickerMode.Mesh, $"Show another prefab's mesh on {Name.Trim() switch { "" => Recipe.Base, var n => n }}",
            (prefab, _) => MeshFrom = prefab);

    [RelayCommand]
    private void ClearMesh() => MeshFrom = "";

    public string MeshLabel => MeshFrom.Length == 0 ? $"{Recipe.Base} (base)" : MeshFrom;
    public bool HasMeshSwap => MeshFrom.Length > 0;

    [RelayCommand]
    private void AddRequirement() => AddRequirementRow("", 1, true);

    private void AddRequirementRow(string item, int amount, bool recover)
    {
        var row = new RequirementRow { Item = item, Amount = amount, Recover = recover };
        row.RemoveCommand = new RelayCommand(() => Requirements.Remove(row));
        Requirements.Add(row);
    }

    [RelayCommand]
    private void AddSnapPoint() => AddSnapRow(0, 1, 0);

    [RelayCommand]
    private void CopyBaseSnapPoints()
    {
        foreach (var p in _base?.Info.SnapPoints ?? Array.Empty<Vector3>())
            AddSnapRow(p.X, p.Y, p.Z);
        if (SnapMode == "keep")
            SnapMode = "replace";
    }

    private void AddSnapRow(double x, double y, double z)
    {
        var row = new SnapRow { X = Math.Round(x, 3), Y = Math.Round(y, 3), Z = Math.Round(z, 3) };
        row.RemoveCommand = new RelayCommand(() => SnapPoints.Remove(row));
        SnapPoints.Add(row);
    }
}
