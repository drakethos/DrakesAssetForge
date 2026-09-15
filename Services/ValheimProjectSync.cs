using System.Text.Json;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class ValheimProjectSyncResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public int ItemCount { get; init; }
}

/// <summary>
/// Copies art bundles into a Valheim mod project and writes a customize stub per item.
/// The mod calls DrakeModsLibs.Art.ArtItemLoader. It does not copy the loader.
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

    public static ValheimProjectSyncResult Sync(ProjectStore store, string valheimProject)
    {
        if (string.IsNullOrWhiteSpace(valheimProject) || !Directory.Exists(valheimProject))
            return Fail("Valheim project folder was not found.");

        var wired = IsWired(valheimProject);
        var written = new List<string>();
        var needsCompile = new List<string>();
        var scriptsOnly = new List<string>();
        var needsExtract = new List<string>();
        var excluded = new List<string>();
        var includedDocs = new List<OwnedItemDocument>();

        foreach (var item in store.LoadAllItems())
        {
            var art = store.LoadArt(item);
            if (!art.IncludeInExport)
            {
                RemoveSyncedItem(valheimProject, item.Id);
                excluded.Add(item.Id);
                continue;
            }

            includedDocs.Add(item);
            var bundle = Path.Combine(item.FolderPath, "art.bundle");
            // Ship art.bundle whenever it exists — imported bundles set the file without IncludeMesh.
            var shipBundle = File.Exists(bundle);
            var hasFbx = art.IncludeMesh && ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath) != null;
            var shipIcon = art.IncludeIcon && ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) != null;
            if (hasFbx && !shipBundle)
                needsCompile.Add(item.Id);
            if (art.NeedsBundleExtract && !shipBundle)
                needsExtract.Add(item.Id);
            if (!shipBundle && !shipIcon)
                scriptsOnly.Add(item.Id);

            // Art is optional — always sync item.json so scripts/properties ship.
            CopyItem(store, item, Path.Combine(valheimProject, "Assets", "Items", item.Id));
            var deployed = TryDeployFolder();
            if (deployed != null)
                CopyItem(store, item, Path.Combine(deployed, "Assets", "Items", item.Id));
            written.Add(item.Id);
        }

        ArtItemHookWriter.Write(valheimProject, includedDocs);

        var wireNote = wired
            ? "Loader is wired (ArtItemLoader.Register)."
            : "Assets copied, but the project does not call ArtItemLoader.Register. Reference DrakeModsLibs and call the loader.";
        var scriptsNote = scriptsOnly.Count == 0
            ? ""
            : $" Scripts/properties only (no art yet): {string.Join(", ", scriptsOnly)}.";
        var compileNote = needsCompile.Count == 0
            ? ""
            : $" Needs Compile art bundle (mesh attached, no art.bundle yet): {string.Join(", ", needsCompile)}.";
        var extractNote = needsExtract.Count == 0
            ? ""
            : $" Needs Extract imported bundle: {string.Join(", ", needsExtract)}.";
        var excludeNote = excluded.Count == 0
            ? ""
            : $" Left out of export ({excluded.Count}): {string.Join(", ", excluded)}.";
        return new ValheimProjectSyncResult
        {
            Success = written.Count > 0 && wired,
            ItemCount = written.Count,
            Message = written.Count == 0
                ? $"No items included for sync.{excludeNote}{compileNote}{extractNote}"
                : $"Synced {written.Count} item(s) to {valheimProject}. {wireNote}{scriptsNote}{compileNote}{extractNote}{excludeNote}",
        };
    }

    public static void RemoveSyncedItem(string? valheimProject, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return;
        if (!string.IsNullOrWhiteSpace(valheimProject))
            TryDeleteDirectory(Path.Combine(valheimProject, "Assets", "Items", itemId));
        var deployed = TryDeployFolder();
        if (deployed != null)
            TryDeleteDirectory(Path.Combine(deployed, "Assets", "Items", itemId));
    }

    public static void MoveSyncedItem(string? valheimProject, string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(oldId) || oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.IsNullOrWhiteSpace(valheimProject))
            TryMoveDirectory(Path.Combine(valheimProject, "Assets", "Items", oldId), Path.Combine(valheimProject, "Assets", "Items", newId));
        var deployed = TryDeployFolder();
        if (deployed != null)
            TryMoveDirectory(Path.Combine(deployed, "Assets", "Items", oldId), Path.Combine(deployed, "Assets", "Items", newId));
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

    private static void CopyItem(ProjectStore store, OwnedItemDocument item, string dest)
    {
        Directory.CreateDirectory(dest);
        var art = store.LoadArt(item);
        var bundle = Path.Combine(item.FolderPath, "art.bundle");
        var bundleDest = Path.Combine(dest, "art.bundle");
        // Always ship an existing art.bundle (FBX compile or imported extract). Never require IncludeMesh.
        if (File.Exists(bundle))
            File.Copy(bundle, bundleDest, overwrite: true);
        else
            TryDeleteFile(bundleDest);

        var material = store.LoadMaterial(item);
        var spawn = string.IsNullOrWhiteSpace(art.PrefabName) ? item.Id : art.PrefabName.Trim();
        var scale = art.Scale <= 0 || float.IsNaN(art.Scale) || float.IsInfinity(art.Scale) ? 1f : art.Scale;
        var materials = material.Mode == MaterialAuthoringMode.Existing
            ? material.ExistingMaterials.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToArray()
            : Array.Empty<string>();

        var iconDest = Path.Combine(dest, "icon.png");
        var icon = art.IncludeIcon ? ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) : null;
        if (icon != null)
            File.Copy(icon, iconDest, overwrite: true);
        else
            TryDeleteFile(iconDest);

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
        }, JsonOptions);
        File.WriteAllText(Path.Combine(dest, "item.json"), wire);
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
