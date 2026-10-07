using System.Text.Json;
using System.Text.Json.Serialization;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class ShaderCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string CachePath { get; }

    public ShaderCatalogService(string? cacheDirectory = null)
    {
        var root = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrakeAssetForge",
            "cache");
        Directory.CreateDirectory(root);
        CachePath = Path.Combine(root, "shaders.json");
    }

    public ShaderCatalogDocument? TryLoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;
            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<ShaderCatalogDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void SaveCache(ShaderCatalogDocument doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
        File.WriteAllText(CachePath, JsonSerializer.Serialize(doc, JsonOptions));
    }

    public ShaderCatalogDocument BuildFromSoftRef(
        string valheimPath,
        SoftRefManifestReader.SoftRefIndex index,
        IProgress<string>? progress = null)
    {
        var managed = ValheimPathFinder.GetManagedDirectory(valheimPath)
                      ?? throw new InvalidOperationException("valheim_Data/Managed not found.");

        var shaders = index.Assets
            .Where(a => a.Kind == CatalogKind.Shader)
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var am = new AssetsManager
        {
            MonoTempGenerator = new MonoCecilTempGenerator(managed),
        };

        var entries = new List<ShaderCatalogEntry>();
        try
        {
            var groups = shaders.GroupBy(s => s.BundleId, StringComparer.OrdinalIgnoreCase).ToList();
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                progress?.Report($"Shader bundle {g + 1}/{groups.Count} ({group.Count()} shaders)…");
                try
                {
                    var bundlePath = Path.Combine(index.BundlesDirectory, group.Key);
                    if (!File.Exists(bundlePath))
                        continue;

                    var bun = am.LoadBundleFile(bundlePath, true);
                    var afile = OpenFirstAssetsFile(am, bun);
                    if (afile == null)
                        continue;

                    var containers = BundleAssetLister.BuildContainerMap(am, afile);
                    foreach (var asset in group)
                    {
                        if (!containers.TryGetValue(NormalizePath(asset.PathInBundle), out var pathId))
                            continue;
                        try
                        {
                            var info = afile.file.GetAssetInfo(pathId);
                            if (info == null)
                                continue;
                            var bf = am.GetBaseField(afile, info);
                            var entry = ParseShaderField(bf, asset);
                            if (entry != null)
                                entries.Add(entry);
                        }
                        catch
                        {
                            // skip one
                        }
                    }
                }
                catch
                {
                    // skip bundle
                }
                finally
                {
                    am.UnloadAll();
                }
            }
        }
        finally
        {
            am.UnloadAll();
        }

        var doc = new ShaderCatalogDocument
        {
            BuiltUtc = DateTimeOffset.UtcNow,
            ValheimPath = valheimPath,
            Shaders = entries
                .GroupBy(e => e.FindName, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(e => e.Properties.Count).First())
                .OrderBy(e => e.FindName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
        SaveCache(doc);
        return doc;
    }

    public MaterialDocument CreateMaterialFromShader(ShaderCatalogEntry shader, MaterialAuthoringMode mode)
    {
        return new MaterialDocument
        {
            Mode = mode,
            ShaderName = shader.FindName,
            ModifiedUtc = DateTimeOffset.UtcNow,
            Properties = shader.Properties.Select(p => new MaterialPropertyValue
            {
                Name = p.Name,
                Description = p.Description,
                Kind = p.Kind,
                Value = DefaultValue(p.Kind),
                TextureRef = "",
            }).ToList(),
        };
    }

    public MaterialDocument? TrySeedFromSoftRefMaterial(
        string valheimPath,
        SoftRefManifestReader.SoftRefIndex index,
        SoftRefAssetEntry materialAsset,
        IReadOnlyList<ShaderCatalogEntry> catalog)
    {
        var managed = ValheimPathFinder.GetManagedDirectory(valheimPath);
        if (managed == null)
            return null;

        var am = new AssetsManager
        {
            MonoTempGenerator = new MonoCecilTempGenerator(managed),
        };

        try
        {
            var bundlePath = Path.Combine(index.BundlesDirectory, materialAsset.BundleId);
            var container = BundleAssetLister.FindContainerEntry(bundlePath, materialAsset.PathInBundle);
            if (container == null)
                return null;

            var bun = am.LoadBundleFile(bundlePath, true);
            var afile = OpenFirstAssetsFile(am, bun);
            if (afile == null)
                return null;

            var bf = am.GetBaseField(afile, afile.file.GetAssetInfo(container.PathId)!);
            var shaderName = "";
            try
            {
                var shaderExt = am.GetExtAsset(afile, bf["m_Shader"]);
                shaderName = shaderExt.baseField["m_ParsedForm"]["m_Name"].AsString;
            }
            catch
            {
                // leave empty
            }

            var schema = catalog.FirstOrDefault(c =>
                c.FindName.Equals(shaderName, StringComparison.OrdinalIgnoreCase));
            var doc = schema != null
                ? CreateMaterialFromShader(schema, MaterialAuthoringMode.Custom)
                : new MaterialDocument
                {
                    Mode = MaterialAuthoringMode.Custom,
                    ShaderName = shaderName,
                    ModifiedUtc = DateTimeOffset.UtcNow,
                };

            doc.SeededFromSoftRefMaterial = materialAsset.PathInBundle;
            ApplySavedProperties(doc, bf["m_SavedProperties"]);
            return doc;
        }
        catch
        {
            return null;
        }
        finally
        {
            am.UnloadAll();
        }
    }

    private static ShaderCatalogEntry? ParseShaderField(AssetTypeValueField bf, SoftRefAssetEntry asset)
    {
        var findName = bf["m_ParsedForm"]["m_Name"].AsString;
        if (string.IsNullOrWhiteSpace(findName))
            return null;

        var props = new List<ShaderPropertySchema>();
        var arr = bf["m_ParsedForm"]["m_PropInfo"]["m_Props"]["Array"];
        if (!arr.IsDummy)
        {
            foreach (var p in arr.Children)
            {
                var name = p["m_Name"].AsString;
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("unity_", StringComparison.OrdinalIgnoreCase))
                    continue;

                props.Add(new ShaderPropertySchema
                {
                    Name = name,
                    Description = SafeString(p, "m_Description"),
                    Kind = MapKind(TryInt(p, "m_Type")),
                });
            }
        }

        return new ShaderCatalogEntry
        {
            FindName = findName,
            SoftRefDisplayName = asset.DisplayName,
            SoftRefPath = asset.PathInBundle,
            Properties = props,
        };
    }

    private static void ApplySavedProperties(MaterialDocument doc, AssetTypeValueField saved)
    {
        if (saved.IsDummy)
            return;

        void Upsert(string name, ShaderPropertyKind kind, string value, string textureRef = "")
        {
            var existing = doc.Properties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                doc.Properties.Add(new MaterialPropertyValue
                {
                    Name = name,
                    Kind = kind,
                    Value = value,
                    TextureRef = textureRef,
                });
            }
            else
            {
                existing.Kind = kind;
                existing.Value = value;
                if (!string.IsNullOrEmpty(textureRef))
                    existing.TextureRef = textureRef;
            }
        }

        try
        {
            foreach (var el in saved["m_TexEnvs"]["Array"].Children)
                Upsert(el["first"].AsString, ShaderPropertyKind.Texture, "", "(donor texture — replace with your PNG later)");
        }
        catch { /* ignore */ }

        try
        {
            foreach (var el in saved["m_Floats"]["Array"].Children)
                Upsert(el["first"].AsString, ShaderPropertyKind.Float, FormatFloat(el["second"].AsFloat));
        }
        catch { /* ignore */ }

        try
        {
            foreach (var el in saved["m_Colors"]["Array"].Children)
            {
                var c = el["second"];
                Upsert(
                    el["first"].AsString,
                    ShaderPropertyKind.Color,
                    $"{FormatFloat(c["r"].AsFloat)},{FormatFloat(c["g"].AsFloat)},{FormatFloat(c["b"].AsFloat)},{FormatFloat(c["a"].AsFloat)}");
            }
        }
        catch { /* ignore */ }

        try
        {
            foreach (var el in saved["m_Ints"]["Array"].Children)
                Upsert(el["first"].AsString, ShaderPropertyKind.Float, el["second"].AsInt.ToString());
        }
        catch { /* ignore */ }
    }

    private static AssetsFileInstance? OpenFirstAssetsFile(AssetsManager am, BundleFileInstance bun)
    {
        var names = bun.file.GetAllFileNames();
        if (names.Count == 0)
            return null;
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

        return am.LoadAssetsFileFromBundle(bun, assetsIndex, false);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static ShaderPropertyKind MapKind(int type) => type switch
    {
        0 => ShaderPropertyKind.Color,
        1 => ShaderPropertyKind.Vector,
        2 => ShaderPropertyKind.Float,
        3 => ShaderPropertyKind.Range,
        4 => ShaderPropertyKind.Texture,
        _ => ShaderPropertyKind.Unknown,
    };

    private static string DefaultValue(ShaderPropertyKind kind) => kind switch
    {
        ShaderPropertyKind.Color => "1,1,1,1",
        ShaderPropertyKind.Vector => "0,0,0,0",
        ShaderPropertyKind.Float or ShaderPropertyKind.Range => "0",
        _ => "",
    };

    private static string FormatFloat(float v) =>
        Math.Abs(v % 1f) < 0.0001f ? ((int)v).ToString() : v.ToString("0.####");

    private static string SafeString(AssetTypeValueField f, string name)
    {
        try
        {
            var v = f[name];
            return v.IsDummy ? "" : v.AsString;
        }
        catch
        {
            return "";
        }
    }

    private static int TryInt(AssetTypeValueField f, string name)
    {
        try
        {
            var v = f[name];
            return v.IsDummy ? -1 : v.AsInt;
        }
        catch
        {
            return -1;
        }
    }
}
