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
    /// <summary>Why this item won't work as written (empty when it's fine).</summary>
    [ObservableProperty] private string _warning = "";
    public bool HasWarning => Warning.Length > 0;
    public IRelayCommand? RemoveCommand { get; set; }

    partial void OnWarningChanged(string value) => OnPropertyChanged(nameof(HasWarning));
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
        _modelFile = recipe.Look.Mesh?.File ?? "";
        _iconFile = recipe.Look.Icon ?? "";
        _iconPrefab = recipe.Look.IconPrefab ?? "";
        _iconTint = recipe.Look.IconTint ?? "";

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
        _holdX = (decimal)recipe.Look.HoldPosition.X;
        _holdY = (decimal)recipe.Look.HoldPosition.Y;
        _holdZ = (decimal)recipe.Look.HoldPosition.Z;
        _holdRotX = (decimal)recipe.Look.HoldRotation.X;
        _holdRotY = (decimal)recipe.Look.HoldRotation.Y;
        _holdRotZ = (decimal)recipe.Look.HoldRotation.Z;
        _holdScaleX = (decimal)recipe.Look.HoldScale.X;
        _holdScaleY = (decimal)recipe.Look.HoldScale.Y;
        _holdScaleZ = (decimal)recipe.Look.HoldScale.Z;
        _modelScale = Math.Round(recipe.Look.Scale.X, 4);
        if (recipe.Look.Scale.X != recipe.Look.Scale.Y || recipe.Look.Scale.Y != recipe.Look.Scale.Z)
            _modelScaleVector = recipe.Look.Scale;
        PartRows.CollectionChanged += OnRowsChanged;
        foreach (var part in recipe.Look.Parts)
            AddPartRow(PartRow.From(part), part.Materials);
        Sprites.CollectionChanged += OnRowsChanged;
        foreach (var s in recipe.Look.Sprites)
            AddSpriteRow(SpriteRow.From(s, Images.LoadFile(main.Pack?.FullPath(s.File))));

        var collision = recipe.Look.Collision;
        _collisionMode = (collision?.Mode ?? Format.CollisionMode.Base).ToString().ToLowerInvariant();
        _boxCenter = collision is { Mode: Format.CollisionMode.Box } ? new Vector3(collision.Center.X, collision.Center.Y, collision.Center.Z) : Vector3.Zero;
        _boxSize = collision is { Mode: Format.CollisionMode.Box } ? new Vector3(collision.Size.X, collision.Size.Y, collision.Size.Z) : Vector3.Zero;

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
    /// <summary>Cost suggestions: Valheim's items plus this pack's own items (by id).</summary>
    public IReadOnlyList<string> ItemNames => (Pack == null ? Array.Empty<string>() : CostItems.PackItemIds(Pack.Recipes).Where(id => id != Recipe.Id))
        .Concat(_main.Vanilla?.ItemNames ?? Array.Empty<string>()).ToList();

    private HashSet<string>? _vanillaItems;
    private IReadOnlySet<string> VanillaItems => _vanillaItems ??= new HashSet<string>(_main.Vanilla?.ItemNames ?? Array.Empty<string>(), StringComparer.Ordinal);

    private void CheckCost(RequirementRow row)
    {
        if (Pack == null || _main.Vanilla == null || row.Item.Trim().Length == 0)
        {
            row.Warning = "";
            return;
        }

        CostItems.Resolve(row.Item, Pack.Recipes, VanillaItems, out var note);
        row.Warning = note ?? "";
    }

    // Identity
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;

    // Inspector
    [ObservableProperty] private string _tab = "Look";
    public bool TabLook => Tab == "Look";
    public bool TabComponents => Tab == "Components";
    public bool TabRecipe => Tab == "Recipe";
    public bool TabSnap => Tab == "Snap";
    public bool TabCollision => Tab == "Collision";
    public bool HasCollisionTab => !IsReskin;

    // Viewport
    [ObservableProperty] private IReadOnlyList<ModelPart>? _parts;
    [ObservableProperty] private Func<int, SlotLook>? _look;
    [ObservableProperty] private IReadOnlyList<Marker>? _markers;
    [ObservableProperty] private IReadOnlyList<Wire>? _wires;
    [ObservableProperty] private string? _viewportMessage = "Loading…";
    [ObservableProperty] private bool _showSnapPoints;
    [ObservableProperty] private bool _showCollision;
    [ObservableProperty] private bool _compareToBase;
    /// <summary>Armour/capes: show the mesh as worn instead of the dropped model.</summary>
    [ObservableProperty] private bool _showWorn;
    public bool HasWornLook => (_meshSource?.Model ?? _base?.Model)?.WornParts.Count > 0;

    // Look
    public ObservableCollection<MaterialEditor> Materials { get; } = new();
    [ObservableProperty] private MaterialEditor? _selectedMaterial;
    [ObservableProperty] private string _meshFrom;

    // A .glb in the pack that replaces the base mesh (look.mesh.file). Exclusive with MeshFrom.
    [ObservableProperty] private string _modelFile = "";
    [ObservableProperty] private string _modelFileError = "";
    private VanillaModel? _fileModel;

    public string ModelFileLabel => ModelFile.Length == 0 ? "None" : ModelFile;
    public bool HasModelFile => ModelFile.Length > 0;
    public bool HasModelFileError => ModelFileError.Length > 0;

    partial void OnModelFileErrorChanged(string value) => OnPropertyChanged(nameof(HasModelFileError));

    // Hold pose (items): how the held item sits, as offsets on Valheim's own attach pose. See ViewTransforms.
    [ObservableProperty] private decimal? _holdX = 0;
    [ObservableProperty] private decimal? _holdY = 0;
    [ObservableProperty] private decimal? _holdZ = 0;
    [ObservableProperty] private decimal? _holdRotX = 0;
    [ObservableProperty] private decimal? _holdRotY = 0;
    [ObservableProperty] private decimal? _holdRotZ = 0;
    [ObservableProperty] private decimal? _holdScaleX = 1;
    [ObservableProperty] private decimal? _holdScaleY = 1;
    [ObservableProperty] private decimal? _holdScaleZ = 1;
    [ObservableProperty] private bool _showMannequin = true;

    partial void OnHoldXChanged(decimal? value) => HoldChanged();
    partial void OnHoldYChanged(decimal? value) => HoldChanged();
    partial void OnHoldZChanged(decimal? value) => HoldChanged();
    partial void OnHoldRotXChanged(decimal? value) => HoldChanged();
    partial void OnHoldRotYChanged(decimal? value) => HoldChanged();
    partial void OnHoldRotZChanged(decimal? value) => HoldChanged();
    partial void OnHoldScaleXChanged(decimal? value) => HoldChanged();
    partial void OnHoldScaleYChanged(decimal? value) => HoldChanged();
    partial void OnHoldScaleZChanged(decimal? value) => HoldChanged();
    partial void OnShowMannequinChanged(bool value) => HoldChanged();

    private void HoldChanged()
    {
        if (_loading)
            return;
        Refresh();
        ScheduleSave();
    }

    // Move boxes show centimetres or metres (AppSettings.MoveInCentimetres); the recipe always stores metres.
    private static bool Cm => AppSettings.MoveInCentimetres;
    private static decimal? ToShown(decimal? metres) => metres is decimal d ? (Cm ? Math.Round(d * 100, 2) : d) : null;
    private static decimal? FromShown(decimal? shown) => shown is decimal d ? (Cm ? d / 100 : d) : null;

    public string MoveUnit => Cm ? "cm" : "m";
    public string MoveHeader => $"Move ({MoveUnit})";
    public string MoveFormat => Cm ? "0.0" : "0.00";
    public decimal MoveStep => Cm ? 1m : 0.01m;
    public decimal MoveLimit => Cm ? 500m : 5m;
    public decimal MoveMin => -MoveLimit;
    public decimal MoveMax => MoveLimit;

    public decimal? MoveXShown { get => ToShown(HoldX); set => HoldX = FromShown(value); }
    public decimal? MoveYShown { get => ToShown(HoldY); set => HoldY = FromShown(value); }
    public decimal? MoveZShown { get => ToShown(HoldZ); set => HoldZ = FromShown(value); }

    [RelayCommand]
    private void ResetHold()
    {
        _holdX = _holdY = _holdZ = _holdRotX = _holdRotY = _holdRotZ = 0;
        _holdScaleX = _holdScaleY = _holdScaleZ = 1;
        OnPropertyChanged(nameof(HoldX));
        OnPropertyChanged(nameof(HoldY));
        OnPropertyChanged(nameof(HoldZ));
        OnPropertyChanged(nameof(HoldRotX));
        OnPropertyChanged(nameof(HoldRotY));
        OnPropertyChanged(nameof(HoldRotZ));
        OnPropertyChanged(nameof(HoldScaleX));
        OnPropertyChanged(nameof(HoldScaleY));
        OnPropertyChanged(nameof(HoldScaleZ));
        HoldChanged();
    }

    /// <summary>The item drawn as Valheim holds it, against the figure: items only, with the figure shown.</summary>
    private bool HeldView => IsItem && ShowMannequin && _base?.Info.Attach != null;

    private Vector3 HoldScaleVector => IsItem ? new Vector3(S(HoldScaleX), S(HoldScaleY), S(HoldScaleZ)) : Vector3.One;

    /// <summary>
    /// Unity-space transforms from the prefab (root) space the model and kitbash parts are read in, to the viewport,
    /// plus the rotation for normals. Dropped/placed: scaled. Held: as Forge Runtime builds it (see HoldPose) —
    /// Valheim mounts attach on the hand with its position and rotation zeroed, so the vanilla attach pose is undone,
    /// then the hold turn, attach's scale and the hold move (in metres) are applied.
    /// </summary>
    private (Matrix4x4 Model, Matrix4x4 Parts, Matrix4x4 Normals) ViewTransforms()
    {
        var modelScale = ViewScale;
        if (!HeldView || _base?.Info.Attach is not { } pose)
        {
            var scale = Matrix4x4.CreateScale(modelScale * HoldScaleVector);
            return (scale, scale, Matrix4x4.Identity);
        }

        var turn = Matrix4x4.CreateFromQuaternion(UnityEuler(F(HoldRotX), F(HoldRotY), F(HoldRotZ)));
        var beforeHold = pose.Scale * modelScale;
        var tail = turn * Matrix4x4.CreateScale(beforeHold * HoldScaleVector) * Matrix4x4.CreateTranslation(F(HoldX), F(HoldY), F(HoldZ));
        Matrix4x4.Invert(Trs(pose.Position, pose.Rotation, pose.Scale), out var vanilla);
        // Kitbash parts sit in a holder at the prefab root's pose under the already scaled attach (see Parts.Apply).
        Matrix4x4.Invert(Trs(pose.Position, pose.Rotation, beforeHold), out var scaledAttach);
        var normals = Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(pose.Rotation)) * turn;
        return (vanilla * tail, Matrix4x4.CreateScale(beforeHold) * scaledAttach * tail, normals);
    }

    private static Matrix4x4 Trs(Vector3 position, Quaternion rotation, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);

    /// <summary>Unity's Quaternion.Euler: Y, then X, then Z applied (q = qy * qx * qz).</summary>
    private static Quaternion UnityEuler(float x, float y, float z) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, ToRadians(y)) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, ToRadians(x)) *
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, ToRadians(z));

    private static float ToRadians(float degrees) => degrees * MathF.PI / 180f;

    /// <summary>A hold number from its box: blank counts as 0 (position and turn).</summary>
    private static float F(decimal? value) => value is decimal d ? (float)d : 0f;

    /// <summary>A hold scale from its box: blank or 0 counts as 1, since a zero scale would hide the item.</summary>
    private static float S(decimal? value) => value is decimal d && d != 0 ? (float)d : 1f;

    partial void OnModelFileChanged(string value)
    {
        if (value.Length > 0 && MeshFrom.Length > 0)
            MeshFrom = "";
        OnPropertyChanged(nameof(MeshLabel));
        OnPropertyChanged(nameof(ModelFileLabel));
        OnPropertyChanged(nameof(HasModelFile));
        LoadFileModel();
        BuildMaterialEditors();
        Refresh();
        ScheduleSave();
    }

    private void LoadFileModel()
    {
        _fileModel = null;
        ModelFileError = "";
        if (ModelFile.Length == 0 || Pack?.FullPath(ModelFile) is not { } path)
            return;
        _fileModel = GlbPreview.Load(path, out var error);
        ModelFileError = error ?? "";
    }
    [ObservableProperty] private string _iconFile;
    [ObservableProperty] private string _iconPrefab = "";
    /// <summary>"#RRGGBB" multiplied over the icon; blank for none.</summary>
    [ObservableProperty] private string _iconTint = "";

    partial void OnIconTintChanged(string value)
    {
        LoadIcon();
        ScheduleSave();
    }

    [RelayCommand]
    private void ClearIconTint() => IconTint = "";

    /// <summary>The icon with the tint multiplied in, for the preview (the game does the same on load).</summary>
    private RgbaImage Tinted(RgbaImage image)
    {
        if (!RecipeSerializer.TryParseColor(IconTint, out var c))
            return image;
        var bgra = new byte[image.Bgra.Length];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = (byte)(image.Bgra[i] * c[2]);
            bgra[i + 1] = (byte)(image.Bgra[i + 1] * c[1]);
            bgra[i + 2] = (byte)(image.Bgra[i + 2] * c[0]);
            bgra[i + 3] = image.Bgra[i + 3];
        }
        return new RgbaImage { Width = image.Width, Height = image.Height, Bgra = bgra };
    }
    [ObservableProperty] private Bitmap? _iconImage;
    [ObservableProperty] private string _scripts = "";

    // Sprites: flat images on the model
    public ObservableCollection<SpriteRow> Sprites { get; } = new();
    [ObservableProperty] private bool _hideMesh;
    public bool HasSprites => Sprites.Count > 0;

    // Individual meshes of the base model (rocks in a cluster, a bit of a chest), ticked to hide.
    public ObservableCollection<MeshToggleRow> MeshRows { get; } = new();
    public bool HasMeshRows => MeshRows.Count > 1;

    private void BuildMeshRows(PrefabInfo info)
    {
        MeshRows.Clear();
        var hidden = new HashSet<string>(Recipe.Look.HideMeshes, StringComparer.Ordinal);
        foreach (var renderer in info.Renderers.Where(r => r.MeshName != "(none)"))
            MeshRows.Add(new MeshToggleRow(renderer.Path, renderer.MeshName, hidden.Contains(renderer.Path), OnMeshToggled));
        OnPropertyChanged(nameof(HasMeshRows));
    }

    private void OnMeshToggled()
    {
        if (_loading)
            return;
        Refresh();
        ScheduleSave();
    }

    /// <summary>A model part is named "{renderer path}#{submesh}"; this gives back the renderer path.</summary>
    private static string RendererPath(string partName)
    {
        var hash = partName.LastIndexOf('#');
        return hash < 0 ? partName : partName[..hash];
    }

    /// <summary>Paths of the base model's meshes that are hidden right now.</summary>
    private HashSet<string> HiddenMeshPaths() =>
        MeshRows.Where(r => r.IsHidden).Select(r => r.Path).ToHashSet(StringComparer.Ordinal);

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
    public bool MarkerEditing => (TabSnap && SnapEditable) || (TabLook && HasParts) || (TabCollision && CollisionBox);
    public string ViewportHint => !MarkerEditing
        ? "Drag to orbit · wheel to zoom · double-click to reset. Approximate shading; Push to game for the real look."
        : TabLook
            ? "Drag a purple P handle to move that part · Delete removes the selected part · drag elsewhere to orbit · wheel to zoom"
            : TabCollision
                ? "Drag the green C handle to move the box, or a side handle (top, left…) to move that side · drag elsewhere to orbit · wheel to zoom"
                : "Drag a numbered point to move it · Delete removes the selected one · drag elsewhere to orbit · wheel to zoom";

    // Collision: keep the base's colliders, or one box / the model's own meshes instead.
    public static readonly string[] CollisionModes = { "base", "box", "mesh" };
    [ObservableProperty] private string _collisionMode;
    /// <summary>The box, local to the prefab root before look.scale (metres), as the recipe stores it.</summary>
    private Vector3 _boxCenter;
    private Vector3 _boxSize;

    public bool CollisionBase => CollisionMode == "base";
    public bool CollisionBox => CollisionMode == "box";
    public bool CollisionMesh => CollisionMode == "mesh";
    public string BaseCollisionText => _base?.Info.Colliders is not { } colliders
        ? ""
        : colliders.Count(c => !c.IsTrigger && c.Enabled) is var solid and > 0
            ? $"The base collides with {solid} shape{(solid == 1 ? "" : "s")} (blue outlines). {(CollisionBase ? "" : "They are switched off in game; triggers stay.")}"
            : "The base has no solid collision.";
    /// <summary>A MeshCollider added on the Components tab sits on the root, which has no mesh, so it collides with nothing.</summary>
    public bool HasRootMeshCollider => Components.AddedNames.Contains("MeshCollider");

    /// <summary>Pieces scale their root, so the box scales with the model; items scale only attach, so it doesn't.</summary>
    private Vector3 BoxScale => IsItem ? Vector3.One : ViewScale;

    public decimal? BoxCenterX { get => BoxShown(_boxCenter.X); set => SetBox(value, null, 0); }
    public decimal? BoxCenterY { get => BoxShown(_boxCenter.Y); set => SetBox(value, null, 1); }
    public decimal? BoxCenterZ { get => BoxShown(_boxCenter.Z); set => SetBox(value, null, 2); }
    public decimal? BoxSizeX { get => BoxShown(_boxSize.X); set => SetBox(null, value, 0); }
    public decimal? BoxSizeY { get => BoxShown(_boxSize.Y); set => SetBox(null, value, 1); }
    public decimal? BoxSizeZ { get => BoxShown(_boxSize.Z); set => SetBox(null, value, 2); }
    public decimal SizeMin => Cm ? 1m : 0.01m;

    private static decimal? BoxShown(float metres) => ToShown((decimal)Math.Round(metres, 4));

    private void SetBox(decimal? center, decimal? size, int axis)
    {
        if (FromShown(center ?? size) is not decimal metres)
            return;
        var v = (float)metres;
        if (center != null)
            _boxCenter = With(_boxCenter, axis, v);
        else
            _boxSize = With(_boxSize, axis, Math.Max(v, 0.01f));
        BoxEdited(save: true);
    }

    private static Vector3 With(Vector3 v, int axis, float value) => axis == 0 ? v with { X = value } : axis == 1 ? v with { Y = value } : v with { Z = value };
    private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private void BoxEdited(bool save)
    {
        foreach (var name in new[] { nameof(BoxCenterX), nameof(BoxCenterY), nameof(BoxCenterZ), nameof(BoxSizeX), nameof(BoxSizeY), nameof(BoxSizeZ) })
            OnPropertyChanged(name);
        Refresh();
        if (save)
            ScheduleSave();
    }

    partial void OnCollisionModeChanged(string value)
    {
        foreach (var name in new[] { nameof(CollisionBase), nameof(CollisionBox), nameof(CollisionMesh), nameof(BaseCollisionText), nameof(MarkerEditing), nameof(ViewportHint) })
            OnPropertyChanged(name);
        // A new box starts around the model.
        if (value == "box" && (_boxSize.X <= 0 || _boxSize.Y <= 0 || _boxSize.Z <= 0))
            FitCollision();
        Refresh();
        ScheduleSave();
    }

    partial void OnShowCollisionChanged(bool value) => Refresh();

    [RelayCommand]
    private void SetCollisionMode(string mode) => CollisionMode = mode;

    /// <summary>The box hugs the model as drawn (model scale included).</summary>
    [RelayCommand]
    private void FitCollision()
    {
        if (_shape.IsEmpty)
            return;
        var toBox = ViewScale / BoxScale;
        _boxCenter = Round(_shape.Center * toBox);
        _boxSize = Round(Vector3.Max(_shape.Size * toBox, new Vector3(0.01f)));
        BoxEdited(save: true);
    }

    private static Vector3 Round(Vector3 v) => new((float)Math.Round(v.X, 3), (float)Math.Round(v.Y, 3), (float)Math.Round(v.Z, 3));

    /// <summary>Marker ids from here up are the collision box: the centre, then the +X, −X, +Y, −Y, +Z, −Z sides.</summary>
    private const int BoxMarkerBase = 2000;
    // Named as the viewport's compass shows them: right is −X, front is +Z.
    private static readonly string[] FaceLabels = { "left", "right", "top", "bottom", "front", "back" };

    private IEnumerable<Marker> BoxMarkers()
    {
        var scale = BoxScale;
        yield return new Marker(_boxCenter * scale, 0xFF7CE88A, 11, BoxMarkerBase, "C");
        for (var face = 0; face < 6; face++)
        {
            var axis = face / 2;
            var sign = face % 2 == 0 ? 1f : -1f;
            var at = With(_boxCenter, axis, Axis(_boxCenter, axis) + sign * Axis(_boxSize, axis) / 2f);
            yield return new Marker(at * scale, 0xFF4FB862, 9, BoxMarkerBase + 1 + face, FaceLabels[face]);
        }
    }

    /// <summary>Drags the centre (moves the box) or one face (that side moves, the opposite side stays).</summary>
    private void MoveBoxMarker(int id, Vector3 local, string constraint)
    {
        var snapped = SnapGeometry.Snap(local, _shape, SnapToEdges, (float)SnapGrid,
            constraint is "free" or "x" or "floor", constraint is "free" or "y", constraint is "free" or "z" or "floor");
        if (id == BoxMarkerBase)
        {
            _boxCenter = Round(snapped);
        }
        else
        {
            var face = id - BoxMarkerBase - 1;
            var axis = face / 2;
            var sign = face % 2 == 0 ? 1f : -1f;
            var opposite = Axis(_boxCenter, axis) - sign * Axis(_boxSize, axis) / 2f;
            var moved = Axis(snapped, axis);
            if ((moved - opposite) * sign < 0.01f)
                moved = opposite + sign * 0.01f;
            _boxCenter = Round(With(_boxCenter, axis, (moved + opposite) / 2f));
            _boxSize = Round(With(_boxSize, axis, Math.Abs(moved - opposite)));
        }
        BoxEdited(save: false);
    }

    /// <summary>Collision outlines: the base's colliders (blue when used, dim red when switched off), triggers, and the box.</summary>
    private List<Wire> CollisionWires(bool compare)
    {
        var wires = new List<Wire>();
        if (_base == null)
            return wires;
        var replaced = !compare && !CollisionBase;
        var scale = Matrix4x4.CreateScale(ViewScale);
        foreach (var c in _base.Info.Colliders.Where(c => c.Enabled))
        {
            // Item colliders on the root keep their size; the model scale is on attach.
            var toView = IsItem && !c.Path.StartsWith(AttachPath, StringComparison.Ordinal) ? Matrix4x4.Identity : scale;
            var color = c.IsTrigger ? 0x80E8C83Cu : replaced ? 0x70E05A5Au : 0xFF5FB4F0u;
            CollisionGeometry.AddCollider(wires, c, toView, color);
        }
        if (!compare && CollisionBox)
            CollisionGeometry.AddBox(wires, _boxCenter, _boxSize, Matrix4x4.CreateScale(BoxScale), 0xFF7CE88A);
        return wires;
    }

    private const string AttachPath = "attach";

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
        if (id >= BoxMarkerBase)
            return;
        if (id >= PartMarkerBase)
            SelectedPart = PartRows.ElementAtOrDefault(id - PartMarkerBase);
        else
            SelectedSnap = SnapPoints.ElementAtOrDefault(id);
    }

    public void MoveMarker(int id, Vector3 unityPosition, string constraint)
    {
        if (id >= BoxMarkerBase)
        {
            MoveBoxMarker(id, unityPosition / BoxScale, constraint);
            return;
        }
        var local = unityPosition / ViewScale;
        if (id >= PartMarkerBase && Matrix4x4.Invert(ViewTransforms().Parts, out var fromView))
            local = Vector3.Transform(unityPosition, fromView);
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
        if (id >= BoxMarkerBase)
            return;
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
        BuildMeshRows(_base.Info);
        DescribeEffects(_base.Info);
        _ = Components.BuildAsync(_base.Info, Recipe.Fields, Recipe.RemoveComponents, Recipe.AddComponents.Select(a => a.Type),
            Vanilla == null ? null : Vanilla.ComponentDefaultsAsync, Vanilla == null ? null : Vanilla.ValheimScriptsAsync);
        BaseSnapText = _base.Info.SnapPoints.Count == 0
            ? "The base has no snap points."
            : $"The base has {_base.Info.SnapPoints.Count}: {string.Join("  ", _base.Info.SnapPoints.Select(p => $"({p.X:0.##}, {p.Y:0.##}, {p.Z:0.##})"))}";
        OnPropertyChanged(nameof(BaseCollisionText));
        LoadFileModel();
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
    private VanillaModel? ShownModel => CompareToBase ? _base?.Model : (_fileModel ?? _meshSource?.Model ?? _base?.Model);

    private void BuildMaterialEditors()
    {
        Materials.Clear();
        var model = _fileModel ?? _meshSource?.Model ?? _base?.Model;
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
            IconImage = Images.ToBitmap(Tinted(custom));
        else if (IconFile.Length == 0 && IconPrefab.Length > 0 && Vanilla?.Catalog.ByName.TryGetValue(IconPrefab, out var source) == true)
            Vanilla?.RequestIcon(source, bitmap => IconImage = bitmap);
        else
            IconImage = _base?.Icon != null ? Images.ToBitmap(Tinted(_base.Icon)) : null;
    }

    /// <summary>Re-render with current edits.</summary>
    private void Refresh()
    {
        var model = ShownModel;
        if (model == null)
        {
            Parts = null;
            Wires = null;
            ViewportMessage ??= "No model";
            return;
        }

        var worn = ShowWorn && model.WornParts.Count > 0;
        var parts = (worn ? model.WornParts : model.Parts).ToList();
        var hidden = HiddenMeshPaths();
        if (hidden.Count > 0 && ReferenceEquals(model, _base?.Model))
            parts.RemoveAll(p => hidden.Contains(RendererPath(p.Name)));
        var scale = ViewScale;
        // Parts, scale and sprites belong to the dropped/placed model, as in game.
        if (!worn && !CompareToBase)
        {
            if (HideMesh)
                parts.Clear();
            var kitbash = PartRows.SelectMany((row, i) => row.ViewParts(i)).ToList();
            // Snap points belong to the placed model, in its own (unscaled) space.
            _shape = SnapGeometry.Bounds(parts.Concat(kitbash));
            ShapeText = _shape.IsEmpty ? "" : $"Model: {_shape.Size.X:0.##} wide × {_shape.Size.Y:0.##} tall × {_shape.Size.Z:0.##} deep (m)";

            var (toModel, toParts, normals) = ViewTransforms();
            parts = parts.Select(p => PartRow.Transform(p, toModel, normals, p.MaterialSlot))
                .Concat(kitbash.Select(p => PartRow.Transform(p, toParts, normals, p.MaterialSlot))).ToList();
            if (HeldView)
            {
                // A figure with its right hand at the hand bone. Sprites sit on the item's root, so they aren't held.
                parts.AddRange(Mannequin.Parts());
            }
            else
            {
                // Items scale their visual (attach) only; their sprites sit on the root and keep their size.
                var spriteScale = IsItem ? Vector3.One : scale;
                parts.AddRange(Sprites.Where(s => !s.IsMissing).Select((s, i) =>
                {
                    var part = s.ToPart(SpriteRow.SlotBase + i);
                    return spriteScale == Vector3.One ? part : PartRow.Transform(part, Matrix4x4.CreateScale(spriteScale), Matrix4x4.Identity, part.MaterialSlot);
                }));
            }
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
            if (slot == Mannequin.Slot)
                return new SlotLook(null, new Vector4(0.62f, 0.62f, 0.68f, 1f));
            if (slot == Mannequin.FrontSlot)
                return new SlotLook(null, new Vector4(0.95f, 0.55f, 0.15f, 1f));
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
        {
            var toParts = ViewTransforms().Parts;
            markers.AddRange(PartRows.Select((r, i) => new Marker(Vector3.Transform(r.Position, toParts), 0xFFB07CE8, 11, PartMarkerBase + i, "P" + (i + 1), r.IsSelected)));
        }

        // Collision belongs to the placed/dropped model, not the held or worn one.
        var showCollision = (ShowCollision || TabCollision) && !worn && !HeldView;
        Wires = showCollision ? CollisionWires(CompareToBase) : null;
        if (showCollision && TabCollision && CollisionBox && !CompareToBase)
            markers.AddRange(BoxMarkers());

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
        OnPropertyChanged(nameof(TabCollision));
        OnPropertyChanged(nameof(HasRootMeshCollider));
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
        if (value.Length > 0 && ModelFile.Length > 0)
            ModelFile = "";
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

    private string CostItemId(string item) =>
        Pack != null && _main.Vanilla != null ? CostItems.Resolve(item, Pack.Recipes, VanillaItems, out _) ?? item.Trim() : item.Trim();

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

        Recipe.Look.Mesh = ModelFile.Length > 0 ? new MeshSource { File = ModelFile }
            : MeshFrom.Length > 0 ? new MeshSource { Prefab = MeshFrom } : null;
        Recipe.Look.Icon = IconFile.Length > 0 ? IconFile : null;
        Recipe.Look.IconPrefab = IconFile.Length == 0 && IconPrefab.Length > 0 ? IconPrefab : null;
        Recipe.Look.IconTint = IconTint.Length > 0 && RecipeSerializer.TryParseColor(IconTint, out _) ? IconTint : null;
        Recipe.Look.Materials = Materials.Where(m => m.Part == null).Select(m => m.ToOverride()).Where(o => o != null).Select(o => o!).ToList();
        Recipe.Look.Parts = PartRows.Select(r => r.ToRecipe()).ToList();
        Recipe.Look.Scale = _modelScaleVector ?? new Vec3((float)Math.Round(Math.Max(ModelScale, 0.001), 4), (float)Math.Round(Math.Max(ModelScale, 0.001), 4), (float)Math.Round(Math.Max(ModelScale, 0.001), 4));
        Recipe.Look.Sprites = Sprites.Select(s => s.ToRecipe()).ToList();
        Recipe.Look.HideMesh = HideMesh;
        Recipe.Look.HoldPosition = new Vec3(F(HoldX), F(HoldY), F(HoldZ));
        Recipe.Look.HoldRotation = new Vec3(F(HoldRotX), F(HoldRotY), F(HoldRotZ));
        Recipe.Look.HoldScale = IsItem ? new Vec3(S(HoldScaleX), S(HoldScaleY), S(HoldScaleZ)) : new Vec3(1, 1, 1);
        Recipe.Look.HideMeshes = MeshRows.Where(r => r.IsHidden).Select(r => r.Path).ToList();
        Recipe.Look.Collision = CollisionMode switch
        {
            "box" => new CollisionRecipe
            {
                Mode = Format.CollisionMode.Box,
                Center = new Vec3(_boxCenter.X, _boxCenter.Y, _boxCenter.Z),
                Size = new Vec3(Math.Max(_boxSize.X, 0.01f), Math.Max(_boxSize.Y, 0.01f), Math.Max(_boxSize.Z, 0.01f))
            },
            "mesh" => new CollisionRecipe { Mode = Format.CollisionMode.Mesh },
            _ => null
        };

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
                // A display name ("Piece of Paper") or wrong case is saved as the id the game needs.
                Requirements = Requirements.Where(r => r.Item.Trim().Length > 0)
                    .Select(r => new Requirement { Item = CostItemId(r.Item), Amount = r.Amount, Recover = r.Recover }).ToList()
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
        IconPrefab = "";
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
        IconPrefab = "";
        LoadIcon();
        ScheduleSave();
    }

    [RelayCommand]
    private void ChooseIconFromPrefab() =>
        _main.Workspace.Picker = new AssetPickerViewModel(_main, PickerMode.Icon, "Borrow an icon: pick a prefab whose icon to use",
            (prefab, _) =>
            {
                IconPrefab = prefab;
                IconFile = "";
                LoadIcon();
                ScheduleSave();
            });

    [RelayCommand]
    private void ResetIcon()
    {
        IconFile = "";
        IconPrefab = "";
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

    [RelayCommand]
    private async Task ChooseModelFile()
    {
        var file = await Dialogs.PickFileAsync("Choose a 3D model (glTF binary, .glb)", "3D models", "*.glb");
        if (file == null || Pack == null)
            return;
        // Copied into the pack, so the pack carries its own model like its textures.
        ModelFile = Pack.AddFile(file, "models");
    }

    [RelayCommand]
    private void ClearModelFile() => ModelFile = "";

    public string MeshLabel => ModelFile.Length > 0 ? ModelFile : MeshFrom.Length == 0 ? $"{Recipe.Base} (base)" : MeshFrom;
    public bool HasMeshSwap => MeshFrom.Length > 0;

    [RelayCommand]
    private void AddRequirement() => AddRequirementRow("", 1, true);

    private void AddRequirementRow(string item, int amount, bool recover)
    {
        var row = new RequirementRow { Item = item, Amount = amount, Recover = recover };
        row.RemoveCommand = new RelayCommand(() => Requirements.Remove(row));
        row.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(RequirementRow.Item))
                CheckCost(row);
        };
        CheckCost(row);
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
