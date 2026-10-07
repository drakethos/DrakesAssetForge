using AssetsTools.NET.Extra;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public static class BundleAssetLister
{
    public static IReadOnlyList<BundleObjectItem> ListObjects(
        string bundlePath,
        int maxObjects = 4000,
        string? nameFilter = null)
    {
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("Bundle not found.", bundlePath);

        var filter = nameFilter?.Trim() ?? string.Empty;
        var results = new List<BundleObjectItem>();
        var am = new AssetsManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath, true);
            var names = bun.file.GetAllFileNames();
            if (names.Count == 0)
                return results;

            var assetsIndex = 0;
            for (var i = 0; i < names.Count; i++)
            {
                if (!names[i].EndsWith(".resS", StringComparison.OrdinalIgnoreCase) &&
                    !names[i].EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
                {
                    assetsIndex = i;
                    break;
                }
            }

            var afileInst = am.LoadAssetsFileFromBundle(bun, assetsIndex, false);

            if (TryListContainer(am, afileInst, results, maxObjects, filter))
                return Sort(results);

            foreach (var info in afileInst.file.AssetInfos)
            {
                if (results.Count >= maxObjects)
                    break;

                string name;
                try
                {
                    var bf = am.GetBaseField(afileInst, info);
                    name = bf["m_Name"].IsDummy ? string.Empty : bf["m_Name"].AsString;
                }
                catch
                {
                    name = string.Empty;
                }

                if (!string.IsNullOrEmpty(filter) &&
                    !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new BundleObjectItem
                {
                    PathId = info.PathId,
                    TypeId = info.TypeId,
                    TypeName = TypeName(info.TypeId),
                    Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name,
                });
            }
        }
        finally
        {
            am.UnloadAll();
        }

        return Sort(results);
    }

    public static BundleObjectItem? FindContainerEntry(string bundlePath, string pathInBundle)
    {
        if (!File.Exists(bundlePath))
            return null;

        var needle = pathInBundle.Replace('\\', '/');
        var fileName = Path.GetFileName(needle);
        var am = new AssetsManager();
        try
        {
            var bun = am.LoadBundleFile(bundlePath, true);
            if (bun.file.GetAllFileNames().Count == 0)
                return null;

            var afileInst = am.LoadAssetsFileFromBundle(bun, 0, false);
            var abInfos = afileInst.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (abInfos.Count == 0)
                return null;

            var bf = am.GetBaseField(afileInst, abInfos[0]);
            foreach (var item in bf["m_Container.Array"].Children)
            {
                var name = item[0].AsString.Replace('\\', '/');
                if (name.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(name).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return new BundleObjectItem
                    {
                        PathId = item[1]["asset.m_PathID"].AsLong,
                        TypeId = 0,
                        TypeName = ContainerTypeName(Path.GetExtension(name)),
                        Name = name,
                    };
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            am.UnloadAll();
        }

        return null;
    }

    /// <summary>
    /// PathInBundle (normalized) → PathId from AssetBundle.m_Container, for an already-open bundle.
    /// </summary>
    public static Dictionary<string, long> BuildContainerMap(
        AssetsManager am,
        AssetsFileInstance afileInst)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var abInfos = afileInst.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (abInfos.Count == 0)
                return map;

            var bf = am.GetBaseField(afileInst, abInfos[0]);
            foreach (var item in bf["m_Container.Array"].Children)
            {
                var name = item[0].AsString.Replace('\\', '/');
                var pathId = item[1]["asset.m_PathID"].AsLong;
                if (string.IsNullOrWhiteSpace(name) || pathId == 0)
                    continue;
                map[name] = pathId;
            }
        }
        catch
        {
            // empty map
        }

        return map;
    }

    private static bool TryListContainer(
        AssetsManager am,
        AssetsFileInstance afileInst,
        List<BundleObjectItem> results,
        int maxObjects,
        string filter)
    {
        try
        {
            var abInfos = afileInst.file.GetAssetsOfType(AssetClassID.AssetBundle);
            if (abInfos.Count == 0)
                return false;

            var bf = am.GetBaseField(afileInst, abInfos[0]);
            var container = bf["m_Container.Array"];
            if (container.Children.Count == 0)
                return false;

            foreach (var item in container.Children)
            {
                var name = item[0].AsString;
                if (!string.IsNullOrEmpty(filter) &&
                    !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new BundleObjectItem
                {
                    PathId = item[1]["asset.m_PathID"].AsLong,
                    TypeId = 0,
                    TypeName = ContainerTypeName(Path.GetExtension(name)),
                    Name = name,
                });

                if (string.IsNullOrEmpty(filter) && results.Count >= maxObjects)
                    break;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static List<BundleObjectItem> Sort(List<BundleObjectItem> results) =>
        results
            .OrderBy(o => o.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ContainerTypeName(string ext) => ext.ToLowerInvariant() switch
    {
        ".prefab" => "Prefab",
        ".png" or ".tga" or ".jpg" => "Texture",
        ".mat" => "Material",
        ".shader" => "Shader",
        ".wav" or ".mp3" => "Audio",
        ".asset" => "Asset",
        ".fbx" or ".obj" => "MeshSrc",
        _ => string.IsNullOrEmpty(ext) ? "Container" : ext.TrimStart('.'),
    };

    private static string TypeName(int typeId) => typeId switch
    {
        1 => "GameObject",
        4 => "Transform",
        21 => "Material",
        23 => "MeshRenderer",
        28 => "Texture2D",
        33 => "MeshFilter",
        43 => "Mesh",
        48 => "Shader",
        49 => "TextAsset",
        83 => "AudioClip",
        114 => "MonoBehaviour",
        115 => "MonoScript",
        213 => "Sprite",
        _ => $"Type_{typeId}",
    };
}
