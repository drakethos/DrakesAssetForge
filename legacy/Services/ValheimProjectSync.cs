using System.Text.Json;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class ValheimProjectSyncResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int ItemCount { get; init; }
    public int BundledCount { get; init; }
}

/// <summary>
/// Copies art into a Valheim mod project and writes customize stubs.
/// Folder groups ship as Assets/Items/&lt;folder&gt;/&lt;folder&gt;.bundle + &lt;itemId&gt;.json.
/// Ungrouped items keep legacy Assets/Items/&lt;id&gt;/item.json + art.bundle.
/// SoftRef/Valheim catalog content is never bulk-dumped.
/// </summary>
public static class ValheimProjectSync
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string? FindDefaultTestProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "DrakesAssetForgeTest", "DrakesAssetForgeTest.csproj");
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate);
            dir = dir.Parent;
        }

        return null;
    }

    public static bool IsWired(string valheimProject)
    {
        if (string.IsNullOrWhiteSpace(valheimProject) || !Directory.Exists(valheimProject))
            return false;

        foreach (var file in Directory.EnumerateFiles(valheimProject, "*.cs", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("ArtItemLoader.Register", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>Top-level group folder name (keys), or empty for ungrouped items.</summary>
    public static string TopGroupName(OwnedItemDocument item)
    {
        if (string.IsNullOrWhiteSpace(item.GroupPath))
            return "";
        var g = item.GroupPath.Replace('\\', '/');
        var slash = g.IndexOf('/');
        return slash < 0 ? g : g[..slash];
    }

    public static string FolderBundlePath(ProjectStore store, string groupName) =>
        Path.Combine(store.ItemsRoot, groupName, groupName + ".bundle");

    public static ValheimProjectSyncResult Sync(ProjectStore store, string valheimProject)
    {
        if (string.IsNullOrWhiteSpace(valheimProject) || !Directory.Exists(valheimProject))
            return Fail("Code project folder was not found.");

        var wired = IsWired(valheimProject);
        var written = new List<string>();
        var bundled = new List<string>();
        var noCustomArt = new List<string>();
        var missingArt = new List<string>();
        var excluded = new List<string>();
        var includedDocs = new List<OwnedItemDocument>();
        var folderPacks = new List<string>();

        var all = store.LoadAllItems().ToList();
        foreach (var item in all)
        {
            var art = store.LoadArt(item);
            if (!art.IncludeInExport)
            {
                RemoveSyncedItem(valheimProject, item.Id);
                excluded.Add(item.Id);
            }
        }

        var included = all
            .Select(i => (Item: i, Art: store.LoadArt(i)))
            .Where(x => x.Art.IncludeInExport)
            .ToList();

        var groups = included.GroupBy(
            x => TopGroupName(x.Item),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                foreach (var (item, art) in group)
                {
                    includedDocs.Add(item);
                    var hasItemBundle = File.Exists(Path.Combine(item.FolderPath, "art.bundle"));
                    if (hasItemBundle)
                        bundled.Add(item.Id);
                    else if (ExpectsCustomArt(item, art))
                        missingArt.Add(item.Id);
                    else
                        noCustomArt.Add(item.Id);

                    // Clear any previous folder-pack json for this id, then write legacy.
                    RemoveSyncedItem(valheimProject, item.Id);
                    CopyLegacyItem(store, item, Path.Combine(valheimProject, "Assets", "Items", item.Id));
                    var deployed = TryDeployFolder();
                    if (deployed != null)
                        CopyLegacyItem(store, item, Path.Combine(deployed, "Assets", "Items", item.Id));
                    written.Add(item.Id);
                }

                continue;
            }

            var groupName = group.Key;
            var folderBundle = FolderBundlePath(store, groupName);
            var members = group.ToList();
            var anyArt = members.Any(m => File.Exists(Path.Combine(m.Item.FolderPath, "art.bundle")));
            if (File.Exists(folderBundle))
            {
                foreach (var (item, _) in members)
                    bundled.Add(item.Id);
            }
            else if (anyArt || members.Any(m => ExpectsCustomArt(m.Item, m.Art)))
            {
                foreach (var (item, art) in members.Where(m => ExpectsCustomArt(m.Item, m.Art)))
                    missingArt.Add(item.Id);
            }
            else
            {
                foreach (var (item, _) in members)
                    noCustomArt.Add(item.Id);
            }

            foreach (var (item, _) in members)
            {
                includedDocs.Add(item);
                // Drop legacy per-item folders so loader uses the pack layout.
                RemoveLegacyItemFolder(valheimProject, item.Id);
                written.Add(item.Id);
            }

            CopyFolderPack(store, groupName, members, Path.Combine(valheimProject, "Assets", "Items", groupName));
            var deploy = TryDeployFolder();
            if (deploy != null)
                CopyFolderPack(store, groupName, members, Path.Combine(deploy, "Assets", "Items", groupName));
            folderPacks.Add(groupName);
        }

        ArtItemHookWriter.Write(valheimProject, includedDocs);

        var wireNote = wired
            ? "Loader is wired (ArtItemLoader.Register)."
            : "Assets copied, but the project does not call ArtItemLoader.Register. Reference DrakeModsLibs and call the loader.";
        var packNote = folderPacks.Count == 0
            ? ""
            : $" Folder packs: {string.Join(", ", folderPacks.Select(g => g + "/" + g + ".bundle"))}.";
        var bundleNote = bundled.Count == 0
            ? ""
            : $" Art for {bundled.Count} item(s).";
        var noArtNote = noCustomArt.Count == 0
            ? ""
            : $" No custom art (donor + hooks only): {string.Join(", ", noCustomArt)}.";
        var missingNote = missingArt.Count == 0
            ? ""
            : $" Missing folder/item art: {string.Join(", ", missingArt)}.";
        var excludeNote = excluded.Count == 0
            ? ""
            : $" Left out of export ({excluded.Count}): {string.Join(", ", excluded)}.";

        var success = written.Count > 0 && wired && missingArt.Count == 0;
        return new ValheimProjectSyncResult
        {
            Success = success,
            ItemCount = written.Count,
            BundledCount = bundled.Count,
            Message = written.Count == 0
                ? $"No items included for export.{excludeNote}"
                : $"Exported {written.Count} item(s) to {valheimProject}. {wireNote}{packNote}{bundleNote}{noArtNote}{missingNote}{excludeNote}",
        };
    }

    private static bool ExpectsCustomArt(OwnedItemDocument item, ArtDocument art)
    {
        if (File.Exists(Path.Combine(item.FolderPath, "art.bundle")))
            return false;
        if (art.NeedsBundleExtract)
            return true;
        if (!string.IsNullOrWhiteSpace(art.SourceBundlePath))
            return true;
        if (art.IncludeMesh && ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath) != null)
            return true;
        return false;
    }

    private static void CopyFolderPack(
        ProjectStore store,
        string groupName,
        IReadOnlyList<(OwnedItemDocument Item, ArtDocument Art)> members,
        string destFolder)
    {
        Directory.CreateDirectory(destFolder);

        // Remove stale wires / icons / legacy item subfolders inside the pack folder.
        foreach (var file in Directory.EnumerateFiles(destFolder, "*.json"))
            TryDeleteFile(file);
        foreach (var file in Directory.EnumerateFiles(destFolder, "*.png"))
            TryDeleteFile(file);
        foreach (var sub in Directory.EnumerateDirectories(destFolder))
            TryDeleteDirectory(sub);

        var folderBundleSrc = FolderBundlePath(store, groupName);
        var folderBundleDest = Path.Combine(destFolder, groupName + ".bundle");
        if (File.Exists(folderBundleSrc))
            File.Copy(folderBundleSrc, folderBundleDest, overwrite: true);
        else
            TryDeleteFile(folderBundleDest);

        // Drop Unity side-car manifests if present from a previous copy.
        TryDeleteFile(folderBundleDest + ".manifest");
        TryDeleteFile(Path.Combine(destFolder, groupName));

        foreach (var (item, art) in members)
        {
            WriteWireJson(store, item, art, Path.Combine(destFolder, item.Id + ".json"), artPrefab: item.Id);

            var iconDest = Path.Combine(destFolder, item.Id + ".png");
            var icon = art.IncludeIcon ? ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) : null;
            if (icon != null)
                File.Copy(icon, iconDest, overwrite: true);
            else
                TryDeleteFile(iconDest);
        }
    }

    private static void CopyLegacyItem(ProjectStore store, OwnedItemDocument item, string dest)
    {
        Directory.CreateDirectory(dest);
        var art = store.LoadArt(item);
        var bundle = Path.Combine(item.FolderPath, "art.bundle");
        var bundleDest = Path.Combine(dest, "art.bundle");
        if (File.Exists(bundle))
            File.Copy(bundle, bundleDest, overwrite: true);
        else
            TryDeleteFile(bundleDest);

        WriteWireJson(store, item, art, Path.Combine(dest, "item.json"), artPrefab: "art");

        var iconDest = Path.Combine(dest, "icon.png");
        var icon = art.IncludeIcon ? ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) : null;
        if (icon != null)
            File.Copy(icon, iconDest, overwrite: true);
        else
            TryDeleteFile(iconDest);
    }

    private static void WriteWireJson(
        ProjectStore store,
        OwnedItemDocument item,
        ArtDocument art,
        string destPath,
        string artPrefab)
    {
        var material = store.LoadMaterial(item);
        var spawn = string.IsNullOrWhiteSpace(art.PrefabName) ? item.Id : art.PrefabName.Trim();
        var scale = art.Scale <= 0 || float.IsNaN(art.Scale) || float.IsInfinity(art.Scale) ? 1f : art.Scale;
        var materials = material.Mode == MaterialAuthoringMode.Existing
            ? material.ExistingMaterials.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray()
            : Array.Empty<string>();

        var wire = JsonSerializer.Serialize(new
        {
            id = spawn,
            sourceId = item.Id,
            donor = item.Donor.PrefabName,
            displayName = string.IsNullOrWhiteSpace(art.PrefabName)
                ? (string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName)
                : spawn,
            scale,
            description = string.IsNullOrWhiteSpace(art.Description) ? null : art.Description.Trim(),
            existingMaterials = materials,
            artPrefab,
        }, JsonOptions);
        File.WriteAllText(destPath, wire);
    }

    public static void RemoveSyncedItem(string? valheimProject, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (!string.IsNullOrWhiteSpace(valheimProject))
        {
            RemoveLegacyItemFolder(valheimProject, itemId);
            RemovePackItemFiles(valheimProject, itemId);
        }

        var deployed = TryDeployFolder();
        if (deployed != null)
        {
            RemoveLegacyItemFolder(deployed, itemId);
            RemovePackItemFiles(deployed, itemId);
        }
    }

    private static void RemoveLegacyItemFolder(string? valheimProject, string itemId)
    {
        if (string.IsNullOrWhiteSpace(valheimProject))
            return;
        TryDeleteDirectory(Path.Combine(valheimProject, "Assets", "Items", itemId));
    }

    private static void RemovePackItemFiles(string valheimProject, string itemId)
    {
        var itemsRoot = Path.Combine(valheimProject, "Assets", "Items");
        if (!Directory.Exists(itemsRoot))
            return;
        foreach (var json in Directory.EnumerateFiles(itemsRoot, itemId + ".json", SearchOption.AllDirectories))
            TryDeleteFile(json);
        foreach (var png in Directory.EnumerateFiles(itemsRoot, itemId + ".png", SearchOption.AllDirectories))
            TryDeleteFile(png);
    }

    public static void MoveSyncedItem(string? valheimProject, string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(oldId) || oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.IsNullOrWhiteSpace(valheimProject))
        {
            TryMoveDirectory(
                Path.Combine(valheimProject, "Assets", "Items", oldId),
                Path.Combine(valheimProject, "Assets", "Items", newId));
            RenamePackItemFiles(valheimProject, oldId, newId);
        }

        var deployed = TryDeployFolder();
        if (deployed != null)
        {
            TryMoveDirectory(
                Path.Combine(deployed, "Assets", "Items", oldId),
                Path.Combine(deployed, "Assets", "Items", newId));
            RenamePackItemFiles(deployed, oldId, newId);
        }
    }

    private static void RenamePackItemFiles(string valheimProject, string oldId, string newId)
    {
        var itemsRoot = Path.Combine(valheimProject, "Assets", "Items");
        if (!Directory.Exists(itemsRoot))
            return;
        foreach (var json in Directory.EnumerateFiles(itemsRoot, oldId + ".json", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(Path.GetDirectoryName(json)!, newId + ".json");
            if (!File.Exists(dest))
                File.Move(json, dest);
            else
                TryDeleteFile(json);
        }

        foreach (var png in Directory.EnumerateFiles(itemsRoot, oldId + ".png", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(Path.GetDirectoryName(png)!, newId + ".png");
            if (!File.Exists(dest))
                File.Move(png, dest);
            else
                TryDeleteFile(png);
        }
    }

    public static void RemoveShippedBundle(string? valheimProject, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (!string.IsNullOrWhiteSpace(valheimProject))
            TryDeleteFile(Path.Combine(valheimProject, "Assets", "Items", itemId, "art.bundle"));
        var deployed = TryDeployFolder();
        if (deployed != null)
            TryDeleteFile(Path.Combine(deployed, "Assets", "Items", itemId, "art.bundle"));
    }

    private static string? TryDeployFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var props = Path.Combine(dir.FullName, "environment.props");
            if (File.Exists(props))
            {
                var text = File.ReadAllText(props);
                var r2 = ReadXmlProp(text, "R2ModPath");
                var profile = ReadXmlProp(text, "ProfileName");
                if (!string.IsNullOrWhiteSpace(r2) && !string.IsNullOrWhiteSpace(profile))
                {
                    var folder = Path.Combine(r2, profile, "BepInEx", "plugins", "DrakesAssetForgeTest");
                    if (Directory.Exists(folder) || File.Exists(Path.Combine(folder, "DrakesAssetForgeTest.dll")))
                        return folder;
                    if (Directory.Exists(Path.Combine(r2, profile, "BepInEx", "plugins")))
                        return folder;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static string? ReadXmlProp(string text, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            "<" + name + ">([^<]+)</" + name + ">");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void TryMoveDirectory(string from, string to)
    {
        if (!Directory.Exists(from) || Directory.Exists(to))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
    }

    private static ValheimProjectSyncResult Fail(string message) =>
        new() { Success = false, Message = message };
}
