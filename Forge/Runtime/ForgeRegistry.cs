using System;
using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesForge.Runtime;

internal sealed class ForgeEntry
{
    public ForgeEntry(ItemRecipe recipe, LoadedPack pack, GameObject prefab)
    {
        Recipe = recipe;
        Pack = pack;
        Prefab = prefab;
        // Structural changes first: the baseline is "the item as built", so hot reload never tries to undo them.
        ComponentChanges.Apply(prefab, recipe, StructuralWarnings);
        Baseline = LookBaseline.Capture(prefab);
    }

    /// <summary>Problems from removing/adding components when the item was built.</summary>
    public List<string> StructuralWarnings { get; } = new();

    public ItemRecipe Recipe { get; set; }
    public LoadedPack Pack { get; set; }
    public GameObject Prefab { get; }
    /// <summary>The prefab as it was before Forge touched it; hot reload resets to this first.</summary>
    public LookBaseline Baseline { get; }
}

/// <summary>Builds recipes into Jotunn items/pieces (or edits vanilla prefabs for reskins) and re-applies them on reload.</summary>
internal sealed class ForgeRegistry
{
    private readonly Dictionary<string, ForgeEntry> _entries = new(StringComparer.Ordinal);
    private readonly TextureCache _textures = new();
    private readonly Action<ForgeBuiltContext>? _onBuilt;

    public ForgeRegistry(Action<ForgeBuiltContext>? onBuilt = null) => _onBuilt = onBuilt;

    /// <summary>Runs the owning mod's hook; a throwing hook is reported, never breaks the build.</summary>
    private void Built(ForgeEntry entry, bool isReload, List<string> warnings)
    {
        if (_onBuilt == null)
            return;
        try
        {
            _onBuilt(new ForgeBuiltContext(entry.Recipe, entry.Prefab, entry.Pack, isReload));
        }
        catch (Exception ex)
        {
            warnings.Add($"OnBuilt hook threw: {ex}");
        }
    }

    // Every recipe id in the loaded packs, and display name -> id, so costs can name items from any pack.
    private readonly HashSet<string> _packIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _packNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _sanitizeHooked;

    public void RegisterAll(IEnumerable<LoadedPack> packs)
    {
        var counts = new Dictionary<RecipeKind, int>();
        var all = packs.ToList();
        foreach (var recipe in all.SelectMany(p => p.Recipes))
        {
            _packIds.Add(recipe.Id);
            if (recipe.Name != null && recipe.Kind != RecipeKind.Piece && !_packNames.ContainsKey(recipe.Name))
                _packNames[recipe.Name] = recipe.Id;
        }

        if (!_sanitizeHooked)
        {
            // Last line of defence: a cost Jotunn couldn't resolve leaves an empty requirement, which crashes
            // Piece.DropResources when the piece is removed. Drop those once everything is registered.
            _sanitizeHooked = true;
            PieceManager.OnPiecesRegistered += SanitizeRequirements;
            ItemManager.OnItemsRegistered += SanitizeRequirements;
        }

        foreach (var pack in all)
        {
            foreach (var recipe in pack.Recipes)
            {
                if (_entries.TryGetValue(recipe.Id, out var existing))
                {
                    ForgeLog.Source.LogWarning($"{recipe.Id}: already defined by pack {existing.Pack.Manifest.Id}; skipping the copy in {pack.Manifest.Id}.");
                    continue;
                }

                try
                {
                    if (Register(pack, recipe))
                        counts[recipe.Kind] = counts.TryGetValue(recipe.Kind, out var n) ? n + 1 : 1;
                }
                catch (Exception ex)
                {
                    ForgeLog.Source.LogError($"{recipe.Id}: failed to build: {ex}");
                }
            }
        }

        var summary = string.Join(", ", counts.Select(c => $"{c.Value} {c.Key.ToString().ToLowerInvariant()}(s)"));
        ForgeLog.Source.LogInfo(summary.Length == 0 ? "Forge: nothing to build." : $"Forge built {summary}.");
    }

    public void Reload(LoadedPack pack)
    {
        _textures.Clear();
        foreach (var recipe in pack.Recipes)
        {
            if (!_entries.TryGetValue(recipe.Id, out var entry))
            {
                ForgeLog.Source.LogWarning($"{recipe.Id}: new since launch. Restart the game to register it.");
                continue;
            }

            if (!string.Equals(entry.Pack.Root, pack.Root, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.Recipe.RemoveComponents.SequenceEqual(recipe.RemoveComponents) ||
                !entry.Recipe.AddComponents.Select(a => a.Type).SequenceEqual(recipe.AddComponents.Select(a => a.Type)))
                ForgeLog.Source.LogWarning($"{recipe.Id}: added/removed components changed. Restart the game for that part; the rest reloads now.");
            if (entry.Recipe.Kind != recipe.Kind || entry.Recipe.Base != recipe.Base)
            {
                ForgeLog.Source.LogWarning($"{recipe.Id}: kind or base changed. Restart the game to rebuild it.");
                continue;
            }

            try
            {
                entry.Recipe = recipe;
                entry.Pack = pack;
                entry.Baseline.Restore(entry.Prefab);
                var warnings = Apply(entry);
                if (recipe.Kind != RecipeKind.Reskin)
                    CraftUpdater.Apply(entry, warnings, ResolveItem);
                Built(entry, true, warnings);
                var synced = InstanceSync.Sync(entry.Prefab);
                Report(entry, warnings, $"reloaded, {synced} placed/dropped instance(s) updated");
            }
            catch (Exception ex)
            {
                ForgeLog.Source.LogError($"{recipe.Id}: reload failed: {ex}");
            }
        }
    }

    private bool Register(LoadedPack pack, ItemRecipe recipe)
    {
        var basePrefab = PrefabManager.Instance.GetPrefab(recipe.Base);
        if (basePrefab == null)
        {
            ForgeLog.Source.LogWarning($"{recipe.Id}: base prefab '{recipe.Base}' isn't in this Valheim version.");
            return false;
        }

        ForgeEntry entry;
        switch (recipe.Kind)
        {
            case RecipeKind.Item:
            {
                if (basePrefab.GetComponent<ItemDrop>() == null)
                {
                    ForgeLog.Source.LogWarning($"{recipe.Id}: '{recipe.Base}' is not an item (no ItemDrop). Use kind \"piece\"?");
                    return false;
                }

                var custom = new CustomItem(recipe.Id, recipe.Base, new ItemConfig
                {
                    Name = recipe.Name,
                    Description = recipe.Description,
                    CraftingStation = GameNames.Station(recipe.Craft?.Station),
                    MinStationLevel = recipe.Craft?.StationLevel ?? 1,
                    Requirements = Requirements(recipe)
                });
                entry = new ForgeEntry(recipe, pack, custom.ItemPrefab);
                Report(entry, ApplyAndBuild(entry), $"item from {recipe.Base}");
                ItemManager.Instance.AddItem(custom);
                break;
            }
            case RecipeKind.Piece:
            {
                if (basePrefab.GetComponent<Piece>() == null)
                {
                    ForgeLog.Source.LogWarning($"{recipe.Id}: '{recipe.Base}' is not a build piece (no Piece). Use kind \"item\"?");
                    return false;
                }

                var custom = new CustomPiece(recipe.Id, recipe.Base, new PieceConfig
                {
                    Name = recipe.Name,
                    Description = recipe.Description,
                    PieceTable = GameNames.PieceTable(recipe.Craft?.Tool),
                    Category = recipe.Craft?.Category,
                    CraftingStation = GameNames.Station(recipe.Craft?.Station),
                    Requirements = Requirements(recipe)
                });
                entry = new ForgeEntry(recipe, pack, custom.PiecePrefab);
                Report(entry, ApplyAndBuild(entry), $"piece from {recipe.Base}");
                PieceManager.Instance.AddPiece(custom);
                break;
            }
            default:
                entry = new ForgeEntry(recipe, pack, basePrefab);
                Report(entry, ApplyAndBuild(entry), "reskin");
                break;
        }

        _entries[recipe.Id] = entry;
        return true;
    }

    private List<string> ApplyAndBuild(ForgeEntry entry)
    {
        var warnings = Apply(entry);
        warnings.InsertRange(0, entry.StructuralWarnings);
        Built(entry, false, warnings);
        return warnings;
    }

    /// <summary>Everything that lives on the prefab itself. Safe to call again after <see cref="LookBaseline.Restore"/>.</summary>
    private List<string> Apply(ForgeEntry entry)
    {
        var warnings = new List<string>();
        var recipe = entry.Recipe;
        var prefab = entry.Prefab;

        LookApplier.Apply(prefab, recipe.Look, entry.Pack, _textures, warnings);
        ApplyNameAndDescription(prefab, recipe);
        FieldSetter.Apply(prefab, recipe.Fields, warnings);
        if (recipe.Snap != null)
        {
            if (prefab.GetComponent<Piece>() == null)
                warnings.Add("snap points only work on pieces");
            else
                SnapPoints.Apply(prefab, recipe.Snap);
        }

        Effects.Apply(prefab, recipe.Effects, warnings);
        Behaviours.Apply(prefab, recipe.Behaviours, warnings);
        return warnings;
    }

    private static void ApplyNameAndDescription(GameObject prefab, ItemRecipe recipe)
    {
        if (recipe.Name == null && recipe.Description == null)
            return;

        if (prefab.GetComponent<ItemDrop>() is { } drop)
        {
            if (recipe.Name != null)
                drop.m_itemData.m_shared.m_name = recipe.Name;
            if (recipe.Description != null)
                drop.m_itemData.m_shared.m_description = recipe.Description;
        }

        if (prefab.GetComponent<Piece>() is { } piece)
        {
            if (recipe.Name != null)
                piece.m_name = recipe.Name;
            if (recipe.Description != null)
                piece.m_description = recipe.Description;
        }
    }

    private RequirementConfig[] Requirements(ItemRecipe recipe)
    {
        var result = new List<RequirementConfig>();
        foreach (var r in recipe.Craft?.Requirements ?? new List<Requirement>())
        {
            var item = ResolveItem(r.Item, out var note);
            if (note != null)
                ForgeLog.Source.LogWarning($"{recipe.Id}: craft: {note}");
            if (item != null)
                result.Add(new RequirementConfig(item, r.Amount, r.AmountPerLevel, r.Recover));
        }

        return result.ToArray();
    }

    /// <summary>
    /// A cost's item as a prefab name: a Valheim/mod item prefab, an item from a loaded pack, or (forgiving) a pack
    /// item's display name. Null when nothing matches, so the cost is left out instead of becoming an empty
    /// requirement (which crashes Piece.DropResources when the piece is removed).
    /// </summary>
    internal string? ResolveItem(string name, out string? note)
    {
        note = null;
        name = name.Trim();
        if (name.Length == 0)
            return null;
        if (_packIds.Contains(name) || PrefabManager.Instance.GetPrefab(name)?.GetComponent<ItemDrop>() != null)
            return name;
        if (_packNames.TryGetValue(name, out var id))
        {
            note = $"'{name}' is a display name; using the item id '{id}' (write the id in the recipe)";
            return id;
        }

        note = $"no item '{name}' (use its prefab name, e.g. Wood, Bronze, or an item id from your pack); left out of the cost";
        return null;
    }

    private void SanitizeRequirements()
    {
        foreach (var entry in _entries.Values)
        {
            if (entry.Prefab.GetComponent<Piece>() is { } piece && piece.m_resources != null && piece.m_resources.Any(r => r == null || r.m_resItem == null))
            {
                piece.m_resources = piece.m_resources.Where(r => r != null && r.m_resItem != null).ToArray();
                ForgeLog.Source.LogWarning($"{entry.Recipe.Id}: removed cost entries whose item doesn't exist");
            }

            if (ObjectDB.instance == null)
                continue;
            foreach (var recipe in ObjectDB.instance.m_recipes.Where(r => r != null && r.m_item != null && r.m_item.name == entry.Prefab.name))
                if (recipe.m_resources != null && recipe.m_resources.Any(r => r == null || r.m_resItem == null))
                {
                    recipe.m_resources = recipe.m_resources.Where(r => r != null && r.m_resItem != null).ToArray();
                    ForgeLog.Source.LogWarning($"{entry.Recipe.Id}: removed recipe cost entries whose item doesn't exist");
                }
        }
    }

    private static void Report(ForgeEntry entry, List<string> warnings, string what)
    {
        var log = ForgeLog.Source;
        if (warnings.Count == 0)
        {
            log.LogInfo($"  {entry.Recipe.Id}: {what}");
            return;
        }

        log.LogWarning($"  {entry.Recipe.Id}: {what}, {warnings.Count} problem(s):");
        foreach (var w in warnings.Distinct())
            log.LogWarning($"    - {w}");
    }
}
