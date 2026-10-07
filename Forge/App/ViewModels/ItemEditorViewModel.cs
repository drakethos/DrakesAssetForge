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
    /// <summary>1-based, matching the number drawn beside the point in the viewport.</summary>
    [ObservableProperty] private int _number;
    /// <summary>Where it sits on the model: "bottom-left-front corner", "top centre"…</summary>
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _isSelected;
    public IRelayCommand? RemoveCommand { get; set; }
    public IRelayCommand? SelectCommand { get; set; }

    public Vector3 Position => new((float)X, (float)Y, (float)Z);

    public void MoveTo(Vector3 p)
    {
        X = Math.Round(p.X, 3);
        Y = Math.Round(p.Y, 3);
        Z = Math.Round(p.Z, 3);
    }
}

/// <summary>
/// Edits one recipe. Every change rebuilds the recipe, re-renders the viewport, and saves after a short
/// pause (and pushes to the game when live push is on).
/// </summary>
public sealed partial class ItemEditorViewModel : ObservableObject, IMarkerEditor
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

        var fx = recipe.Effects;
        _lightColorOn = fx?.LightColor != null;
        _lightColor = fx?.LightColor ?? "#FFB066";
        _lightIntensity = fx?.LightIntensity ?? 1;
        _lightRange = fx?.LightRange ?? 1;
        _flameTintOn = fx?.FlameTint != null;
        _flameTint = fx?.FlameTint ?? "#66CCFF";

        var craft = recipe.Craft;
        _tool = craft?.Tool ?? "Hammer";
        _category = craft?.Category ?? "Misc";
        _station = craft?.Station ?? "none";
        _stationLevel = craft?.StationLevel ?? 1;
        _craftOn = craft != null;
        foreach (var r in craft?.Requirements ?? new List<Requirement>())
            AddRequirementRow(r.Item, r.Amount, r.Recover);

        Components = new ComponentsPanel(ScheduleSave);

        _hideMesh = recipe.Look.HideMesh;
        _modelScale = Math.Round(recipe.Look.Scale.X, 4);
        if (recipe.Look.Scale.X != recipe.Look.Scale.Y || recipe.Look.Scale.Y != recipe.Look.Scale.Z)
            _modelScaleVector = recipe.Look.Scale;
        PartRows.CollectionChanged += OnRowsChanged;
        foreach (var part in recipe.Look.Parts)
            AddPartRow(PartRow.From(part), part.Materials);
        Sprites.CollectionChanged += OnRowsChanged;
        foreach (var s in recipe.Look.Sprites)
            AddSpriteRow(SpriteRow.From(s, Images.LoadFile(main.Pack?.FullPath(s.File))));

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

    // Sprites: flat images on the model
    public ObservableCollection<SpriteRow> Sprites { get; } = new();
    [ObservableProperty] private bool _hideMesh;
    public bool HasSprites => Sprites.Count > 0;

    partial void OnHideMeshChanged(bool value)
    {
        Refresh();
        ScheduleSave();
    }

    // Kitbash parts: other prefabs' meshes on this model
    public ObservableCollection<PartRow> PartRows { get; } = new();
    public bool HasParts => PartRows.Count > 0;
    [ObservableProperty] private PartRow? _selectedPart;
    /// <summary>Whole-model scale (uniform in the editor; a hand-written non-uniform scale is kept until edited).</summary>
    [ObservableProperty] private double _modelScale = 1;
    private Vec3? _modelScaleVector;

    /// <summary>The scale the viewport shows: the model scale (vector if the recipe had one).</summary>
    private Vector3 ViewScale => _modelScaleVector is { } v ? new Vector3(v.X, v.Y, v.Z) : new Vector3((float)Math.Max(ModelScale, 0.001));

    partial void OnModelScaleChanged(double value)
    {
        _modelScaleVector = null;
        Refresh();
        ScheduleSave();
    }

    partial void OnSelectedPartChanged(PartRow? oldValue, PartRow? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;
        Refresh();
    }

    /// <summary>Pick a Valheim prefab; its meshes are added at the model's origin, selected so they can be dragged.</summary>
    [RelayCommand]
    private void AddPart() =>
        _main.Workspace.Picker = new AssetPickerViewModel(_main, PickerMode.Mesh, "Add a part: pick a prefab whose meshes to put on this model", (prefab, _) =>
        {
            var row = AddPartRow(new PartRow(prefab), new List<MaterialOverride>());
            SelectedPart = row;
            if (!TabLook)
                Tab = "Look";
        });

    private PartRow AddPartRow(PartRow row, List<MaterialOverride> overrides)
    {
        row.RemoveCommand = new RelayCommand(() =>
        {
            if (SelectedPart == row)
                SelectedPart = null;
            foreach (var m in row.Materials)
                Materials.Remove(m);
            PartRows.Remove(row);
        });
        row.SelectCommand = new RelayCommand(() => SelectedPart = SelectedPart == row ? null : row);
        PartRows.Add(row);
        _ = LoadPartAsync(row, overrides);
        return row;
    }

    /// <summary>Loads the part's model, then its materials (listed with the model's own, prefixed "Part n").</summary>
    private async Task LoadPartAsync(PartRow row, List<MaterialOverride> overrides)
    {
        if (Vanilla?.TryLoadPreviewAsync(row.Prefab) is not { } task)
        {
            Status($"'{row.Prefab}' isn't in this Valheim install.");
            return;
        }

        try
        {
            row.Model = (await task).Model;
        }
        catch (Exception ex)
        {
            Status($"Couldn't read '{row.Prefab}': {ex.Message}");
            return;
        }

        var names = row.Model?.Parts.Select(p => p.Name.Split('#')[0].Split('/').Last()).Distinct().ToList() ?? new List<string>();
        row.MeshNames = names.Count == 0 ? "no meshes" : "meshes: " + string.Join(", ", names);
        foreach (var group in (row.Model?.Materials ?? Array.Empty<ModelMaterial>()).GroupBy(m => m.Info.Name))
        {
            var first = group.First();
            var editor = new MaterialEditor(this, group.Key, first.Info, first.Albedo, group.Count()) { Part = row };
            if (overrides.FirstOrDefault(o => o.Target == group.Key) is { } o && Pack != null)
                editor.LoadFrom(o, Pack);
            row.Materials.Add(editor);
            if (Materials.Count > 0)
                Materials.Add(editor);
        }

        Refresh();
    }

    [RelayCommand]
    private async Task AddSprite()
    {
        var file = await Dialogs.PickFileAsync("Choose an image (PNG with transparency)", "Images", "*.png");
        if (file == null || Pack == null)
            return;
        var relative = Pack.AddFile(file, "textures");
        var image = Images.LoadFile(Pack.FullPath(relative));
        if (image == null)
        {
            Status($"Couldn't read {Path.GetFileName(file)} as an image.");
            return;
        }

        AddSprite(relative, image);
    }

    /// <summary>A new sprite, 1 m on its longer side, standing on the origin.</summary>
    public SpriteRow AddSprite(string relative, RgbaImage image)
    {
        var row = new SpriteRow(relative, image);
        if (row.Aspect >= 1)
            row.Width = 1;
        else
            row.Height = 1;
        AddSpriteRow(row);
        return row;
    }

    private void AddSpriteRow(SpriteRow row)
    {
        row.RemoveCommand = new RelayCommand(() => Sprites.Remove(row));
        Sprites.Add(row);
        OnPropertyChanged(nameof(HasSprites));
    }

    // Fire & lights (every Light and particle effect on the base)
    [ObservableProperty] private bool _hasEffects;
    [ObservableProperty] private string _effectsSummary = "";
    [ObservableProperty] private bool _lightColorOn;
    [ObservableProperty] private string _lightColor;
    [ObservableProperty] private double _lightIntensity;
    [ObservableProperty] private double _lightRange;
    [ObservableProperty] private bool _flameTintOn;
    [ObservableProperty] private string _flameTint;
    [ObservableProperty] private bool _hasLights;
    [ObservableProperty] private bool _hasParticles;
    public string LightIntensityText => $"× {LightIntensity:0.##}";
    public string LightRangeText => $"× {LightRange:0.##}";

    partial void OnLightColorOnChanged(bool value) => ScheduleSave();
    partial void OnLightColorChanged(string value) => ScheduleSave();
    partial void OnFlameTintOnChanged(bool value) => ScheduleSave();
    partial void OnFlameTintChanged(string value) => ScheduleSave();

    partial void OnLightIntensityChanged(double value)
    {
        OnPropertyChanged(nameof(LightIntensityText));
        ScheduleSave();
    }

    partial void OnLightRangeChanged(double value)
    {
        OnPropertyChanged(nameof(LightRangeText));
        ScheduleSave();
    }

    [RelayCommand]
    private void ResetEffects()
    {
        LightColorOn = false;
        LightIntensity = 1;
        LightRange = 1;
        FlameTintOn = false;
    }

    private void DescribeEffects(PrefabInfo info)
    {
        HasLights = info.Lights.Count > 0;
        HasParticles = info.Particles.Count > 0;
        HasEffects = HasLights || HasParticles;
        var parts = new List<string>();
        if (HasLights)
            parts.Add($"{info.Lights.Count} light{(info.Lights.Count == 1 ? "" : "s")}");
        if (HasParticles)
            parts.Add($"{info.Particles.Count} particle effect{(info.Particles.Count == 1 ? "" : "s")} (flames, sparks, smoke)");
        EffectsSummary = $"The base has {string.Join(" and ", parts)}.";
        if (Recipe.Effects?.LightColor == null && info.Lights.FirstOrDefault() is { } light)
        {
            static int B(float f) => (int)Math.Clamp(Math.Round(f * 255), 0, 255);
            var was = _loading;
            _loading = true;
            LightColor = $"#{B(light.Color[0]):X2}{B(light.Color[1]):X2}{B(light.Color[2]):X2}";
            _loading = was;
        }
    }

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
    [ObservableProperty] private SnapRow? _selectedSnap;
    /// <summary>How dragging a point moves it: free, x, y, z, floor.</summary>
    [ObservableProperty] private string _snapConstraint = "free";
    /// <summary>Stick to the model's edges, centre and 0 while dragging.</summary>
    [ObservableProperty] private bool _snapToEdges = true;
    /// <summary>Grid step in metres while dragging (0 = off).</summary>
    [ObservableProperty] private double _snapGrid;
    [ObservableProperty] private string _shapeText = "";
    /// <summary>The placed model's box (Unity space): what labels, auto points and the magnet measure against.</summary>
    private Box _shape = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool SnapKeep => SnapMode == "keep";
    public bool SnapAdd => SnapMode == "add";
    public bool SnapReplace => SnapMode == "replace";
    public bool SnapEditable => SnapMode != "keep";
    public bool HasSnapPoints => SnapPoints.Count > 0;
    public bool LockFree => SnapConstraint == "free";
    public bool LockX => SnapConstraint == "x";
    public bool LockY => SnapConstraint == "y";
    public bool LockZ => SnapConstraint == "z";
    public bool LockFloor => SnapConstraint == "floor";
    public static readonly double[] GridSteps = { 0, 0.05, 0.1, 0.25, 0.5, 1 };
    public bool MarkerEditing => (TabSnap && SnapEditable) || (TabLook && HasParts);
    public string ViewportHint => !MarkerEditing
        ? "Drag to orbit · wheel to zoom · double-click to reset. Approximate shading; Push to game for the real look."
        : TabLook
            ? "Drag a purple P handle to move that part · Delete removes the selected part · drag elsewhere to orbit · wheel to zoom"
            : "Drag a numbered point to move it · Delete removes the selected one · drag elsewhere to orbit · wheel to zoom";

    partial void OnSnapConstraintChanged(string value)
    {
        foreach (var name in new[] { nameof(LockFree), nameof(LockX), nameof(LockY), nameof(LockZ), nameof(LockFloor) })
            OnPropertyChanged(name);
    }

    partial void OnSelectedSnapChanged(SnapRow? oldValue, SnapRow? newValue)
    {
        if (oldValue != null)
            oldValue.IsSelected = false;
        if (newValue != null)
            newValue.IsSelected = true;
        Refresh();
    }

    [RelayCommand]
    private void SetSnapMode(string mode) => SnapMode = mode;

    [RelayCommand]
    private void SetConstraint(string constraint) => SnapConstraint = constraint;

    /// <summary>Points from the model's shape. Auto-detect replaces your points; the others add (skipping duplicates).</summary>
    [RelayCommand]
    private void AutoSnap(string preset)
    {
        var kind = Enum.Parse<SnapGeometry.Preset>(preset);
        if (_shape.IsEmpty)
            return;
        if (kind == SnapGeometry.Preset.Detect)
            SnapPoints.Clear();
        foreach (var p in SnapGeometry.Points(_shape, kind))
            if (!SnapPoints.Any(r => Vector3.Distance(r.Position, p) < 0.01f))
                AddSnapRow(p.X, p.Y, p.Z);
        if (SnapMode == "keep")
            SnapMode = "replace";
        SelectedSnap = null;
        Refresh();
    }

    [RelayCommand]
    private void ClearSnapPoints()
    {
        SnapPoints.Clear();
        SelectedSnap = null;
    }

    /// <summary>Marker ids from here up are kitbash parts (below are snap points).</summary>
    private const int PartMarkerBase = 1000;

    // IMarkerEditor: the viewport picks and drags snap points and parts. Positions arrive in the viewport's
    // (scaled) space; points and parts are stored in the model's own space.
    public void SelectMarker(int id)
    {
        if (id >= PartMarkerBase)
            SelectedPart = PartRows.ElementAtOrDefault(id - PartMarkerBase);
        else
            SelectedSnap = SnapPoints.ElementAtOrDefault(id);
    }

    public void MoveMarker(int id, Vector3 unityPosition, string constraint)
    {
        var local = unityPosition / ViewScale;
        var snapped = SnapGeometry.Snap(local, _shape, SnapToEdges, (float)SnapGrid,
            constraint is "free" or "x" or "floor", constraint is "free" or "y", constraint is "free" or "z" or "floor");
        if (id >= PartMarkerBase)
            PartRows.ElementAtOrDefault(id - PartMarkerBase)?.MoveTo(snapped);
        else
            SnapPoints.ElementAtOrDefault(id)?.MoveTo(snapped);
    }

    public void EndMarkerDrag(int id) => ScheduleSave();

    public void DeleteMarker(int id)
    {
        if (id >= PartMarkerBase)
        {
            PartRows.ElementAtOrDefault(id - PartMarkerBase)?.RemoveCommand?.Execute(null);
            return;
        }

        if (SnapPoints.ElementAtOrDefault(id) is { } row)
        {
            SnapPoints.Remove(row);
            SelectedSnap = null;
        }
    }

    /// <summary>Numbers and words for every point, after the shape or a point changed.</summary>
    private void UpdateSnapLabels()
    {
        for (var i = 0; i < SnapPoints.Count; i++)
        {
            SnapPoints[i].Number = i + 1;
            SnapPoints[i].Label = SnapGeometry.Label(SnapPoints[i].Position, _shape);
        }
    }

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
        DescribeEffects(_base.Info);
        _ = Components.BuildAsync(_base.Info, Recipe.Fields, Recipe.RemoveComponents, Recipe.AddComponents.Select(a => a.Type),
            Vanilla == null ? null : Vanilla.ComponentDefaultsAsync, Vanilla == null ? null : Vanilla.ValheimScriptsAsync);
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

        foreach (var row in PartRows)
            foreach (var editor in row.Materials)
                Materials.Add(editor);

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

        var worn = ShowWorn && model.WornParts.Count > 0;
        var parts = (worn ? model.WornParts : model.Parts).ToList();
        var scale = ViewScale;
        // Parts, scale and sprites belong to the dropped/placed model, as in game.
        if (!worn && !CompareToBase)
        {
            if (HideMesh)
                parts.Clear();
            for (var i = 0; i < PartRows.Count; i++)
                parts.AddRange(PartRows[i].ViewParts(i));
            if (scale != Vector3.One)
                parts = parts.Select(p => PartRow.Transform(p, Matrix4x4.CreateScale(scale), Matrix4x4.Identity, p.MaterialSlot)).ToList();
            // Items scale their visual (attach) only; their sprites sit on the root and keep their size.
            var spriteScale = IsItem ? Vector3.One : scale;
            parts.AddRange(Sprites.Where(s => !s.IsMissing).Select((s, i) =>
            {
                var part = s.ToPart(SpriteRow.SlotBase + i);
                return spriteScale == Vector3.One ? part : PartRow.Transform(part, Matrix4x4.CreateScale(spriteScale), Matrix4x4.Identity, part.MaterialSlot);
            }));
        }

        var sprites = Sprites.Where(s => !s.IsMissing).ToList();
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
        var partRows = PartRows.ToList();
        Look = slot =>
        {
            if (slot >= PartRow.SlotBase)
            {
                var row = partRows.ElementAtOrDefault((slot - PartRow.SlotBase) / PartRow.SlotStride);
                var local = (slot - PartRow.SlotBase) % PartRow.SlotStride;
                if (row?.Model is not { } partModel)
                    return new SlotLook(null, new Vector4(0.8f, 0.8f, 0.8f, 1f));
                var partLook = SoftwareRenderer.DefaultLook(partModel, local);
                if (local >= partModel.Materials.Count)
                    return partLook;
                var editor = row.Materials.FirstOrDefault(e => e.Target == partModel.Materials[local].Info.Name);
                if (editor is { IsModified: true })
                    partLook = editor.Apply(partLook, false);
                return (editor != null && editor == selected) || row.IsSelected ? partLook with { Highlight = true } : partLook;
            }

            if (slot >= SpriteRow.SlotBase)
                return new SlotLook(sprites[slot - SpriteRow.SlotBase].Image, Vector4.One);
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

        // The placed model (not worn, not compare) is what snap points belong to.
        if (!worn && !CompareToBase)
        {
            var unscaled = SnapGeometry.Bounds(parts);
            _shape = unscaled.IsEmpty ? unscaled : new Box(unscaled.Min / scale, unscaled.Max / scale);
            ShapeText = _shape.IsEmpty ? "" : $"Model: {_shape.Size.X:0.##} wide × {_shape.Size.Y:0.##} tall × {_shape.Size.Z:0.##} deep (m)";
        }

        UpdateSnapLabels();
        var markers = new List<Marker>();
        if (ShowSnapPoints || TabSnap)
        {
            var mode = SnapMode;
            if (mode != "replace")
                markers.AddRange((_base?.Info.SnapPoints ?? Array.Empty<Vector3>()).Select(p => new Marker(p * scale, 0xFF7FB2E5)));
            if (mode != "keep")
                markers.AddRange(SnapPoints.Select((p, i) => new Marker(p.Position * scale, 0xFFE8893C, 11, i, (i + 1).ToString(), p.IsSelected)));
        }

        // Parts: a purple handle each on the Look tab, dragged like snap points.
        if (TabLook && !worn && !CompareToBase)
            markers.AddRange(PartRows.Select((r, i) => new Marker(r.Position * scale, 0xFFB07CE8, 11, PartMarkerBase + i, "P" + (i + 1), r.IsSelected)));

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
        OnPropertyChanged(nameof(MarkerEditing));
        OnPropertyChanged(nameof(ViewportHint));
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
        foreach (var name in new[] { nameof(SnapKeep), nameof(SnapAdd), nameof(SnapReplace), nameof(SnapEditable), nameof(MarkerEditing), nameof(ViewportHint) })
            OnPropertyChanged(name);
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
            row.PropertyChanged += (_, a) =>
            {
                // Display-only properties (numbers, labels, selection) are set by Refresh itself.
                if (a.PropertyName is not (nameof(SnapRow.Label) or nameof(SnapRow.Number) or nameof(SnapRow.IsSelected) or nameof(PartRow.MeshNames)))
                    RowEdited(sender);
            };
        RowEdited(sender);
    }

    private void RowEdited(object? collection)
    {
        if (collection == PartRows)
        {
            for (var i = 0; i < PartRows.Count; i++)
                PartRows[i].Number = i + 1;
            OnPropertyChanged(nameof(HasParts));
            OnPropertyChanged(nameof(MarkerEditing));
            OnPropertyChanged(nameof(ViewportHint));
        }

        if (collection == SnapPoints || collection == Sprites || collection == PartRows)
            Refresh();
        if (collection == Sprites)
            OnPropertyChanged(nameof(HasSprites));
        if (collection == SnapPoints)
            OnPropertyChanged(nameof(HasSnapPoints));
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (_loading)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Stops the autosave timer without writing. Used when the recipe is being deleted.</summary>
    public void DiscardPendingSave() => _saveTimer.Stop();

    /// <summary>Writes the editor state into the recipe and saves it.</summary>
    public void SaveNow()
    {
        _saveTimer.Stop();
        // A recipe removed from the pack must stay removed: saving would recreate its file.
        if (Pack == null || _loading || !Pack.Recipes.Contains(Recipe))
            return;

        Recipe.Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        Recipe.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        Recipe.Look.Mesh = MeshFrom.Length > 0 ? new MeshSource { Prefab = MeshFrom } : null;
        Recipe.Look.Icon = IconFile.Length > 0 ? IconFile : null;
        Recipe.Look.Materials = Materials.Where(m => m.Part == null).Select(m => m.ToOverride()).Where(o => o != null).Select(o => o!).ToList();
        Recipe.Look.Parts = PartRows.Select(r => r.ToRecipe()).ToList();
        Recipe.Look.Scale = _modelScaleVector ?? new Vec3((float)Math.Round(Math.Max(ModelScale, 0.001), 4), (float)Math.Round(Math.Max(ModelScale, 0.001), 4), (float)Math.Round(Math.Max(ModelScale, 0.001), 4));
        Recipe.Look.Sprites = Sprites.Select(s => s.ToRecipe()).ToList();
        Recipe.Look.HideMesh = HideMesh;

        Recipe.Fields = Components.Collect();
        Recipe.RemoveComponents = Components.RemovedNames.ToList();
        Recipe.AddComponents = Components.AddedNames.Select(n => new ComponentAdd { Type = n }).ToList();

        var fx = new EffectsRecipe
        {
            LightColor = LightColorOn && RecipeSerializer.TryParseColor(LightColor, out _) ? LightColor : null,
            LightIntensity = Math.Abs(LightIntensity - 1) > 0.005 ? (float)Math.Round(LightIntensity, 2) : null,
            LightRange = Math.Abs(LightRange - 1) > 0.005 ? (float)Math.Round(LightRange, 2) : null,
            FlameTint = FlameTintOn && RecipeSerializer.TryParseColor(FlameTint, out _) ? FlameTint : null
        };
        Recipe.Effects = fx.IsEmpty ? null : fx;

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
        var image = SoftwareRenderer.Render(Parts ?? model.Parts, Look, camera ?? new OrbitCamera(), 128, 128, background: 0x00000000);
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

    /// <summary>A new point on top of the model, selected so it can be dragged straight away.</summary>
    [RelayCommand]
    private void AddSnapPoint()
    {
        var at = _shape.IsEmpty ? new Vector3(0, 1, 0) : _shape.Center with { Y = _shape.Max.Y };
        var row = AddSnapRow(at.X, at.Y, at.Z);
        if (SnapMode == "keep")
            SnapMode = "add";
        SelectedSnap = row;
    }

    [RelayCommand]
    private void CopyBaseSnapPoints()
    {
        foreach (var p in _base?.Info.SnapPoints ?? Array.Empty<Vector3>())
            if (!SnapPoints.Any(r => Vector3.Distance(r.Position, p) < 0.01f))
                AddSnapRow(p.X, p.Y, p.Z);
        if (SnapMode == "keep")
            SnapMode = "replace";
    }

    private SnapRow AddSnapRow(double x, double y, double z)
    {
        var row = new SnapRow { X = Math.Round(x, 3), Y = Math.Round(y, 3), Z = Math.Round(z, 3) };
        row.RemoveCommand = new RelayCommand(() =>
        {
            if (SelectedSnap == row)
                SelectedSnap = null;
            SnapPoints.Remove(row);
        });
        row.SelectCommand = new RelayCommand(() => SelectedSnap = SelectedSnap == row ? null : row);
        SnapPoints.Add(row);
        return row;
    }
}
