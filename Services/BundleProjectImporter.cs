using System.Text.Json;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class BundleImportResult
{
    public int ImportedCount { get; init; }
    public int SkippedVanilla { get; init; }
    public int NeedsExtract { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<OwnedItemDocument> Items { get; init; } = Array.Empty<OwnedItemDocument>();
}

/// <summary>
/// Imports Unity asset bundles or synced mod Assets/Items trees into the open project
/// as normal owned items. Skips SoftRef / Valheim vanilla prefab names.
/// </summary>
public static class BundleProjectImporter
{
    private const string DefaultDonor = "LeatherScraps";

    public static BundleImportResult ImportBundles(
        ProjectStore store,
        IEnumerable<string> bundlePaths,
        IReadOnlySet<string> vanillaNames)
    {
        var imported = new List<OwnedItemDocument>();
        var skippedVanilla = 0;
        var needsExtract = 0;
        var errors = new List<string>();

        foreach (var path in bundlePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var result = ImportOneBundle(store, path, vanillaNames);
                imported.AddRange(result.Items);
                skippedVanilla += result.SkippedVanilla;
                needsExtract += result.NeedsExtract;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        return new BundleImportResult
        {
            ImportedCount = imported.Count,
            SkippedVanilla = skippedVanilla,
            NeedsExtract = needsExtract,
            Items = imported,
            Message = BuildMessage(imported.Count, skippedVanilla, needsExtract, errors),
        };
    }

    /// <summary>
    /// Opens a mod <c>Assets</c> (or plugin) folder and imports every Unity asset bundle found —
    /// including SoftRef-style extensionless files such as <c>ploam</c> / <c>drake</c>.
    /// </summary>
    public static BundleImportResult ImportModAssetsFolder(
        ProjectStore store,
        string folder,
        IReadOnlySet<string> vanillaNames)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("Folder was not found.");

        var scanRoot = ResolveAssetsScanRoot(folder);
        var bundles = DiscoverUnityBundleFiles(scanRoot);
        if (bundles.Count == 0)
        {
            return new BundleImportResult
            {
                Message =
                    $"No Unity asset bundles found under {scanRoot}. " +
                    "Pick the mod Assets folder (files like 'ploam' with no extension) or a plugin folder that contains Assets/.",
            };
        }

        return ImportBundles(store, bundles, vanillaNames);
    }

    /// <summary>
    /// Finds UnityFS/UnityRaw bundle files in a folder (non-recursive for the Assets root;
    /// also checks one-level child dirs). Skips manifests and common non-bundle extensions.
    /// </summary>
    public static IReadOnlyList<string> DiscoverUnityBundleFiles(string folder)
    {
        if (!Directory.Exists(folder))
            return Array.Empty<string>();

        var results = new List<string>();
        foreach (var file in EnumerateCandidateBundleFiles(folder))
        {
            if (LooksLikeUnityAssetBundle(file))
                results.Add(file);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool LooksLikeUnityAssetBundle(string path)
    {
        if (!File.Exists(path))
            return false;

        var name = Path.GetFileName(path);
        if (name.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
            return false;

        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext))
        {
            switch (ext.ToLowerInvariant())
            {
                case ".dll":
                case ".pdb":
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".json":
                case ".md":
                case ".txt":
                case ".cs":
                case ".xml":
                case ".yml":
                case ".yaml":
                case ".cfg":
                case ".config":
                case ".exe":
                case ".zip":
                case ".mdb":
                    return false;
            }
        }

        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            var read = fs.Read(header);
            if (read < 7)
                return false;

            // UnityFS / UnityRaw / UnityWeb — same headers SoftRef and Valheim mod Assets use.
            if (header[0] == (byte)'U' && header[1] == (byte)'n' && header[2] == (byte)'i' &&
                header[3] == (byte)'t' && header[4] == (byte)'y')
                return true;

            // Some older bundles start with raw Assets file signature.
            if (header[0] == (byte)'A' && header[1] == (byte)'s' && header[2] == (byte)'s' &&
                header[3] == (byte)'e' && header[4] == (byte)'t')
                return true;
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static IEnumerable<string> EnumerateCandidateBundleFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
            yield return file;

        // Plugin root → Assets/*. SoftRef-style Bundles/*. Nested Assets/Items is handled elsewhere.
        foreach (var sub in new[] { "Assets", "Bundles" })
        {
            var child = Path.Combine(folder, sub);
            if (!Directory.Exists(child))
                continue;
            foreach (var file in Directory.EnumerateFiles(child))
                yield return file;
        }
    }

    private static string ResolveAssetsScanRoot(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Bundles", StringComparison.OrdinalIgnoreCase))
            return folder;

        var assets = Path.Combine(folder, "Assets");
        if (Directory.Exists(assets))
            return assets;

        var bundles = Path.Combine(folder, "Bundles");
        if (Directory.Exists(bundles))
            return bundles;

        return folder;
    }

    public static BundleImportResult ImportModItemsFolder(
        ProjectStore store,
        string folder,
        IReadOnlySet<string> vanillaNames)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("Folder was not found.");

        var itemsRoot = ResolveItemsRoot(folder);
        var imported = new List<OwnedItemDocument>();
        var skippedVanilla = 0;
        var needsExtract = 0;
        var group = SanitizeGroupFromFolder(folder);

        foreach (var itemDir in EnumerateItemFolders(itemsRoot))
        {
            var wirePath = Path.Combine(itemDir, "item.json");
            if (!File.Exists(wirePath))
                continue;

            string? id = null;
            string? donor = null;
            string? displayName = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(wirePath));
                id = ReadString(doc.RootElement, "id") ?? Path.GetFileName(itemDir);
                donor = ReadString(doc.RootElement, "donor");
                displayName = ReadString(doc.RootElement, "displayName") ?? id;
            }
            catch
            {
                id = Path.GetFileName(itemDir);
            }

            if (string.IsNullOrWhiteSpace(id))
                continue;

            if (IsVanillaExact(id, vanillaNames))
            {
                skippedVanilla++;
                continue;
            }

            var owned = store.CreateImportedItem(
                preferredId: id,
                displayName: displayName ?? id,
                donorPrefabName: string.IsNullOrWhiteSpace(donor) ? DefaultDonor : donor!,
                preferredGroup: group);

            var art = store.LoadArt(owned);
            art.PrefabName = owned.Id;

            var bundleSrc = Path.Combine(itemDir, "art.bundle");
            if (File.Exists(bundleSrc))
            {
                File.Copy(bundleSrc, Path.Combine(owned.FolderPath, "art.bundle"), overwrite: true);
                art.IncludeMesh = true;
            }

            var iconSrc = Path.Combine(itemDir, "icon.png");
            if (File.Exists(iconSrc))
            {
                art.IconPath = store.ImportArtFile(owned, iconSrc, "icon.png");
                art.IncludeIcon = true;
            }

            // Copy scale / description from thin wire when present.
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(wirePath));
                if (doc.RootElement.TryGetProperty("scale", out var scaleEl) &&
                    scaleEl.TryGetSingle(out var scale) && scale > 0)
                    art.Scale = scale;
                art.Description = ReadString(doc.RootElement, "description");
            }
            catch
            {
                // ignore
            }

            store.SaveArt(owned, art);
            store.LoadItem(owned.DocumentPath); // refresh badges via reload path
            imported.Add(owned);
        }

        return new BundleImportResult
        {
            ImportedCount = imported.Count,
            SkippedVanilla = skippedVanilla,
            NeedsExtract = needsExtract,
            Items = imported,
            Message = BuildMessage(imported.Count, skippedVanilla, needsExtract, Array.Empty<string>()),
        };
    }

    private static BundleImportResult ImportOneBundle(
        ProjectStore store,
        string bundlePath,
        IReadOnlySet<string> vanillaNames)
    {
        var objects = BundleAssetLister.ListObjects(bundlePath, maxObjects: 8000);
        var prefabs = objects
            .Where(o => IsPrefabLike(o))
            .Select(o => PrefabDisplayName(o.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var skipped = prefabs.Count(n => IsVanillaExact(n, vanillaNames));
        var custom = prefabs.Where(n => !IsVanillaExact(n, vanillaNames)).ToList();

        // ArtForge single-art bundles: container may only list art.prefab / GameObject "art".
        var isArtForgeArt = custom.Count == 1 &&
                            custom[0].Equals("art", StringComparison.OrdinalIgnoreCase);
        if (custom.Count == 0 && prefabs.Count == 0)
        {
            // Opaque unknown — import as one item named after the file.
            custom.Add(Path.GetFileNameWithoutExtension(bundlePath));
        }
        else if (isArtForgeArt || (custom.Count == 0 && prefabs.Any(p => p.Equals("art", StringComparison.OrdinalIgnoreCase))))
        {
            custom = new List<string> { Path.GetFileNameWithoutExtension(bundlePath) };
            isArtForgeArt = true;
        }

        var group = ProjectStore.SanitizeId(Path.GetFileNameWithoutExtension(bundlePath));
        if (string.IsNullOrEmpty(group))
            group = "Imported";
        if (!store.ListGroups().Contains(group, StringComparer.OrdinalIgnoreCase) &&
            !Directory.Exists(Path.Combine(store.ItemsRoot, group)))
        {
            store.TryCreateGroup(group, out _, out _);
        }

        var items = new List<OwnedItemDocument>();
        var needsExtract = 0;
        string? sharedImportRel = null;

        var singleCopy = isArtForgeArt || custom.Count == 1;
        if (!singleCopy)
            sharedImportRel = store.ImportSourceBundle(bundlePath);

        foreach (var prefabName in custom)
        {
            var owned = store.CreateImportedItem(
                preferredId: prefabName,
                displayName: prefabName,
                donorPrefabName: DefaultDonor,
                preferredGroup: group);

            var art = store.LoadArt(owned);
            art.PrefabName = owned.Id;

            if (singleCopy)
            {
                File.Copy(bundlePath, Path.Combine(owned.FolderPath, "art.bundle"), overwrite: true);
                art.IncludeMesh = true;
            }
            else
            {
                art.SourceBundlePath = sharedImportRel;
                art.SourcePrefabName = prefabName;
                art.NeedsBundleExtract = true;
                art.IncludeMesh = true;
                needsExtract++;
            }

            TryExtractIcon(store, owned, art, bundlePath, objects, prefabName);
            store.SaveArt(owned, art);
            items.Add(owned);
        }

        return new BundleImportResult
        {
            ImportedCount = items.Count,
            SkippedVanilla = skipped,
            NeedsExtract = needsExtract,
            Items = items,
        };
    }

    private static void TryExtractIcon(
        ProjectStore store,
        OwnedItemDocument owned,
        ArtDocument art,
        string bundlePath,
        IReadOnlyList<BundleObjectItem> objects,
        string prefabName)
    {
        try
        {
            var tex = objects.FirstOrDefault(o =>
                (o.TypeName.Equals("Texture2D", StringComparison.OrdinalIgnoreCase) ||
                 o.TypeName.Equals("Texture", StringComparison.OrdinalIgnoreCase) ||
                 o.TypeName.Equals("Sprite", StringComparison.OrdinalIgnoreCase)) &&
                (o.Name.Contains(prefabName, StringComparison.OrdinalIgnoreCase) ||
                 o.Name.Contains("icon", StringComparison.OrdinalIgnoreCase)));
            if (tex == null || tex.PathId == 0)
                return;

            var png = SoftRefPreviewService.TryExportTexturePng(bundlePath, tex.PathId);
            if (png == null || png.Length == 0)
                return;

            var artFolder = store.GetArtAssetsFolder(owned);
            Directory.CreateDirectory(artFolder);
            var dest = Path.Combine(artFolder, "icon.png");
            File.WriteAllBytes(dest, png);
            art.IconPath = "art/icon.png";
            art.IncludeIcon = true;
        }
        catch
        {
            // optional
        }
    }

    public static HashSet<string> BuildVanillaNameSet(IEnumerable<SoftRefAssetEntry>? assets)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (assets == null)
            return set;

        foreach (var asset in assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.DisplayName))
                set.Add(asset.DisplayName.Trim());
            if (!string.IsNullOrWhiteSpace(asset.PathInBundle))
            {
                var leaf = Path.GetFileNameWithoutExtension(asset.PathInBundle.Replace('\\', '/'));
                if (!string.IsNullOrWhiteSpace(leaf))
                    set.Add(leaf);
            }
        }

        return set;
    }

    private static bool IsPrefabLike(BundleObjectItem o) =>
        o.TypeName.Equals("Prefab", StringComparison.OrdinalIgnoreCase) ||
        o.TypeName.Equals("GameObject", StringComparison.OrdinalIgnoreCase);

    private static string PrefabDisplayName(string name)
    {
        var n = name.Replace('\\', '/');
        var leaf = Path.GetFileNameWithoutExtension(n);
        return string.IsNullOrWhiteSpace(leaf) ? n.Trim() : leaf.Trim();
    }

    private static bool IsVanillaExact(string name, IReadOnlySet<string> vanilla) =>
        !string.IsNullOrWhiteSpace(name) && vanilla.Contains(name.Trim());

    private static string ResolveItemsRoot(string folder)
    {
        var direct = Path.Combine(folder, "Assets", "Items");
        if (Directory.Exists(direct))
            return direct;
        if (Directory.Exists(Path.Combine(folder, "Items")))
            return Path.Combine(folder, "Items");
        // Already an Items folder or a plugin root with item children.
        return folder;
    }

    private static IEnumerable<string> EnumerateItemFolders(string root)
    {
        if (!Directory.Exists(root))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (File.Exists(Path.Combine(dir, "item.json")))
            {
                yield return dir;
                continue;
            }

            foreach (var nested in EnumerateItemFolders(dir))
                yield return nested;
        }
    }

    private static string SanitizeGroupFromFolder(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.Equals("Items", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileName(Path.GetDirectoryName(folder) ?? "Imported");
        return ProjectStore.SanitizeId(string.IsNullOrWhiteSpace(name) ? "Imported" : name);
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string BuildMessage(int imported, int skipped, int needsExtract, IReadOnlyList<string> errors)
    {
        var parts = new List<string> { $"Imported {imported} item(s)." };
        if (skipped > 0)
            parts.Add($"Skipped {skipped} Valheim vanilla name(s).");
        if (needsExtract > 0)
            parts.Add($"{needsExtract} need Unity extract → art.bundle (Export or Extract imported bundles).");
        if (errors.Count > 0)
            parts.Add("Errors: " + string.Join("; ", errors));
        return string.Join(" ", parts);
    }
}
