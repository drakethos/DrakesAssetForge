using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakeAssetForge.Models;
using DrakeAssetForge.Services;

namespace DrakeAssetForge.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private SoftRefManifestReader.SoftRefIndex? _index;
    private IReadOnlyList<SoftRefAssetEntry> _allAssets = Array.Empty<SoftRefAssetEntry>();
    private IReadOnlyList<BundleListItem> _allBundles = Array.Empty<BundleListItem>();
    private IReadOnlyList<BundleObjectItem> _bundleObjectsAll = Array.Empty<BundleObjectItem>();
    private string? _resolvedContainerPath;
    private string? _resolvedPathId;
    private SoftRefPropertySpyService? _fieldSpy;
    private int _fieldSpyGeneration;
    private ProjectStore _projectStore = new(ProjectStore.DefaultProjectRoot());
    private readonly ShaderCatalogService _shaderCatalogService = new();
    private bool _suppressOwnedSelectionSideEffects;
    private PrefabPropertySpyResult? _lastPrefabSpy;
    private MaterialDocument _workingMaterial = new();
    private ArtDocument _workingArt = new();
    private bool _suppressMaterialSelection;

    /// <summary>Wired by MainWindow for open-file dialogs (title, patterns → local path).</summary>
    public Func<string, IReadOnlyList<string>, Task<string?>>? PickOpenFileAsync { get; set; }

    [ObservableProperty]
    private AppScreen _appScreen = AppScreen.Catalog;

    [ObservableProperty]
    private InspectorSheet _inspectorSheet = InspectorSheet.Scripts;

    [ObservableProperty]
    private bool _showPinsTray = true;

    [ObservableProperty]
    private string _valheimPath = string.Empty;

    [ObservableProperty]
    private string _statusText = "File → Open Valheim Folder… to get started.";

    [ObservableProperty]
    private string _assetFilter = string.Empty;

    [ObservableProperty]
    private string _objectFilter = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hideResourceNoise = true;

    [ObservableProperty]
    private CatalogViewMode _viewMode = CatalogViewMode.Items;

    [ObservableProperty]
    private string _selectedSubCategory = "All";

    [ObservableProperty]
    private SoftRefAssetEntry? _selectedAsset;

    [ObservableProperty]
    private SoftRefAssetEntry? _selectedPin;

    [ObservableProperty]
    private BundleListItem? _selectedBundle;

    [ObservableProperty]
    private string _selectionLabel = "Nothing selected";

    [ObservableProperty]
    private string _inspectorModeLabel = "Inspector · read-only";

    [ObservableProperty]
    private string _previewCaption = "Select an icon, texture, or item to preview.";

    [ObservableProperty]
    private Bitmap? _previewBitmap;

    [ObservableProperty]
    private bool _hasPreviewImage;

    [ObservableProperty]
    private BundleObjectItem? _selectedBundleObject;

    [ObservableProperty]
    private string _exportStubNote =
        "Export pipeline lands after Project + art compile. Catalog first.";

    [ObservableProperty]
    private string _projectPathLabel = "";

    [ObservableProperty]
    private OwnedItemDocument? _selectedOwnedItem;

    [ObservableProperty]
    private int _visibleCount;

    [ObservableProperty]
    private int _itemsTabCount;

    [ObservableProperty]
    private int _piecesTabCount;

    [ObservableProperty]
    private int _prefabsTabCount;

    [ObservableProperty]
    private int _iconsTabCount;

    [ObservableProperty]
    private int _recipesTabCount;

    [ObservableProperty]
    private int _rawTabCount;

    [ObservableProperty]
    private string _scriptsSpyStatus = "Select a prefab to spy Valheim scripts.";

    [ObservableProperty]
    private string _recipeSpyStatus = "Select an Item or Recipe to spy craft data.";

    [ObservableProperty]
    private SpyScriptComponent? _selectedSpyScript;

    public ObservableCollection<SoftRefAssetEntry> Assets { get; } = new();
    public ObservableCollection<SoftRefAssetEntry> Pins { get; } = new();
    public ObservableCollection<BundleObjectItem> BundleObjects { get; } = new();
    public ObservableCollection<string> SubCategories { get; } = new();
    [ObservableProperty]
    private string _materialStatus = "Build the shader catalog after SoftRef loads, then edit materials on Project items.";

    [ObservableProperty]
    private bool _isShaderCatalogBusy;

    [ObservableProperty]
    private bool _useDonorMaterials = true;

    [ObservableProperty]
    private string _materialShaderFilter = "";

    [ObservableProperty]
    private ShaderCatalogEntry? _selectedShader;

    [ObservableProperty]
    private string _artStatus = "Clone into Project, then attach FBX/PNG on the Art sheet.";

    [ObservableProperty]
    private string _artMeshPath = "";

    [ObservableProperty]
    private string _artIconPath = "";

    [ObservableProperty]
    private string _artDiffusePath = "";

    public ObservableCollection<SpyScriptComponent> SpyScripts { get; } = new();
    public ObservableCollection<PropertyNode> ScriptPropertyTree { get; } = new();
    public ObservableCollection<EditableFieldRow> ScriptFields { get; } = new();
    public ObservableCollection<FieldRow> RecipeFields { get; } = new();
    public ObservableCollection<PropertyNode> RecipePropertyTree { get; } = new();
    public ObservableCollection<RecipeRequirementRow> RecipeRequirements { get; } = new();
    public ObservableCollection<OwnedItemDocument> OwnedItems { get; } = new();
    public ObservableCollection<ShaderCatalogEntry> ShaderCatalog { get; } = new();
    public ObservableCollection<ShaderCatalogEntry> FilteredShaders { get; } = new();
    public ObservableCollection<MaterialPropertyRow> MaterialProperties { get; } = new();

    public bool CanEditMaterial => !IsInspectorReadOnly && SelectedOwnedItem != null;
    public bool CanEditArt => !IsInspectorReadOnly && SelectedOwnedItem != null;
    public bool IsCustomMaterialMode => CanEditMaterial && !UseDonorMaterials;
    public string MaterialModeHint =>
        CanEditMaterial
            ? (UseDonorMaterials
                ? "Donor mode: clone keeps vanilla materials. Uncheck to pick a Shader.Find + edit material.json."
                : "Custom mode: Save writes material.json for runtime Shader.Find + property stamps.")
            : "Spy: browse Shader.Find names and property schemas. Clone into Project to assign one to an item.";

    public bool IsCatalogScreen => AppScreen == AppScreen.Catalog;
    public bool IsProjectScreen => AppScreen == AppScreen.Project;
    public bool IsExportScreen => AppScreen == AppScreen.Export;
    public bool IsInspectorReadOnly => AppScreen == AppScreen.Catalog;
    public bool HasOwnedSelection => SelectedOwnedItem != null;

    public bool IsSheetScripts => InspectorSheet == InspectorSheet.Scripts;
    public bool IsSheetMaterial => InspectorSheet == InspectorSheet.Material;
    public bool IsSheetArt => InspectorSheet == InspectorSheet.Art;
    public bool IsSheetRecipe => InspectorSheet == InspectorSheet.Recipe;
    public bool IsSheetPrefab => InspectorSheet == InspectorSheet.Prefab;
    public bool IsSheetCodegen => InspectorSheet == InspectorSheet.Codegen;

    public string ResolvedContainerPath => _resolvedContainerPath ?? "(open owning bundle)";
    public string ResolvedPathId => _resolvedPathId ?? "—";

    public MainViewModel()
    {
        var detected = ValheimPathFinder.TryFindFromEnvironmentProps();
        if (!string.IsNullOrWhiteSpace(detected))
            ValheimPath = detected;
        RefreshShellLabels();
        StatusText = string.IsNullOrWhiteSpace(ValheimPath)
            ? "File → Open Valheim Folder… to get started."
            : "Loading SoftRef…";
        ReloadOwnedItems();
        TryLoadShaderCatalogCache();
    }

    /// <summary>Called once the main window is ready (auto SoftRef load).</summary>
    public void StartInitialLoad()
    {
        if (!string.IsNullOrWhiteSpace(ValheimPath))
            _ = LoadValheimAsync();
    }

    public async Task SetValheimPathAndReloadAsync(string path)
    {
        ValheimPath = path;
        await LoadValheimAsync();
    }

    partial void OnAppScreenChanged(AppScreen value)
    {
        OnPropertyChanged(nameof(IsCatalogScreen));
        OnPropertyChanged(nameof(IsProjectScreen));
        OnPropertyChanged(nameof(IsExportScreen));
        OnPropertyChanged(nameof(IsInspectorReadOnly));
        OnPropertyChanged(nameof(CanEditMaterial));
        OnPropertyChanged(nameof(CanEditArt));
        OnPropertyChanged(nameof(IsCustomMaterialMode));
        OnPropertyChanged(nameof(MaterialModeHint));
        RefreshShellLabels();

        if (value == AppScreen.Project)
        {
            ReloadOwnedItems();
            if (SelectedOwnedItem != null)
                LoadOwnedItemIntoInspector(SelectedOwnedItem);
        }
        else if (value == AppScreen.Catalog && SelectedAsset != null)
        {
            _ = RefreshFieldSpyAsync();
        }
    }

    partial void OnUseDonorMaterialsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomMaterialMode));
        OnPropertyChanged(nameof(MaterialModeHint));
        if (!CanEditMaterial)
            return;

        _workingMaterial.Mode = value ? MaterialAuthoringMode.Donor : MaterialAuthoringMode.Custom;
        if (!value && SelectedShader == null && FilteredShaders.Count > 0)
        {
            SelectedShader = FilteredShaders.FirstOrDefault(s =>
                                s.FindName.Contains("Creature", StringComparison.OrdinalIgnoreCase))
                            ?? FilteredShaders.FirstOrDefault();
        }

        MaterialStatus = value
            ? "Donor mode — runtime keeps clone materials (mesh/icon swap only)."
            : "Custom mode — runtime will Shader.Find + apply these properties.";
    }

    partial void OnMaterialShaderFilterChanged(string value) => ApplyShaderFilter();

    partial void OnSelectedShaderChanged(ShaderCatalogEntry? value)
    {
        if (_suppressMaterialSelection || value == null)
            return;

        if (CanEditMaterial && !UseDonorMaterials)
            ApplyShaderSchemaToWorkingMaterial(value);
        else
            ShowShaderSchemaBrowse(value);
    }

    partial void OnInspectorSheetChanged(InspectorSheet value)
    {
        OnPropertyChanged(nameof(IsSheetScripts));
        OnPropertyChanged(nameof(IsSheetMaterial));
        OnPropertyChanged(nameof(IsSheetArt));
        OnPropertyChanged(nameof(IsSheetRecipe));
        OnPropertyChanged(nameof(IsSheetPrefab));
        OnPropertyChanged(nameof(IsSheetCodegen));
    }

    partial void OnAssetFilterChanged(string value) => ApplyCatalogFilter();
    partial void OnObjectFilterChanged(string value)
    {
        // Re-query bundle when filter changes and a bundle is already selected.
        if (SelectedBundle != null && !IsBusy)
            _ = InspectSelectedBundleAsync();
        else
            ApplyObjectFilter();
    }

    partial void OnHideResourceNoiseChanged(bool value) => RefreshSubCategoriesAndFilter();
    partial void OnViewModeChanged(CatalogViewMode value) => RefreshSubCategoriesAndFilter();
    partial void OnSelectedSubCategoryChanged(string value) => ApplyCatalogFilter();

    partial void OnSelectedAssetChanged(SoftRefAssetEntry? value)
    {
        if (AppScreen == AppScreen.Project)
            return;

        _resolvedContainerPath = null;
        _resolvedPathId = null;
        RefreshShellLabels();
        OnPropertyChanged(nameof(ResolvedContainerPath));
        OnPropertyChanged(nameof(ResolvedPathId));
        if (value != null &&
            value.Kind is CatalogKind.ItemPrefab or CatalogKind.PiecePrefab or CatalogKind.OtherPrefab)
        {
            ObjectFilter = value.DisplayName;
        }

        _ = RefreshPreviewAsync();
        _ = RefreshFieldSpyAsync();
    }

    partial void OnSelectedOwnedItemChanged(OwnedItemDocument? value)
    {
        OnPropertyChanged(nameof(HasOwnedSelection));
        OnPropertyChanged(nameof(CanEditMaterial));
        OnPropertyChanged(nameof(CanEditArt));
        OnPropertyChanged(nameof(IsCustomMaterialMode));
        OnPropertyChanged(nameof(MaterialModeHint));
        if (_suppressOwnedSelectionSideEffects)
            return;

        RefreshShellLabels();
        if (value == null)
        {
            ClearFieldSpy("Select a Project item to edit.", "Select a Project item to edit.");
            ClearPreview("Select a Project item to preview its donor icon.");
            ClearMaterialUi("Select a Project item to edit materials.");
            ClearArtUi("Select a Project item to attach art.");
            return;
        }

        LoadOwnedItemIntoInspector(value);
        _ = RefreshOwnedPreviewAsync(value);
    }

    partial void OnSelectedBundleObjectChanged(BundleObjectItem? value)
    {
        if (value != null)
            _ = RefreshPreviewFromBundleObjectAsync(value);
    }

    partial void OnSelectedPinChanged(SoftRefAssetEntry? value)
    {
        if (value != null)
            SelectedAsset = value;
    }

    partial void OnSelectedBundleChanged(BundleListItem? value)
    {
        if (value != null)
            _ = InspectSelectedBundleAsync();
    }

    [RelayCommand]
    public void SetAppScreen(string? screenName)
    {
        if (Enum.TryParse<AppScreen>(screenName, ignoreCase: true, out var screen))
            AppScreen = screen;
    }

    [RelayCommand]
    public void SetInspectorSheet(string? sheetName)
    {
        if (Enum.TryParse<InspectorSheet>(sheetName, ignoreCase: true, out var sheet))
            InspectorSheet = sheet;
    }

    [RelayCommand]
    public void TogglePinsTray() => ShowPinsTray = !ShowPinsTray;

    [RelayCommand]
    public async Task LoadValheimAsync()
    {
        if (IsBusy)
            return;

        var path = ValheimPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path) || !ValheimPathFinder.LooksLikeValheimInstall(path))
        {
            StatusText = "Invalid Valheim path (expected a folder containing valheim_Data).";
            return;
        }

        IsBusy = true;
        StatusText = "Loading SoftRef manifest…";
        try
        {
            var softRef = ValheimPathFinder.GetSoftRefRoot(path);
            if (softRef == null)
            {
                StatusText = "SoftRef folder not found under valheim_Data/StreamingAssets.";
                return;
            }

            var index = await Task.Run(() => SoftRefManifestReader.Load(softRef));
            var bundles = await Task.Run(() => SoftRefManifestReader.ListBundleFiles(index));

            _index = index;
            _allAssets = index.Assets;
            _allBundles = bundles;
            ValheimPath = path;

            _fieldSpy?.Dispose();
            _fieldSpy = null;
            var managed = ValheimPathFinder.GetManagedDirectory(path);
            if (managed != null)
                _fieldSpy = new SoftRefPropertySpyService(managed);

            ClearFieldSpy("Select a prefab to spy Valheim scripts.",
                "Select an Item or Recipe to spy craft data.");

            UpdateTabCounts();
            RefreshSubCategoriesAndFilter();
            BundleObjects.Clear();
            RefreshShellLabels();

            StatusText =
                $"Loaded {index.Assets.Count:N0} SoftRef assets / {bundles.Count:N0} bundles. " +
                $"Item prefabs: {ItemsTabCount:N0}.";

            if (ShaderCatalog.Count == 0)
                _ = BuildShaderCatalogAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task InspectSelectedBundleAsync()
    {
        if (IsBusy || SelectedBundle == null)
            return;

        IsBusy = true;
        StatusText = $"Reading bundle {SelectedBundle.BundleId}…";
        try
        {
            var path = SelectedBundle.FullPath;
            var filter = ObjectFilter;
            var objects = await Task.Run(() => BundleAssetLister.ListObjects(path, nameFilter: filter));
            _bundleObjectsAll = objects;
            ApplyObjectFilter();

            StatusText = string.IsNullOrWhiteSpace(filter)
                ? $"Bundle {SelectedBundle.BundleId}: {objects.Count:N0} container entries."
                : $"Bundle {SelectedBundle.BundleId}: {objects.Count:N0} matching '{filter}'.";
        }
        catch (Exception ex)
        {
            BundleObjects.Clear();
            StatusText = $"Bundle inspect failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void RevealSelectedAssetBundle()
    {
        if (SelectedAsset == null || _index == null)
            return;

        var match = _allBundles.FirstOrDefault(b =>
            b.BundleId.Equals(SelectedAsset.BundleId, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            StatusText = $"Bundle file not found on disk for id '{SelectedAsset.BundleId}'.";
            return;
        }

        ObjectFilter = SelectedAsset.DisplayName;

        var container = BundleAssetLister.FindContainerEntry(match.FullPath, SelectedAsset.PathInBundle);
        if (container != null)
        {
            _resolvedContainerPath = container.Name;
            _resolvedPathId = container.PathId.ToString();
            StatusText =
                $"Resolved {SelectedAsset.DisplayName} → PathId {container.PathId} in {match.BundleId}.";
            OnPropertyChanged(nameof(ResolvedContainerPath));
            OnPropertyChanged(nameof(ResolvedPathId));
        }

        RefreshShellLabels();
        SelectedBundle = match;
        _ = RefreshPreviewAsync();
    }

    [RelayCommand]
    public async Task RefreshPreviewAsync()
    {
        ClearPreview("Loading preview…");

        if (SelectedAsset == null || _index == null)
        {
            ClearPreview("Select an icon, texture, or item to preview.");
            return;
        }

        try
        {
            SoftRefPreviewResult result;

            if (SelectedAsset.Kind == CatalogKind.Icon ||
                IsTexturePath(SelectedAsset.NormalizedPath))
            {
                var bundlePath = ResolveBundlePath(SelectedAsset.BundleId);
                if (bundlePath == null)
                {
                    ClearPreview($"Bundle missing: {SelectedAsset.BundleId}");
                    return;
                }

                result = await Task.Run(() =>
                    SoftRefPreviewService.PreviewTextureByContainerPath(
                        bundlePath,
                        SelectedAsset.PathInBundle));
            }
            else if (SelectedAsset.Kind is CatalogKind.ItemPrefab or CatalogKind.PiecePrefab)
            {
                var icon = SoftRefPreviewService.FindIconForItem(SelectedAsset, _allAssets);
                if (icon == null)
                {
                    ClearPreview($"No SoftRef icon found for '{SelectedAsset.DisplayName}'. Pick Icons view or a Texture row.");
                    return;
                }

                var bundlePath = ResolveBundlePath(icon.BundleId);
                if (bundlePath == null)
                {
                    ClearPreview($"Icon bundle missing: {icon.BundleId}");
                    return;
                }

                result = await Task.Run(() =>
                    SoftRefPreviewService.PreviewTextureByContainerPath(
                        bundlePath,
                        icon.PathInBundle));

                if (result.Error == null && result.Bitmap != null)
                    result = new SoftRefPreviewResult
                    {
                        Bitmap = result.Bitmap,
                        Caption = $"Icon for {SelectedAsset.DisplayName} · {result.Caption}",
                    };
            }
            else if (SelectedAsset.Extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
                     SelectedAsset.Extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
            {
                ClearPreview($"Mesh source '{SelectedAsset.FileName}' — open bundle and select a Mesh object for stats.");
                return;
            }
            else
            {
                ClearPreview($"No texture preview for {SelectedAsset.Kind}. Try Icons or a Texture container row.");
                return;
            }

            ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            ClearPreview($"Preview error: {ex.Message}");
        }
    }

    private async Task RefreshPreviewFromBundleObjectAsync(BundleObjectItem obj)
    {
        if (SelectedBundle == null)
            return;

        ClearPreview("Loading preview…");
        try
        {
            SoftRefPreviewResult result;
            var type = obj.TypeName;
            if (type is "Texture" or "Texture2D" or "Sprite")
            {
                result = await Task.Run(() =>
                    SoftRefPreviewService.PreviewTextureFromBundle(SelectedBundle.FullPath, obj.PathId));
            }
            else if (type is "Mesh" or "MeshSrc")
            {
                result = await Task.Run(() =>
                    SoftRefPreviewService.PreviewMeshStats(SelectedBundle.FullPath, obj.PathId));
            }
            else if (type == "Prefab" && SelectedAsset != null)
            {
                await RefreshPreviewAsync();
                return;
            }
            else
            {
                ClearPreview($"{type}: no preview handler yet.");
                return;
            }

            ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            ClearPreview($"Preview error: {ex.Message}");
        }
    }

    private void ApplyPreviewResult(SoftRefPreviewResult result)
    {
        var old = PreviewBitmap;
        PreviewBitmap = result.Bitmap;
        old?.Dispose();

        HasPreviewImage = PreviewBitmap != null;
        PreviewCaption = string.IsNullOrWhiteSpace(result.Error)
            ? result.Caption
            : $"{result.Caption}: {result.Error}";

        if (result.Error != null)
            StatusText = PreviewCaption;
        else if (HasPreviewImage)
            StatusText = $"Preview ready — {PreviewCaption}";
    }

    private void ClearPreview(string caption)
    {
        var old = PreviewBitmap;
        PreviewBitmap = null;
        old?.Dispose();
        HasPreviewImage = false;
        PreviewCaption = caption;
    }

    private async Task RefreshFieldSpyAsync()
    {
        var gen = Interlocked.Increment(ref _fieldSpyGeneration);
        SpyScripts.Clear();
        ScriptFields.Clear();
        RecipeFields.Clear();
        RecipeRequirements.Clear();
        SelectedSpyScript = null;
        _lastPrefabSpy = null;

        if (SelectedAsset == null || _index == null)
        {
            ClearFieldSpy("Select a prefab to spy Valheim scripts.",
                "Select an Item or Recipe to spy craft data.");
            return;
        }

        if (_fieldSpy == null)
        {
            ClearFieldSpy("valheim_Data/Managed not found — cannot deserialize scripts.",
                "valheim_Data/Managed not found — cannot deserialize recipes.");
            return;
        }

        var asset = SelectedAsset;
        var spy = _fieldSpy;
        var allAssets = _allAssets;

        ScriptsSpyStatus = "Scanning Valheim scripts…";
        RecipeSpyStatus = "Reading Recipe…";

        try
        {
            PrefabPropertySpyResult? prefabResult = null;
            AssetPropertySpyResult? recipeResult = null;
            IReadOnlyList<RecipeRequirementRow> recipeReqs = Array.Empty<RecipeRequirementRow>();
            SoftRefAssetEntry? recipeAsset = null;

            await Task.Run(() =>
            {
                if (asset.Kind is CatalogKind.ItemPrefab or CatalogKind.PiecePrefab or CatalogKind.OtherPrefab)
                {
                    var bundlePath = ResolveBundlePath(asset.BundleId);
                    if (bundlePath != null)
                    {
                        var container = BundleAssetLister.FindContainerEntry(bundlePath, asset.PathInBundle);
                        prefabResult = spy.SpyPrefab(bundlePath, asset.PathInBundle);

                        if (asset.Kind == CatalogKind.ItemPrefab)
                        {
                            recipeAsset = SoftRefPropertySpyService.FindRecipeForItem(asset, allAssets);
                            if (recipeAsset != null)
                            {
                                var recipeBundle = ResolveBundlePath(recipeAsset.BundleId);
                                if (recipeBundle != null)
                                {
                                    recipeResult = spy.SpyScriptableAsset(recipeBundle, recipeAsset.PathInBundle);
                                    recipeReqs = spy.ReadRecipeRequirements(recipeBundle, recipeAsset.PathInBundle);
                                }
                            }
                        }

                        if (container != null)
                        {
                            _resolvedContainerPath = container.Name;
                            _resolvedPathId = container.PathId.ToString();
                        }
                    }
                    else
                    {
                        prefabResult = new PrefabPropertySpyResult { Error = $"Bundle missing: {asset.BundleId}" };
                    }
                }
                else if (asset.Kind == CatalogKind.Recipe)
                {
                    recipeAsset = asset;
                    var recipeBundle = ResolveBundlePath(asset.BundleId);
                    if (recipeBundle != null)
                    {
                        var container = BundleAssetLister.FindContainerEntry(recipeBundle, asset.PathInBundle);
                        recipeResult = spy.SpyScriptableAsset(recipeBundle, asset.PathInBundle);
                        recipeReqs = spy.ReadRecipeRequirements(recipeBundle, asset.PathInBundle);
                        if (container != null)
                        {
                            _resolvedContainerPath = container.Name;
                            _resolvedPathId = container.PathId.ToString();
                        }
                    }
                    else
                    {
                        recipeResult = new AssetPropertySpyResult { Error = $"Bundle missing: {asset.BundleId}" };
                    }
                }
            });

            if (gen != _fieldSpyGeneration)
                return;

            OnPropertyChanged(nameof(ResolvedContainerPath));
            OnPropertyChanged(nameof(ResolvedPathId));
            RefreshShellLabels();

            if (prefabResult != null)
            {
                _lastPrefabSpy = prefabResult;
                if (prefabResult.Error != null && prefabResult.Scripts.Count == 0)
                {
                    ScriptsSpyStatus = prefabResult.Error;
                }
                else
                {
                    foreach (var script in prefabResult.Scripts)
                        SpyScripts.Add(script);

                    ScriptsSpyStatus =
                        $"{prefabResult.PrefabName} · {prefabResult.Scripts.Count} Valheim script(s) (locked)";

                    SelectedSpyScript =
                        prefabResult.Scripts.FirstOrDefault(s => s.ClassName.Equals("ItemDrop", StringComparison.OrdinalIgnoreCase))
                        ?? prefabResult.Scripts.FirstOrDefault(s => s.ClassName.Equals("Piece", StringComparison.OrdinalIgnoreCase))
                        ?? prefabResult.Scripts.FirstOrDefault();

                    if (InspectorSheet == InspectorSheet.Prefab)
                        InspectorSheet = InspectorSheet.Scripts;
                }
            }
            else if (asset.Kind != CatalogKind.Recipe)
            {
                ScriptsSpyStatus = $"No prefab script spy for {asset.Kind}.";
            }
            else
            {
                ScriptsSpyStatus = "Select a prefab for script spy.";
            }

            if (asset.Kind is CatalogKind.ItemPrefab or CatalogKind.Recipe)
            {
                if (recipeResult == null)
                {
                    RecipeSpyStatus = recipeAsset == null
                        ? (asset.Kind == CatalogKind.ItemPrefab
                            ? $"No Recipe_* SoftRef match for '{asset.DisplayName}'."
                            : "Recipe dump failed.")
                        : "Recipe dump failed.";
                }
                else if (recipeResult.Error != null && recipeResult.Fields.Count == 0)
                {
                    RecipeSpyStatus = recipeResult.Error;
                }
                else
                {
                    RecipeFields.Clear();
                    RecipePropertyTree.Clear();
                    foreach (var row in recipeResult.Fields)
                        RecipeFields.Add(row);
                    foreach (var node in PropertyTreeBuilder.FromFlatRows(
                                 recipeResult.Fields,
                                 stripRootPrefix: recipeResult.ClassName,
                                 expandDepth: 1))
                        RecipePropertyTree.Add(node);
                    foreach (var row in recipeReqs)
                        RecipeRequirements.Add(row);
                    RecipeSpyStatus =
                        $"{recipeResult.ClassName} '{recipeResult.AssetName}' · {recipeReqs.Count} ingredient(s) (locked)";
                }
            }
            else
            {
                RecipeSpyStatus = "Recipe spy is for Item / Recipe SoftRefs.";
            }
        }
        catch (Exception ex)
        {
            if (gen != _fieldSpyGeneration)
                return;
            ClearFieldSpy($"Script spy failed: {ex.Message}", $"Recipe spy failed: {ex.Message}");
        }
    }

    partial void OnSelectedSpyScriptChanged(SpyScriptComponent? value)
    {
        ScriptFields.Clear();
        ScriptPropertyTree.Clear();
        if (value == null)
            return;

        foreach (var row in value.Fields)
            ScriptFields.Add(new EditableFieldRow { Path = row.Path, Value = row.Value });

        foreach (var node in PropertyTreeBuilder.FromFlatRows(value.Fields, stripRootPrefix: value.ClassName, expandDepth: 1))
            ScriptPropertyTree.Add(node);
    }

    private void ClearFieldSpy(string scriptsStatus, string recipeStatus)
    {
        SpyScripts.Clear();
        ScriptFields.Clear();
        ScriptPropertyTree.Clear();
        RecipeFields.Clear();
        RecipePropertyTree.Clear();
        RecipeRequirements.Clear();
        SelectedSpyScript = null;
        _lastPrefabSpy = null;
        ScriptsSpyStatus = scriptsStatus;
        RecipeSpyStatus = recipeStatus;
    }

    private string? ResolveBundlePath(string bundleId)
    {
        var match = _allBundles.FirstOrDefault(b =>
            b.BundleId.Equals(bundleId, StringComparison.OrdinalIgnoreCase));
        return match?.FullPath;
    }

    private static bool IsTexturePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".dds", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public void PinSelected()
    {
        if (SelectedAsset == null)
        {
            StatusText = "Select a SoftRef asset to pin.";
            return;
        }

        if (Pins.Any(p => p.AssetId.Equals(SelectedAsset.AssetId, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"{SelectedAsset.DisplayName} is already pinned.";
            return;
        }

        Pins.Add(SelectedAsset);
        StatusText = $"Pinned {SelectedAsset.DisplayName}.";
        RefreshShellLabels();
    }

    [RelayCommand]
    public void UnpinSelected()
    {
        var target = SelectedPin ?? SelectedAsset;
        if (target == null)
            return;

        var existing = Pins.FirstOrDefault(p =>
            p.AssetId.Equals(target.AssetId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return;

        Pins.Remove(existing);
        StatusText = $"Unpinned {existing.DisplayName}.";
    }

    [RelayCommand]
    public Task EditInProjectAsync() => CloneIntoProjectAsync();

    [RelayCommand]
    public async Task CloneIntoProjectAsync()
    {
        if (SelectedAsset == null)
        {
            StatusText = "Select a SoftRef donor first.";
            return;
        }

        if (SelectedAsset.Kind is not (CatalogKind.ItemPrefab or CatalogKind.PiecePrefab))
        {
            StatusText = "Clone currently supports Item / Piece prefabs.";
            return;
        }

        if (_fieldSpy == null || _index == null)
        {
            StatusText = "Load SoftRef (with Managed assemblies) before cloning.";
            return;
        }

        var donor = SelectedAsset;
        if (!Pins.Any(p => p.AssetId.Equals(donor.AssetId, StringComparison.OrdinalIgnoreCase)))
            Pins.Add(donor);

        StatusText = $"Cloning '{donor.DisplayName}' into Project…";
        try
        {
            var spy = _fieldSpy;
            var allAssets = _allAssets;
            var bundlePath = ResolveBundlePath(donor.BundleId);
            if (bundlePath == null)
            {
                StatusText = $"Bundle missing: {donor.BundleId}";
                return;
            }

            var (prefabSpy, recipeSpy, recipeReqs) = await Task.Run(() =>
            {
                var prefab = spy.SpyPrefab(bundlePath, donor.PathInBundle);
                AssetPropertySpyResult? recipe = null;
                IReadOnlyList<RecipeRequirementRow> reqs = Array.Empty<RecipeRequirementRow>();
                if (donor.Kind == CatalogKind.ItemPrefab)
                {
                    var recipeAsset = SoftRefPropertySpyService.FindRecipeForItem(donor, allAssets);
                    if (recipeAsset != null)
                    {
                        var recipeBundle = ResolveBundlePath(recipeAsset.BundleId);
                        if (recipeBundle != null)
                        {
                            recipe = spy.SpyScriptableAsset(recipeBundle, recipeAsset.PathInBundle);
                            reqs = spy.ReadRecipeRequirements(recipeBundle, recipeAsset.PathInBundle);
                        }
                    }
                }

                return (prefab, recipe, reqs);
            });

            if (prefabSpy.Error != null && prefabSpy.Scripts.Count == 0)
            {
                StatusText = $"Clone failed: {prefabSpy.Error}";
                return;
            }

            var doc = _projectStore.CloneFromSpy(donor, prefabSpy, recipeSpy, recipeReqs);
            ReloadOwnedItems();
            AppScreen = AppScreen.Project;
            SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == doc.Id);
            InspectorSheet = InspectorSheet.Art;
            StatusText =
                $"Cloned '{donor.DisplayName}' → '{doc.Id}'. Art sheet open — Browse an icon PNG to test Preview.";
        }
        catch (Exception ex)
        {
            StatusText = $"Clone failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void SaveOwnedItem()
    {
        if (SelectedOwnedItem == null)
        {
            StatusText = "Select a Project item to save.";
            return;
        }

        try
        {
            if (SelectedSpyScript != null)
            {
                // Pull edits from the tree back into flat fields.
                ScriptFields.Clear();
                foreach (var row in PropertyTreeBuilder.FlattenLeaves(ScriptPropertyTree))
                    ScriptFields.Add(row);

                var script = SelectedOwnedItem.Scripts.FirstOrDefault(s =>
                    s.ClassName.Equals(SelectedSpyScript.ClassName, StringComparison.OrdinalIgnoreCase) &&
                    s.PathId == SelectedSpyScript.PathId);
                if (script != null)
                {
                    foreach (var row in ScriptFields)
                    {
                        script.Fields[row.Path] = row.Value ?? "";
                        SelectedOwnedItem.Fields[row.Path] = row.Value ?? "";
                    }
                }
                else
                {
                    _projectStore.ApplyEditableFields(SelectedOwnedItem, ScriptFields);
                }
            }
            else
            {
                _projectStore.ApplyEditableFields(SelectedOwnedItem, ScriptFields);
            }

            if (SelectedOwnedItem.Fields.TryGetValue("prefab", out var prefab) &&
                !string.IsNullOrWhiteSpace(prefab))
            {
                SelectedOwnedItem.DisplayName = prefab;
            }

            _projectStore.SaveItem(SelectedOwnedItem);
            SaveWorkingMaterial(SelectedOwnedItem);
            SaveWorkingArt(SelectedOwnedItem);
            var id = SelectedOwnedItem.Id;
            ReloadOwnedItems();
            SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == id);
            StatusText = $"Saved {id} (+ material.json + art.json)";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private void ReloadOwnedItems()
    {
        _projectStore.EnsureCreated();
        ProjectPathLabel = _projectStore.ProjectRoot;
        var items = _projectStore.LoadAllItems();
        var selectedId = SelectedOwnedItem?.Id;

        _suppressOwnedSelectionSideEffects = true;
        try
        {
            OwnedItems.Clear();
            foreach (var item in items)
                OwnedItems.Add(item);
        }
        finally
        {
            _suppressOwnedSelectionSideEffects = false;
        }

        if (selectedId != null)
        {
            var match = OwnedItems.FirstOrDefault(o => o.Id == selectedId);
            if (!ReferenceEquals(SelectedOwnedItem, match))
                SelectedOwnedItem = match;
        }
    }

    private void LoadOwnedItemIntoInspector(OwnedItemDocument doc)
    {
        SpyScripts.Clear();
        ScriptFields.Clear();
        RecipeFields.Clear();
        RecipeRequirements.Clear();

        foreach (var script in doc.Scripts)
        {
            SpyScripts.Add(new SpyScriptComponent
            {
                ClassName = script.ClassName,
                PathId = script.PathId,
                IsMatchedValheimScript = true,
                Fields = script.Fields
                    .Select(kv => new FieldRow { Path = kv.Key, Value = kv.Value })
                    .ToList(),
            });
        }

        // Flat fallback if older item.json has only Fields.
        if (SpyScripts.Count == 0 && doc.Fields.Count > 0)
        {
            SpyScripts.Add(new SpyScriptComponent
            {
                ClassName = "OwnedFields",
                PathId = "",
                IsMatchedValheimScript = true,
                Fields = doc.Fields
                    .Select(kv => new FieldRow { Path = kv.Key, Value = kv.Value })
                    .ToList(),
            });
        }

        ScriptsSpyStatus =
            $"{doc.DisplayName} · donor {doc.Donor.PrefabName} · {SpyScripts.Count} script(s) · editable";

        SelectedSpyScript =
            SpyScripts.FirstOrDefault(s => s.ClassName.Equals("ItemDrop", StringComparison.OrdinalIgnoreCase))
            ?? SpyScripts.FirstOrDefault();

        if (doc.Recipe == null)
        {
            RecipeSpyStatus = "No recipe seed on this owned item.";
        }
        else
        {
            RecipeFields.Clear();
            RecipePropertyTree.Clear();
            foreach (var kv in doc.Recipe.Fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                RecipeFields.Add(new FieldRow { Path = kv.Key, Value = kv.Value });

            foreach (var node in PropertyTreeBuilder.FromFlatRows(
                         RecipeFields.Select(f => new FieldRow { Path = f.Path, Value = f.Value }),
                         stripRootPrefix: string.IsNullOrWhiteSpace(doc.Recipe.ClassName) ? null : doc.Recipe.ClassName,
                         expandDepth: 1))
                RecipePropertyTree.Add(node);

            foreach (var req in doc.Recipe.Requirements)
            {
                RecipeRequirements.Add(new RecipeRequirementRow
                {
                    ItemName = req.ItemName,
                    Token = req.Token,
                    Amount = req.Amount,
                    AmountPerLevel = req.AmountPerLevel,
                });
            }

            RecipeSpyStatus =
                $"{doc.Recipe.ClassName} '{doc.Recipe.RecipeName}' · {doc.Recipe.Requirements.Count} ingredient(s) (seed)";
        }

        LoadMaterialIntoUi(doc);
        LoadArtIntoUi(doc);
    }

    private void TryLoadShaderCatalogCache()
    {
        var cached = _shaderCatalogService.TryLoadCache();
        if (cached == null || cached.Shaders.Count == 0)
        {
            MaterialStatus = "No shader catalog yet — loads automatically after SoftRef (or click Rebuild).";
            return;
        }

        ApplyShaderCatalog(cached);
        MaterialStatus = $"Shader catalog cached ({cached.Shaders.Count}) — built {cached.BuiltUtc:u}";
    }

    [RelayCommand]
    public async Task BuildShaderCatalogAsync()
    {
        if (IsShaderCatalogBusy)
            return;
        if (_index == null || string.IsNullOrWhiteSpace(ValheimPath))
        {
            MaterialStatus = "Load SoftRef first, then rebuild the shader catalog.";
            return;
        }

        IsShaderCatalogBusy = true;
        MaterialStatus = "Building shader catalog from SoftRef…";
        try
        {
            var path = ValheimPath;
            var index = _index;
            var progress = new Progress<string>(msg => MaterialStatus = msg);
            var doc = await Task.Run(() => _shaderCatalogService.BuildFromSoftRef(path, index, progress));
            ApplyShaderCatalog(doc);
            MaterialStatus =
                $"Shader catalog ready — {doc.Shaders.Count} Shader.Find names (cache: {_shaderCatalogService.CachePath})";
            StatusText = MaterialStatus;
        }
        catch (Exception ex)
        {
            MaterialStatus = $"Shader catalog failed: {ex.Message}";
        }
        finally
        {
            IsShaderCatalogBusy = false;
        }
    }

    private void ApplyShaderCatalog(ShaderCatalogDocument doc)
    {
        ShaderCatalog.Clear();
        foreach (var shader in doc.Shaders)
            ShaderCatalog.Add(shader);
        ApplyShaderFilter();
    }

    private void ApplyShaderFilter()
    {
        var filter = MaterialShaderFilter?.Trim() ?? "";
        FilteredShaders.Clear();
        IEnumerable<ShaderCatalogEntry> query = ShaderCatalog;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(s =>
                s.FindName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                s.SoftRefDisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in query.Take(400))
            FilteredShaders.Add(s);
    }

    private void ApplyShaderSchemaToWorkingMaterial(ShaderCatalogEntry shader)
    {
        _workingMaterial = _shaderCatalogService.CreateMaterialFromShader(
            shader,
            UseDonorMaterials ? MaterialAuthoringMode.Donor : MaterialAuthoringMode.Custom);
        MaterialProperties.Clear();
        foreach (var prop in _workingMaterial.Properties)
            MaterialProperties.Add(MaterialPropertyRow.FromValue(prop));
        MaterialStatus = $"Shader '{shader.FindName}' — {shader.Properties.Count} properties (will save to material.json)";
    }

    private void ShowShaderSchemaBrowse(ShaderCatalogEntry shader)
    {
        MaterialProperties.Clear();
        foreach (var prop in shader.Properties)
        {
            MaterialProperties.Add(new MaterialPropertyRow
            {
                Name = prop.Name,
                Description = prop.Description,
                Kind = prop.Kind,
                Value = prop.Kind switch
                {
                    ShaderPropertyKind.Color => "1,1,1,1",
                    ShaderPropertyKind.Vector => "0,0,0,0",
                    ShaderPropertyKind.Float or ShaderPropertyKind.Range => "0",
                    _ => "",
                },
                TextureRef = prop.Kind == ShaderPropertyKind.Texture ? "(texture slot)" : "",
            });
        }

        MaterialStatus = CanEditMaterial
            ? $"Browsing '{shader.FindName}' — uncheck Donor to assign this shader to the owned item."
            : $"Browsing '{shader.FindName}' ({shader.Properties.Count} props) — Clone into Project to assign.";
    }

    private void LoadMaterialIntoUi(OwnedItemDocument doc)
    {
        _workingMaterial = _projectStore.LoadMaterial(doc);
        _suppressMaterialSelection = true;
        try
        {
            UseDonorMaterials = _workingMaterial.Mode == MaterialAuthoringMode.Donor;
            SelectedShader = string.IsNullOrWhiteSpace(_workingMaterial.ShaderName)
                ? null
                : ShaderCatalog.FirstOrDefault(s =>
                    s.FindName.Equals(_workingMaterial.ShaderName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppressMaterialSelection = false;
        }

        MaterialProperties.Clear();
        foreach (var prop in _workingMaterial.Properties)
            MaterialProperties.Add(MaterialPropertyRow.FromValue(prop));

        MaterialStatus = _workingMaterial.Mode == MaterialAuthoringMode.Donor
            ? $"Donor materials for '{doc.DisplayName}' (no Shader.Find override)."
            : $"Custom '{_workingMaterial.ShaderName}' — {MaterialProperties.Count} props";
    }

    private void SaveWorkingMaterial(OwnedItemDocument doc)
    {
        _workingMaterial.Mode = UseDonorMaterials ? MaterialAuthoringMode.Donor : MaterialAuthoringMode.Custom;
        _workingMaterial.ShaderName = SelectedShader?.FindName ?? _workingMaterial.ShaderName;
        _workingMaterial.Properties = MaterialProperties.Select(p => p.ToValue()).ToList();
        _projectStore.SaveMaterial(doc, _workingMaterial);
    }

    private void LoadArtIntoUi(OwnedItemDocument doc)
    {
        _workingArt = _projectStore.LoadArt(doc);
        ArtMeshPath = _workingArt.MeshPath ?? "";
        ArtIconPath = _workingArt.IconPath ?? "";
        ArtDiffusePath = _workingArt.DiffusePath ?? "";
        ArtStatus = SummarizeArtStatus();
    }

    private void SaveWorkingArt(OwnedItemDocument doc)
    {
        _workingArt.MeshPath = string.IsNullOrWhiteSpace(ArtMeshPath) ? null : ArtMeshPath;
        _workingArt.IconPath = string.IsNullOrWhiteSpace(ArtIconPath) ? null : ArtIconPath;
        _workingArt.DiffusePath = string.IsNullOrWhiteSpace(ArtDiffusePath) ? null : ArtDiffusePath;
        _projectStore.SaveArt(doc, _workingArt);
    }

    private void PersistArtAndRefreshBadges()
    {
        if (SelectedOwnedItem == null)
            return;

        SaveWorkingArt(SelectedOwnedItem);
        var id = SelectedOwnedItem.Id;
        ReloadOwnedItems();
        SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == id);
        if (SelectedOwnedItem != null)
            LoadArtIntoUi(SelectedOwnedItem);
    }

    private void ClearArtUi(string status)
    {
        _workingArt = new ArtDocument();
        ArtMeshPath = "";
        ArtIconPath = "";
        ArtDiffusePath = "";
        ArtStatus = status;
    }

    private string SummarizeArtStatus()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ArtMeshPath))
            parts.Add("mesh");
        if (!string.IsNullOrWhiteSpace(ArtIconPath))
            parts.Add("icon");
        if (!string.IsNullOrWhiteSpace(ArtDiffusePath))
            parts.Add("diffuse");

        return parts.Count == 0
            ? "No art attached yet — browse FBX/PNG (copied into art/)."
            : $"Attached: {string.Join(", ", parts)}. Save writes art.json.";
    }

    [RelayCommand]
    public async Task BrowseArtMeshAsync() =>
        await BrowseAndImportArtAsync(
            title: "Select mesh FBX",
            patterns: new[] { "*.fbx" },
            destFileName: "mesh.fbx",
            apply: relative => ArtMeshPath = relative);

    [RelayCommand]
    public async Task BrowseArtIconAsync() =>
        await BrowseAndImportArtAsync(
            title: "Select icon PNG",
            patterns: new[] { "*.png" },
            destFileName: "icon.png",
            apply: relative => ArtIconPath = relative,
            refreshPreview: true);

    [RelayCommand]
    public async Task BrowseArtDiffuseAsync()
    {
        if (!CanEditArt || SelectedOwnedItem == null)
        {
            ArtStatus = "Clone into Project first.";
            return;
        }

        if (PickOpenFileAsync == null)
        {
            ArtStatus = "File picker not available.";
            return;
        }

        var source = await PickOpenFileAsync(
            "Select diffuse / albedo PNG",
            new[] { "*.png", "*.jpg", "*.jpeg", "*.tga" });
        if (string.IsNullOrWhiteSpace(source))
            return;

        try
        {
            var ext = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";
            var destName = "diffuse" + ext.ToLowerInvariant();
            var relative = _projectStore.ImportArtFile(SelectedOwnedItem, source, destName);
            ArtDiffusePath = relative;
            if (IsCustomMaterialMode)
            {
                var slot = MaterialProperties.FirstOrDefault(p =>
                               p.IsTexture &&
                               (p.Name.Contains("MainTex", StringComparison.OrdinalIgnoreCase) ||
                                p.Name.Contains("Albedo", StringComparison.OrdinalIgnoreCase) ||
                                p.Name.Contains("Main", StringComparison.OrdinalIgnoreCase)))
                           ?? MaterialProperties.FirstOrDefault(p => p.IsTexture);
                if (slot != null)
                    slot.TextureRef = relative;
            }

            PersistArtAndRefreshBadges();
            ArtStatus = SummarizeArtStatus();
            StatusText = $"Saved {destName} → {relative}. Open folder to verify.";
        }
        catch (Exception ex)
        {
            ArtStatus = $"Import failed: {ex.Message}";
            StatusText = ArtStatus;
        }
    }
    [RelayCommand]
    public void ClearArtMesh()
    {
        if (!CanEditArt)
            return;
        ArtMeshPath = "";
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Cleared mesh path.";
    }

    [RelayCommand]
    public void ClearArtIcon()
    {
        if (!CanEditArt || SelectedOwnedItem == null)
            return;
        ArtIconPath = "";
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Cleared icon path — Preview falls back to SoftRef donor.";
        if (SelectedOwnedItem != null)
            _ = RefreshOwnedPreviewAsync(SelectedOwnedItem);
    }

    [RelayCommand]
    public void ClearArtDiffuse()
    {
        if (!CanEditArt)
            return;
        ArtDiffusePath = "";
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Cleared diffuse path.";
    }

    [RelayCommand]
    public void RevealOwnedItemFolder()
    {
        if (SelectedOwnedItem == null || string.IsNullOrWhiteSpace(SelectedOwnedItem.FolderPath))
        {
            StatusText = "Select a Project item first.";
            return;
        }

        var folder = SelectedOwnedItem.FolderPath;
        if (!Directory.Exists(folder))
        {
            StatusText = $"Folder missing: {folder}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
            StatusText = $"Opened {folder}";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open folder: {ex.Message}";
        }
    }

    private async Task BrowseAndImportArtAsync(
        string title,
        IReadOnlyList<string> patterns,
        string destFileName,
        Action<string> apply,
        bool refreshPreview = false)
    {
        if (!CanEditArt || SelectedOwnedItem == null)
        {
            ArtStatus = "Clone into Project first.";
            return;
        }

        if (PickOpenFileAsync == null)
        {
            ArtStatus = "File picker not available.";
            return;
        }

        var source = await PickOpenFileAsync(title, patterns);
        if (string.IsNullOrWhiteSpace(source))
            return;

        try
        {
            var relative = _projectStore.ImportArtFile(SelectedOwnedItem, source, destFileName);
            apply(relative);
            PersistArtAndRefreshBadges();
            ArtStatus = SummarizeArtStatus();
            StatusText = $"Saved {destFileName} → {relative}. Preview/list should update.";
            if (refreshPreview && SelectedOwnedItem != null)
                _ = RefreshOwnedPreviewAsync(SelectedOwnedItem);
        }
        catch (Exception ex)
        {
            ArtStatus = $"Import failed: {ex.Message}";
            StatusText = ArtStatus;
        }
    }

    private void ClearMaterialUi(string status)
    {
        MaterialProperties.Clear();
        _workingMaterial = new MaterialDocument { Mode = MaterialAuthoringMode.Donor };
        _suppressMaterialSelection = true;
        try
        {
            UseDonorMaterials = true;
            SelectedShader = null;
        }
        finally
        {
            _suppressMaterialSelection = false;
        }

        MaterialStatus = status;
    }

    private async Task RefreshOwnedPreviewAsync(OwnedItemDocument doc)
    {
        // Prefer user-attached icon PNG when present.
        var iconAbs = ProjectStore.ResolveArtAbsolutePath(doc, string.IsNullOrWhiteSpace(ArtIconPath) ? null : ArtIconPath)
                      ?? ProjectStore.ResolveArtAbsolutePath(doc, _projectStore.LoadArt(doc).IconPath);
        if (iconAbs != null)
        {
            try
            {
                await using var stream = File.OpenRead(iconAbs);
                var bmp = new Bitmap(stream);
                PreviewBitmap?.Dispose();
                PreviewBitmap = bmp;
                HasPreviewImage = true;
                PreviewCaption = $"Owned '{doc.DisplayName}' · custom icon {Path.GetFileName(iconAbs)}";
                return;
            }
            catch (Exception ex)
            {
                ClearPreview($"Custom icon failed ({ex.Message}); falling back to SoftRef donor.");
            }
        }

        ClearPreview("Loading donor icon…");
        if (_index == null || string.IsNullOrWhiteSpace(doc.Donor.PrefabName))
        {
            ClearPreview("Load SoftRef to preview the donor icon for this owned item.");
            return;
        }

        var donor = _allAssets.FirstOrDefault(a =>
            a.Kind == CatalogKind.ItemPrefab &&
            (a.AssetId.Equals(doc.Donor.SoftRefAssetId, StringComparison.OrdinalIgnoreCase) ||
             a.DisplayName.Equals(doc.Donor.PrefabName, StringComparison.OrdinalIgnoreCase)));

        if (donor == null)
        {
            ClearPreview($"Donor '{doc.Donor.PrefabName}' not found in SoftRef index.");
            return;
        }

        var icon = SoftRefPreviewService.FindIconForItem(donor, _allAssets);
        if (icon == null)
        {
            ClearPreview($"No icon for donor '{donor.DisplayName}'.");
            return;
        }

        var bundlePath = ResolveBundlePath(icon.BundleId);
        if (bundlePath == null)
        {
            ClearPreview($"Icon bundle missing: {icon.BundleId}");
            return;
        }

        try
        {
            var result = await Task.Run(() =>
                SoftRefPreviewService.PreviewTextureByContainerPath(bundlePath, icon.PathInBundle));
            if (result.Error == null && result.Bitmap != null)
            {
                result = new SoftRefPreviewResult
                {
                    Bitmap = result.Bitmap,
                    Caption = $"Owned '{doc.DisplayName}' · donor icon {result.Caption}",
                };
            }

            ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            ClearPreview($"Preview error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void SetViewMode(string? modeName)
    {
        if (Enum.TryParse<CatalogViewMode>(modeName, ignoreCase: true, out var mode))
            ViewMode = mode;
    }

    private void RefreshShellLabels()
    {
        InspectorModeLabel = IsInspectorReadOnly ? "Inspector · read-only" : "Inspector · edit";

        if (SelectedOwnedItem != null && AppScreen == AppScreen.Project)
        {
            SelectionLabel = $"{SelectedOwnedItem.DisplayName} · owned · donor {SelectedOwnedItem.Donor.PrefabName}";
            return;
        }

        if (SelectedAsset == null)
        {
            SelectionLabel = "Nothing selected";
            return;
        }

        var pinned = Pins.Any(p =>
            p.AssetId.Equals(SelectedAsset.AssetId, StringComparison.OrdinalIgnoreCase));
        SelectionLabel =
            $"{SelectedAsset.DisplayName} · SoftRef {SelectedAsset.Kind}" +
            (pinned ? " · pinned" : "");
    }

    private void UpdateTabCounts()
    {
        ItemsTabCount = _allAssets.Count(a => a.Kind == CatalogKind.ItemPrefab && !a.IsResourceNoise);
        PiecesTabCount = _allAssets.Count(a => a.Kind == CatalogKind.PiecePrefab && !a.IsResourceNoise);
        PrefabsTabCount = _allAssets.Count(a =>
            !a.IsResourceNoise &&
            a.Kind is CatalogKind.ItemPrefab or CatalogKind.PiecePrefab or CatalogKind.OtherPrefab);
        IconsTabCount = _allAssets.Count(a => a.Kind == CatalogKind.Icon);
        RecipesTabCount = _allAssets.Count(a => a.Kind == CatalogKind.Recipe);
        RawTabCount = _allAssets.Count;
    }

    private void RefreshSubCategoriesAndFilter()
    {
        var previous = SelectedSubCategory;
        SubCategories.Clear();
        SubCategories.Add("All");

        var scoped = _allAssets.Where(a => CatalogClassifier.MatchesView(a, ViewMode, HideResourceNoise));
        foreach (var sub in scoped
                     .Select(a => a.SubCategory)
                     .Where(s => !string.IsNullOrWhiteSpace(s))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            SubCategories.Add(sub);
        }

        if (!string.IsNullOrWhiteSpace(previous) &&
            SubCategories.Contains(previous, StringComparer.OrdinalIgnoreCase))
        {
            SelectedSubCategory = SubCategories.First(s =>
                s.Equals(previous, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedSubCategory = "All";
        }

        ApplyCatalogFilter();
    }

    private void ApplyCatalogFilter()
    {
        Assets.Clear();
        var q = AssetFilter?.Trim() ?? string.Empty;
        var sub = SelectedSubCategory;

        IEnumerable<SoftRefAssetEntry> query = _allAssets
            .Where(a => CatalogClassifier.MatchesView(a, ViewMode, HideResourceNoise));

        if (!string.IsNullOrWhiteSpace(sub) &&
            !sub.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a =>
                a.SubCategory.Equals(sub, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.PathInBundle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.BundleId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.SubCategory.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.AssetId.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        query = query
            .OrderBy(a => a.SubCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);

        var list = query.Take(8000).ToList();
        foreach (var a in list)
            Assets.Add(a);

        VisibleCount = list.Count;
    }

    private void ApplyObjectFilter()
    {
        BundleObjects.Clear();
        var q = ObjectFilter?.Trim() ?? string.Empty;
        IEnumerable<BundleObjectItem> query = _bundleObjectsAll;
        if (!string.IsNullOrEmpty(q))
        {
            query = query.Where(o =>
                o.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                o.TypeName.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var o in query.Take(5000))
            BundleObjects.Add(o);
    }
}
