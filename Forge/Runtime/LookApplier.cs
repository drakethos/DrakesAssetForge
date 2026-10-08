using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesForge.Format;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>Applies a recipe's look: borrowed mesh, material overrides, icon.</summary>
internal static class LookApplier
{
    /// <summary>Mesh renderers in hierarchy order (particles, trails and lines excluded). Slot indices count across these.</summary>
    public static IEnumerable<Renderer> VisualRenderers(GameObject root) =>
        root.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer);

    /// <summary>
    /// Replaces the base mesh with a .glb from the pack (look.mesh.file). Each submesh becomes its own child renderer,
    /// textured from the file's base colour image. Material is a copy of the base's own, so it keeps the vanilla shader.
    /// </summary>
    private static void ApplyModelFile(GameObject prefab, string file, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        GlbModel model;
        try
        {
            model = GlbReader.Read(File.ReadAllBytes(Path.Combine(pack.Root, file)));
        }
        catch (Exception ex) when (ex is GlbException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"look.mesh.file '{file}': {ex.Message}");
            return;
        }
        foreach (var warning in model.Warnings)
            warnings.Add($"look.mesh.file '{file}': {warning}");

        var donors = VisualRenderers(prefab).ToList();
        var template = donors.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null);
        foreach (var donor in donors)
            donor.enabled = false;

        // Under the visual root (attach for items), so held items show the model as well as dropped ones.
        var visual = new GameObject(GameNames.VisualChild);
        visual.transform.SetParent(Parts.VisualRoot(prefab), false);

        // Named as the app names them (its material list), so material overrides can target "glb default" etc.
        var materials = model.Materials.Select((m, i) => GlbMaterial(m, m.Name ?? $"glb material {i}", template, textures, file, warnings)).ToList();
        var fallback = GlbMaterial(new GlbMaterial(), "glb default", template, textures, file, warnings);
        for (var i = 0; i < model.Submeshes.Count; i++)
        {
            var sub = model.Submeshes[i];
            var part = new GameObject($"glb{i}_{sub.Name ?? "mesh"}");
            part.transform.SetParent(visual.transform, false);

            var mesh = new Mesh { name = part.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = Vectors(sub.Positions);
            if (sub.Normals.Length > 0)
                mesh.normals = Vectors(sub.Normals);
            if (sub.Uvs.Length > 0)
                mesh.uv = Uvs(sub.Uvs);
            mesh.triangles = sub.Indices;
            if (sub.Normals.Length == 0)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = sub.Material >= 0 && sub.Material < materials.Count ? materials[sub.Material] : fallback;
        }
    }

    private static Material GlbMaterial(DrakesForge.Format.GlbMaterial source, string name, Material? template, TextureCache textures, string file, List<string> warnings)
    {
        var material = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        material.name = name;
        if (material.HasProperty("_Color") && source.BaseColor.Length == 4)
            material.SetColor("_Color", new Color(source.BaseColor[0], source.BaseColor[1], source.BaseColor[2], source.BaseColor[3]));
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", source.BaseColorImage != null ? textures.LoadBytes(source.BaseColorImage, $"{file} texture", warnings) : null);
        return material;
    }

    private static Vector3[] Vectors(float[] xyz)
    {
        var result = new Vector3[xyz.Length / 3];
        for (var i = 0; i < result.Length; i++)
            result[i] = new Vector3(xyz[i * 3], xyz[i * 3 + 1], xyz[i * 3 + 2]);
        return result;
    }

    private static Vector2[] Uvs(float[] uv)
    {
        var result = new Vector2[uv.Length / 2];
        for (var i = 0; i < result.Length; i++)
            result[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
        return result;
    }

    /// <summary>Turns off the base model's renderers listed by path ("rock_a/mesh", as the app writes them).</summary>
    internal static void HideMeshes(GameObject prefab, List<string> paths)
    {
        if (paths.Count == 0)
            return;
        var wanted = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (var renderer in VisualRenderers(prefab))
            if (wanted.Contains(RelativePath(renderer.transform, prefab.transform)))
                renderer.enabled = false;
    }

    private static string RelativePath(Transform t, Transform root)
    {
        var path = "";
        for (; t != null && t != root; t = t.parent)
            path = path.Length == 0 ? t.name : t.name + "/" + path;
        return path;
    }

    public static void Apply(GameObject prefab, LookRecipe look, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        if (look.Mesh != null)
            ApplyMesh(prefab, look.Mesh, pack, textures, warnings);
        var bodyOverrides = look.Materials.Where(m => m.Target == MaterialOverride.ArmorTarget).ToList();
        var meshOverrides = look.Materials.Where(m => m.Target != MaterialOverride.ArmorTarget).ToList();
        if (meshOverrides.Count > 0)
            ApplyMaterials(prefab, meshOverrides, pack, textures, warnings);
        foreach (var body in bodyOverrides)
            ApplyArmorMaterial(prefab, body, pack, textures, warnings);
        if (look.Icon != null)
            ApplyIcon(prefab, look.Icon, pack, textures, warnings);
        // Last, so material overrides and slot numbers only ever see the model's own renderers.
        // hideMesh hides the model's own meshes only: before parts and sprites are added.
        if (look.HideMesh)
            foreach (var renderer in VisualRenderers(prefab))
                renderer.enabled = false;
        HideMeshes(prefab, look.HideMeshes);
        Parts.Apply(prefab, look, pack, textures, warnings);
        HoldPose.Apply(prefab, look);
        Sprites.Apply(prefab, look, pack, textures, warnings);
    }

    private static void ApplyMesh(GameObject prefab, MeshSource mesh, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        if (mesh.File != null)
        {
            ApplyModelFile(prefab, mesh.File, pack, textures, warnings);
            return;
        }

        var source = PrefabManager.Instance.GetPrefab(mesh.Prefab);
        if (source == null)
        {
            warnings.Add($"look.mesh.prefab '{mesh.Prefab}' not found");
            return;
        }

        // Only the highest-detail LOD, otherwise every LOD level would draw on top of each other.
        var lodGroup = source.GetComponentInChildren<LODGroup>(true);
        var sourceRenderers = lodGroup != null && lodGroup.GetLODs().Length > 0
            ? lodGroup.GetLODs()[0].renderers.Where(r => r != null)
            : VisualRenderers(source);

        foreach (var donor in VisualRenderers(prefab))
            donor.enabled = false;

        var visual = new GameObject(GameNames.VisualChild);
        visual.transform.SetParent(Parts.VisualRoot(prefab), false);

        var srcRoot = source.transform;
        var copied = 0;
        foreach (var renderer in sourceRenderers)
        {
            if (renderer is not MeshRenderer meshRenderer || renderer.GetComponent<MeshFilter>() is not { } filter)
            {
                warnings.Add($"look.mesh: skipped '{renderer.name}' (only static meshes can be borrowed)");
                continue;
            }

            var part = new GameObject(renderer.name);
            part.transform.SetParent(visual.transform, false);
            part.transform.localPosition = srcRoot.InverseTransformPoint(renderer.transform.position);
            part.transform.localRotation = Quaternion.Inverse(srcRoot.rotation) * renderer.transform.rotation;
            var srcScale = srcRoot.lossyScale;
            var scale = renderer.transform.lossyScale;
            part.transform.localScale = new Vector3(scale.x / srcScale.x, scale.y / srcScale.y, scale.z / srcScale.z);
            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = LookBaseline.OriginalMaterials(source, meshRenderer);
            copied++;
        }

        if (copied == 0)
            warnings.Add($"look.mesh.prefab '{mesh.Prefab}' has no static meshes to borrow");
        if (prefab.GetComponent<ItemDrop>() != null)
            warnings.Add("look.mesh changes the dropped/world model only; the equipped model is unchanged");
    }

    internal static void ApplyMaterials(GameObject prefab, List<MaterialOverride> overrides, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        // Built once per override + original material, so slots sharing a material keep sharing it.
        var built = new Dictionary<(int, Material), Material>();
        var slot = 0;
        var matchedAny = new bool[overrides.Count];

        foreach (var renderer in VisualRenderers(prefab))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var i = 0; i < materials.Length; i++, slot++)
            {
                var original = materials[i];
                if (original == null)
                    continue;

                for (var o = 0; o < overrides.Count; o++)
                {
                    var ov = overrides[o];
                    if (!Matches(ov, original, slot))
                        continue;
                    matchedAny[o] = true;

                    var current = materials[i];
                    if (!built.TryGetValue((o, current), out var result))
                    {
                        result = Build(ov, current, pack, textures, warnings);
                        built[(o, current)] = result;
                    }

                    if (result != current)
                    {
                        materials[i] = result;
                        changed = true;
                    }
                }
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        for (var o = 0; o < overrides.Count; o++)
        {
            if (matchedAny[o])
                continue;
            var ov = overrides[o];
            warnings.Add(ov.Target != null
                ? $"look.materials: no material named '{ov.Target}' on this prefab"
                : $"look.materials: no slot {ov.Slot} (prefab has {slot})");
        }
    }

    /// <summary>Chest/legs armour: the material Valheim paints on the player's body (applied on equip).</summary>
    private static void ApplyArmorMaterial(GameObject prefab, MaterialOverride ov, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        var shared = prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
        if (shared?.m_armorMaterial == null)
        {
            warnings.Add("look.materials @armor: this item has no body armour material (only chest and leg armour do)");
            return;
        }

        shared.m_armorMaterial = Build(ov, shared.m_armorMaterial, pack, textures, warnings, borrowArmor: true);
    }

    private static bool Matches(MaterialOverride ov, Material original, int slot)
    {
        if (ov.Target != null)
            return GameNames.MaterialName(original) == ov.Target;
        if (ov.Slot.HasValue)
            return ov.Slot.Value == slot;
        return true;
    }

    private static Material Build(MaterialOverride ov, Material original, LoadedPack pack, TextureCache textures, List<string> warnings, bool borrowArmor = false)
    {
        var source = (borrowArmor ? ResolveArmorSource(ov, warnings) : ResolveSource(ov, warnings)) ?? original;
        var edits = ov.Shader != null || ov.Tint != null || ov.Textures.Count > 0 || ov.Floats.Count > 0 || ov.Colors.Count > 0;
        if (!edits)
            return source;

        var material = new Material(source) { name = GameNames.MaterialName(source) + "_forge" };

        if (ov.Shader != null)
        {
            var shader = Shader.Find(ov.Shader);
            if (shader != null)
                material.shader = shader;
            else
                warnings.Add($"shader '{ov.Shader}' not found");
        }

        if (ov.Tint != null && RecipeSerializer.TryParseColor(ov.Tint, out var tint))
            SetColor(material, "_Color", tint, warnings);

        foreach (var c in ov.Colors)
            if (RecipeSerializer.TryParseColor(c.Value, out var color))
                SetColor(material, c.Key, color, warnings);

        foreach (var f in ov.Floats)
        {
            if (material.HasProperty(f.Key))
                material.SetFloat(f.Key, f.Value);
            else
                warnings.Add($"shader {material.shader.name} has no property {f.Key}");
        }

        foreach (var t in ov.Textures)
        {
            if (!material.HasProperty(t.Key))
            {
                warnings.Add($"shader {material.shader.name} has no texture {t.Key}");
                continue;
            }

            var tex = textures.Load(pack.Resolve(t.Value), t.Value, warnings, IsNormalMap(t.Key));
            if (tex != null)
                material.SetTexture(t.Key, tex);
        }

        return material;
    }

    /// <summary>Normal maps need linear color space and Unity's packed layout (see TextureCache).</summary>
    private static bool IsNormalMap(string property) =>
        property.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || property.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Borrowing for the body: another armour's body material, as it was before Forge.</summary>
    private static Material? ResolveArmorSource(MaterialOverride ov, List<string> warnings)
    {
        if (ov.FromPrefab == null)
            return ov.FromMaterial != null ? PrefabManager.Cache.GetPrefab<Material>(ov.FromMaterial) : null;
        var prefab = PrefabManager.Instance.GetPrefab(ov.FromPrefab);
        var material = prefab != null ? LookBaseline.OriginalArmorMaterial(prefab) : null;
        if (material == null)
            warnings.Add($"'{ov.FromPrefab}' has no body armour material to borrow");
        return material;
    }

    private static Material? ResolveSource(MaterialOverride ov, List<string> warnings)
    {
        if (ov.FromPrefab != null)
        {
            var prefab = PrefabManager.Instance.GetPrefab(ov.FromPrefab);
            if (prefab == null)
            {
                warnings.Add($"material source prefab '{ov.FromPrefab}' not found");
                return null;
            }

            var materials = LookBaseline.OriginalMaterials(prefab).Where(m => m != null).ToList();
            if (ov.FromMaterial == null)
                return materials.FirstOrDefault();

            var named = materials.FirstOrDefault(m => GameNames.MaterialName(m) == ov.FromMaterial);
            if (named == null)
                warnings.Add($"'{ov.FromPrefab}' has no material '{ov.FromMaterial}' (has: {string.Join(", ", materials.Select(GameNames.MaterialName).Distinct())})");
            return named;
        }

        if (ov.FromMaterial != null)
        {
            var material = PrefabManager.Cache.GetPrefab<Material>(ov.FromMaterial);
            if (material == null)
                warnings.Add($"material '{ov.FromMaterial}' not found");
            return material;
        }

        return null;
    }

    private static void SetColor(Material material, string property, float[] rgba, List<string> warnings)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, new Color(rgba[0], rgba[1], rgba[2], rgba[3]));
            // Emission only shows when the shader keyword is on (vanilla materials without glow have it off).
            if (property == "_EmissionColor")
            {
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
        else
            warnings.Add($"shader {material.shader.name} has no color {property}");
    }

    private static void ApplyIcon(GameObject prefab, string iconPath, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        var sprite = textures.LoadSprite(pack.Resolve(iconPath), iconPath, warnings);
        if (sprite == null)
            return;

        if (prefab.GetComponent<ItemDrop>() is { } drop)
            drop.m_itemData.m_shared.m_icons = new[] { sprite };
        else if (prefab.GetComponent<Piece>() is { } piece)
            piece.m_icon = sprite;
        else
            warnings.Add("look.icon: prefab is neither an item nor a piece");
    }
}
