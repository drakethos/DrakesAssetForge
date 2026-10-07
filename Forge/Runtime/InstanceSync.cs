using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>After a hot reload, copies the prefab's look onto copies already placed or dropped in the world.</summary>
internal static class InstanceSync
{
    public static int Sync(GameObject prefab)
    {
        if (ZNetScene.instance == null)
            return 0;

        var prefabRenderers = new Dictionary<string, Renderer>();
        foreach (var renderer in LookApplier.VisualRenderers(prefab))
            prefabRenderers[PathOf(renderer.transform, prefab.transform)] = renderer;

        var count = 0;
        foreach (var view in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
        {
            var instance = view.gameObject;
            if (instance == prefab || GameNames.PrefabName(instance.name) != prefab.name)
                continue;

            CopyEffects(prefab, instance);
            ReplaceChild(prefab, instance, GameNames.VisualChild);
            ReplaceChild(prefab, instance, GameNames.GlowChild);
            ReplaceChild(prefab, instance, GameNames.SpritesChild);
            SyncParts(prefab, instance);

            foreach (var renderer in LookApplier.VisualRenderers(instance))
            {
                if (!prefabRenderers.TryGetValue(PathOf(renderer.transform, instance.transform), out var source))
                    continue;
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.enabled = source.enabled;
            }

            count++;
        }

        return count;
    }

    /// <summary>Light and particle settings, matched by path (fire colour on braziers already placed).</summary>
    private static void CopyEffects(GameObject prefab, GameObject instance)
    {
        var lights = new Dictionary<string, Light>();
        foreach (var l in prefab.GetComponentsInChildren<Light>(true))
            lights[PathOf(l.transform, prefab.transform)] = l;
        foreach (var l in instance.GetComponentsInChildren<Light>(true))
            if (lights.TryGetValue(PathOf(l.transform, instance.transform), out var src))
            {
                l.color = src.color;
                l.intensity = src.intensity;
                l.range = src.range;
            }

        var systems = new Dictionary<string, ParticleSystem>();
        foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            systems[PathOf(ps.transform, prefab.transform)] = ps;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            if (systems.TryGetValue(PathOf(ps.transform, instance.transform), out var src))
            {
                var main = ps.main;
                main.startColor = src.main.startColor;
                var col = ps.colorOverLifetime;
                col.color = src.colorOverLifetime.color;
            }
    }

    /// <summary>Kitbash parts and model scale live on the visual root (attach on items).</summary>
    private static void SyncParts(GameObject prefab, GameObject instance)
    {
        Parts.Remove(instance);
        var from = Parts.VisualRoot(prefab);
        var to = Parts.VisualRoot(instance);
        to.localScale = from.localScale;
        var holder = from.Find(GameNames.PartsChild);
        if (holder == null)
            return;
        var copy = Object.Instantiate(holder.gameObject, to, false);
        copy.name = GameNames.PartsChild;
        copy.transform.localPosition = holder.localPosition;
        copy.transform.localRotation = holder.localRotation;
        copy.transform.localScale = holder.localScale;
    }

    private static void ReplaceChild(GameObject prefab, GameObject instance, string name)
    {
        LookBaseline.DestroyChild(instance, name);
        var source = prefab.transform.Find(name);
        if (source == null)
            return;
        var copy = Object.Instantiate(source.gameObject, instance.transform, false);
        copy.name = name;
    }

    private static string PathOf(Transform t, Transform root)
    {
        var sb = new StringBuilder();
        for (; t != null && t != root; t = t.parent)
            sb.Insert(0, "/" + t.name);
        return sb.ToString();
    }
}
