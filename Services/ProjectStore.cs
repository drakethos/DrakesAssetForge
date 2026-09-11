using System.Text.Json;
using System.Text.Json.Serialization;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class ProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ProjectRoot { get; }
    public string ItemsRoot => Path.Combine(ProjectRoot, "Items");

    public ProjectStore(string projectRoot)
    {
        ProjectRoot = projectRoot;
    }

    public static string DefaultProjectRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrakeAssetForge",
            "Projects",
            "Default");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(ItemsRoot);
        var marker = Path.Combine(ProjectRoot, "project.json");
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, """
                {
                  "name": "Default",
                  "schemaVersion": 1
                }
                """);
        }
    }

    public IReadOnlyList<OwnedItemDocument> LoadAllItems()
    {
        EnsureCreated();
        var results = new List<OwnedItemDocument>();
        if (!Directory.Exists(ItemsRoot))
            return results;

        foreach (var dir in Directory.EnumerateDirectories(ItemsRoot))
        {
            var docPath = Path.Combine(dir, "item.json");
            if (!File.Exists(docPath))
                continue;
            try
            {
                var doc = LoadItem(docPath);
                if (doc != null)
                    results.Add(doc);
            }
            catch
            {
                // skip corrupt
            }
        }

        return results
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public OwnedItemDocument? LoadItem(string documentPath)
    {
        var json = File.ReadAllText(documentPath);
        var doc = JsonSerializer.Deserialize<OwnedItemDocument>(json, JsonOptions);
        if (doc == null)
            return null;

        doc.DocumentPath = documentPath;
        doc.FolderPath = Path.GetDirectoryName(documentPath) ?? "";
        ApplyArtBadges(doc);
        return doc;
    }

    private void ApplyArtBadges(OwnedItemDocument doc)
    {
        var art = LoadArt(doc);
        doc.HasMeshArt = !string.IsNullOrWhiteSpace(art.MeshPath) &&
                         ResolveArtAbsolutePath(doc, art.MeshPath) != null;
        doc.HasIconArt = !string.IsNullOrWhiteSpace(art.IconPath) &&
                         ResolveArtAbsolutePath(doc, art.IconPath) != null;
        doc.HasDiffuseArt = !string.IsNullOrWhiteSpace(art.DiffusePath) &&
                            ResolveArtAbsolutePath(doc, art.DiffusePath) != null;
    }

    public OwnedItemDocument CloneFromSpy(
        SoftRefAssetEntry donor,
        PrefabPropertySpyResult prefabSpy,
        AssetPropertySpyResult? recipeSpy,
        IReadOnlyList<RecipeRequirementRow>? recipeRequirements,
        string? preferredId = null)
    {
        EnsureCreated();

        var baseId = SanitizeId(preferredId ?? $"Custom_{donor.DisplayName}");
        var id = EnsureUniqueId(baseId);
        var folder = Path.Combine(ItemsRoot, id);
        Directory.CreateDirectory(folder);

        var now = DateTimeOffset.UtcNow;
        var scripts = prefabSpy.Scripts.Select(s => new OwnedScriptSeed
        {
            ClassName = s.ClassName,
            PathId = s.PathId,
            Fields = s.Fields.ToDictionary(f => f.Path, f => f.Value, StringComparer.OrdinalIgnoreCase),
        }).ToList();

        // Flat editable map = all scripts for now (prefixed). Prefer ItemDrop paths unprefixed for convenience.
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var script in prefabSpy.Scripts)
        {
            foreach (var row in script.Fields)
            {
                var key = script.ClassName.Equals("ItemDrop", StringComparison.OrdinalIgnoreCase)
                    ? row.Path
                    : row.Path;
                fields[key] = row.Value;
            }
        }

        fields["prefab"] = id;
        if (fields.ContainsKey("ItemDrop.m_itemData.m_shared.m_name"))
            fields["ItemDrop.m_itemData.m_shared.m_name"] = $"$item_{id.ToLowerInvariant()}";
        else if (fields.ContainsKey("m_shared.m_name"))
            fields["m_shared.m_name"] = $"$item_{id.ToLowerInvariant()}";

        var doc = new OwnedItemDocument
        {
            Id = id,
            DisplayName = id,
            Donor = new DonorRef
            {
                SoftRefAssetId = donor.AssetId,
                SoftRefPath = donor.PathInBundle,
                BundleId = donor.BundleId,
                PrefabName = string.IsNullOrWhiteSpace(prefabSpy.PrefabName) ? donor.DisplayName : prefabSpy.PrefabName,
                Kind = donor.Kind.ToString(),
            },
            Scripts = scripts,
            Fields = fields,
            CreatedUtc = now,
            ModifiedUtc = now,
            FolderPath = folder,
            DocumentPath = Path.Combine(folder, "item.json"),
        };

        if (recipeSpy != null && recipeSpy.Error == null)
        {
            doc.Recipe = new OwnedRecipeSeed
            {
                SoftRefPath = recipeSpy.SoftRefPath,
                RecipeName = $"Recipe_{id}",
                ClassName = recipeSpy.ClassName,
                Fields = recipeSpy.Fields.ToDictionary(f => f.Path, f => f.Value, StringComparer.OrdinalIgnoreCase),
                Requirements = (recipeRequirements ?? Array.Empty<RecipeRequirementRow>()).Select(r => new OwnedRecipeRequirement
                {
                    ItemName = r.ItemName,
                    Token = r.Token,
                    Amount = r.Amount,
                    AmountPerLevel = r.AmountPerLevel,
                }).ToList(),
            };
            doc.Recipe.Fields["m_Name"] = doc.Recipe.RecipeName;
        }

        SaveItem(doc);
        SaveMaterial(doc, new MaterialDocument
        {
            Mode = MaterialAuthoringMode.Donor,
            ShaderName = "",
            ModifiedUtc = now,
        });
        SaveArt(doc, new ArtDocument { ModifiedUtc = now });
        return doc;
    }

    public string GetMaterialPath(OwnedItemDocument doc) =>
        Path.Combine(doc.FolderPath, "material.json");

    public string GetArtPath(OwnedItemDocument doc) =>
        Path.Combine(doc.FolderPath, "art.json");

    public string GetArtAssetsFolder(OwnedItemDocument doc) =>
        Path.Combine(doc.FolderPath, "art");

    public ArtDocument LoadArt(OwnedItemDocument doc)
    {
        var path = GetArtPath(doc);
        if (!File.Exists(path))
            return new ArtDocument { ModifiedUtc = DateTimeOffset.UtcNow };

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ArtDocument>(json, JsonOptions)
                   ?? new ArtDocument();
        }
        catch
        {
            return new ArtDocument();
        }
    }

    public void SaveArt(OwnedItemDocument doc, ArtDocument art)
    {
        if (string.IsNullOrWhiteSpace(doc.FolderPath))
            throw new InvalidOperationException("Owned item folder path is required.");

        Directory.CreateDirectory(doc.FolderPath);
        art.ModifiedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(GetArtPath(doc), JsonSerializer.Serialize(art, JsonOptions));
    }

    /// <summary>
    /// Copies a source file into <c>Items/&lt;id&gt;/art/</c> and returns a path relative to the item folder.
    /// </summary>
    public string ImportArtFile(OwnedItemDocument doc, string sourcePath, string destFileName)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Art source file not found.", sourcePath);

        var artFolder = GetArtAssetsFolder(doc);
        Directory.CreateDirectory(artFolder);
        var dest = Path.Combine(artFolder, destFileName);
        File.Copy(sourcePath, dest, overwrite: true);
        return Path.Combine("art", destFileName).Replace('\\', '/');
    }

    public static string? ResolveArtAbsolutePath(OwnedItemDocument doc, string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        if (Path.IsPathRooted(storedPath))
            return File.Exists(storedPath) ? storedPath : null;

        if (string.IsNullOrWhiteSpace(doc.FolderPath))
            return null;

        var full = Path.GetFullPath(Path.Combine(doc.FolderPath, storedPath.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(full) ? full : null;
    }

    public MaterialDocument LoadMaterial(OwnedItemDocument doc)
    {
        var path = GetMaterialPath(doc);
        if (!File.Exists(path))
        {
            return new MaterialDocument
            {
                Mode = MaterialAuthoringMode.Donor,
                ModifiedUtc = DateTimeOffset.UtcNow,
            };
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MaterialDocument>(json, JsonOptions)
                   ?? new MaterialDocument { Mode = MaterialAuthoringMode.Donor };
        }
        catch
        {
            return new MaterialDocument { Mode = MaterialAuthoringMode.Donor };
        }
    }

    public void SaveMaterial(OwnedItemDocument doc, MaterialDocument material)
    {
        if (string.IsNullOrWhiteSpace(doc.FolderPath))
            throw new InvalidOperationException("Owned item folder path is required.");

        Directory.CreateDirectory(doc.FolderPath);
        material.ModifiedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(GetMaterialPath(doc), JsonSerializer.Serialize(material, JsonOptions));
    }

    public void SaveItem(OwnedItemDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.DocumentPath))
            throw new InvalidOperationException("DocumentPath is required.");

        Directory.CreateDirectory(Path.GetDirectoryName(doc.DocumentPath)!);
        doc.ModifiedUtc = DateTimeOffset.UtcNow;

        // Push flat field edits back into script seeds when paths match.
        foreach (var script in doc.Scripts)
        {
            foreach (var key in script.Fields.Keys.ToList())
            {
                if (doc.Fields.TryGetValue(key, out var value))
                    script.Fields[key] = value;
            }
        }

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        File.WriteAllText(doc.DocumentPath, json);
    }

    public void ApplyEditableFields(OwnedItemDocument doc, IEnumerable<EditableFieldRow> rows)
    {
        doc.Fields = rows.ToDictionary(r => r.Path, r => r.Value ?? "", StringComparer.OrdinalIgnoreCase);
    }

    private string EnsureUniqueId(string baseId)
    {
        var id = baseId;
        var n = 2;
        while (Directory.Exists(Path.Combine(ItemsRoot, id)))
        {
            id = $"{baseId}_{n}";
            n++;
        }

        return id;
    }

    private static string SanitizeId(string raw)
    {
        var chars = raw.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "CustomItem" : s;
    }
}
