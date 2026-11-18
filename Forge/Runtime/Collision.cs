using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// look.collision: swaps the base's solid colliders for one box or the visible model's meshes. The base's colliders are
/// switched off rather than removed (triggers stay on), so a reload can switch them back on (see LookBaseline).
/// </summary>
internal static class Collision
{
    /// <summary>Solid colliders Forge didn't add: the ones a collision setting replaces.</summary>
    public static List<Collider> BaseSolids(GameObject prefab) =>
        prefab.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && !IsForge(c.transform, prefab.transform)).ToList();

    public static void Apply(GameObject prefab, LookRecipe look, List<string> warnings)
    {
        if (look.Collision is not { } collision || collision.Mode == CollisionMode.Base)
            return;

        var solids = BaseSolids(prefab).Where(c => c.enabled).ToList();
        // Same layer as the base's collision, so placement, building on top and hits treat it the same way.
        var holder = new GameObject(GameNames.CollisionChild) { layer = solids.FirstOrDefault()?.gameObject.layer ?? prefab.layer };
        holder.transform.SetParent(prefab.transform, false);

        var added = collision.Mode == CollisionMode.Box ? AddBox(holder, collision) : AddMeshes(prefab, holder, warnings);
        if (added == 0)
        {
            warnings.Add("look.collision: nothing to collide with was made, so the base's collision stays");
            Object.DestroyImmediate(holder);
            return;
        }

        foreach (var solid in solids)
            solid.enabled = false;
    }

    private static int AddBox(GameObject holder, CollisionRecipe collision)
    {
        var box = holder.AddComponent<BoxCollider>();
        box.center = new Vector3(collision.Center.X, collision.Center.Y, collision.Center.Z);
        box.size = new Vector3(collision.Size.X, collision.Size.Y, collision.Size.Z);
        return 1;
    }

    /// <summary>A collider per drawn mesh (model file, borrowed mesh, kitbash parts), posed like its renderer.</summary>
    private static int AddMeshes(GameObject prefab, GameObject holder, List<string> warnings)
    {
        // Physics objects (dropped items) can only use convex mesh colliders.
        var convex = prefab.GetComponent<Rigidbody>() != null;
        var root = prefab.transform;
        var added = 0;
        foreach (var renderer in LookApplier.VisualRenderers(prefab).OfType<MeshRenderer>())
        {
            if (!renderer.enabled || renderer.GetComponent<MeshFilter>()?.sharedMesh is not { } mesh)
                continue;
            if (!mesh.isReadable)
            {
                warnings.Add($"look.collision: mesh '{mesh.name}' can't be used for collision (Valheim keeps it unreadable); use a box instead");
                continue;
            }

            var t = renderer.transform;
            var part = new GameObject(renderer.name) { layer = holder.layer };
            part.transform.SetParent(holder.transform, false);
            part.transform.localPosition = root.InverseTransformPoint(t.position);
            part.transform.localRotation = Quaternion.Inverse(root.rotation) * t.rotation;
            var rootScale = root.lossyScale;
            var scale = t.lossyScale;
            part.transform.localScale = new Vector3(scale.x / rootScale.x, scale.y / rootScale.y, scale.z / rootScale.z);
            var collider = part.AddComponent<MeshCollider>();
            collider.convex = convex;
            collider.sharedMesh = mesh;
            added++;
        }

        return added;
    }

    private static bool IsForge(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name == GameNames.CollisionChild && t.parent == root)
                return true;
        return false;
    }
}
