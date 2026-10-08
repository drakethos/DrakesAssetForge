using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// Hot reload of costs and stations. At launch Jotunn builds these from the configs; afterwards
/// they live on Piece.m_resources and the ObjectDB Recipe, so reload edits those directly.
/// </summary>
internal static class CraftUpdater
{
    public delegate string? ItemResolver(string name, out string? note);

    public static void Apply(ForgeEntry entry, List<string> warnings, ItemResolver resolve)
    {
        var craft = entry.Recipe.Craft;
        if (craft == null || ObjectDB.instance == null)
            return;

        var resources = craft.Requirements
            .Select(r =>
            {
                var name = resolve(r.Item, out var note);
                if (note != null)
                    warnings.Add("craft: " + note);
                return (r, drop: name == null ? null : ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>());
            })
            .Where(x =>
            {
                if (x.drop == null)
                    warnings.Add($"craft: no item named '{x.r.Item}'");
                return x.drop != null;
            })
            .Select(x => new Piece.Requirement
            {
                m_resItem = x.drop,
                m_amount = x.r.Amount,
                m_amountPerLevel = x.r.AmountPerLevel,
                m_recover = x.r.Recover
            })
            .ToArray();

        var station = FindStation(GameNames.Station(craft.Station), warnings);

        if (entry.Prefab.GetComponent<Piece>() is { } piece)
        {
            piece.m_resources = resources;
            piece.m_craftingStation = station;
            return;
        }

        var recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_item != null && r.m_item.name == entry.Prefab.name);
        if (recipe == null)
        {
            warnings.Add("craft: no recipe was registered at launch (restart to add one)");
            return;
        }

        recipe.m_resources = resources;
        recipe.m_craftingStation = station;
        recipe.m_minStationLevel = craft.StationLevel;
    }

    private static CraftingStation? FindStation(string? prefabName, List<string> warnings)
    {
        if (prefabName == null)
            return null;
        var station = PrefabManager.Instance.GetPrefab(prefabName)?.GetComponent<CraftingStation>();
        if (station == null)
            warnings.Add($"craft: no crafting station '{prefabName}'");
        return station;
    }
}
