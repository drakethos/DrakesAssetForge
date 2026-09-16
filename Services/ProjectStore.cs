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

    public string? TryLoadValheimProjectPath()
    {
        var marker = Path.Combine(ProjectRoot, "project.json");
        if (!File.Exists(marker))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            return doc.RootElement.TryGetProperty("valheimProjectPath", out var path) ? path.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public void SaveValheimProjectPath(string path)
    {
        EnsureCreated();
        var marker = Path.Combine(ProjectRoot, "project.json");
        var name = "Default";
        var schema = 1;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } existing)
                name = existing;
            if (doc.RootElement.TryGetProperty("schemaVersion", out var s) && s.TryGetInt32(out var v))
                schema = v;
        }
        catch
        {
            // Rewrite a minimal marker.
        }

        var json = JsonSerializer.Serialize(new
        {
            name,
            schemaVersion = schema,
            valheimProjectPath = path,
        }, JsonOptions);
        File.WriteAllText(marker, json);
    }

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

        CollectItems(ItemsRoot, "", results);
        return results
            .OrderBy(d => d.GroupPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> ListGroups()
    {
        EnsureCreated();
        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(ItemsRoot))
            return Array.Empty<string>();

        foreach (var dir in Directory.EnumerateDirectories(ItemsRoot))
        {
            if (File.Exists(Path.Combine(dir, "item.json")))
                continue;
            groups.Add(Path.GetFileName(dir));
        }

        foreach (var item in LoadAllItems())
        {
            if (!string.IsNullOrWhiteSpace(item.GroupPath))
                groups.Add(item.GroupPath.Split('/', '\\')[0]);
        }

        return groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void CollectItems(string directory, string groupPath, List<OwnedItemDocument> results)
    {
        foreach (var dir in Directory.EnumerateDirectories(directory))
        {
            var docPath = Path.Combine(dir, "item.json");
            if (File.Exists(docPath))
            {
                try
                {
                    var doc = LoadItem(docPath);
                    if (doc == null)
                        continue;
                    doc.GroupPath = groupPath;
                    results.Add(doc);
                }
                catch
                {
                    // skip corrupt
                }

                continue;
            }

            var name = Path.GetFileName(dir);
            var childGroup = string.IsNullOrEmpty(groupPath) ? name : groupPath + "/" + name;
            CollectItems(dir, childGroup, results);
        }
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
        doc.HasCompiledArt = File.Exists(Path.Combine(doc.FolderPath, "art.bundle"));
        doc.NeedsBundleExtract = art.NeedsBundleExtract && !doc.HasCompiledArt;
        doc.IncludeInExport = art.IncludeInExport;
    }

    public string ImportsRoot => Path.Combine(ProjectRoot, "Imports");

    /// <summary>Copies a source Unity bundle into Imports/ and returns a project-relative path.</summary>
    public string ImportSourceBundle(string sourceBundlePath)
    {
        if (!File.Exists(sourceBundlePath))
            throw new FileNotFoundException("Bundle not found.", sourceBundlePath);

        Directory.CreateDirectory(ImportsRoot);
        var name = SanitizeId(Path.GetFileNameWithoutExtension(sourceBundlePath));
        if (string.IsNullOrEmpty(name))
            name = "bundle";
        var destName = name + Path.GetExtension(sourceBundlePath);
        if (string.IsNullOrEmpty(Path.GetExtension(destName)))
            destName += ".bundle";
        var dest = Path.Combine(ImportsRoot, destName);
        var n = 2;
        while (File.Exists(dest) && !FilesEqual(sourceBundlePath, dest))
        {
            destName = $"{name}_{n}{Path.GetExtension(destName)}";
            dest = Path.Combine(ImportsRoot, destName);
            n++;
        }

        if (!File.Exists(dest))
            File.Copy(sourceBundlePath, dest, overwrite: false);
        return Path.Combine("Imports", Path.GetFileName(dest)).Replace('\\', '/');
    }

    public string? ResolveProjectRelativePath(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        if (Path.IsPathRooted(relative))
            return File.Exists(relative) ? relative : null;
        var full = Path.GetFullPath(Path.Combine(ProjectRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// Resolves an import source bundle (Imports/… or mod-style Assets/&lt;name&gt; without extension).
    /// When the mod lives in the same folder as the project, copies Assets/&lt;name&gt; into Imports/ if needed.
    /// </summary>
    public string? ResolveSourceBundlePath(string? relativeSourcePath, bool repairImports = true)
    {
        var direct = ResolveProjectRelativePath(relativeSourcePath);
        if (direct != null)
            return direct;

        if (string.IsNullOrWhiteSpace(relativeSourcePath))
            return null;

        var normalized = relativeSourcePath.Replace('\\', '/');
        if (!normalized.StartsWith("Imports/", StringComparison.OrdinalIgnoreCase))
            return null;

        var fileName = Path.GetFileName(normalized);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(stem))
            return null;

        // LockSmith-style: project root is the mod repo; shipped bundles are Assets/drake, Assets/ploam (no extension).
        var modAsset = Path.Combine(ProjectRoot, "Assets", stem);
        if (!File.Exists(modAsset))
            return null;

        if (!repairImports)
            return modAsset;

        try
        {
            var rel = ImportSourceBundle(modAsset);
            return ResolveProjectRelativePath(rel);
        }
        catch
        {
            return modAsset;
        }
    }

    /// <summary>Creates an owned item folder (scripts optional). Used by bundle / mod imports.</summary>
    public OwnedItemDocument CreateImportedItem(
        string preferredId,
        string displayName,
        string donorPrefabName,
        string? preferredGroup = null,
        Dictionary<string, string>? fields = null,
        List<OwnedScriptSeed>? scripts = null)
    {
        EnsureCreated();
        var baseId = SanitizeId(preferredId);
        if (string.IsNullOrEmpty(baseId))
            baseId = "Imported_Item";
        var id = EnsureUniqueId(baseId);
        var group = SanitizeGroupPath(preferredGroup);
        var folder = string.IsNullOrEmpty(group)
            ? Path.Combine(ItemsRoot, id)
            : Path.Combine(ItemsRoot, group.Replace('/', Path.DirectorySeparatorChar), id);
        Directory.CreateDirectory(folder);

        var now = DateTimeOffset.UtcNow;
        var doc = new OwnedItemDocument
        {
            Id = id,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim(),
            Donor = new DonorRef
            {
                PrefabName = string.IsNullOrWhiteSpace(donorPrefabName) ? "LeatherScraps" : donorPrefabName.Trim(),
                Kind = "Imported",
            },
            Scripts = scripts ?? new List<OwnedScriptSeed>(),
            Fields = fields ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CreatedUtc = now,
            ModifiedUtc = now,
            FolderPath = folder,
            DocumentPath = Path.Combine(folder, "item.json"),
            GroupPath = group,
        };
        if (!doc.Fields.ContainsKey("prefab"))
            doc.Fields["prefab"] = id;

        SaveItem(doc);
        SaveMaterial(doc, new MaterialDocument
        {
            Mode = MaterialAuthoringMode.Donor,
            ShaderName = "",
            ModifiedUtc = now,
        });
        SaveArt(doc, new ArtDocument { PrefabName = id, ModifiedUtc = now });
        return doc;
    }

    private static bool FilesEqual(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            return fa.Length == fb.Length;
        }
        catch
        {
            return false;
        }
    }

    public OwnedItemDocument CloneFromSpy(
        SoftRefAssetEntry donor,
        PrefabPropertySpyResult prefabSpy,
        AssetPropertySpyResult? recipeSpy,
        IReadOnlyList<RecipeRequirementRow>? recipeRequirements,
        string? preferredId = null,
        string? preferredGroup = null)
    {
        EnsureCreated();

        var baseId = SanitizeId(preferredId ?? $"Custom_{donor.DisplayName}");
        var id = EnsureUniqueId(baseId);
        var group = SanitizeGroupPath(preferredGroup);
        var folder = string.IsNullOrEmpty(group)
            ? Path.Combine(ItemsRoot, id)
            : Path.Combine(ItemsRoot, group.Replace('/', Path.DirectorySeparatorChar), id);
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
            GroupPath = group,
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
            return new ArtDocument { IncludeInExport = true, ModifiedUtc = DateTimeOffset.UtcNow };

        try
        {
            var json = File.ReadAllText(path);
            var art = JsonSerializer.Deserialize<ArtDocument>(json, JsonOptions)
                      ?? new ArtDocument { IncludeInExport = true };
            // Older art.json files omit the key; treat missing as included.
            if (json.IndexOf("includeInExport", StringComparison.OrdinalIgnoreCase) < 0)
                art.IncludeInExport = true;
            return art;
        }
        catch
        {
            return new ArtDocument { IncludeInExport = true };
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

    public void DeleteItem(OwnedItemDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.FolderPath) || !Directory.Exists(doc.FolderPath))
            return;
        Directory.Delete(doc.FolderPath, recursive: true);
    }

    public bool TryCreateGroup(string rawName, out string groupPath, out string error)
    {
        groupPath = SanitizeGroupPath(rawName);
        error = "";
        if (string.IsNullOrEmpty(groupPath))
        {
            error = "Enter a group name.";
            return false;
        }

        if (groupPath.Contains('/'))
        {
            error = "One folder level for now.";
            return false;
        }

        if (ItemIdExists(groupPath))
        {
            error = $"'{groupPath}' is already an item id.";
            return false;
        }

        var folder = Path.Combine(ItemsRoot, groupPath);
        if (Directory.Exists(folder))
        {
            error = $"Group '{groupPath}' already exists.";
            return false;
        }

        Directory.CreateDirectory(folder);
        return true;
    }

    public bool TryRenameGroup(string oldPath, string rawName, out string newPath, out string error)
    {
        newPath = SanitizeGroupPath(rawName);
        error = "";
        oldPath = SanitizeGroupPath(oldPath);
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath))
        {
            error = "Group name is required.";
            return false;
        }

        if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
        {
            newPath = oldPath;
            return true;
        }

        var from = Path.Combine(ItemsRoot, oldPath.Replace('/', Path.DirectorySeparatorChar));
        var to = Path.Combine(ItemsRoot, newPath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(from))
        {
            error = "Group folder is missing.";
            return false;
        }

        if (Directory.Exists(to) || ItemIdExists(newPath))
        {
            error = $"'{newPath}' already exists.";
            return false;
        }

        Directory.Move(from, to);
        return true;
    }

    public bool TryDeleteGroup(string groupPath, bool deleteItems, out string error)
    {
        error = "";
        groupPath = SanitizeGroupPath(groupPath);
        if (string.IsNullOrEmpty(groupPath))
        {
            error = "Cannot delete the project root.";
            return false;
        }

        var folder = Path.Combine(ItemsRoot, groupPath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(folder))
            return true;

        var items = LoadAllItems().Where(i => i.GroupPath.Equals(groupPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count > 0 && !deleteItems)
        {
            error = $"Group has {items.Count} item(s). Delete them first, or confirm deleting the whole group.";
            return false;
        }

        Directory.Delete(folder, recursive: true);
        return true;
    }

    public bool TryMoveItemToGroup(OwnedItemDocument doc, string? groupPath, out string error)
    {
        error = "";
        groupPath = SanitizeGroupPath(groupPath);
        var current = SanitizeGroupPath(doc.GroupPath);
        if (current.Equals(groupPath, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(groupPath))
        {
            var groupFolder = Path.Combine(ItemsRoot, groupPath.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(groupFolder))
                Directory.CreateDirectory(groupFolder);
            if (File.Exists(Path.Combine(groupFolder, "item.json")))
            {
                error = $"'{groupPath}' is an item, not a group.";
                return false;
            }
        }

        var destParent = string.IsNullOrEmpty(groupPath)
            ? ItemsRoot
            : Path.Combine(ItemsRoot, groupPath.Replace('/', Path.DirectorySeparatorChar));
        var dest = Path.Combine(destParent, doc.Id);
        if (Directory.Exists(dest))
        {
            error = $"'{doc.Id}' already exists in that group.";
            return false;
        }

        Directory.Move(doc.FolderPath, dest);
        doc.FolderPath = dest;
        doc.DocumentPath = Path.Combine(dest, "item.json");
        doc.GroupPath = groupPath;
        return true;
    }

    public bool TryDuplicateItem(
        OwnedItemDocument source,
        out OwnedItemDocument? copy,
        out string error,
        string? preferredGroup = null)
    {
        copy = null;
        error = "";
        if (string.IsNullOrWhiteSpace(source.FolderPath) || !Directory.Exists(source.FolderPath))
        {
            error = "Item folder is missing.";
            return false;
        }

        var newId = EnsureUniqueId(source.Id + "_Copy");
        var group = preferredGroup != null
            ? SanitizeGroupPath(preferredGroup)
            : SanitizeGroupPath(source.GroupPath);
        var destParent = string.IsNullOrEmpty(group)
            ? ItemsRoot
            : Path.Combine(ItemsRoot, group.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(destParent);
        var dest = Path.Combine(destParent, newId);
        if (Directory.Exists(dest))
        {
            error = $"'{newId}' already exists.";
            return false;
        }

        CopyDirectory(source.FolderPath, dest);
        var docPath = Path.Combine(dest, "item.json");
        copy = LoadItem(docPath);
        if (copy == null)
        {
            error = "Duplicated folder, but item.json failed to load.";
            return false;
        }

        var oldId = copy.Id;
        copy.Id = newId;
        copy.GroupPath = group;
        if (string.IsNullOrWhiteSpace(copy.DisplayName) ||
            copy.DisplayName.Equals(oldId, StringComparison.OrdinalIgnoreCase))
            copy.DisplayName = newId;
        if (copy.Fields.TryGetValue("prefab", out var prefab) &&
            (string.IsNullOrWhiteSpace(prefab) || prefab.Equals(oldId, StringComparison.OrdinalIgnoreCase)))
            copy.Fields["prefab"] = newId;

        var art = LoadArt(copy);
        if (string.IsNullOrWhiteSpace(art.PrefabName) ||
            art.PrefabName.Equals(oldId, StringComparison.OrdinalIgnoreCase))
            art.PrefabName = newId;
        SaveArt(copy, art);
        SaveItem(copy);
        return true;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
    }

    public bool TryRenameItem(OwnedItemDocument doc, string rawName, out string newId, out string error)
    {
        newId = SanitizeId(rawName);
        error = "";
        if (string.IsNullOrWhiteSpace(doc.FolderPath) || !Directory.Exists(doc.FolderPath))
        {
            error = "Item folder is missing.";
            return false;
        }

        if (newId.Equals(doc.Id, StringComparison.OrdinalIgnoreCase))
        {
            newId = doc.Id;
            return true;
        }

        if (ItemIdExists(newId) || GroupExists(newId))
        {
            error = $"'{newId}' already exists in the project.";
            return false;
        }

        var oldId = doc.Id;
        var parent = Path.GetDirectoryName(doc.FolderPath)!;
        var newFolder = Path.Combine(parent, newId);
        Directory.Move(doc.FolderPath, newFolder);
        doc.Id = newId;
        doc.FolderPath = newFolder;
        doc.DocumentPath = Path.Combine(newFolder, "item.json");
        if (string.IsNullOrWhiteSpace(doc.DisplayName) ||
            doc.DisplayName.Equals(oldId, StringComparison.OrdinalIgnoreCase))
            doc.DisplayName = newId;
        if (doc.Fields.TryGetValue("prefab", out var prefab) &&
            prefab.Equals(oldId, StringComparison.OrdinalIgnoreCase))
            doc.Fields["prefab"] = newId;

        var art = LoadArt(doc);
        if (string.IsNullOrWhiteSpace(art.PrefabName) ||
            art.PrefabName.Equals(oldId, StringComparison.OrdinalIgnoreCase))
        {
            art.PrefabName = newId;
            SaveArt(doc, art);
        }

        SaveItem(doc);
        return true;
    }

    public void DeleteArtFile(OwnedItemDocument doc, string? storedPath)
    {
        var full = ResolveArtAbsolutePath(doc, storedPath);
        if (full == null)
            return;
        var artRoot = Path.GetFullPath(GetArtAssetsFolder(doc));
        if (!full.StartsWith(artRoot, StringComparison.OrdinalIgnoreCase))
            return;
        File.Delete(full);
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
        while (ItemIdExists(id) || GroupExists(id))
        {
            id = $"{baseId}_{n}";
            n++;
        }

        return id;
    }

    private bool ItemIdExists(string id) =>
        LoadAllItems().Any(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private bool GroupExists(string name)
    {
        var folder = Path.Combine(ItemsRoot, name);
        return Directory.Exists(folder) && !File.Exists(Path.Combine(folder, "item.json"));
    }

    public static string SanitizeGroupPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var parts = raw.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeId)
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join("/", parts);
    }

    public static string SanitizeId(string raw)
    {
        var chars = raw.Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')
            .ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "CustomItem" : s;
    }
}
