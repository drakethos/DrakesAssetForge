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
    private int _ownedPreviewGeneration;
    private ProjectStore _projectStore = new(ProjectStore.DefaultProjectRoot());
    private readonly ShaderCatalogService _shaderCatalogService = new();
    private readonly SoftRefThumbnailCache _thumbnails = new();
    private bool _suppressOwnedSelectionSideEffects;
    private PrefabPropertySpyResult? _lastPrefabSpy;
    private MaterialDocument _workingMaterial = new();
    private ArtDocument _workingArt = new();
    private bool _suppressMaterialSelection;

    /// <summary>Wired by MainWindow for open-file dialogs (title, patterns → local path).</summary>
    public Func<string, IReadOnlyList<string>, Task<string?>>? PickOpenFileAsync { get; set; }

    /// <summary>Wired by MainWindow for multi-select open-file dialogs.</summary>
    public Func<string, IReadOnlyList<string>, Task<IReadOnlyList<string>>>? PickOpenFilesAsync { get; set; }

    /// <summary>Wired by MainWindow for save-file dialogs.</summary>
    public Func<string, IReadOnlyList<string>, string, Task<string?>>? PickSaveFileAsync { get; set; }

    /// <summary>Wired by MainWindow for folder dialogs.</summary>
    public Func<string, Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, Task<bool>>? ConfirmAsync { get; set; }

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
    private string _meshPreviewPath = "";

    [ObservableProperty]
    private string _meshDiffusePath = "";

    public bool HasMeshPreview => !string.IsNullOrWhiteSpace(MeshPreviewPath) && File.Exists(MeshPreviewPath);
    public bool ShowIconPreview => HasPreviewImage && !HasMeshPreview;
    public bool ShowPreviewPlaceholder => !HasPreviewImage && !HasMeshPreview;

    [ObservableProperty]
    private BundleObjectItem? _selectedBundleObject;

    [ObservableProperty]
    private string _exportStubNote =
        "Export packs each Project folder into one Assets/Items/<folder>/<folder>.bundle with <itemId>.json beside it (e.g. keys/keys.bundle + masterkey.json). Ungrouped items still use the legacy per-item folder. SoftRef catalog is never bulk-dumped.";

    [ObservableProperty]
    private string _unityEditorPath = "";

    [ObservableProperty]
    private string _unityStatus = "Looking for a Unity editor…";

    [ObservableProperty]
    private string _requiredUnityVersion = "";

    [ObservableProperty]
    private bool _isArtCompileBusy;

    [ObservableProperty]
    private string _projectPathLabel = "";

    [ObservableProperty]
    private string _valheimProjectPath = "";

    [ObservableProperty]
    private string _valheimProjectStatus = "Set a code project folder. Export makes art.bundles then wires Assets/Items + hooks.";

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
    private bool _useExistingMaterials;

    [ObservableProperty]
    private string _existingMaterialFilter = "";

    [ObservableProperty]
    private CatalogMaterialChoice? _selectedCatalogMaterial;

    [ObservableProperty]
    private string? _selectedExistingMaterial;

    [ObservableProperty]
    private string _typedMaterialName = "";

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

    [ObservableProperty]
    private string _artPrefabName = "";

    [ObservableProperty]
    private string _artScale = "1";

    [ObservableProperty]
    private string _artDescription = "";

    [ObservableProperty]
    private bool _includeMeshInBundle = true;

    [ObservableProperty]
    private bool _includeIconInBundle = true;

    [ObservableProperty]
    private bool _includeDiffuseInBundle = true;

    [ObservableProperty]
    private bool _hasCompiledBundle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtractSelected))]
    private bool _needsBundleExtract;

    public bool CanExtractSelected
    {
        get
        {
            foreach (var item in GetSelectedOwnedItems())
            {
                var art = _projectStore.LoadArt(item);
                if (art.NeedsBundleExtract && !File.Exists(Path.Combine(item.FolderPath, "art.bundle")))
                    return true;
            }

            return NeedsBundleExtract;
        }
    }

    private bool _suppressArtInclude;
    private bool _suppressProjectTreeSelection;

    public ObservableCollection<SpyScriptComponent> SpyScripts { get; } = new();
    public ObservableCollection<PropertyNode> ScriptPropertyTree { get; } = new();
    public ObservableCollection<EditableFieldRow> ScriptFields { get; } = new();
    public ObservableCollection<FieldRow> RecipeFields { get; } = new();
    public ObservableCollection<PropertyNode> RecipePropertyTree { get; } = new();
    public ObservableCollection<RecipeRequirementRow> RecipeRequirements { get; } = new();
    public ObservableCollection<OwnedItemDocument> OwnedItems { get; } = new();
    public ObservableCollection<ProjectTreeNode> ProjectTree { get; } = new();
    public ObservableCollection<string> ProjectGroups { get; } = new();
    public ObservableCollection<ExportTreeNode> ExportTree { get; } = new();

    public string ExportIncludeSummary
    {
        get
        {
            var items = ExportTree.SelectMany(n => n.EnumerateItems()).ToList();
            var total = items.Count;
            var included = items.Count(i => i.IsChecked == true);
            var groups = ExportTree.Count(n => n.IsFolder);
            return total == 0
                ? "No project items yet — clone or import first."
                : $"{included} of {total} item(s) in {groups} folder(s) (+ root) marked for export.";
        }
    }

    private readonly List<ProjectTreeNode> _projectTreeSelection = new();
    private readonly List<string> _projectClipboardIds = new();
    private bool _projectClipboardIsCut;

    public bool HasProjectItemSelection =>
        _projectTreeSelection.Any(n => n.Item != null) ||
        SelectedProjectNode?.Item != null ||
        SelectedOwnedItem != null;

    public bool HasProjectClipboard => _projectClipboardIds.Count > 0;

    public void SyncProjectTreeSelection(IReadOnlyList<ProjectTreeNode> nodes)
    {
        _projectTreeSelection.Clear();
        _projectTreeSelection.AddRange(nodes);
        OnPropertyChanged(nameof(HasProjectItemSelection));

        var primary = nodes.LastOrDefault(n => n.Item != null) ?? nodes.LastOrDefault();
        if (!ReferenceEquals(SelectedProjectNode, primary))
            SelectedProjectNode = primary;
    }

    public IReadOnlyList<OwnedItemDocument> GetSelectedOwnedItems()
    {
        var fromTree = _projectTreeSelection
            .Where(n => n.Item != null)
            .Select(n => n.Item!)
            .GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (fromTree.Count > 0)
            return fromTree;

        var single = SelectedProjectNode?.Item ?? SelectedOwnedItem;
        return single == null ? Array.Empty<OwnedItemDocument>() : new[] { single };
    }

    [ObservableProperty]
    private ProjectTreeNode? _selectedProjectNode;
    public ObservableCollection<ShaderCatalogEntry> ShaderCatalog { get; } = new();
    public ObservableCollection<ShaderCatalogEntry> FilteredShaders { get; } = new();
    public ObservableCollection<MaterialPropertyRow> MaterialProperties { get; } = new();
    public ObservableCollection<CatalogMaterialChoice> FilteredCatalogMaterials { get; } = new();
    public ObservableCollection<string> ExistingMaterialNames { get; } = new();

    public bool CanEditMaterial => !IsInspectorReadOnly && SelectedOwnedItem != null;
    public bool CanEditArt => !IsInspectorReadOnly && SelectedOwnedItem != null;
    public bool IsCustomMaterialMode => CanEditMaterial && !UseDonorMaterials && !UseExistingMaterials;
    public bool IsExistingMaterialMode => CanEditMaterial && UseExistingMaterials;
    public string MaterialModeHint =>
        CanEditMaterial
            ? UseDonorMaterials
                ? "Donor mode: clone keeps vanilla materials. The new mesh still needs Existing materials, or it has nothing to draw with."
                : UseExistingMaterials
                    ? "Existing mode: pick catalog materials. Runtime looks them up by name and stamps them onto the art prefab. Nothing is packed."
                    : "Custom mode: Save writes material.json for runtime Shader.Find + property stamps."
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

        var lastProject = AppSettings.TryLoadLastProjectPath();
        if (!string.IsNullOrWhiteSpace(lastProject) && Directory.Exists(lastProject))
            _projectStore = new ProjectStore(lastProject);
        else
            _projectStore = new ProjectStore(ProjectStore.DefaultProjectRoot());

        RefreshShellLabels();
        StatusText = string.IsNullOrWhiteSpace(ValheimPath)
            ? "File → Open Valheim Folder… to get started."
            : "Loading SoftRef…";
        ReloadOwnedItems();
        LoadValheimProjectSetting();
        TryLoadShaderCatalogCache();
        RefreshUnityDetection();
    }

    public void OpenProject(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return;

        Directory.CreateDirectory(projectRoot);
        _projectStore = new ProjectStore(projectRoot);
        _projectStore.EnsureCreated();
        AppSettings.SaveLastProjectPath(_projectStore.ProjectRoot);
        SelectedOwnedItem = null;
        SelectedProjectNode = null;
        ReloadOwnedItems();
        LoadValheimProjectSetting();
        StatusText = $"Opened project: {_projectStore.ProjectRoot}";
        AppScreen = AppScreen.Project;
    }

    [RelayCommand]
    public async Task NewProjectAsync()
    {
        if (PickFolderAsync == null)
        {
            StatusText = "Folder picker not available.";
            return;
        }

        var folder = await PickFolderAsync("Create or choose an empty folder for the new project");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            StatusText = "Choose an empty folder for a new project.";
            return;
        }

        OpenProject(folder);
        StatusText = $"Created project: {_projectStore.ProjectRoot}";
    }

    [RelayCommand]
    public async Task OpenProjectAsync()
    {
        if (PickFolderAsync == null)
        {
            StatusText = "Folder picker not available.";
            return;
        }

        var folder = await PickFolderAsync("Open Asset Forge project folder (contains project.json)");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var marker = Path.Combine(folder, "project.json");
        var items = Path.Combine(folder, "Items");
        if (!File.Exists(marker) && !Directory.Exists(items))
        {
            // Allow opening a folder that will become a project.
            if (Directory.EnumerateFileSystemEntries(folder).Any())
            {
                StatusText = "That folder is not an Asset Forge project (missing project.json / Items).";
                return;
            }
        }

        OpenProject(folder);
    }

    [RelayCommand]
    public async Task SaveProjectAsAsync()
    {
        if (PickFolderAsync == null)
        {
            StatusText = "Folder picker not available.";
            return;
        }

        var folder = await PickFolderAsync("Save project as… (destination folder)");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            ProjectPackageService.CopyProject(_projectStore.ProjectRoot, folder);
            OpenProject(folder);
            StatusText = $"Saved project copy to {_projectStore.ProjectRoot}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save Project As failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ExportProjectPackageAsync()
    {
        if (PickSaveFileAsync == null)
        {
            StatusText = "Save dialog not available.";
            return;
        }

        var path = await PickSaveFileAsync(
            "Export project package",
            [".daf"],
            Path.GetFileName(_projectStore.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".daf");
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (!path.EndsWith(".daf", StringComparison.OrdinalIgnoreCase))
                path += ".daf";
            ProjectPackageService.ExportDaf(_projectStore.ProjectRoot, path);
            StatusText = $"Exported package: {path}";
        }
        catch (Exception ex)
        {
            StatusText = $"Export package failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportProjectPackageAsync()
    {
        if (PickOpenFileAsync == null || PickFolderAsync == null)
        {
            StatusText = "File picker not available.";
            return;
        }

        var daf = await PickOpenFileAsync("Import project package (.daf)", [".daf", "*.daf"]);
        if (string.IsNullOrWhiteSpace(daf))
            return;

        var dest = await PickFolderAsync("Choose an empty folder to unpack the project into");
        if (string.IsNullOrWhiteSpace(dest))
            return;

        try
        {
            ProjectPackageService.ImportDaf(daf, dest);
            OpenProject(dest);
            StatusText = $"Imported package into {_projectStore.ProjectRoot}";
        }
        catch (Exception ex)
        {
            StatusText = $"Import package failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ImportAssetBundlesAsync()
    {
        if (PickOpenFilesAsync == null)
        {
            StatusText = "File picker not available.";
            return;
        }

        var files = await PickOpenFilesAsync(
            "Import Unity asset bundle(s) — including extensionless SoftRef/mod files",
            ["*", "*.bundle", "*.unity3d", "art.bundle"]);
        if (files.Count == 0)
            return;

        // File pickers sometimes return non-bundles when * is allowed.
        var bundles = files.Where(BundleProjectImporter.LooksLikeUnityAssetBundle).ToList();
        if (bundles.Count == 0)
        {
            StatusText = "None of the selected files look like Unity asset bundles (UnityFS).";
            return;
        }

        try
        {
            IsBusy = true;
            var vanilla = BundleProjectImporter.BuildVanillaNameSet(_allAssets);
            var result = BundleProjectImporter.ImportBundles(_projectStore, bundles, vanilla);
            ReloadOwnedItems();
            AppScreen = AppScreen.Project;
            StatusText = result.Message;
            if (result.NeedsExtract > 0)
                StatusText += " Use File → Extract imported bundles… when Unity is configured.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import bundles failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ImportModAssetsFolderAsync()
    {
        if (PickFolderAsync == null)
        {
            StatusText = "Folder picker not available.";
            return;
        }

        var folder = await PickFolderAsync(
            "Open mod Assets folder (extensionless Unity bundles, e.g. LockSmith/Assets with ploam)");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            IsBusy = true;
            var vanilla = BundleProjectImporter.BuildVanillaNameSet(_allAssets);
            var result = BundleProjectImporter.ImportModAssetsFolder(_projectStore, folder, vanilla);
            ReloadOwnedItems();
            AppScreen = AppScreen.Project;
            StatusText = result.Message;
            if (result.NeedsExtract > 0)
                StatusText += " Use File → Extract imported bundles… when Unity is configured.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import mod Assets failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ImportModItemsFolderAsync()
    {
        if (PickFolderAsync == null)
        {
            StatusText = "Folder picker not available.";
            return;
        }

        var folder = await PickFolderAsync("Import mod Assets/Items folder (or plugin folder with item.json trees)");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        try
        {
            IsBusy = true;
            var vanilla = BundleProjectImporter.BuildVanillaNameSet(_allAssets);
            var result = BundleProjectImporter.ImportModItemsFolder(_projectStore, folder, vanilla);
            if (result.ImportedCount == 0)
            {
                // Same plugin Assets/ folder people often pick — fall through to UnityFS discovery.
                var assetsResult = BundleProjectImporter.ImportModAssetsFolder(_projectStore, folder, vanilla);
                if (assetsResult.ImportedCount > 0)
                    result = assetsResult;
            }

            ReloadOwnedItems();
            AppScreen = AppScreen.Project;
            StatusText = result.Message;
            if (result.NeedsExtract > 0)
                StatusText += " Use File → Extract imported bundles… when Unity is configured.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import mod items failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExtractSelectedBundleAsync()
    {
        var pending = GetSelectedOwnedItems()
            .Select(i => (Item: i, Art: _projectStore.LoadArt(i)))
            .Where(x => x.Art.NeedsBundleExtract && !File.Exists(Path.Combine(x.Item.FolderPath, "art.bundle")))
            .ToList();
        if (pending.Count == 0)
        {
            StatusText = "Select imported item(s) that need extract.";
            return;
        }

        await ExtractItemsAsync(pending);
    }

    [RelayCommand]
    public async Task ExtractImportedBundlesAsync()
    {
        var pending = _projectStore.LoadAllItems()
            .Select(i => (Item: i, Art: _projectStore.LoadArt(i)))
            .Where(x => x.Art.NeedsBundleExtract && !File.Exists(Path.Combine(x.Item.FolderPath, "art.bundle")))
            .ToList();
        if (pending.Count == 0)
        {
            StatusText = "No imported items need extract.";
            return;
        }

        await ExtractItemsAsync(pending);
    }

    private sealed class ExtractBatchResult
    {
        public int Ok { get; init; }
        public int Fail { get; init; }
        public string? LastError { get; init; }
        public bool Success => Fail == 0;
    }

    private async Task<ExtractBatchResult> ExtractItemsAsync(IReadOnlyList<(OwnedItemDocument Item, ArtDocument Art)> pending)
    {
        var unity = UnityEditorPath;
        if (string.IsNullOrWhiteSpace(unity) || !File.Exists(unity))
        {
            var msg = "Set Unity.exe on the Export screen before extracting (File → Extract also needs it).";
            StatusText = msg;
            AppScreen = AppScreen.Export;
            return new ExtractBatchResult { Fail = pending.Count, LastError = msg };
        }

        var template = UnityArtCompiler.FindTemplateSource();
        if (template == null)
        {
            const string msg = "Unity template missing.";
            StatusText = msg;
            return new ExtractBatchResult { Fail = pending.Count, LastError = msg };
        }

        try
        {
            IsBusy = true;
            var ok = 0;
            var fail = 0;
            string? lastFail = null;
            foreach (var (item, art) in pending)
            {
                var source = _projectStore.ResolveSourceBundlePath(art.SourceBundlePath);
                if (source == null || string.IsNullOrWhiteSpace(art.SourcePrefabName))
                {
                    fail++;
                    lastFail =
                        $"{item.Id}: missing source bundle '{art.SourceBundlePath}'. " +
                        "If this project is the mod repo, ensure Assets/drake (or ploam) exists, or File → Import Mod Assets Folder.";
                    continue;
                }

                var prefabName = BundleProjectImporter.ResolvePrefabNameInBundle(source, art.SourcePrefabName)
                                 ?? art.SourcePrefabName;

                var progress = new Progress<string>(msg => StatusText = $"{item.Id}: {msg}");
                var result = await UnityArtCompiler.ExtractPrefabAsync(
                    unity,
                    template,
                    new UnityArtExtractRequest
                    {
                        SourceBundlePath = source,
                        PrefabName = prefabName,
                        OutputBundlePath = Path.Combine(item.FolderPath, "art.bundle"),
                        BundleName = "art",
                    },
                    progress);

                if (result.Success)
                {
                    art.NeedsBundleExtract = false;
                    art.IncludeMesh = true;
                    if (!prefabName.Equals(art.SourcePrefabName, StringComparison.Ordinal))
                        art.SourcePrefabName = prefabName;
                    _projectStore.SaveArt(item, art);
                    ok++;
                }
                else
                {
                    fail++;
                    lastFail = $"{item.Id}: {result.Message}";
                    StatusText = lastFail;
                }
            }

            ReloadOwnedItems();
            if (SelectedOwnedItem != null)
                RefreshCompiledBundleFlag(SelectedOwnedItem);
            StatusText = fail == 0
                ? $"Extracted {ok} art bundle(s)."
                : $"Extracted {ok}, failed {fail}. {lastFail}";
            return new ExtractBatchResult { Ok = ok, Fail = fail, LastError = lastFail };
        }
        catch (Exception ex)
        {
            StatusText = $"Extract failed: {ex.Message}";
            return new ExtractBatchResult { Fail = pending.Count, LastError = ex.Message };
        }
        finally
        {
            IsBusy = false;
        }
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
            {
                LoadOwnedItemIntoInspector(SelectedOwnedItem);
                _ = RefreshOwnedPreviewAsync(SelectedOwnedItem);
            }
        }
        else if (value == AppScreen.Catalog && SelectedAsset != null)
        {
            _ = RefreshFieldSpyAsync();
        }
        else if (value == AppScreen.Export)
        {
            RefreshUnityDetection();
            RefreshExportTree();
        }
    }

    partial void OnUseExistingMaterialsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomMaterialMode));
        OnPropertyChanged(nameof(IsExistingMaterialMode));
        OnPropertyChanged(nameof(MaterialModeHint));
        if (!CanEditMaterial)
            return;
        if (value)
            UseDonorMaterials = false;
        _workingMaterial.Mode = value
            ? MaterialAuthoringMode.Existing
            : UseDonorMaterials ? MaterialAuthoringMode.Donor : MaterialAuthoringMode.Custom;
        MaterialStatus = value
            ? "Existing materials — add catalog names below. One name applies to every slot."
            : MaterialModeHint;
    }

    partial void OnExistingMaterialFilterChanged(string value) => ApplyCatalogMaterialFilter();

    partial void OnUseDonorMaterialsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCustomMaterialMode));
        OnPropertyChanged(nameof(IsExistingMaterialMode));
        OnPropertyChanged(nameof(MaterialModeHint));
        if (!CanEditMaterial)
            return;

        if (value)
            UseExistingMaterials = false;
        _workingMaterial.Mode = value
            ? MaterialAuthoringMode.Donor
            : UseExistingMaterials ? MaterialAuthoringMode.Existing : MaterialAuthoringMode.Custom;
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

        if (CanEditMaterial && !UseDonorMaterials && !UseExistingMaterials)
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
        OnPropertyChanged(nameof(IsExistingMaterialMode));
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
            HasCompiledBundle = false;
            NeedsBundleExtract = false;
            return;
        }

        RefreshCompiledBundleFlag(value);

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
            _thumbnails.Configure(ResolveBundlePath, _allAssets);
            ApplyCatalogMaterialFilter();
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
        if (AppScreen == AppScreen.Project)
        {
            if (SelectedOwnedItem == null)
            {
                ClearPreview("Select a Project item to preview.");
                return;
            }

            await RefreshOwnedPreviewAsync(SelectedOwnedItem);
            return;
        }

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

    partial void OnHasPreviewImageChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowIconPreview));
        OnPropertyChanged(nameof(ShowPreviewPlaceholder));
    }

    partial void OnMeshPreviewPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasMeshPreview));
        OnPropertyChanged(nameof(ShowIconPreview));
        OnPropertyChanged(nameof(ShowPreviewPlaceholder));
    }

    partial void OnArtMeshPathChanged(string value)
    {
        if (SelectedOwnedItem == null || AppScreen != AppScreen.Project)
            return;
        MeshPreviewPath = ProjectStore.ResolveArtAbsolutePath(SelectedOwnedItem, value) ?? "";
    }

    partial void OnArtDiffusePathChanged(string value)
    {
        if (SelectedOwnedItem == null || AppScreen != AppScreen.Project)
            return;
        MeshDiffusePath = ProjectStore.ResolveArtAbsolutePath(SelectedOwnedItem, value) ?? "";
    }

    private void ClearPreview(string caption, bool clearMesh = true)
    {
        var old = PreviewBitmap;
        PreviewBitmap = null;
        old?.Dispose();
        HasPreviewImage = false;
        if (clearMesh)
        {
            MeshPreviewPath = "";
            MeshDiffusePath = "";
        }
        PreviewCaption = caption;
    }

    private void UpdateMeshPreview(OwnedItemDocument doc)
    {
        var art = _projectStore.LoadArt(doc);
        var meshRel = string.IsNullOrWhiteSpace(ArtMeshPath) ? art.MeshPath : ArtMeshPath;
        var diffuseRel = string.IsNullOrWhiteSpace(ArtDiffusePath) ? art.DiffusePath : ArtDiffusePath;
        MeshPreviewPath = ProjectStore.ResolveArtAbsolutePath(doc, meshRel) ?? "";
        MeshDiffusePath = ProjectStore.ResolveArtAbsolutePath(doc, diffuseRel) ?? "";
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
        _thumbnails.RequestCatalogThumb(SelectedAsset);
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

            var doc = _projectStore.CloneFromSpy(donor, prefabSpy, recipeSpy, recipeReqs, preferredGroup: CurrentDropGroup());
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

            ApplySpawnName(SelectedOwnedItem);
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

    partial void OnSelectedProjectNodeChanged(ProjectTreeNode? value)
    {
        if (_suppressProjectTreeSelection)
            return;

        if (value?.Item != null)
        {
            if (!ReferenceEquals(SelectedOwnedItem, value.Item))
                SelectedOwnedItem = value.Item;
            return;
        }

        // Folder selected — keep current item if it lives under this group; otherwise clear inspector item.
        if (value?.IsFolder == true)
        {
            if (SelectedOwnedItem != null &&
                SelectedOwnedItem.GroupPath.Equals(value.GroupPath, StringComparison.OrdinalIgnoreCase))
                return;
        }
    }

    private void ReloadOwnedItems()
    {
        _projectStore.EnsureCreated();
        ProjectPathLabel = _projectStore.ProjectRoot;
        var items = _projectStore.LoadAllItems();
        var selectedId = SelectedOwnedItem?.Id;
        var selectedGroup = SelectedProjectNode?.IsFolder == true ? SelectedProjectNode.GroupPath : null;

        _suppressOwnedSelectionSideEffects = true;
        _suppressProjectTreeSelection = true;
        try
        {
            OwnedItems.Clear();
            foreach (var item in items)
                OwnedItems.Add(item);

            RebuildProjectTree(items);
            ProjectGroups.Clear();
            ProjectGroups.Add("(project root)");
            foreach (var group in _projectStore.ListGroups())
                ProjectGroups.Add(group);
        }
        finally
        {
            _suppressOwnedSelectionSideEffects = false;
            _suppressProjectTreeSelection = false;
        }

        if (selectedId != null)
        {
            var match = OwnedItems.FirstOrDefault(o => o.Id == selectedId);
            if (!ReferenceEquals(SelectedOwnedItem, match))
                SelectedOwnedItem = match;
            SelectedProjectNode = FindTreeNodeByItemId(selectedId) ?? SelectedProjectNode;
        }
        else if (!string.IsNullOrEmpty(selectedGroup))
        {
            SelectedProjectNode = FindTreeNodeByGroup(selectedGroup);
        }
    }

    private void RebuildProjectTree(IReadOnlyList<OwnedItemDocument> items)
    {
        ProjectTree.Clear();
        var folders = new Dictionary<string, ProjectTreeNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in _projectStore.ListGroups())
        {
            var node = new ProjectTreeNode(group, group);
            folders[group] = node;
            ProjectTree.Add(node);
        }

        foreach (var item in items.OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var leaf = new ProjectTreeNode(item.DisplayName, item.GroupPath, item);
            if (string.IsNullOrWhiteSpace(item.GroupPath))
            {
                ProjectTree.Add(leaf);
                continue;
            }

            if (!folders.TryGetValue(item.GroupPath, out var folder))
            {
                folder = new ProjectTreeNode(item.GroupPath, item.GroupPath);
                folders[item.GroupPath] = folder;
                ProjectTree.Add(folder);
            }

            folder.Children.Add(leaf);
            folder.RefreshSubtitle();
        }

        // Stable order: folders first, then root items.
        var ordered = ProjectTree
            .OrderByDescending(n => n.IsFolder)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ProjectTree.Clear();
        foreach (var node in ordered)
            ProjectTree.Add(node);

        _thumbnails.InvalidateAllOwned();
        foreach (var node in EnumerateTreeNodes())
            _thumbnails.RequestProjectThumb(node, _projectStore);
    }

    public void RequestCatalogThumbnail(SoftRefAssetEntry? asset)
    {
        if (asset != null)
            _thumbnails.RequestCatalogThumb(asset);
    }

    private ProjectTreeNode? FindTreeNodeByItemId(string id)
    {
        foreach (var node in ProjectTree)
        {
            if (node.Item?.Id.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
                return node;
            foreach (var child in node.Children)
            {
                if (child.Item?.Id.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
                    return child;
            }
        }

        return null;
    }

    private ProjectTreeNode? FindTreeNodeByGroup(string groupPath)
    {
        return ProjectTree.FirstOrDefault(n =>
            n.IsFolder && n.GroupPath.Equals(groupPath, StringComparison.OrdinalIgnoreCase));
    }

    private string? CurrentDropGroup()
    {
        if (SelectedProjectNode == null)
            return null;
        if (SelectedProjectNode.IsFolder)
            return SelectedProjectNode.GroupPath;
        return SelectedProjectNode.Item?.GroupPath;
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

    private void ApplyCatalogMaterialFilter()
    {
        var filter = ExistingMaterialFilter?.Trim() ?? "";
        FilteredCatalogMaterials.Clear();
        if (_allAssets.Count == 0)
            return;

        IEnumerable<SoftRefAssetEntry> query = _allAssets.Where(a => a.Kind == CatalogKind.Material && !a.IsResourceNoise);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(a =>
                a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.PathInBundle.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var asset in query.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).Take(400))
        {
            if (string.IsNullOrWhiteSpace(asset.DisplayName))
                continue;
            FilteredCatalogMaterials.Add(new CatalogMaterialChoice
            {
                Name = asset.DisplayName,
                Path = asset.PathInBundle,
            });
        }
    }

    [RelayCommand]
    private void AddExistingMaterial()
    {
        if (!CanEditMaterial)
            return;

        var name = !string.IsNullOrWhiteSpace(TypedMaterialName)
            ? TypedMaterialName.Trim()
            : SelectedCatalogMaterial?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            MaterialStatus = "Pick a catalog material or type a name.";
            return;
        }

        if (ExistingMaterialNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MaterialStatus = $"'{name}' is already in the list.";
            return;
        }

        ExistingMaterialNames.Add(name);
        TypedMaterialName = "";
        UseExistingMaterials = true;
        MaterialStatus = ExistingMaterialNames.Count == 1
            ? $"'{name}' applies to every renderer slot."
            : $"{ExistingMaterialNames.Count} materials — assigned to slots in order.";
    }

    [RelayCommand]
    private void RemoveExistingMaterial()
    {
        if (string.IsNullOrWhiteSpace(SelectedExistingMaterial))
            return;
        ExistingMaterialNames.Remove(SelectedExistingMaterial);
        SelectedExistingMaterial = null;
        MaterialStatus = $"{ExistingMaterialNames.Count} existing material(s).";
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
            UseExistingMaterials = _workingMaterial.Mode == MaterialAuthoringMode.Existing;
            UseDonorMaterials = _workingMaterial.Mode != MaterialAuthoringMode.Existing
                && _workingMaterial.Mode == MaterialAuthoringMode.Donor;
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

        ExistingMaterialNames.Clear();
        foreach (var name in _workingMaterial.ExistingMaterials)
        {
            if (!string.IsNullOrWhiteSpace(name))
                ExistingMaterialNames.Add(name.Trim());
        }

        MaterialStatus = _workingMaterial.Mode switch
        {
            MaterialAuthoringMode.Existing =>
                $"Existing materials for '{doc.DisplayName}' — {ExistingMaterialNames.Count} name(s).",
            MaterialAuthoringMode.Custom =>
                $"Custom '{_workingMaterial.ShaderName}' — {MaterialProperties.Count} props",
            _ => $"Donor materials for '{doc.DisplayName}' (no Shader.Find override).",
        };
    }

    private void SaveWorkingMaterial(OwnedItemDocument doc)
    {
        _workingMaterial.Mode = UseExistingMaterials
            ? MaterialAuthoringMode.Existing
            : UseDonorMaterials ? MaterialAuthoringMode.Donor : MaterialAuthoringMode.Custom;
        _workingMaterial.ShaderName = SelectedShader?.FindName ?? _workingMaterial.ShaderName;
        _workingMaterial.Properties = MaterialProperties.Select(p => p.ToValue()).ToList();
        _workingMaterial.ExistingMaterials = ExistingMaterialNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _projectStore.SaveMaterial(doc, _workingMaterial);
    }

    private void LoadArtIntoUi(OwnedItemDocument doc)
    {
        _workingArt = _projectStore.LoadArt(doc);
        ArtMeshPath = _workingArt.MeshPath ?? "";
        ArtIconPath = _workingArt.IconPath ?? "";
        ArtDiffusePath = _workingArt.DiffusePath ?? "";
        ArtPrefabName = _workingArt.PrefabName ?? "";
        ArtScale = (_workingArt.Scale <= 0 ? 1f : _workingArt.Scale).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ArtDescription = _workingArt.Description ?? "";
        _suppressArtInclude = true;
        try
        {
            IncludeMeshInBundle = _workingArt.IncludeMesh;
            IncludeIconInBundle = _workingArt.IncludeIcon;
            IncludeDiffuseInBundle = _workingArt.IncludeDiffuse;
        }
        finally
        {
            _suppressArtInclude = false;
        }

        ArtStatus = SummarizeArtStatus();
    }

    private void SaveWorkingArt(OwnedItemDocument doc)
    {
        _workingArt.MeshPath = string.IsNullOrWhiteSpace(ArtMeshPath) ? null : ArtMeshPath;
        _workingArt.IconPath = string.IsNullOrWhiteSpace(ArtIconPath) ? null : ArtIconPath;
        _workingArt.DiffusePath = string.IsNullOrWhiteSpace(ArtDiffusePath) ? null : ArtDiffusePath;
        _workingArt.PrefabName = SanitizePrefabName(ArtPrefabName);
        ArtPrefabName = _workingArt.PrefabName ?? "";
        _workingArt.IncludeMesh = IncludeMeshInBundle;
        _workingArt.IncludeIcon = IncludeIconInBundle;
        _workingArt.IncludeDiffuse = IncludeDiffuseInBundle;
        if (!float.TryParse(ArtScale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && !float.TryParse(ArtScale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out scale))
            scale = 1f;
        if (scale <= 0 || float.IsNaN(scale) || float.IsInfinity(scale))
            scale = 1f;
        _workingArt.Scale = scale;
        ArtScale = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _workingArt.Description = string.IsNullOrWhiteSpace(ArtDescription) ? null : ArtDescription.Trim();
        _projectStore.SaveArt(doc, _workingArt);
    }

    private void ApplySpawnName(OwnedItemDocument doc)
    {
        var spawn = SanitizePrefabName(ArtPrefabName);
        if (string.IsNullOrWhiteSpace(spawn))
            return;
        doc.Fields["prefab"] = spawn;
        doc.DisplayName = spawn;
    }

    private static string? SanitizePrefabName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var chars = raw.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .ToArray();
        var s = new string(chars).Trim('_');
        if (s.Length == 0)
            return null;
        if (!char.IsLetter(s[0]))
            s = "Item_" + s;
        return s;
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
        ArtPrefabName = "";
        ArtScale = "1";
        ArtDescription = "";
        ArtStatus = status;
    }

    private string SummarizeArtStatus()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ArtMeshPath))
            parts.Add(IncludeMeshInBundle ? "mesh" : "mesh (out)");
        if (!string.IsNullOrWhiteSpace(ArtIconPath))
            parts.Add(IncludeIconInBundle ? "icon" : "icon (out)");
        if (!string.IsNullOrWhiteSpace(ArtDiffusePath))
            parts.Add(IncludeDiffuseInBundle ? "diffuse" : "diffuse (out)");
        if (!string.IsNullOrWhiteSpace(ArtPrefabName))
            parts.Add($"spawn {ArtPrefabName}");
        if (float.TryParse(ArtScale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale)
            && Math.Abs(scale - 1f) > 0.001f)
            parts.Add($"scale {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        return parts.Count == 0
            ? SelectedOwnedItem != null && File.Exists(Path.Combine(SelectedOwnedItem.FolderPath, "art.bundle"))
                ? "art.bundle present — Export ships it. Rebuild only if you changed FBX/diffuse."
                : SelectedOwnedItem?.NeedsBundleExtract == true
                    ? "Imported multi-prefab — Export (or Extract) packs it into art.bundle via Unity."
                    : "No custom art — Export ships item.json + hooks only (donor stays in-game)."
            : SelectedOwnedItem != null &&
              !string.IsNullOrWhiteSpace(ArtMeshPath) &&
              IncludeMeshInBundle &&
              !File.Exists(Path.Combine(SelectedOwnedItem.FolderPath, "art.bundle"))
                ? $"Attached: {string.Join(", ", parts)}. Export will compile FBX → art.bundle then wire the mod."
                : $"Attached: {string.Join(", ", parts)}. Save writes art.json.";
    }

    [RelayCommand]
    public async Task BrowseArtMeshAsync() =>
        await BrowseAndImportArtAsync(
            title: "Select mesh FBX",
            patterns: new[] { "*.fbx" },
            destFileName: "mesh.fbx",
            apply: relative =>
            {
                ArtMeshPath = relative;
                IncludeMeshInBundle = true;
            });

    [RelayCommand]
    public async Task BrowseArtIconAsync() =>
        await BrowseAndImportArtAsync(
            title: "Select icon PNG",
            patterns: new[] { "*.png" },
            destFileName: "icon.png",
            apply: relative =>
            {
                ArtIconPath = relative;
                IncludeIconInBundle = true;
            },
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
            IncludeDiffuseInBundle = true;
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
    partial void OnIncludeMeshInBundleChanged(bool value) => PersistIncludeChange();
    partial void OnIncludeIconInBundleChanged(bool value) => PersistIncludeChange();
    partial void OnIncludeDiffuseInBundleChanged(bool value) => PersistIncludeChange();

    private void PersistIncludeChange()
    {
        if (_suppressArtInclude || !CanEditArt || SelectedOwnedItem == null)
            return;
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Art checklist saved. Run Export to make/ship art.bundles into the code project.";
    }

    [RelayCommand]
    public void NewProjectGroup()
    {
        var baseName = "New group";
        string? created = null;
        string error = "";
        for (var n = 1; n <= 50; n++)
        {
            var name = n == 1 ? baseName : $"{baseName} {n}";
            if (_projectStore.TryCreateGroup(name, out created, out error))
                break;
            created = null;
        }

        if (string.IsNullOrEmpty(created))
        {
            StatusText = string.IsNullOrWhiteSpace(error) ? "Could not create a new group." : error;
            return;
        }

        ReloadOwnedItems();
        SelectedProjectNode = FindTreeNodeByGroup(created);
        BeginRenameProjectNode();
        // Focus happens from the view when F2 is pressed; also nudge after New group.
        StatusText = $"Created group '{created}'. Press F2 if the name field is not focused, then Enter.";
    }

    [RelayCommand]
    public void BeginRenameProjectNode()
    {
        var node = SelectedProjectNode;
        if (node == null)
            return;
        foreach (var other in EnumerateTreeNodes())
            other.IsEditing = false;
        node.EditName = node.Name;
        node.IsEditing = true;
    }

    [RelayCommand]
    public void CommitRenameProjectNode()
    {
        var node = SelectedProjectNode;
        if (node == null || !node.IsEditing)
            return;

        var typed = node.EditName?.Trim() ?? "";
        node.IsEditing = false;
        if (string.IsNullOrWhiteSpace(typed) || typed.Equals(node.Name, StringComparison.OrdinalIgnoreCase))
        {
            node.EditName = node.Name;
            return;
        }

        if (node.IsFolder)
        {
            if (!_projectStore.TryRenameGroup(node.GroupPath, typed, out var newPath, out var error))
            {
                StatusText = error;
                node.EditName = node.Name;
                return;
            }

            ReloadOwnedItems();
            SelectedProjectNode = FindTreeNodeByGroup(newPath);
            StatusText = $"Renamed group to '{newPath}'.";
            return;
        }

        if (node.Item == null)
            return;

        var oldId = node.Item.Id;
        if (!_projectStore.TryRenameItem(node.Item, typed, out var newId, out var itemError))
        {
            StatusText = string.IsNullOrWhiteSpace(itemError) ? "Rename failed." : itemError;
            node.EditName = node.Name;
            return;
        }

        if (!oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
            ValheimProjectSync.MoveSyncedItem(ValheimProjectPath, oldId, newId);
        if (!oldId.Equals(newId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ValheimProjectPath))
            SyncValheimProject();

        ReloadOwnedItems();
        SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == newId);
        SelectedProjectNode = FindTreeNodeByItemId(newId);
        StatusText = oldId.Equals(newId, StringComparison.OrdinalIgnoreCase)
            ? $"'{newId}' is already the name."
            : $"Renamed {oldId} → {newId}.";
    }

    [RelayCommand]
    public void CancelRenameProjectNode()
    {
        if (SelectedProjectNode == null)
            return;
        SelectedProjectNode.IsEditing = false;
        SelectedProjectNode.EditName = SelectedProjectNode.Name;
    }

    [RelayCommand]
    public async Task DeleteProjectSelectionAsync()
    {
        var selectedItems = GetSelectedOwnedItems();
        if (selectedItems.Count > 1)
        {
            var message = $"Delete {selectedItems.Count} selected items? This cannot be undone.";
            if (ConfirmAsync != null && !await ConfirmAsync(message))
                return;

            foreach (var item in selectedItems)
            {
                ValheimProjectSync.RemoveSyncedItem(ValheimProjectPath, item.Id);
                _projectStore.DeleteItem(item);
            }

            ReloadOwnedItems();
            SelectedOwnedItem = null;
            SelectedProjectNode = null;
            StatusText = $"Deleted {selectedItems.Count} item(s).";
            return;
        }

        var node = SelectedProjectNode;
        if (node == null)
            return;

        if (node.IsFolder)
        {
            var count = node.Children.Count;
            var message = count == 0
                ? $"Delete empty group '{node.Name}'?"
                : $"Delete group '{node.Name}' and its {count} item(s)? This cannot be undone.";
            if (ConfirmAsync != null && !await ConfirmAsync(message))
                return;

            var deletedIds = node.Children
                .Select(c => c.Item?.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();
            if (!_projectStore.TryDeleteGroup(node.GroupPath, deleteItems: true, out var error))
            {
                StatusText = error;
                return;
            }

            foreach (var id in deletedIds)
                ValheimProjectSync.RemoveSyncedItem(ValheimProjectPath, id);

            ReloadOwnedItems();
            SelectedOwnedItem = null;
            SelectedProjectNode = null;
            StatusText = $"Deleted group '{node.Name}'.";
            return;
        }

        if (node.Item == null)
            return;

        SelectedOwnedItem = node.Item;
        await DeleteOwnedItemAsync();
    }

    [RelayCommand]
    public void MoveOwnedItemToGroup(string? groupLabel)
    {
        if (SelectedOwnedItem == null)
            return;
        MoveItemIntoGroup(SelectedOwnedItem, groupLabel);
    }

    public void MoveItemIntoGroup(OwnedItemDocument item, string? groupLabel) =>
        MoveItemsIntoGroup(new[] { item }, groupLabel);

    public void MoveSelectedItemsIntoGroup(string? groupLabel) =>
        MoveItemsIntoGroup(GetSelectedOwnedItems(), groupLabel);

    public void MoveItemsIntoGroup(IReadOnlyList<OwnedItemDocument> items, string? groupLabel)
    {
        if (items.Count == 0)
        {
            StatusText = "Select one or more items to move.";
            return;
        }

        var group = string.IsNullOrWhiteSpace(groupLabel) || groupLabel == "(project root)"
            ? ""
            : groupLabel;
        var moved = 0;
        string? lastError = null;
        string? lastId = null;

        foreach (var id in items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            var fresh = _projectStore.LoadAllItems()
                .FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (fresh == null)
                continue;
            if (!_projectStore.TryMoveItemToGroup(fresh, group, out var error))
            {
                lastError = error;
                continue;
            }

            moved++;
            lastId = id;
        }

        ReloadOwnedItems();
        if (lastId != null)
        {
            SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == lastId);
            SelectedProjectNode = FindTreeNodeByItemId(lastId);
        }

        if (moved == 0)
        {
            StatusText = string.IsNullOrWhiteSpace(lastError) ? "Move failed." : lastError;
            return;
        }

        StatusText = string.IsNullOrEmpty(group)
            ? $"Moved {moved} item(s) to the project root."
            : $"Moved {moved} item(s) into '{group}'.";
    }

    [RelayCommand]
    public void CopySelectedProjectItems()
    {
        var items = GetSelectedOwnedItems();
        if (items.Count == 0)
        {
            StatusText = "Select item(s) to copy.";
            return;
        }

        _projectClipboardIds.Clear();
        _projectClipboardIds.AddRange(items.Select(i => i.Id));
        _projectClipboardIsCut = false;
        OnPropertyChanged(nameof(HasProjectClipboard));
        StatusText = $"Copied {items.Count} item(s). Ctrl+V pastes into the selected group.";
    }

    [RelayCommand]
    public void CutSelectedProjectItems()
    {
        var items = GetSelectedOwnedItems();
        if (items.Count == 0)
        {
            StatusText = "Select item(s) to cut.";
            return;
        }

        _projectClipboardIds.Clear();
        _projectClipboardIds.AddRange(items.Select(i => i.Id));
        _projectClipboardIsCut = true;
        OnPropertyChanged(nameof(HasProjectClipboard));
        StatusText = $"Cut {items.Count} item(s). Ctrl+V moves them into the selected group.";
    }

    [RelayCommand]
    public void PasteProjectItems()
    {
        if (_projectClipboardIds.Count == 0)
        {
            StatusText = "Clipboard is empty. Copy or cut items first.";
            return;
        }

        var targetGroup = CurrentDropGroup() ?? "";
        var ids = _projectClipboardIds.ToList();
        var ok = 0;
        string? lastId = null;
        string? lastError = null;

        if (_projectClipboardIsCut)
        {
            foreach (var id in ids)
            {
                var fresh = _projectStore.LoadAllItems()
                    .FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (fresh == null)
                    continue;
                if (!_projectStore.TryMoveItemToGroup(fresh, targetGroup, out var error))
                {
                    lastError = error;
                    continue;
                }

                ok++;
                lastId = id;
            }

            _projectClipboardIds.Clear();
            _projectClipboardIsCut = false;
            OnPropertyChanged(nameof(HasProjectClipboard));
        }
        else
        {
            foreach (var id in ids)
            {
                var fresh = _projectStore.LoadAllItems()
                    .FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (fresh == null)
                    continue;
                if (!_projectStore.TryDuplicateItem(fresh, out var copy, out var error, preferredGroup: targetGroup) ||
                    copy == null)
                {
                    lastError = error;
                    continue;
                }

                ok++;
                lastId = copy.Id;
            }
        }

        ReloadOwnedItems();
        if (lastId != null)
        {
            SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == lastId);
            SelectedProjectNode = FindTreeNodeByItemId(lastId);
        }

        StatusText = ok == 0
            ? (string.IsNullOrWhiteSpace(lastError) ? "Paste failed." : lastError)
            : $"Pasted {ok} item(s) into {(string.IsNullOrEmpty(targetGroup) ? "project root" : targetGroup)}.";
    }

    [RelayCommand]
    public void DuplicateOwnedItem()
    {
        var sources = GetSelectedOwnedItems();
        if (sources.Count == 0)
        {
            StatusText = "Select an item to duplicate.";
            return;
        }

        var ok = 0;
        string? lastId = null;
        string? lastError = null;
        foreach (var source in sources)
        {
            if (!_projectStore.TryDuplicateItem(source, out var copy, out var error) || copy == null)
            {
                lastError = error;
                continue;
            }

            ok++;
            lastId = copy.Id;
        }

        ReloadOwnedItems();
        if (lastId != null)
        {
            SelectedOwnedItem = OwnedItems.FirstOrDefault(o => o.Id == lastId);
            SelectedProjectNode = FindTreeNodeByItemId(lastId);
        }

        StatusText = ok == 0
            ? (string.IsNullOrWhiteSpace(lastError) ? "Duplicate failed." : lastError)
            : $"Duplicated {ok} item(s).";
    }

    [RelayCommand]
    public void RevealProjectSelection()
    {
        if (SelectedProjectNode?.IsFolder == true)
        {
            var folder = Path.Combine(_projectStore.ItemsRoot, SelectedProjectNode.GroupPath.Replace('/', Path.DirectorySeparatorChar));
            RevealPath(folder);
            return;
        }

        RevealOwnedItemFolder();
    }

    private void RevealPath(string folder)
    {
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

    private IEnumerable<ProjectTreeNode> EnumerateTreeNodes()
    {
        foreach (var node in ProjectTree)
        {
            yield return node;
            foreach (var child in node.Children)
                yield return child;
        }
    }

    [RelayCommand]
    public async Task DeleteOwnedItemAsync()
    {
        if (SelectedOwnedItem == null)
            return;

        var id = SelectedOwnedItem.Id;
        if (ConfirmAsync != null)
        {
            var ok = await ConfirmAsync($"Delete '{id}' from the project and the synced mod folder? This cannot be undone.");
            if (!ok)
                return;
        }

        try
        {
            _projectStore.DeleteItem(SelectedOwnedItem);
            ValheimProjectSync.RemoveSyncedItem(ValheimProjectPath, id);
            ReloadOwnedItems();
            SelectedOwnedItem = null;
            SelectedProjectNode = null;
            StatusText = $"Deleted {id}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RemoveCompiledBundle()
    {
        if (SelectedOwnedItem == null)
            return;

        var bundle = Path.Combine(SelectedOwnedItem.FolderPath, "art.bundle");
        try
        {
            if (File.Exists(bundle))
                File.Delete(bundle);
            ValheimProjectSync.RemoveShippedBundle(ValheimProjectPath, SelectedOwnedItem.Id);
            RefreshCompiledBundleFlag(SelectedOwnedItem);
            StatusText = $"Removed art.bundle for {SelectedOwnedItem.Id}. The item stays in the project.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not remove bundle: {ex.Message}";
        }
    }

    private void RefreshCompiledBundleFlag(OwnedItemDocument? doc)
    {
        HasCompiledBundle = doc != null && File.Exists(Path.Combine(doc.FolderPath, "art.bundle"));
        if (doc == null)
        {
            NeedsBundleExtract = false;
            return;
        }

        var art = _projectStore.LoadArt(doc);
        NeedsBundleExtract = art.NeedsBundleExtract && !HasCompiledBundle;
    }

    [RelayCommand]
    public void ClearArtMesh()
    {
        if (!CanEditArt || SelectedOwnedItem == null)
            return;
        _projectStore.DeleteArtFile(SelectedOwnedItem, ArtMeshPath);
        ArtMeshPath = "";
        IncludeMeshInBundle = false;
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Removed mesh from the item. The compiled bundle is unchanged until you compile again or remove it.";
    }

    [RelayCommand]
    public void ClearArtIcon()
    {
        if (!CanEditArt || SelectedOwnedItem == null)
            return;
        _projectStore.DeleteArtFile(SelectedOwnedItem, ArtIconPath);
        ArtIconPath = "";
        IncludeIconInBundle = false;
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Removed icon. Sync assets to drop icon.png from the mod folder.";
        if (SelectedOwnedItem != null)
            _ = RefreshOwnedPreviewAsync(SelectedOwnedItem);
    }

    [RelayCommand]
    public void ClearArtDiffuse()
    {
        if (!CanEditArt || SelectedOwnedItem == null)
            return;
        _projectStore.DeleteArtFile(SelectedOwnedItem, ArtDiffusePath);
        ArtDiffusePath = "";
        IncludeDiffuseInBundle = false;
        PersistArtAndRefreshBadges();
        ArtStatus = SummarizeArtStatus();
        StatusText = "Removed diffuse. Compile again to drop it from art.bundle.";
    }

    [RelayCommand]
    public void RevealOwnedItemFolder()
    {
        if (SelectedOwnedItem == null || string.IsNullOrWhiteSpace(SelectedOwnedItem.FolderPath))
        {
            StatusText = "Select a Project item first.";
            return;
        }

        RevealPath(SelectedOwnedItem.FolderPath);
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
            if (SelectedOwnedItem != null)
            {
                _thumbnails.InvalidateOwned(SelectedOwnedItem.Id);
                var node = FindTreeNodeByItemId(SelectedOwnedItem.Id);
                if (node != null)
                {
                    node.Thumbnail = null;
                    _thumbnails.RequestProjectThumb(node, _projectStore);
                }
            }
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
            UseExistingMaterials = false;
            SelectedShader = null;
            ExistingMaterialNames.Clear();
        }
        finally
        {
            _suppressMaterialSelection = false;
        }

        MaterialStatus = status;
    }

    private async Task RefreshOwnedPreviewAsync(OwnedItemDocument doc)
    {
        var gen = Interlocked.Increment(ref _ownedPreviewGeneration);
        UpdateMeshPreview(doc);
        var art = _projectStore.LoadArt(doc);

        try
        {
            var custom = await Task.Run(() => OwnedArtPreview.Resolve(_projectStore, doc, art));
            if (gen != _ownedPreviewGeneration || SelectedOwnedItem?.Id != doc.Id)
            {
                custom.Bitmap?.Dispose();
                return;
            }

            if (custom.Bitmap != null)
            {
                PreviewBitmap?.Dispose();
                PreviewBitmap = custom.Bitmap;
                HasPreviewImage = true;
                PreviewCaption = custom.Caption;
                // Reload art UI if icon was just persisted.
                if (string.IsNullOrWhiteSpace(ArtIconPath) &&
                    !string.IsNullOrWhiteSpace(_projectStore.LoadArt(doc).IconPath))
                    LoadArtIntoUi(doc);
                return;
            }

            if (!string.IsNullOrWhiteSpace(custom.MeshPath))
            {
                ClearPreview(custom.Caption, clearMesh: false);
                MeshPreviewPath = custom.MeshPath;
                PreviewCaption = custom.Caption;
                return;
            }

            // Imported / custom items: never show SoftRef LeatherScraps as if it were the art.
            if (!custom.UsedDonorFallback)
            {
                ClearPreview(custom.Caption, clearMesh: false);
                return;
            }
        }
        catch (Exception ex)
        {
            if (gen != _ownedPreviewGeneration || SelectedOwnedItem?.Id != doc.Id)
                return;
            ClearPreview($"Custom preview failed: {ex.Message}", clearMesh: false);
            return;
        }

        if (gen != _ownedPreviewGeneration || SelectedOwnedItem?.Id != doc.Id)
            return;

        // SoftRef donor only for real SoftRef clones (not Imported LeatherScraps stubs).
        if (_index == null || string.IsNullOrWhiteSpace(doc.Donor.PrefabName))
        {
            ClearPreview(
                $"Owned '{doc.DisplayName}' · no custom preview yet.",
                clearMesh: false);
            return;
        }

        var donor = _allAssets.FirstOrDefault(a =>
            a.Kind == CatalogKind.ItemPrefab &&
            (a.AssetId.Equals(doc.Donor.SoftRefAssetId, StringComparison.OrdinalIgnoreCase) ||
             a.DisplayName.Equals(doc.Donor.PrefabName, StringComparison.OrdinalIgnoreCase)));

        if (donor == null)
        {
            ClearPreview($"Owned '{doc.DisplayName}' · no custom art yet; SoftRef donor '{doc.Donor.PrefabName}' not loaded.", clearMesh: false);
            return;
        }

        var icon = SoftRefPreviewService.FindIconForItem(donor, _allAssets);
        if (icon == null)
        {
            ClearPreview($"Owned '{doc.DisplayName}' · no SoftRef icon for donor '{donor.DisplayName}'.", clearMesh: false);
            return;
        }

        var bundlePath = ResolveBundlePath(icon.BundleId);
        if (bundlePath == null)
        {
            ClearPreview($"Owned '{doc.DisplayName}' · donor icon bundle missing.", clearMesh: false);
            return;
        }

        try
        {
            var result = await Task.Run(() =>
                SoftRefPreviewService.PreviewTextureByContainerPath(bundlePath, icon.PathInBundle));
            if (gen != _ownedPreviewGeneration || SelectedOwnedItem?.Id != doc.Id)
            {
                result.Bitmap?.Dispose();
                return;
            }

            if (result.Error == null && result.Bitmap != null)
            {
                result = new SoftRefPreviewResult
                {
                    Bitmap = result.Bitmap,
                    Caption =
                        $"Owned '{doc.DisplayName}' · SoftRef donor ({doc.Donor.PrefabName})",
                };
            }

            ApplyPreviewResult(result);
        }
        catch (Exception ex)
        {
            if (gen != _ownedPreviewGeneration || SelectedOwnedItem?.Id != doc.Id)
                return;
            ClearPreview($"Preview error: {ex.Message}", clearMesh: false);
        }
    }

    private void RefreshUnityDetection()
    {
        RequiredUnityVersion = UnityArtCompiler.DetectValheimUnityVersion(ValheimPath) ?? "";
        var saved = UnityArtCompiler.TryLoadSavedUnityExe();
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            UnityEditorPath = saved;
        else
            UnityEditorPath = UnityArtCompiler.FindBestEditor(
                string.IsNullOrWhiteSpace(RequiredUnityVersion) ? null : RequiredUnityVersion,
                out _) ?? "";

        UnityStatus = string.IsNullOrWhiteSpace(UnityEditorPath)
            ? "No Unity editor found. Browse to Unity.exe (Valheim wants " +
              (string.IsNullOrWhiteSpace(RequiredUnityVersion) ? "Unity 6" : RequiredUnityVersion) + ")."
            : DescribeUnityChoice();
    }

    private string DescribeUnityChoice()
    {
        var version = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(UnityEditorPath))) ?? "";
        if (!string.IsNullOrWhiteSpace(RequiredUnityVersion) &&
            version.Equals(RequiredUnityVersion, StringComparison.OrdinalIgnoreCase))
            return $"Unity {version} matches Valheim.";

        if (!string.IsNullOrWhiteSpace(RequiredUnityVersion))
            return $"Valheim is Unity {RequiredUnityVersion}. Selected editor is {version}. Prefer an exact match.";

        return $"Using {UnityEditorPath}";
    }

    [RelayCommand]
    public async Task BrowseUnityEditorAsync()
    {
        if (PickOpenFileAsync == null)
        {
            UnityStatus = "File picker not available.";
            return;
        }

        var picked = await PickOpenFileAsync("Select Unity.exe", new[] { "Unity.exe", "*.exe" });
        if (string.IsNullOrWhiteSpace(picked))
            return;

        UnityEditorPath = picked;
        UnityArtCompiler.SaveUnityExe(picked);
        UnityStatus = DescribeUnityChoice();
    }

    [RelayCommand]
    public async Task CompileArtBundleAsync()
    {
        if (IsArtCompileBusy)
            return;
        if (SelectedOwnedItem == null)
        {
            StatusText = "Select a Project item first.";
            UnityStatus = StatusText;
            AppScreen = AppScreen.Export;
            return;
        }

        var art = _projectStore.LoadArt(SelectedOwnedItem);
        if (!art.IncludeMesh)
        {
            StatusText = "Mesh is unchecked, so it will not be packed. Check it on the Art sheet first.";
            UnityStatus = StatusText;
            return;
        }

        var mesh = ProjectStore.ResolveArtAbsolutePath(SelectedOwnedItem, art.MeshPath);
        if (mesh == null)
        {
            StatusText = "Attach an FBX on the Art sheet before rebuilding art.bundle.";
            UnityStatus = StatusText;
            return;
        }

        if (!EnsureUnityReady(out var unityError))
        {
            StatusText = unityError;
            UnityStatus = unityError;
            AppScreen = AppScreen.Export;
            return;
        }

        IsArtCompileBusy = true;
        AppScreen = AppScreen.Export;
        try
        {
            var result = await CompileItemArtAsync(SelectedOwnedItem, art, force: true);
            UnityStatus = result.Message;
            StatusText = result.Success
                ? $"{SelectedOwnedItem.Id}: {result.Message} Run Export to wire the code project."
                : result.Message;
            RefreshCompiledBundleFlag(SelectedOwnedItem);
            RefreshExportTree();
        }
        catch (Exception ex)
        {
            UnityStatus = $"Compile failed: {ex.Message}";
            StatusText = UnityStatus;
        }
        finally
        {
            IsArtCompileBusy = false;
        }
    }

    private void LoadValheimProjectSetting()
    {
        var saved = _projectStore.TryLoadValheimProjectPath();
        if (!string.IsNullOrWhiteSpace(saved))
        {
            ValheimProjectPath = saved;
            ValheimProjectStatus = ValheimProjectSync.IsWired(saved)
                ? $"Wired to {saved}"
                : $"Path set, but ArtItemLoader.Register was not found in {saved}";
            return;
        }

        var detected = ValheimProjectSync.FindDefaultTestProject();
        if (string.IsNullOrWhiteSpace(detected))
            return;

        ValheimProjectPath = detected;
        _projectStore.SaveValheimProjectPath(detected);
        ValheimProjectStatus = $"Using {detected}";
    }

    [RelayCommand]
    public async Task BrowseValheimProjectAsync()
    {
        if (PickFolderAsync == null)
        {
            ValheimProjectStatus = "Folder picker not available.";
            return;
        }

        var picked = await PickFolderAsync("Select Valheim project folder");
        if (string.IsNullOrWhiteSpace(picked))
            return;

        ValheimProjectPath = picked;
        _projectStore.SaveValheimProjectPath(picked);
        ValheimProjectStatus = ValheimProjectSync.IsWired(picked)
            ? $"Wired to {picked}"
            : "Folder set. It does not call ArtItemLoader.Register yet.";
    }

    [RelayCommand]
    public async Task ExportToModAsync()
    {
        if (string.IsNullOrWhiteSpace(ValheimProjectPath))
        {
            ValheimProjectStatus = "Set a code project folder first.";
            return;
        }

        if (IsArtCompileBusy)
            return;

        PersistExportTreeIncludes();

        if (SelectedOwnedItem != null)
        {
            ApplySpawnName(SelectedOwnedItem);
            SaveWorkingMaterial(SelectedOwnedItem);
            SaveWorkingArt(SelectedOwnedItem);
            _projectStore.SaveItem(SelectedOwnedItem);
        }

        var included = _projectStore.LoadAllItems()
            .Select(i => (Item: i, Art: _projectStore.LoadArt(i)))
            .Where(x => x.Art.IncludeInExport)
            .ToList();

        if (included.Count == 0)
        {
            var empty = ValheimProjectSync.Sync(_projectStore, ValheimProjectPath);
            ValheimProjectStatus = empty.Message;
            StatusText = empty.Message;
            RefreshExportTree();
            return;
        }

        IsArtCompileBusy = true;
        try
        {
            var prepareError = await EnsureArtBundlesForExportAsync(included);
            if (prepareError != null)
            {
                ValheimProjectStatus = prepareError;
                StatusText = prepareError;
                RefreshExportTree();
                return;
            }

            var packError = await EnsureFolderPackBundlesAsync(included);
            if (packError != null)
            {
                ValheimProjectStatus = packError;
                StatusText = packError;
                RefreshExportTree();
                return;
            }

            _projectStore.SaveValheimProjectPath(ValheimProjectPath);
            var result = ValheimProjectSync.Sync(_projectStore, ValheimProjectPath);
            ValheimProjectStatus = result.Message;
            StatusText = result.Message;
            RefreshExportTree();
        }
        finally
        {
            IsArtCompileBusy = false;
        }
    }

    /// <summary>Fire-and-forget export used after renames / art checklist changes.</summary>
    public void SyncValheimProject() => _ = ExportToModAsync();

    /// <summary>
    /// Extract pending imports and compile FBX for included items that lack art.bundle.
    /// Returns an error message if Unity is required but missing, or visual items still lack bundles.
    /// </summary>
    private async Task<string?> EnsureArtBundlesForExportAsync(
        IReadOnlyList<(OwnedItemDocument Item, ArtDocument Art)> included)
    {
        var extractPending = included
            .Where(x =>
                !File.Exists(Path.Combine(x.Item.FolderPath, "art.bundle")) &&
                (x.Art.NeedsBundleExtract || !string.IsNullOrWhiteSpace(x.Art.SourceBundlePath)))
            .ToList();

        var compilePending = included
            .Where(x =>
                !File.Exists(Path.Combine(x.Item.FolderPath, "art.bundle")) &&
                x.Art.IncludeMesh &&
                ProjectStore.ResolveArtAbsolutePath(x.Item, x.Art.MeshPath) != null)
            .ToList();

        if (extractPending.Count > 0 || compilePending.Count > 0)
        {
            if (!EnsureUnityReady(out var unityError))
                return unityError;

            if (extractPending.Count > 0)
            {
                StatusText = $"Extracting {extractPending.Count} imported prefab(s) into art.bundle…";
                var extractResult = await ExtractItemsAsync(extractPending);
                if (extractResult.Fail > 0)
                {
                    return extractResult.Fail == extractPending.Count
                        ? $"Export stopped — Unity extract failed for all {extractResult.Fail} item(s). {extractResult.LastError}"
                        : $"Export stopped — Unity extract failed for {extractResult.Fail} item(s). {extractResult.LastError}";
                }
            }

            if (compilePending.Count > 0)
            {
                StatusText = $"Compiling {compilePending.Count} FBX item(s) into art.bundle…";
                var fail = 0;
                string? lastFail = null;
                foreach (var (item, art) in compilePending)
                {
                    // Re-check — extract may have produced a bundle for a weird edge case.
                    if (File.Exists(Path.Combine(item.FolderPath, "art.bundle")))
                        continue;
                    var result = await CompileItemArtAsync(item, art, force: false);
                    if (!result.Success)
                    {
                        fail++;
                        lastFail = $"{item.Id}: {result.Message}";
                    }
                }

                if (fail > 0)
                    return $"Export stopped — FBX compile failed for {fail} item(s). {lastFail}";
            }
        }

        // Reload art docs after extract/compile mutations.
        var missingVisual = new List<string>();
        foreach (var (item, _) in included)
        {
            var art = _projectStore.LoadArt(item);
            var hasBundle = File.Exists(Path.Combine(item.FolderPath, "art.bundle"));
            if (!hasBundle && ExpectsCustomArtBundle(item, art))
                missingVisual.Add(item.Id);
        }

        if (missingVisual.Count > 0)
        {
            return "Export stopped — these items need art.bundle but still lack one: " +
                   string.Join(", ", missingVisual) +
                   ". Set Unity and fix extract/FBX sources, or uncheck them.";
        }

        return null;
    }

    /// <summary>
    /// For each project group (e.g. keys), merge per-item art.bundles into Items/keys/keys.bundle
    /// with prefabs named after each item id.
    /// </summary>
    private async Task<string?> EnsureFolderPackBundlesAsync(
        IReadOnlyList<(OwnedItemDocument Item, ArtDocument Art)> included)
    {
        var groups = included
            .GroupBy(x => ValheimProjectSync.TopGroupName(x.Item), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        if (groups.Count == 0)
            return null;

        foreach (var group in groups)
        {
            var groupName = group.Key;
            var entries = group
                .Select(x => (
                    Item: x.Item,
                    Art: x.Art,
                    ArtBundle: Path.Combine(x.Item.FolderPath, "art.bundle")))
                .Where(x => File.Exists(x.ArtBundle))
                .Select(x => (
                    x.ArtBundle,
                    PrefabName: x.Item.Id,
                    DiffusePath: x.Art.IncludeDiffuse
                        ? ProjectStore.ResolveArtAbsolutePath(x.Item, x.Art.DiffusePath)
                        : null))
                .ToList();

            if (entries.Count == 0)
                continue;

            if (!EnsureUnityReady(out var unityError))
                return unityError;

            var output = ValheimProjectSync.FolderBundlePath(_projectStore, groupName);
            StatusText = $"Packing {groupName}.bundle ({entries.Count} prefab(s))…";
            var progress = new Progress<string>(msg =>
            {
                UnityStatus = msg;
                StatusText = msg;
            });
            var result = await UnityArtCompiler.PackFolderAsync(
                UnityEditorPath,
                UnityArtCompiler.FindTemplateSource()!,
                new UnityArtPackFolderRequest
                {
                    OutputBundlePath = output,
                    BundleName = groupName,
                    Entries = entries,
                },
                progress);

            if (!result.Success)
                return $"Export stopped — could not pack {groupName}.bundle. {result.Message}";
        }

        return null;
    }

    private static bool ExpectsCustomArtBundle(OwnedItemDocument item, ArtDocument art)
    {
        if (art.NeedsBundleExtract)
            return true;
        if (!string.IsNullOrWhiteSpace(art.SourceBundlePath))
            return true;
        if (art.IncludeMesh && ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath) != null)
            return true;
        return false;
    }

    private bool EnsureUnityReady(out string error)
    {
        if (string.IsNullOrWhiteSpace(UnityEditorPath) || !File.Exists(UnityEditorPath))
            RefreshUnityDetection();

        if (string.IsNullOrWhiteSpace(UnityEditorPath) || !File.Exists(UnityEditorPath))
        {
            error = "Set Unity.exe on the Export screen — Export needs it to make art.bundles (extract or FBX compile).";
            return false;
        }

        if (UnityArtCompiler.FindTemplateSource() == null)
        {
            error = "UnityTemplate folder is missing next to the app.";
            return false;
        }

        error = "";
        return true;
    }

    private async Task<UnityArtCompileResult> CompileItemArtAsync(
        OwnedItemDocument item,
        ArtDocument art,
        bool force)
    {
        var mesh = ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath);
        if (mesh == null)
            return new UnityArtCompileResult { Success = false, Message = "No FBX attached." };

        if (!force && File.Exists(Path.Combine(item.FolderPath, "art.bundle")))
            return new UnityArtCompileResult { Success = true, Message = "art.bundle already present." };

        var template = UnityArtCompiler.FindTemplateSource()!;
        UnityArtCompiler.SaveUnityExe(UnityEditorPath);
        var progress = new Progress<string>(msg =>
        {
            UnityStatus = $"{item.Id}: {msg}";
            StatusText = UnityStatus;
        });
        var result = await UnityArtCompiler.CompileAsync(
            UnityEditorPath,
            template,
            new UnityArtCompileRequest
            {
                MeshPath = mesh,
                DiffusePath = art.IncludeDiffuse
                    ? ProjectStore.ResolveArtAbsolutePath(item, art.DiffusePath)
                    : null,
                OutputBundlePath = Path.Combine(item.FolderPath, "art.bundle"),
                BundleName = item.Id,
            },
            progress);

        if (result.Success)
        {
            art.IncludeMesh = true;
            _projectStore.SaveArt(item, art);
            RefreshCompiledBundleFlag(item);
        }

        return result;
    }
    public void RefreshExportTree()
    {
        foreach (var root in ExportTree.ToList())
        {
            foreach (var node in root.EnumerateAll())
                node.PropertyChanged -= OnExportTreeNodePropertyChanged;
        }

        ExportTree.Clear();
        var items = _projectStore.LoadAllItems();
        var folders = new Dictionary<string, ExportTreeNode>(StringComparer.OrdinalIgnoreCase);

        ExportTreeNode EnsureFolder(string groupPath)
        {
            if (folders.TryGetValue(groupPath, out var existing))
                return existing;
            var name = groupPath.Contains('/')
                ? groupPath[(groupPath.LastIndexOf('/') + 1)..]
                : groupPath;
            var folder = new ExportTreeNode(name, groupPath);
            folder.PropertyChanged += OnExportTreeNodePropertyChanged;
            folders[groupPath] = folder;
            ExportTree.Add(folder);
            return folder;
        }

        foreach (var group in _projectStore.ListGroups().OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
            EnsureFolder(group);

        foreach (var item in items.OrderBy(i => i.GroupPath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var art = _projectStore.LoadArt(item);
            var status = BuildExportStatus(item, art);
            var leaf = new ExportTreeNode(
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName,
                item.GroupPath,
                item,
                status)
            {
                IsChecked = art.IncludeInExport,
            };
            leaf.PropertyChanged += OnExportTreeNodePropertyChanged;

            if (string.IsNullOrWhiteSpace(item.GroupPath))
                ExportTree.Add(leaf);
            else
                EnsureFolder(item.GroupPath).AddChild(leaf);
        }

        // Folders first, then root items — Explorer-like.
        var ordered = ExportTree
            .OrderByDescending(n => n.IsFolder)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ExportTree.Clear();
        foreach (var node in ordered)
            ExportTree.Add(node);

        foreach (var folder in ExportTree.Where(n => n.IsFolder))
            folder.RefreshFolderCheckFromChildren();

        OnPropertyChanged(nameof(ExportIncludeSummary));
    }

    private string BuildExportStatus(OwnedItemDocument item, ArtDocument art)
    {
        var parts = new List<string>();
        if (File.Exists(Path.Combine(item.FolderPath, "art.bundle")))
            parts.Add("has art.bundle");
        else if (art.NeedsBundleExtract)
        {
            var src = _projectStore.ResolveSourceBundlePath(art.SourceBundlePath, repairImports: false);
            parts.Add(src == null
                ? $"missing source {art.SourceBundlePath}"
                : $"Export will extract {art.SourcePrefabName} → art.bundle");
        }
        else if (art.IncludeMesh && ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath) != null)
            parts.Add("Export will compile FBX → art.bundle");
        else
            parts.Add("no custom art (hooks only)");
        if (art.IncludeIcon && ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) != null)
            parts.Add("icon");
        return string.Join(" · ", parts);
    }

    private void OnExportTreeNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportTreeNode.IsChecked) || sender is not ExportTreeNode node)
            return;
        if (node.IsSuppressingCascade)
            return;

        if (node.ShouldCascadeCheck && node.IsChecked is { })
        {
            foreach (var itemNode in node.EnumerateItems())
                PersistItemExportInclude(itemNode.Item!, itemNode.IsChecked == true);
            node.RefreshSubtitle();
            OnPropertyChanged(nameof(ExportIncludeSummary));
            return;
        }

        if (node.IsItem && node.Item != null)
        {
            PersistItemExportInclude(node.Item, node.IsChecked == true);
            node.Parent?.RefreshFolderCheckFromChildren();
            OnPropertyChanged(nameof(ExportIncludeSummary));
        }
    }

    private void PersistItemExportInclude(OwnedItemDocument item, bool include)
    {
        var art = _projectStore.LoadArt(item);
        if (art.IncludeInExport == include)
            return;
        art.IncludeInExport = include;
        _projectStore.SaveArt(item, art);
        item.IncludeInExport = include;
        FindTreeNodeByItemId(item.Id)?.RefreshSubtitle();
    }

    private void PersistExportTreeIncludes()
    {
        foreach (var node in ExportTree.SelectMany(n => n.EnumerateItems()))
        {
            if (node.Item != null)
                PersistItemExportInclude(node.Item, node.IsChecked == true);
        }
    }

    [RelayCommand]
    public void ExportSelectAll()
    {
        foreach (var node in ExportTree.ToList())
            node.ApplyCheckCascade(true);
        PersistExportTreeIncludes();
        OnPropertyChanged(nameof(ExportIncludeSummary));
        StatusText = "All folders/items included in export.";
    }

    [RelayCommand]
    public void ExportSelectNone()
    {
        foreach (var node in ExportTree.ToList())
            node.ApplyCheckCascade(false);
        PersistExportTreeIncludes();
        OnPropertyChanged(nameof(ExportIncludeSummary));
        StatusText = "No items included in export.";
    }

    [RelayCommand]
    public void ExportIncludeProjectSelectionOnly()
    {
        var selectedIds = new HashSet<string>(
            GetSelectedOwnedItems().Select(i => i.Id),
            StringComparer.OrdinalIgnoreCase);
        if (selectedIds.Count == 0)
        {
            StatusText = "Select item(s) on the Project screen first (Ctrl/Shift), then use this.";
            return;
        }

        foreach (var node in ExportTree.SelectMany(n => n.EnumerateItems()))
            node.IsChecked = selectedIds.Contains(node.Item!.Id);
        foreach (var folder in ExportTree.Where(n => n.IsFolder))
            folder.RefreshFolderCheckFromChildren();
        PersistExportTreeIncludes();
        OnPropertyChanged(nameof(ExportIncludeSummary));
        StatusText = $"Export set to {selectedIds.Count} Project-selected item(s).";
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
