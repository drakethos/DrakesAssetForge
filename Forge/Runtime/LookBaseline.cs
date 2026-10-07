using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// Snapshot of what Forge may change on a prefab (materials, renderer visibility, icons, snap points),
/// so a hot reload can start from the untouched prefab. Component fields are not snapshotted:
/// removing a field from a recipe keeps its last value until restart.
/// </summary>
internal sealed class LookBaseline
{
    // Prefabs Forge has changed, so borrowing from them always gets the vanilla look regardless of load order.
    private static readonly Dictionary<GameObject, LookBaseline> ByPrefab = new();

    private readonly List<(Renderer Renderer, Material[] Materials, bool Enabled)> _renderers = new();
    private Sprite[]? _itemIcons;
    private Material? _armorMaterial;
    private Sprite? _pieceIcon;
    private SnapPoints.Snapshot? _snaps;
    private Effects.Snapshot _effects = new();

    public static LookBaseline Capture(GameObject prefab)
    {
        var baseline = new LookBaseline();
        foreach (var renderer in LookApplier.VisualRenderers(prefab))
            baseline._renderers.Add((renderer, renderer.sharedMaterials, renderer.enabled));

        if (prefab.GetComponent<ItemDrop>() is { } drop)
        {
            baseline._itemIcons = drop.m_itemData.m_shared.m_icons;
            baseline._armorMaterial = drop.m_itemData.m_shared.m_armorMaterial;
        }
        if (prefab.GetComponent<Piece>() is { } piece)
        {
            baseline._pieceIcon = piece.m_icon;
            baseline._snaps = SnapPoints.Capture(prefab);
        }

        baseline._effects = Effects.Capture(prefab);
        ByPrefab[prefab] = baseline;
        return baseline;
    }

    /// <summary>Materials of <paramref name="prefab"/> as they were before Forge, in slot order.</summary>
    public static IEnumerable<Material> OriginalMaterials(GameObject prefab)
    {
        if (!ByPrefab.TryGetValue(prefab, out var baseline))
            return LookApplier.VisualRenderers(prefab).SelectMany(r => r.sharedMaterials);
        return baseline._renderers.SelectMany(r => r.Materials);
    }

    /// <summary>An armour's body material as it was before Forge.</summary>
    public static Material? OriginalArmorMaterial(GameObject prefab) =>
        ByPrefab.TryGetValue(prefab, out var baseline) ? baseline._armorMaterial : prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_armorMaterial;

    /// <summary>One renderer's pre-Forge materials (current ones if Forge never touched its prefab).</summary>
    public static Material[] OriginalMaterials(GameObject prefab, Renderer renderer)
    {
        if (ByPrefab.TryGetValue(prefab, out var baseline))
            foreach (var entry in baseline._renderers)
                if (entry.Renderer == renderer)
                    return entry.Materials;
        return renderer.sharedMaterials;
    }

    public void Restore(GameObject prefab)
    {
        DestroyChild(prefab, GameNames.VisualChild);
        DestroyChild(prefab, GameNames.GlowChild);
        DestroyChild(prefab, GameNames.SpritesChild);
        Effects.Restore(_effects);

        foreach (var (renderer, materials, enabled) in _renderers)
        {
            if (renderer == null)
                continue;
            renderer.sharedMaterials = materials;
            renderer.enabled = enabled;
        }

        if (prefab.GetComponent<ItemDrop>() is { } drop)
        {
            if (_itemIcons != null)
                drop.m_itemData.m_shared.m_icons = _itemIcons;
            drop.m_itemData.m_shared.m_armorMaterial = _armorMaterial;
        }
        if (prefab.GetComponent<Piece>() is { } piece)
        {
            piece.m_icon = _pieceIcon;
            if (_snaps != null)
                SnapPoints.Restore(prefab, _snaps);
        }
    }

    internal static void DestroyChild(GameObject root, string name)
    {
        var child = root.transform.Find(name);
        if (child != null)
            Object.DestroyImmediate(child.gameObject);
    }
}
