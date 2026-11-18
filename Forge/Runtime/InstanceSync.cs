using System.Collections.Generic;
using System.Linq;
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

        SyncItemData(prefab);
        SyncEquipped(prefab);

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
            HoldPose.CopyToInstance(prefab, instance);
            ReplaceChild(prefab, instance, GameNames.GlowChild);
            ReplaceChild(prefab, instance, GameNames.SpritesChild);
            SyncParts(prefab, instance);
            SyncCollision(prefab, instance);

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

    /// <summary>
    /// Items already made keep their own pointer to the stats (damage, durability, weight…), so a changed field
    /// would only show on new ones. Point every copy in the player's pack, open containers and on the ground back
    /// at the prefab's shared data.
    /// </summary>
    private static void SyncItemData(GameObject prefab)
    {
        var shared = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
        if (shared == null)
            return;

        void Fix(ItemDrop.ItemData? item)
        {
            if (item?.m_dropPrefab != null && item.m_dropPrefab.name == prefab.name)
                item.m_shared = shared;
        }

        if (Player.m_localPlayer != null)
            foreach (var item in Player.m_localPlayer.GetInventory().GetAllItems())
                Fix(item);
        foreach (var container in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            if (container.GetInventory() is { } inventory)
                foreach (var item in inventory.GetAllItems())
                    Fix(item);
        foreach (var drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            Fix(drop.m_itemData);
    }

    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

    // Humanoid's equipped item → the VisEquipment field holding its model in the world. Reflection: several are protected.
    private static readonly (string Item, string Instance)[] EquippedSlots =
    {
        ("m_rightItem", "m_rightItemInstance"), ("m_leftItem", "m_leftItemInstance"), ("m_helmetItem", "m_helmetItemInstance"),
        ("m_chestItem", "m_chestItemInstances"), ("m_legItem", "m_legItemInstances"),
        ("m_shoulderItem", "m_shoulderItemInstances"), ("m_utilityItem", "m_utilityItemInstances")
    };

    /// <summary>The model in the local player's hand or on their body is cloned when equipped; give it the prefab's new materials too.</summary>
    private static void SyncEquipped(GameObject prefab)
    {
        var player = Player.m_localPlayer;
        var vis = player != null ? player.GetComponent<VisEquipment>() : null;
        if (vis == null)
            return;

        var root = prefab.transform.Find("attach_skin") ?? Parts.VisualRoot(prefab);
        var source = new Dictionary<string, Renderer>();
        foreach (var renderer in LookApplier.VisualRenderers(prefab))
            source[PathOf(renderer.transform, root)] = renderer;

        foreach (var (itemField, instanceField) in EquippedSlots)
        {
            var item = typeof(Humanoid).GetField(itemField, Flags)?.GetValue(player) as ItemDrop.ItemData;
            if (item?.m_dropPrefab == null || item.m_dropPrefab.name != prefab.name)
                continue;

            var held = typeof(VisEquipment).GetField(instanceField, Flags)?.GetValue(vis);
            var instances = held is GameObject one ? new List<GameObject> { one } : (held as IEnumerable<GameObject>)?.ToList();
            if (instances == null)
                continue;
            foreach (var instance in instances)
            {
                if (instance == null)
                    continue;
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                    if (source.TryGetValue(PathOf(renderer.transform, instance.transform), out var from))
                        renderer.sharedMaterials = from.sharedMaterials;
            }
        }
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

    /// <summary>Forge's collision child, and which of the base's solid colliders are on, matched by path.</summary>
    private static void SyncCollision(GameObject prefab, GameObject instance)
    {
        ReplaceChild(prefab, instance, GameNames.CollisionChild);
        var enabled = new Dictionary<string, bool>();
        foreach (var c in Collision.BaseSolids(prefab))
            enabled[PathOf(c.transform, prefab.transform) + "|" + c.GetType().Name] = c.enabled;
        foreach (var c in Collision.BaseSolids(instance))
            if (enabled.TryGetValue(PathOf(c.transform, instance.transform) + "|" + c.GetType().Name, out var on))
                c.enabled = on;
    }

    private static void ReplaceChild(GameObject prefab, GameObject instance, string name)
    {
        // The visual child lives under the visual root (attach for items); other children under the prefab root.
        var from = name == GameNames.VisualChild ? Parts.VisualRoot(prefab) : prefab.transform;
        var to = name == GameNames.VisualChild ? Parts.VisualRoot(instance) : instance.transform;
        LookBaseline.DestroyChild(to.gameObject, name);
        var source = from.Find(name);
        if (source == null)
            return;
        var copy = Object.Instantiate(source.gameObject, to, false);
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
