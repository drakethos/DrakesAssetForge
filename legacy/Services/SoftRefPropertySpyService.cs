using AssetsTools.NET;
using AssetsTools.NET.Extra;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

/// <summary>
/// Generic SoftRef property spy: Valheim MonoBehaviours on prefabs + ScriptableObject assets.
/// Skips Unity engine components; names scripts via assembly_valheim field fingerprints.
/// </summary>
public sealed class SoftRefPropertySpyService : IDisposable
{
    private const int MaxFlattenDepth = 5;
    private const int MaxArrayElements = 24;
    private const int MaxFieldsPerScript = 800;

    private readonly string _managedDirectory;
    private readonly ValheimScriptNameResolver _names;
    private AssetsManager? _am;
    private string? _openBundlePath;
    private AssetsFileInstance? _afile;

    public SoftRefPropertySpyService(string managedDirectory)
    {
        _managedDirectory = managedDirectory;
        _names = new ValheimScriptNameResolver(managedDirectory);
    }

    public PrefabPropertySpyResult SpyPrefab(string bundlePath, string pathInBundle)
    {
        try
        {
            var (am, afile) = OpenBundle(bundlePath);
            var container = BundleAssetLister.FindContainerEntry(bundlePath, pathInBundle);
            if (container == null)
                return FailPrefab($"Container entry not found for '{pathInBundle}'.");

            var info = afile.file.GetAssetInfo(container.PathId);
            if (info == null)
                return FailPrefab($"PathId {container.PathId} missing.");

            if ((AssetClassID)info.TypeId != AssetClassID.GameObject)
                return FailPrefab($"Expected GameObject, got {(AssetClassID)info.TypeId}.");

            var goBf = am.GetBaseField(afile, info);
            var prefabName = ReadString(goBf, "m_Name") ?? "(unnamed)";
            var scripts = new List<SpyScriptComponent>();

            var comps = goBf["m_Component"]["Array"];
            if (!comps.IsDummy)
            {
                foreach (var c in comps.Children)
                {
                    var ext = am.GetExtAsset(afile, c["component"]);
                    if (ext.info == null || (AssetClassID)ext.info.TypeId != AssetClassID.MonoBehaviour)
                        continue;

                    var mb = ext.baseField;
                    var customNames = mb.Children
                        .Select(f => f.FieldName)
                        .Where(n => n is not ("m_GameObject" or "m_Enabled" or "m_Script" or "m_Name"))
                        .ToList();

                    if (customNames.Count == 0)
                        continue;

                    var className = _names.ResolveMonoBehaviourName(customNames);
                    if (className == null)
                        continue; // not a Valheim script fingerprint

                    var fields = new List<FieldRow>();
                    FlattenObject(am, afile, mb, className, 0, fields);

                    scripts.Add(new SpyScriptComponent
                    {
                        ClassName = className,
                        PathId = ext.info.PathId.ToString(),
                        Fields = fields,
                        IsMatchedValheimScript = true,
                    });
                }
            }

            return new PrefabPropertySpyResult
            {
                PrefabName = prefabName,
                Scripts = scripts,
            };
        }
        catch (Exception ex)
        {
            return FailPrefab(ex.Message);
        }
    }

    public AssetPropertySpyResult SpyScriptableAsset(string bundlePath, string pathInBundle)
    {
        try
        {
            var (am, afile) = OpenBundle(bundlePath);
            var container = BundleAssetLister.FindContainerEntry(bundlePath, pathInBundle);
            if (container == null)
                return FailAsset($"Container entry not found for '{pathInBundle}'.");

            var info = afile.file.GetAssetInfo(container.PathId);
            if (info == null)
                return FailAsset($"PathId {container.PathId} missing.");

            var bf = am.GetBaseField(afile, info);
            var assetName = ReadString(bf, "m_Name") ?? Path.GetFileNameWithoutExtension(pathInBundle);
            var customNames = bf.Children
                .Select(f => f.FieldName)
                .Where(n => n is not ("m_GameObject" or "m_Enabled" or "m_Script" or "m_Name"))
                .ToList();

            var className = _names.ResolveScriptableObjectName(customNames)
                            ?? _names.ResolveMonoBehaviourName(customNames)
                            ?? "ScriptableObject";

            var fields = new List<FieldRow>
            {
                new() { Path = "m_Name", Value = assetName },
            };
            FlattenObject(am, afile, bf, className, 0, fields);

            return new AssetPropertySpyResult
            {
                AssetName = assetName,
                SoftRefPath = pathInBundle,
                ClassName = className,
                Fields = fields,
            };
        }
        catch (Exception ex)
        {
            return FailAsset(ex.Message);
        }
    }

    public static SoftRefAssetEntry? FindRecipeForItem(
        SoftRefAssetEntry item,
        IReadOnlyList<SoftRefAssetEntry> allAssets)
    {
        var name = item.DisplayName;
        var exact = allAssets.FirstOrDefault(a =>
            a.Kind == CatalogKind.Recipe &&
            a.DisplayName.Equals($"Recipe_{name}", StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        return allAssets.FirstOrDefault(a =>
            a.Kind == CatalogKind.Recipe &&
            (a.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
             a.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)));
    }

    public IReadOnlyList<RecipeRequirementRow> ReadRecipeRequirements(
        string bundlePath,
        string pathInBundle)
    {
        var (am, afile) = OpenBundle(bundlePath);
        var container = BundleAssetLister.FindContainerEntry(bundlePath, pathInBundle);
        if (container == null)
            return Array.Empty<RecipeRequirementRow>();

        var info = afile.file.GetAssetInfo(container.PathId);
        if (info == null)
            return Array.Empty<RecipeRequirementRow>();

        var bf = am.GetBaseField(afile, info);
        var requirements = new List<RecipeRequirementRow>();
        var arr = bf["m_resources"]["Array"];
        if (arr.IsDummy)
            return requirements;

        foreach (var el in arr.Children)
        {
            var (goName, token) = ReadItemDropNames(am, afile, el["m_resItem"]);
            requirements.Add(new RecipeRequirementRow
            {
                ItemName = string.IsNullOrEmpty(goName) ? "(item)" : goName,
                Token = token,
                Amount = TryReadInt(el, "m_amount"),
                AmountPerLevel = TryReadInt(el, "m_amountPerLevel"),
            });
        }

        return requirements;
    }

    public void Dispose()
    {
        CloseBundle();
        _names.Dispose();
    }

    private void FlattenObject(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField root,
        string rootName,
        int depth,
        List<FieldRow> output)
    {
        foreach (var child in root.Children)
        {
            if (child.FieldName is "m_GameObject" or "m_Enabled" or "m_Script")
                continue;
            if (rootName.Length > 0 && child.FieldName == "m_Name" && depth == 0)
                continue;

            FlattenField(am, afile, child, $"{rootName}.{child.FieldName}", depth + 1, output);
            if (output.Count >= MaxFieldsPerScript)
                return;
        }
    }

    private void FlattenField(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField field,
        string path,
        int depth,
        List<FieldRow> output)
    {
        if (output.Count >= MaxFieldsPerScript)
            return;

        if (IsPPtr(field))
        {
            output.Add(new FieldRow { Path = path, Value = DescribePPtr(am, afile, field) });
            return;
        }

        if (field.Children.Count == 0)
        {
            output.Add(new FieldRow { Path = path, Value = FormatScalar(field) });
            return;
        }

        if (field.TypeName.Contains("Array", StringComparison.OrdinalIgnoreCase) ||
            field.FieldName == "Array")
        {
            var arr = field.FieldName == "Array" ? field : field["Array"];
            if (arr.IsDummy)
                arr = field;

            var count = arr.Children.Count;
            output.Add(new FieldRow { Path = path, Value = $"[{count}]" });

            var limit = Math.Min(count, MaxArrayElements);
            for (var i = 0; i < limit; i++)
            {
                FlattenField(am, afile, arr.Children[i], $"{path}[{i}]", depth + 1, output);
                if (output.Count >= MaxFieldsPerScript)
                    return;
            }

            if (count > limit)
                output.Add(new FieldRow { Path = $"{path}…", Value = $"+{count - limit} more" });
            return;
        }

        if (depth >= MaxFlattenDepth)
        {
            output.Add(new FieldRow { Path = path, Value = $"{field.TypeName} ({field.Children.Count} fields)" });
            return;
        }

        // Prefer dumping meaningful nested scalars; skip empty padding objects.
        foreach (var child in field.Children)
        {
            FlattenField(am, afile, child, $"{path}.{child.FieldName}", depth + 1, output);
            if (output.Count >= MaxFieldsPerScript)
                return;
        }
    }

    private (AssetsManager am, AssetsFileInstance afile) OpenBundle(string bundlePath)
    {
        if (_am != null &&
            _afile != null &&
            string.Equals(_openBundlePath, bundlePath, StringComparison.OrdinalIgnoreCase))
        {
            return (_am, _afile);
        }

        CloseBundle();

        var am = new AssetsManager
        {
            MonoTempGenerator = new MonoCecilTempGenerator(_managedDirectory),
        };
        var bun = am.LoadBundleFile(bundlePath, true);
        var names = bun.file.GetAllFileNames();
        if (names.Count == 0)
            throw new InvalidOperationException("Bundle has no files.");

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

        var afile = am.LoadAssetsFileFromBundle(bun, assetsIndex, false);
        _am = am;
        _afile = afile;
        _openBundlePath = bundlePath;
        return (am, afile);
    }

    private void CloseBundle()
    {
        _am?.UnloadAll();
        _am = null;
        _afile = null;
        _openBundlePath = null;
    }

    private static bool IsPPtr(AssetTypeValueField field)
    {
        if (field.TypeName.StartsWith("PPtr", StringComparison.OrdinalIgnoreCase))
            return true;
        return field.Children.Count == 2 &&
               field.Children.Any(c => c.FieldName == "m_FileID") &&
               field.Children.Any(c => c.FieldName == "m_PathID");
    }

    private static string DescribePPtr(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField ptr)
    {
        try
        {
            if (ptr.IsDummy || ptr["m_PathID"].AsLong == 0)
                return "(null)";

            var (goName, token) = ReadItemDropNames(am, afile, ptr);
            if (!string.IsNullOrEmpty(goName) || !string.IsNullOrEmpty(token))
            {
                if (!string.IsNullOrEmpty(goName) && !string.IsNullOrEmpty(token) && goName != token)
                    return $"{goName} ({token})";
                return string.IsNullOrEmpty(goName) ? token : goName;
            }

            var ext = am.GetExtAsset(afile, ptr);
            if (ext.info == null)
                return $"PathId {ptr["m_PathID"].AsLong}";

            var name = ReadString(ext.baseField, "m_Name");
            if (!string.IsNullOrEmpty(name))
                return name;

            try
            {
                var go = am.GetExtAsset(afile, ext.baseField["m_GameObject"]);
                var goN = ReadString(go.baseField, "m_Name");
                if (!string.IsNullOrEmpty(goN))
                    return goN;
            }
            catch
            {
                // ignore
            }

            return $"{(AssetClassID)ext.info.TypeId} #{ptr["m_PathID"].AsLong}";
        }
        catch (Exception ex)
        {
            return $"unresolved ({ex.Message})";
        }
    }

    private static (string GoName, string Token) ReadItemDropNames(
        AssetsManager am,
        AssetsFileInstance afile,
        AssetTypeValueField ptr)
    {
        try
        {
            if (ptr.IsDummy || ptr["m_PathID"].AsLong == 0)
                return ("", "");

            var ext = am.GetExtAsset(afile, ptr);
            if (ext.info == null || ext.baseField == null)
                return ("", "");

            var token = "";
            try
            {
                var shared = ext.baseField["m_itemData"]["m_shared"];
                if (!shared.IsDummy)
                    token = ReadString(shared, "m_name") ?? "";
            }
            catch
            {
                // not ItemDrop
            }

            var goName = "";
            try
            {
                var go = am.GetExtAsset(afile, ext.baseField["m_GameObject"]);
                goName = ReadString(go.baseField, "m_Name") ?? "";
            }
            catch
            {
                // ignore
            }

            if (string.IsNullOrEmpty(goName))
                goName = ReadString(ext.baseField, "m_Name") ?? "";

            return (goName, token);
        }
        catch
        {
            return ("", "");
        }
    }

    private static string FormatScalar(AssetTypeValueField f)
    {
        if (f.IsDummy)
            return "?";
        try
        {
            return f.TypeName switch
            {
                "float" or "double" => FormatFloat(f.AsFloat),
                "bool" or "UInt8" => f.AsInt != 0 ? "true" : "false",
                _ => f.AsString,
            };
        }
        catch
        {
            try { return f.AsString; }
            catch { return "?"; }
        }
    }

    private static string FormatFloat(float v) =>
        Math.Abs(v % 1f) < 0.0001f ? ((int)v).ToString() : v.ToString("0.###");

    private static string? ReadString(AssetTypeValueField bf, string name)
    {
        try
        {
            var f = bf[name];
            return f.IsDummy ? null : f.AsString;
        }
        catch
        {
            return null;
        }
    }

    private static int TryReadInt(AssetTypeValueField bf, string name)
    {
        try
        {
            var f = bf[name];
            return f.IsDummy ? 0 : f.AsInt;
        }
        catch
        {
            return 0;
        }
    }

    private static PrefabPropertySpyResult FailPrefab(string error) => new() { Error = error };
    private static AssetPropertySpyResult FailAsset(string error) => new() { Error = error };
}
