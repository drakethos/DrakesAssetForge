using DrakesForge.Format;

namespace DrakesForge.App.Services;

/// <summary>
/// Build/craft costs must name items by prefab (Wood, Bronze) or by an item id from the pack. A display name
/// ("Piece of Paper") or a typo leaves an empty requirement in game, which crashes removing the piece.
/// </summary>
public static class CostItems
{
    /// <summary>Item ids a cost can use from this pack (items and reskinned items, not pieces).</summary>
    public static IEnumerable<string> PackItemIds(IEnumerable<ItemRecipe> recipes) =>
        recipes.Where(r => r.Kind != RecipeKind.Piece).Select(r => r.Id);

    /// <summary>
    /// The prefab name a cost item should use, and a note when it isn't usable as written.
    /// Returns the pack item's id for its display name; null (with a note) when nothing matches.
    /// </summary>
    public static string? Resolve(string item, IReadOnlyCollection<ItemRecipe> recipes, IReadOnlySet<string> vanillaItems, out string? note)
    {
        note = null;
        var name = item.Trim();
        if (name.Length == 0)
        {
            note = "a cost has no item";
            return null;
        }

        if (vanillaItems.Contains(name) || PackItemIds(recipes).Contains(name, StringComparer.Ordinal))
            return name;

        var byName = recipes.FirstOrDefault(r => r.Kind != RecipeKind.Piece && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (byName != null)
        {
            note = $"'{name}' is a display name; the cost needs the item id '{byName.Id}'";
            return byName.Id;
        }

        var caseFix = vanillaItems.FirstOrDefault(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
        if (caseFix != null)
        {
            note = $"'{name}' should be written '{caseFix}'";
            return caseFix;
        }

        note = $"no item '{name}' (use a prefab name like Wood or Bronze, or an item id from this pack)";
        return null;
    }
}
