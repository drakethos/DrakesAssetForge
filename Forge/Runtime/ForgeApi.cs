using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>One material slot of a prefab, as it was before Forge changed anything.</summary>
public sealed class ForgePart
{
    internal ForgePart(int slot, string rendererPath, string materialName)
    {
        Slot = slot;
        RendererPath = rendererPath;
        MaterialName = materialName;
    }

    /// <summary>Index across the prefab's mesh renderers (what a recipe's <c>slot</c> means).</summary>
    public int Slot { get; }
    /// <summary>Where the renderer sits under the prefab root ("attach/model").</summary>
    public string RendererPath { get; }
    /// <summary>The vanilla material name (what a recipe's <c>target</c> matches).</summary>
    public string MaterialName { get; }
}

/// <summary>What editors such as Forge Bench may ask Forge Runtime about the player's own game.</summary>
public static class ForgeApi
{
    /// <summary>The material slots of a vanilla prefab, in recipe slot order, with their original names.</summary>
    public static IReadOnlyList<ForgePart> PartsOf(string prefabName)
    {
        var prefab = PrefabManager.Instance.GetPrefab(prefabName);
        return prefab == null ? new List<ForgePart>() : PartsOf(prefab);
    }

    public static IReadOnlyList<ForgePart> PartsOf(GameObject prefab)
    {
        var parts = new List<ForgePart>();
        var slot = 0;
        foreach (var renderer in LookApplier.VisualRenderers(prefab))
        {
            foreach (var material in LookBaseline.OriginalMaterials(prefab, renderer))
            {
                if (material != null)
                    parts.Add(new ForgePart(slot, PathOf(renderer.transform, prefab.transform), GameNames.MaterialName(material)));
                slot++;
            }
        }

        return parts;
    }

    /// <summary>The distinct vanilla material names of a prefab that could be borrowed from it.</summary>
    public static IReadOnlyList<string> MaterialsOf(string prefabName) =>
        PartsOf(prefabName).Select(p => p.MaterialName).Distinct().ToList();

    private static string PathOf(Transform t, Transform root)
    {
        var path = "";
        for (; t != null && t != root; t = t.parent)
            path = "/" + t.name + path;
        return path.TrimStart('/');
    }
}
