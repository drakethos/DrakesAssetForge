using System;
using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using Jotunn.Managers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DrakesForge.Runtime;

/// <summary>
/// Kitbashing: other vanilla prefabs' meshes placed on this one (<c>look.parts</c>), and the model's overall scale
/// (<c>look.scale</c>). Parts are positioned in the prefab's own space, like the app's preview. On items they live
/// under <c>attach</c>, so they show when the item is held as well as when it's dropped.
/// </summary>
internal static class Parts
{
    /// <summary>Where an item's or piece's visible model lives: items show <c>attach</c> both dropped and held.</summary>
    public static Transform VisualRoot(GameObject prefab) =>
        prefab.GetComponent<ItemDrop>() != null && prefab.transform.Find("attach") is { } attach ? attach : prefab.transform;

    public static void Apply(GameObject prefab, LookRecipe look, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        var visualRoot = VisualRoot(prefab);
        if (look.HasScale)
            visualRoot.localScale = Vector3.Scale(visualRoot.localScale, new Vector3(look.Scale.X, look.Scale.Y, look.Scale.Z));
        if (look.Parts.Count == 0)
            return;

        // The holder sits at the prefab root's pose so part positions mean the same as in the app.
        var holder = new GameObject(GameNames.PartsChild);
        holder.transform.SetParent(visualRoot, false);
        holder.transform.position = prefab.transform.position;
        holder.transform.rotation = prefab.transform.rotation;
        holder.transform.localScale = Vector3.one;

        for (var i = 0; i < look.Parts.Count; i++)
        {
            var part = look.Parts[i];
            var source = PrefabManager.Instance.GetPrefab(part.Prefab);
            if (source == null)
            {
                warnings.Add($"look.parts[{i}]: prefab '{part.Prefab}' not found");
                continue;
            }

            var go = new GameObject($"part{i}_{part.Prefab}");
            go.transform.SetParent(holder.transform, false);
            go.transform.localPosition = new Vector3(part.Position.X, part.Position.Y, part.Position.Z);
            go.transform.localRotation = Quaternion.Euler(part.Rotation.X, part.Rotation.Y, part.Rotation.Z);
            go.transform.localScale = new Vector3(part.Scale.X, part.Scale.Y, part.Scale.Z);

            var copied = CopyMeshes(source, go.transform, part.Child);
            if (copied == 0)
            {
                warnings.Add(part.Child == null
                    ? $"look.parts[{i}]: '{part.Prefab}' has no meshes to borrow"
                    : $"look.parts[{i}]: no mesh in '{part.Prefab}' whose name contains '{part.Child}'");
                continue;
            }

            if (part.Materials.Count > 0)
                LookApplier.ApplyMaterials(go, part.Materials, pack, textures, warnings);
        }
    }

    /// <summary>
    /// Copies the source's highest-detail meshes (static, or skinned in its rest pose) under <paramref name="parent"/>,
    /// keeping their layout relative to the source root and its vanilla materials.
    /// </summary>
    private static int CopyMeshes(GameObject source, Transform parent, string? child)
    {
        var lod = source.GetComponentInChildren<LODGroup>(true);
        IEnumerable<Renderer> renderers = lod != null && lod.GetLODs().Length > 0
            ? lod.GetLODs()[0].renderers.Where(r => r != null)
            : LookApplier.VisualRenderers(source);

        var root = source.transform;
        var copied = 0;
        foreach (var renderer in renderers)
        {
            var mesh = renderer switch
            {
                MeshRenderer when renderer.GetComponent<MeshFilter>() is { } filter => filter.sharedMesh,
                SkinnedMeshRenderer skinned => skinned.sharedMesh,
                _ => null
            };
            if (mesh == null || (child != null && !NameMatches(renderer.transform, root, child)))
                continue;

            var copy = new GameObject(renderer.name);
            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = root.InverseTransformPoint(renderer.transform.position);
            copy.transform.localRotation = Quaternion.Inverse(root.rotation) * renderer.transform.rotation;
            var rootScale = root.lossyScale;
            var scale = renderer.transform.lossyScale;
            copy.transform.localScale = new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z);
            copy.AddComponent<MeshFilter>().sharedMesh = mesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = LookBaseline.OriginalMaterials(source, renderer);
            copied++;
        }

        return copied;
    }

    /// <summary>The renderer's own name or any parent's (up to the source root) contains <paramref name="child"/>.</summary>
    private static bool NameMatches(Transform t, Transform root, string child)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.IndexOf(child, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    /// <summary>Removes the parts holder wherever it is (root or attach).</summary>
    public static void Remove(GameObject prefab)
    {
        var holder = VisualRoot(prefab).Find(GameNames.PartsChild);
        if (holder != null)
            Object.DestroyImmediate(holder.gameObject);
    }
}
