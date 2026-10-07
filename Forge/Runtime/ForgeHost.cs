using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using DrakesForge.Format;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesForge.Runtime;

internal static class ForgeLog
{
    public static ManualLogSource Source = BepInEx.Logging.Logger.CreateLogSource("Forge");
}

/// <summary>Handed to OnBuilt hooks right after Forge builds (or hot-reloads) an item from its recipe.</summary>
public sealed class ForgeBuiltContext
{
    internal ForgeBuiltContext(ItemRecipe recipe, GameObject prefab, LoadedPack pack, bool isReload)
    {
        Recipe = recipe;
        Prefab = prefab;
        Pack = pack;
        IsReload = isReload;
    }

    public ItemRecipe Recipe { get; }
    public string Id => Recipe.Id;
    /// <summary>The registered prefab (clone, or the vanilla prefab for reskins).</summary>
    public GameObject Prefab { get; }
    public LoadedPack Pack { get; }
    /// <summary>True when called again after a hot reload: the prefab was reset to vanilla and rebuilt from the recipe.</summary>
    public bool IsReload { get; }

    public ItemDrop? ItemDrop => Prefab.GetComponent<ItemDrop>();
    public ItemDrop.ItemData.SharedData? Shared => ItemDrop?.m_itemData?.m_shared;
    public Piece? Piece => Prefab.GetComponent<Piece>();

    /// <summary>Adds a component only if the prefab doesn't have one yet (OnBuilt runs again on every hot reload).</summary>
    public T AddComponentOnce<T>() where T : Component =>
        Prefab.GetComponent<T>() ?? Prefab.AddComponent<T>();

    /// <summary>Absolute path of a file inside the pack ("textures/glow.png").</summary>
    public string? PackFile(string relative) => Pack.Resolve(relative);
}

public sealed class ForgeOptions
{
    /// <summary>Folders searched for packs (forgepack.json), e.g. BepInEx\plugins, or a mod's own Pack folder.</summary>
    public List<string> PackFolders { get; } = new();
    /// <summary>Forge app push folders: their copy of a pack wins over an installed one.</summary>
    public List<string> DevFolders { get; } = new();
    public bool HotReload { get; set; } = true;
    /// <summary>The shared runtime skips packs marked "embedded" (their own mod loads them).</summary>
    public bool SkipEmbedded { get; set; }
    /// <summary>
    /// True for the shared Forge Runtime. Embedded hosts (a mod's own copy) claim their pack ids at startup,
    /// and the shared runtime then skips any data-pack copy of those packs, so items never register twice.
    /// </summary>
    public bool IsShared { get; set; }
    /// <summary>Your code, run for every item right after Forge builds it (and after each hot reload).</summary>
    public Action<ForgeBuiltContext>? OnBuilt { get; set; }
}

/// <summary>
/// Loads Forge packs and builds them from the player's own Valheim prefabs once vanilla prefabs exist,
/// then hot-reloads them when their files change. Used by the shared Forge Runtime plugin, and embeddable
/// in any mod: <c>ForgeHost.Start(this, options)</c> from your plugin's Awake.
/// </summary>
public sealed class ForgeHost : MonoBehaviour
{
    private ForgeOptions _options = new();
    private ForgeRegistry _registry = null!;
    private DevWatcher? _watcher;
    private List<string> _packRoots = new();

    /// <param name="log">Your plugin's Logger (so Forge messages show under your mod's name).</param>
    public static ForgeHost Start(BaseUnityPlugin owner, ForgeOptions options, ManualLogSource? log = null)
    {
        ForgeLog.Source = log ?? BepInEx.Logging.Logger.CreateLogSource(owner.Info.Metadata.Name);
        var host = owner.gameObject.AddComponent<ForgeHost>();
        host._options = options;
        host._registry = new ForgeRegistry(options.OnBuilt);
        if (!options.IsShared)
            ClaimPacks(options.PackFolders);
        PrefabManager.OnVanillaPrefabsAvailable += host.OnVanillaPrefabsAvailable;
        return host;
    }

    // Shared across every copy of Forge in the process (each mod compiles its own, in its own namespace).
    private const string ClaimsKey = "DrakesForge.EmbeddedPackIds";

    private static List<string> Claims()
    {
        lock (typeof(object))
        {
            if (AppDomain.CurrentDomain.GetData(ClaimsKey) is not List<string> claims)
            {
                claims = new List<string>();
                AppDomain.CurrentDomain.SetData(ClaimsKey, claims);
            }

            return claims;
        }
    }

    private static void ClaimPacks(IEnumerable<string> folders)
    {
        var claims = Claims();
        foreach (var folder in folders)
            foreach (var root in PackReader.FindPackRoots(folder))
            {
                try
                {
                    var id = RecipeSerializer.ReadPack(File.ReadAllText(Path.Combine(root, ForgePack.FileName)), new List<string>()).Id;
                    lock (claims)
                        if (!claims.Contains(id, StringComparer.OrdinalIgnoreCase))
                            claims.Add(id);
                }
                catch (IOException)
                {
                    // unreadable manifest: it will fail to load anyway
                }
            }
    }

    private static bool ClaimedElsewhere(string id)
    {
        var claims = Claims();
        lock (claims)
            return claims.Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    private void OnVanillaPrefabsAvailable()
    {
        // Fires on every main-menu visit; clones only need registering once.
        PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;

        var installed = LoadPacks(_options.PackFolders, isDev: false);
        var dev = LoadPacks(_options.DevFolders, isDev: true);

        // While developing, the pushed copy of a pack wins over an installed copy of the same pack.
        var devIds = new HashSet<string>(dev.Select(p => p.Manifest.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var pack in installed.Where(p => devIds.Contains(p.Manifest.Id)))
            ForgeLog.Source.LogInfo($"Pack {pack.Manifest.Id}: using the pushed copy instead of the installed one at {pack.Root}");
        installed.RemoveAll(p => devIds.Contains(p.Manifest.Id));

        var all = installed.Concat(dev).ToList();
        _registry.RegisterAll(all);

        if (_options.HotReload)
        {
            _packRoots = all.Select(p => p.Root).ToList();
            _watcher = new DevWatcher(_packRoots.Concat(_options.DevFolders));
            ForgeLog.Source.LogInfo($"Hot reload: watching {_watcher.Count} folder(s)");
        }
    }

    private void Update()
    {
        if (_watcher == null)
            return;
        var changed = _watcher.TakeChanges();
        if (changed.Count == 0)
            return;

        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in changed)
        {
            // A pack folder changed: reload it. A push folder changed: reload every pack under it.
            if (_packRoots.Any(r => string.Equals(r, folder, StringComparison.OrdinalIgnoreCase)))
                roots.Add(folder);
            else
                foreach (var root in PackReader.FindPackRoots(folder))
                    roots.Add(root);
        }

        foreach (var root in roots)
        {
            LoadedPack pack;
            try
            {
                pack = PackReader.Load(root);
            }
            catch (Exception ex)
            {
                ForgeLog.Source.LogWarning($"Reload of '{root}' failed: {ex.Message}");
                continue;
            }

            if (_options.SkipEmbedded && pack.Manifest.Embedded)
                continue;
            ForgeLog.Source.LogInfo($"Pack {pack.Manifest.Id} changed, reloading…");
            foreach (var problem in pack.Problems)
                ForgeLog.Source.LogWarning($"  {problem}");
            _registry.Reload(pack);
        }
    }

    private void OnDestroy() => _watcher?.Dispose();

    private List<LoadedPack> LoadPacks(IEnumerable<string> searchRoots, bool isDev)
    {
        var packs = new List<LoadedPack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in searchRoots)
        {
            var found = PackReader.FindPackRoots(root, (dir, ex) => ForgeLog.Source.LogWarning($"Skipped '{dir}' while looking for packs: {ex.Message}"));
            ForgeLog.Source.LogInfo($"Looking for packs in {root}: {found.Count} found");
            foreach (var packRoot in found)
            {
                if (!seen.Add(Path.GetFullPath(packRoot)))
                    continue;

                LoadedPack pack;
                try
                {
                    pack = PackReader.Load(packRoot);
                }
                catch (Exception ex)
                {
                    ForgeLog.Source.LogError($"Pack at '{packRoot}' failed to load: {ex.Message}");
                    continue;
                }

                if (_options.SkipEmbedded && pack.Manifest.Embedded)
                {
                    ForgeLog.Source.LogInfo($"Pack {pack.Manifest.Id}: loaded by its own mod (embedded), skipping");
                    continue;
                }

                if (_options.IsShared && ClaimedElsewhere(pack.Manifest.Id))
                {
                    ForgeLog.Source.LogWarning($"Pack {pack.Manifest.Id} at {pack.Root}: a mod already includes this pack, so this copy is skipped. Remove one of them.");
                    continue;
                }

                var tag = isDev ? " [push]" : "";
                ForgeLog.Source.LogInfo($"Pack {pack.Manifest.Id} {pack.Manifest.Version}{tag}: {pack.Recipes.Count} recipe(s)");
                foreach (var problem in pack.Problems)
                    ForgeLog.Source.LogWarning($"  {problem}");
                packs.Add(pack);
            }
        }

        return packs;
    }
}
