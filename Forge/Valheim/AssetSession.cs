using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace DrakesForge.Valheim;

/// <summary>
/// Owns one AssetsTools manager over the SoftRef bundles. A prefab's bundle is opened together with
/// its manifest dependencies so cross-bundle references (meshes, materials, textures, scripts) resolve.
/// Not thread-safe by itself: callers go through <see cref="Run{T}"/>.
/// </summary>
public sealed class AssetSession : IDisposable
{
    // Opened bundles stay mapped; past this many we drop everything and start over.
    private const int MaxOpenBundles = 120;

    private readonly object _gate = new();
    private readonly AssetsManager _am = new();
    private readonly Dictionary<string, AssetsFileInstance> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, long>> _containers = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lazy<CabIndex> _cabIndex;

    public AssetSession(VanillaCatalog catalog)
    {
        Catalog = catalog;
        _cabIndex = new Lazy<CabIndex>(() => CabIndex.LoadOrBuild(catalog.Install));
        if (Directory.Exists(catalog.Install.ManagedDirectory))
            _am.MonoTempGenerator = new MonoCecilTempGenerator(catalog.Install.ManagedDirectory);
    }

    public VanillaCatalog Catalog { get; }
    internal AssetsManager Manager => _am;

    private ComponentReader? _components;
    internal ComponentReader Components => _components ??= new ComponentReader(Catalog.Install.ManagedDirectory);

    // Decoded sprite atlases: hundreds of icons crop from a handful of big sheets.
    private const int MaxAtlases = 4;
    private readonly LinkedList<((string, long) Key, RgbaImage Image)> _atlases = new();

    internal RgbaImage? CachedAtlas((string, long) key)
    {
        for (var node = _atlases.First; node != null; node = node.Next)
        {
            if (node.Value.Key != key)
                continue;
            _atlases.Remove(node);
            _atlases.AddFirst(node);
            return node.Value.Image;
        }

        return null;
    }

    internal void CacheAtlas((string, long) key, RgbaImage image)
    {
        _atlases.AddFirst((key, image));
        while (_atlases.Count > MaxAtlases)
            _atlases.RemoveLast();
    }

    public T Run<T>(Func<AssetSession, T> work)
    {
        lock (_gate)
            return work(this);
    }

    /// <summary>The prefab's root GameObject, with its bundle and dependencies opened.</summary>
    internal (AssetsFileInstance File, AssetFileInfo Info) OpenPrefab(VanillaEntry entry)
    {
        if (_files.Count > MaxOpenBundles)
            Reset();

        foreach (var dep in Catalog.DependenciesOf(entry.BundleId))
            Open(dep);
        var file = Open(entry.BundleId);

        if (!Container(entry.BundleId, file).TryGetValue(entry.PathInBundle, out var pathId))
            throw new InvalidDataException($"'{entry.PathInBundle}' is not in bundle {entry.BundleId}.");
        var info = file.file.GetAssetInfo(pathId) ?? throw new InvalidDataException($"PathId {pathId} missing in {entry.BundleId}.");
        return (file, info);
    }

    /// <summary>
    /// GetExtAsset that first opens the bundle a cross-file pointer lives in. The manifest's dependency
    /// lists miss some (e.g. MonoScripts), so externals are resolved through the CAB → bundle index.
    /// </summary>
    internal AssetExternal Ext(AssetsFileInstance file, AssetTypeValueField ptr, bool onlyInfo = false)
    {
        var fileId = ptr["m_FileID"].AsInt;
        if (fileId > 0 && fileId <= file.file.Metadata.Externals.Count)
        {
            var external = file.file.Metadata.Externals[fileId - 1].PathName;
            if (_cabIndex.Value.BundleFor(external) is { } bundleId)
                Open(bundleId);
        }

        return _am.GetExtAsset(file, ptr, onlyInfo);
    }

    internal AssetsFileInstance Open(string bundleId)
    {
        if (_files.TryGetValue(bundleId, out var cached))
            return cached;

        var bundle = _am.LoadBundleFile(Catalog.BundlePath(bundleId), true);
        var names = bundle.file.GetAllFileNames();
        var index = names.FindIndex(n => !n.EndsWith(".resS", StringComparison.OrdinalIgnoreCase) && !n.EndsWith(".resource", StringComparison.OrdinalIgnoreCase));
        var file = _am.LoadAssetsFileFromBundle(bundle, Math.Max(index, 0), false);
        _files[bundleId] = file;
        return file;
    }

    private Dictionary<string, long> Container(string bundleId, AssetsFileInstance file)
    {
        if (_containers.TryGetValue(bundleId, out var map))
            return map;

        map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var ab in file.file.GetAssetsOfType(AssetClassID.AssetBundle))
            foreach (var item in _am.GetBaseField(file, ab)["m_Container.Array"].Children)
                map.TryAdd(item[0].AsString, item[1]["asset.m_PathID"].AsLong);
        _containers[bundleId] = map;
        return map;
    }

    private void Reset()
    {
        _am.UnloadAll(true);
        _files.Clear();
        _containers.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
            Reset();
    }
}
