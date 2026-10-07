using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// Flat images on a prefab: a quad per sprite under <see cref="GameNames.SpritesChild"/>, alpha cut out,
/// optionally two-sided (a second, mirrored face, so it works with any shader and text reads from both sides).
/// </summary>
internal static class Sprites
{
    private static Material? _template;

    public static void Apply(GameObject prefab, LookRecipe look, LoadedPack pack, TextureCache textures, List<string> warnings)
    {
        if (look.HideMesh)
            foreach (var renderer in LookApplier.VisualRenderers(prefab))
                renderer.enabled = false;
        if (look.Sprites.Count == 0)
            return;

        var root = new GameObject(GameNames.SpritesChild);
        root.transform.SetParent(prefab.transform, false);
        var index = 0;
        foreach (var sprite in look.Sprites)
        {
            var texture = textures.Load(pack.Resolve(sprite.File), sprite.File, warnings);
            if (texture == null)
                continue;
            texture.wrapMode = TextureWrapMode.Clamp;

            var go = new GameObject("sprite" + index++);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(sprite.Position.X, sprite.Position.Y, sprite.Position.Z);
            go.transform.localRotation = Quaternion.Euler(sprite.Rotation.X, sprite.Rotation.Y, sprite.Rotation.Z);
            go.AddComponent<MeshFilter>().sharedMesh = Quad(sprite.Width, sprite.Height, sprite.DoubleSided);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(texture);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        }
    }

    /// <summary>
    /// Pivot at the bottom centre, front facing +Z (the prefab's forward), image reading correctly from the front.
    /// The back face (if any) is mirrored back so it reads correctly too. Unity's front faces wind clockwise.
    /// </summary>
    public static Mesh Quad(float width, float height, bool doubleSided)
    {
        var w = width / 2f;
        var vertices = new List<Vector3> { new(-w, 0, 0), new(-w, height, 0), new(w, height, 0), new(w, 0, 0) };
        var normals = Enumerable.Repeat(Vector3.forward, 4).ToList();
        var uvs = new List<Vector2> { new(1, 0), new(1, 1), new(0, 1), new(0, 0) };
        var triangles = new List<int> { 2, 1, 0, 2, 0, 3 };
        if (doubleSided)
        {
            vertices.AddRange(vertices.ToList());
            normals.AddRange(Enumerable.Repeat(Vector3.back, 4));
            uvs.AddRange(new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) });
            triangles.AddRange(new[] { 4, 5, 6, 4, 6, 7 });
        }

        var mesh = new Mesh { name = "forge_sprite" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Material Material(Texture2D texture)
    {
        var material = new Material(Template()) { name = "forge_sprite_" + texture.name };
        material.SetTexture("_MainTex", texture);
        material.SetColor("_Color", Color.white);
        foreach (var slot in new[] { "_BumpMap", "_MetallicGlossMap", "_EmissionMap", "_OcclusionMap", "_DetailAlbedoMap" })
            if (material.HasProperty(slot))
                material.SetTexture(slot, null);
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_METALLICGLOSSMAP");
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", 0.15f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Cutoff"))
            material.SetFloat("_Cutoff", 0.5f);
        return material;
    }

    /// <summary>
    /// A vanilla material already using Standard alpha-cutout, so the shader variant is certainly in the build;
    /// falls back to a Standard material switched to cutout.
    /// </summary>
    private static Material Template()
    {
        if (_template != null)
            return _template;

        _template = Resources.FindObjectsOfTypeAll<Material>()
            .FirstOrDefault(m => m != null && m.shader != null && m.shader.name == "Standard" && m.IsKeywordEnabled("_ALPHATEST_ON"));
        if (_template == null)
        {
            ForgeLog.Source.LogInfo("Sprites: no vanilla cutout material found, using Standard set to cutout.");
            _template = new Material(Shader.Find("Standard"));
            _template.SetFloat("_Mode", 1f);
            _template.EnableKeyword("_ALPHATEST_ON");
            _template.SetOverrideTag("RenderType", "TransparentCutout");
            _template.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        return _template;
    }
}
