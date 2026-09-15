using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace DrakesAssetForge.Smoke;

/// <summary>
/// C1 proof: load authored Project items and register Jotunn clones.
/// Donor materials stay unless material.json is Custom — then Shader.Find + PNG.
/// No AssetBundle (that is C2).
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("com.jotunn.jotunn")]
public sealed class SmokePlugin : BaseUnityPlugin
{
    public const string Guid = "com.drakesworkshop.assetforgesmoke";
    public const string Name = "DrakesAssetForgeSmoke";
    public const string Version = "0.1.0";

    internal static ManualLogSource Log = null!;

    private void Awake()
    {
        Log = Logger;
        var itemsRoot = Config.Bind(
            "Smoke",
            "ProjectItemsPath",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DrakeAssetForge",
                "Projects",
                "Default",
                "Items"),
            "Folder of authored items (each subfolder has item.json).");

        PrefabManager.OnVanillaPrefabsAvailable += () => RegisterAll(itemsRoot.Value);
    }

    private static void RegisterAll(string itemsRoot)
    {
        if (!Directory.Exists(itemsRoot))
        {
            Log.LogWarning($"No project items at '{itemsRoot}'. Clone something in Drakes Asset Forge first.");
            return;
        }

        var registered = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(itemsRoot))
        {
            var itemPath = Path.Combine(dir, "item.json");
            if (!File.Exists(itemPath))
                continue;

            try
            {
                if (TryRegister(dir, File.ReadAllText(itemPath)))
                    registered.Add(Path.GetFileName(dir));
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed '{Path.GetFileName(dir)}': {ex}");
            }
        }

        if (registered.Count == 0)
        {
            Log.LogWarning("No items registered. Check donor prefab names and item.json.");
            return;
        }

        Log.LogInfo($"Registered {registered.Count} item(s). In-game: devcommands, then spawn <id>. Ids: {string.Join(", ", registered)}");
    }

    private static bool TryRegister(string folder, string itemJson)
    {
        var id = ReadString(itemJson, "id") ?? Path.GetFileName(folder);
        var donor = ReadString(itemJson, "prefabName");
        if (string.IsNullOrWhiteSpace(donor))
        {
            Log.LogWarning($"{id}: missing donor.prefabName");
            return false;
        }

        if (PrefabManager.Instance.GetPrefab(donor) == null)
        {
            Log.LogWarning($"{id}: donor prefab '{donor}' not found.");
            return false;
        }

        var custom = new CustomItem(id, donor);
        var drop = custom.ItemDrop;
        if (drop?.m_itemData?.m_shared == null)
        {
            Log.LogWarning($"{id}: clone has no ItemDrop.");
            return false;
        }

        drop.m_itemData.m_shared.m_name = "$item_" + id.ToLowerInvariant();
        ApplyIcon(folder, drop);
        ApplyArtBundleMesh(folder, custom.ItemPrefab);
        ApplyCustomMaterial(folder, custom.ItemPrefab);
        ItemManager.Instance.AddItem(custom);
        Log.LogInfo($"Clone '{donor}' → '{id}' (spawn {id})");
        return true;
    }

    private static void ApplyIcon(string folder, ItemDrop drop)
    {
        var artJson = ReadOptional(Path.Combine(folder, "art.json"));
        var iconRel = artJson == null ? null : ReadString(artJson, "iconPath");
        var iconAbs = Resolve(folder, iconRel);
        if (iconAbs == null)
            return;

        var tex = LoadPng(iconAbs);
        if (tex == null)
            return;

        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        drop.m_itemData.m_shared.m_icons = new[] { sprite };
        Log.LogInfo($"  icon {iconAbs}");
    }

    private static void ApplyArtBundleMesh(string folder, GameObject prefab)
    {
        var bundlePath = Path.Combine(folder, "art.bundle");
        if (!File.Exists(bundlePath))
            return;

        try
        {
            var bundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule");
            var load = bundleType?.GetMethod("LoadFromFile", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            var bundle = load?.Invoke(null, new object[] { bundlePath });
            if (bundle == null)
            {
                Log.LogWarning($"  art.bundle failed to load (Unity version mismatch?): {bundlePath}");
                return;
            }

            var art = LoadAsset(bundleType!, bundle, "art", typeof(GameObject)) as GameObject;
            if (art != null)
            {
                AttachArtPrefab(prefab, art);
                return;
            }

            var loadAll = bundleType!.GetMethod("LoadAllAssets", new[] { typeof(Type) });
            var assets = loadAll?.Invoke(bundle, new object[] { typeof(Mesh) }) as Mesh[];
            var mesh = assets?.OrderByDescending(m => m != null ? m.vertexCount : 0).FirstOrDefault();
            if (mesh == null)
            {
                Log.LogWarning("  art.bundle has no 'art' prefab and no Mesh assets.");
                return;
            }

            var swapped = 0;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;
                var meshProp = component.GetType().GetProperty("sharedMesh");
                if (meshProp == null || !typeof(Mesh).IsAssignableFrom(meshProp.PropertyType) || !meshProp.CanWrite)
                    continue;
                meshProp.SetValue(component, mesh);
                swapped++;
            }

            Log.LogWarning($"  art.bundle has no 'art' prefab; swapped mesh '{mesh.name}' onto {swapped} renderer(s). Recompile to pack a prefab.");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"  art.bundle: {ex.Message}");
        }
    }

    private static object LoadAsset(Type bundleType, object bundle, string name, Type type)
    {
        var loadAsset = bundleType.GetMethod("LoadAsset", new[] { typeof(string), typeof(Type) });
        return loadAsset?.Invoke(bundle, new object[] { name, type });
    }

    private static void AttachArtPrefab(GameObject item, GameObject art)
    {
        var visual = UnityEngine.Object.Instantiate(art, item.transform, false);
        visual.name = "art";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        var hidden = 0;
        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.transform.IsChildOf(visual.transform))
                continue;
            var typeName = renderer.GetType().Name;
            if (typeName != "MeshRenderer" && typeName != "SkinnedMeshRenderer")
                continue;
            renderer.enabled = false;
            hidden++;
        }

        Log.LogInfo($"  art prefab '{art.name}' parented; hid {hidden} donor renderer(s)");
    }

    private static void ApplyCustomMaterial(string folder, GameObject prefab)
    {
        var matJson = ReadOptional(Path.Combine(folder, "material.json"));
        if (matJson == null)
            return;

        var mode = ReadString(matJson, "mode");
        if (!string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            Log.LogInfo("  materials: donor (no Shader.Find)");
            return;
        }

        var shaderName = ReadString(matJson, "shaderName");
        if (string.IsNullOrWhiteSpace(shaderName))
        {
            Log.LogWarning("  custom material missing shaderName — keeping donor materials");
            return;
        }

        var shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Log.LogWarning($"  Shader.Find('{shaderName}') failed — keeping donor materials");
            return;
        }

        var material = new Material(shader) { name = prefab.name + "_smoke" };
        ApplyProperties(folder, matJson, material);

        var count = 0;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            var typeName = renderer.GetType().Name;
            if (typeName is "ParticleSystemRenderer" or "TrailRenderer" or "LineRenderer")
                continue;
            renderer.sharedMaterial = material;
            count++;
        }

        Log.LogInfo($"  Shader.Find '{shaderName}' on {count} renderer(s)");
    }

    private static void ApplyProperties(string folder, string matJson, Material material)
    {
        var artJson = ReadOptional(Path.Combine(folder, "art.json"));
        var diffuse = Resolve(folder, artJson == null ? null : ReadString(artJson, "diffusePath"));

        foreach (Match block in Regex.Matches(matJson, @"\{[^{}]*""name""\s*:\s*""([^""]+)""[^{}]*\}"))
        {
            var body = block.Value;
            var name = block.Groups[1].Value;
            if (!material.HasProperty(name))
                continue;

            var texRef = Resolve(folder, ReadString(body, "textureRef"));
            if (texRef != null)
            {
                var tex = LoadPng(texRef);
                if (tex != null)
                    material.SetTexture(name, tex);
                continue;
            }

            var value = ReadString(body, "value");
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (TryParseColor(value!, out var color))
            {
                material.SetColor(name, color);
                continue;
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                material.SetFloat(name, number);
        }

        if (diffuse != null && material.HasProperty("_MainTex") && material.GetTexture("_MainTex") == null)
        {
            var tex = LoadPng(diffuse);
            if (tex != null)
                material.SetTexture("_MainTex", tex);
        }
    }

    private static Texture2D? LoadPng(string path)
    {
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!TryLoadImage(tex, File.ReadAllBytes(path)))
            {
                Log.LogWarning($"  could not decode '{path}'");
                return null;
            }

            tex.name = Path.GetFileName(path);
            return tex;
        }
        catch (Exception ex)
        {
            Log.LogWarning($"  texture '{path}': {ex.Message}");
            return null;
        }
    }

    private static bool TryLoadImage(Texture2D tex, byte[] bytes)
    {
        var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
        var method = type?.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D), typeof(byte[]) }, null);
        if (method == null)
        {
            Log.LogWarning("  ImageConversion.LoadImage not found");
            return false;
        }

        return method.Invoke(null, new object[] { tex, bytes }) is true;
    }

    private static string? Resolve(string folder, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;
        if (Path.IsPathRooted(stored))
            return File.Exists(stored) ? stored : null;

        var relative = stored!.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        return File.Exists(full) ? full : null;
    }

    private static string? ReadOptional(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private static string? ReadString(string json, string key)
    {
        var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
        return match.Success ? Regex.Unescape(match.Groups[1].Value) : null;
    }

    private static bool TryParseColor(string raw, out Color color)
    {
        color = Color.white;
        var parts = raw.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var g) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
            return false;

        var a = 1f;
        if (parts.Length > 3)
            float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out a);

        if (r > 1f || g > 1f || b > 1f)
        {
            r /= 255f;
            g /= 255f;
            b /= 255f;
            if (a > 1f)
                a /= 255f;
        }

        color = new Color(r, g, b, a);
        return true;
    }
}
