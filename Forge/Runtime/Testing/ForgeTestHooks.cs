#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DrakesForge.Runtime.Testing;

/// <summary>
/// DrakesTestLab finds methods marked with an attribute named <c>TestHookAttribute</c> by name, with no reference to it.
/// Debug builds only: these never ship.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestHookAttribute : Attribute
{
    public TestHookAttribute(string name) => Name = name;
    public string Name { get; }
}

internal static class ForgeTestHooks
{
    /// <summary>
    /// What a built prefab draws: "glbParts=N;tris=N;textured=N;vanilla=N". Counts renderers on the prefab that are
    /// enabled (glb parts are the ones whose mesh is named glb*, vanilla is any other enabled mesh renderer).
    /// </summary>
    [TestHook("forge.drawReport")]
    private static string DrawReport(string prefab)
    {
        var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
        if (go == null && ObjectDB.instance != null)
            go = ObjectDB.instance.GetItemPrefab(prefab);
        if (go == null)
        {
            // What the game sees in the default push folder, to tell "pack missing" from "pack failed to build".
            var push = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrakesAssetForge", "Push");
            var seen = System.IO.Directory.Exists(push)
                ? string.Join(", ", System.IO.Directory.GetDirectories(push).Select(d => System.IO.Path.GetFileName(d) + (System.IO.File.Exists(System.IO.Path.Combine(d, "forgepack.json")) ? "" : "(no manifest)")).ToArray())
                : "(no push folder)";
            throw new InvalidOperationException($"prefab '{prefab}' isn't registered; push folder holds: {seen}");
        }

        int glb = 0, tris = 0, textured = 0, vanilla = 0;
        foreach (var r in LookApplier.VisualRenderers(go).Where(r => r.enabled))
        {
            var mesh = r is MeshRenderer ? r.GetComponent<MeshFilter>()?.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
            if (mesh != null && mesh.name.StartsWith("glb", StringComparison.Ordinal))
            {
                glb++;
                tris += mesh.triangles.Length / 3;
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_MainTex") && r.sharedMaterial.GetTexture("_MainTex") != null)
                    textured++;
            }
            else
            {
                vanilla++;
            }
        }

        return $"glbParts={glb};tris={tris};textured={textured};vanilla={vanilla}";
    }
}
#endif
