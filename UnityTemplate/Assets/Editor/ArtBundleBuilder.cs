using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DrakesAssetForge.ArtBuild
{
/// <summary>
/// Silent art compiler. Packs the user's FBX + PNG only — never Valheim shaders.
/// Job file is written beside the Unity project as art-job.json.
/// </summary>
public static class ArtBundleBuilder
{
    public static void Build()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                          ?? throw new InvalidOperationException("Unity project root missing.");
        var jobPath = Path.Combine(projectRoot, "art-job.json");
        if (!File.Exists(jobPath))
            throw new FileNotFoundException("art-job.json missing. The desktop tool should write this before launching Unity.", jobPath);

        var job = JsonUtility.FromJson<ArtJob>(File.ReadAllText(jobPath));
        if (job == null || string.IsNullOrWhiteSpace(job.meshPath) || string.IsNullOrWhiteSpace(job.outputDirectory))
            throw new InvalidOperationException("art-job.json is missing meshPath or outputDirectory.");

        var incoming = Path.Combine(Application.dataPath, "Incoming");
        if (Directory.Exists(incoming))
            Directory.Delete(incoming, true);
        Directory.CreateDirectory(incoming);

        var meshDest = Path.Combine(incoming, "mesh" + Path.GetExtension(job.meshPath));
        File.Copy(job.meshPath, meshDest, true);

        string textureAsset = null;
        if (!string.IsNullOrWhiteSpace(job.diffusePath) && File.Exists(job.diffusePath))
        {
            var texDest = Path.Combine(incoming, "diffuse" + Path.GetExtension(job.diffusePath));
            File.Copy(job.diffusePath, texDest, true);
            textureAsset = "Assets/Incoming/" + Path.GetFileName(texDest);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

        var meshAsset = "Assets/Incoming/" + Path.GetFileName(meshDest);
        var model = AssetImporter.GetAtPath(meshAsset) as ModelImporter;
        if (model != null)
        {
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importAnimation = false;
            model.importCameras = false;
            model.importLights = false;
            model.isReadable = true;
            model.SaveAndReimport();
        }

        var artPrefab = SaveArtPrefab(meshAsset);
        var bundleName = string.IsNullOrWhiteSpace(job.bundleName) ? "art" : job.bundleName.ToLowerInvariant();
        AssignBundle(artPrefab, bundleName);
        if (textureAsset != null)
            AssignBundle(textureAsset, bundleName);

        Directory.CreateDirectory(job.outputDirectory);
        var manifest = BuildPipeline.BuildAssetBundles(
            job.outputDirectory,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
            throw new InvalidOperationException("BuildAssetBundles returned null. See the Unity log.");

        var built = Path.Combine(job.outputDirectory, bundleName);
        if (!File.Exists(built))
            throw new FileNotFoundException("Bundle file was not written.", built);

        AssertArtPrefab(built, "art");
        Debug.Log($"DrakesAssetForge art prefab packed: {built} ({new FileInfo(built).Length} bytes)");
    }

    /// <summary>
    /// Art prefab only. Not an ItemDrop — Valheim scripts and shaders are applied at runtime.
    /// </summary>
    private static string SaveArtPrefab(string meshAsset)
    {
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(meshAsset);
        if (imported == null)
            throw new InvalidOperationException("Imported model has no GameObject. Cannot save an art prefab.");

        var instance = PrefabUtility.InstantiatePrefab(imported) as GameObject;
        if (instance == null)
            throw new InvalidOperationException("Failed to instantiate the imported model.");

        instance.name = "art";
        var meshCount = instance.GetComponentsInChildren<MeshFilter>(true).Length
                        + instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
        if (meshCount == 0)
        {
            UnityEngine.Object.DestroyImmediate(instance);
            throw new InvalidOperationException("Imported model has no mesh. Cannot save an art prefab.");
        }

        const string prefabPath = "Assets/Incoming/art.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
        if (saved == null)
            throw new InvalidOperationException("Failed to save Assets/Incoming/art.prefab.");

        AssetDatabase.SaveAssets();
        Debug.Log($"DrakesAssetForge saved art prefab ({meshCount} mesh component(s)).");
        return prefabPath;
    }

    private static void AssertArtPrefab(string bundlePath, string prefabName)
    {
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
            throw new InvalidOperationException("Could not reopen the built bundle to verify the art prefab.");

        try
        {
            var art = bundle.LoadAsset<GameObject>(prefabName);
            if (art == null)
                throw new InvalidOperationException("Bundle is missing the '" + prefabName + "' prefab.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static void AssignBundle(string assetPath, string bundleName)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null)
            throw new InvalidOperationException("Importer missing for " + assetPath);

        importer.assetBundleName = bundleName;
        importer.SaveAndReimport();
    }

    [Serializable]
    private sealed class ArtJob
    {
        public string meshPath = "";
        public string diffusePath = "";
        public string outputDirectory = "";
        public string bundleName = "art";
    }

    [Serializable]
    private sealed class ExtractJob
    {
        public string sourceBundle = "";
        public string prefabName = "";
        public string outputDirectory = "";
        public string bundleName = "art";
    }

    /// <summary>
    /// Pulls one prefab out of a fat mod bundle (e.g. LockSmith Assets/drake) and repacks it as a clean
    /// <c>art</c> prefab bundle — mesh/renderers only, no Valheim scripts/shaders.
    /// </summary>
    public static void Extract()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                          ?? throw new InvalidOperationException("Unity project root missing.");
        var jobPath = Path.Combine(projectRoot, "extract-job.json");
        if (!File.Exists(jobPath))
            throw new FileNotFoundException("extract-job.json missing.", jobPath);

        var job = JsonUtility.FromJson<ExtractJob>(File.ReadAllText(jobPath));
        if (job == null || string.IsNullOrWhiteSpace(job.sourceBundle) || string.IsNullOrWhiteSpace(job.outputDirectory))
            throw new InvalidOperationException("extract-job.json is missing sourceBundle or outputDirectory.");
        if (!File.Exists(job.sourceBundle))
            throw new FileNotFoundException("Source bundle not found.", job.sourceBundle);

        var incoming = Path.Combine(Application.dataPath, "Incoming");
        if (Directory.Exists(incoming))
            Directory.Delete(incoming, true);
        Directory.CreateDirectory(incoming);

        var source = AssetBundle.LoadFromFile(job.sourceBundle);
        if (source == null)
            throw new InvalidOperationException(
                "Could not load source bundle (wrong Unity version?): " + job.sourceBundle);

        GameObject template = null;
        try
        {
            var prefabName = string.IsNullOrWhiteSpace(job.prefabName) ? "art" : job.prefabName.Trim();
            template = FindPrefabInBundle(source, prefabName);
            if (template == null)
            {
                var names = string.Join(", ", source.GetAllAssetNames() ?? Array.Empty<string>());
                throw new InvalidOperationException(
                    "Prefab '" + prefabName + "' was not found in the source bundle. Assets: " + names);
            }

            var instance = UnityEngine.Object.Instantiate(template);
            var clean = PreserveOriginalVisual(instance) ?? BuildCleanArtVisual(instance);
            UnityEngine.Object.DestroyImmediate(instance);
            if (clean == null)
                throw new InvalidOperationException(
                    "Prefab '" + prefabName + "' has no MeshFilter/SkinnedMeshRenderer to extract.");

            s_meshSerial = 0;
            BakeMeshesAndMaterials(clean, "art");

            const string prefabPath = "Assets/Incoming/art.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(clean, prefabPath);
            UnityEngine.Object.DestroyImmediate(clean);
            if (saved == null)
                throw new InvalidOperationException("Failed to save Assets/Incoming/art.prefab.");
        }
        finally
        {
            source.Unload(true);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        var bundleName = string.IsNullOrWhiteSpace(job.bundleName) ? "art" : job.bundleName.ToLowerInvariant();
        AssignBundle("Assets/Incoming/art.prefab", bundleName);
        foreach (var meshAsset in Directory.GetFiles(incoming, "*.asset"))
            AssignBundle("Assets/Incoming/" + Path.GetFileName(meshAsset), bundleName);

        Directory.CreateDirectory(job.outputDirectory);
        // No StrictMode — source prefabs often reference Valheim scripts we stripped.
        var manifest = BuildPipeline.BuildAssetBundles(
            job.outputDirectory,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
            throw new InvalidOperationException("BuildAssetBundles returned null during extract.");

        var built = Path.Combine(job.outputDirectory, bundleName);
        if (!File.Exists(built))
            throw new FileNotFoundException("Extracted bundle was not written.", built);

        AssertArtPrefab(built, "art");
        Debug.Log($"DrakesAssetForge extracted art prefab: {built} ({new FileInfo(built).Length} bytes)");
    }

    [Serializable]
    private sealed class PackFolderJob
    {
        public string outputDirectory = "";
        public string bundleName = "pack";
        public PackFolderEntry[] entries = Array.Empty<PackFolderEntry>();
    }

    [Serializable]
    private sealed class PackFolderEntry
    {
        public string sourceBundle = "";
        public string prefabName = "";
        public string diffusePath = "";
    }

    /// <summary>
    /// Merges per-item art.bundles (prefab "art") into one folder pack named after the group
    /// (e.g. keys.bundle with prefabs keymaker, masterkey, …).
    /// Job: pack-folder-job.json
    /// </summary>
    public static void PackFolder()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                          ?? throw new InvalidOperationException("Unity project root missing.");
        var jobPath = Path.Combine(projectRoot, "pack-folder-job.json");
        if (!File.Exists(jobPath))
            throw new FileNotFoundException("pack-folder-job.json missing.", jobPath);

        var job = JsonUtility.FromJson<PackFolderJob>(File.ReadAllText(jobPath));
        if (job == null || string.IsNullOrWhiteSpace(job.outputDirectory) || string.IsNullOrWhiteSpace(job.bundleName))
            throw new InvalidOperationException("pack-folder-job.json is missing outputDirectory or bundleName.");
        if (job.entries == null || job.entries.Length == 0)
            throw new InvalidOperationException("pack-folder-job.json has no entries.");

        var incoming = Path.Combine(Application.dataPath, "Incoming");
        if (Directory.Exists(incoming))
            Directory.Delete(incoming, true);
        Directory.CreateDirectory(incoming);

        // Unique mesh asset names across ALL entries — never reuse mesh_0.asset or every
        // prefab collapses onto the last item's meshes (LockSmith keys spike / NurbsPath bug).
        s_meshSerial = 0;
        var packed = new List<string>();
        foreach (var entry in job.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.sourceBundle) || !File.Exists(entry.sourceBundle))
                throw new FileNotFoundException("Pack entry source bundle missing.", entry?.sourceBundle ?? "");
            var prefabName = string.IsNullOrWhiteSpace(entry.prefabName) ? "art" : entry.prefabName.Trim();

            var source = AssetBundle.LoadFromFile(entry.sourceBundle);
            if (source == null)
                throw new InvalidOperationException("Could not load " + entry.sourceBundle);

            try
            {
                var template = FindPrefabInBundle(source, "art") ?? FindPrefabInBundle(source, prefabName);
                if (template == null)
                    throw new InvalidOperationException(
                        "No 'art' prefab in " + entry.sourceBundle + " for '" + prefabName + "'.");

                var instance = UnityEngine.Object.Instantiate(template);
                var clean = PreserveOriginalVisual(instance) ?? BuildCleanArtVisual(instance);
                UnityEngine.Object.DestroyImmediate(instance);
                if (clean == null)
                    throw new InvalidOperationException("No mesh visuals in " + entry.sourceBundle);

                clean.name = prefabName;
                BakeMeshesAndMaterials(clean, prefabName);
                var prefabPath = "Assets/Incoming/" + SanitizeFileName(prefabName) + ".prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(clean, prefabPath);
                UnityEngine.Object.DestroyImmediate(clean);
                if (saved == null)
                    throw new InvalidOperationException("Failed to save " + prefabPath);
                packed.Add(prefabPath);

                // Prefer on-disk PNG (readable import). Bundle textures are often non-readable.
                if (!string.IsNullOrWhiteSpace(entry.diffusePath) && File.Exists(entry.diffusePath))
                    ImportDiffusePng(entry.diffusePath, prefabName);
                else
                    CopyDiffuseTexturesFromBundle(source, prefabName);
            }
            finally
            {
                source.Unload(true);
            }
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        var bundleName = job.bundleName.ToLowerInvariant();
        foreach (var prefabPath in packed)
            AssignBundle(prefabPath, bundleName);
        foreach (var meshAsset in Directory.GetFiles(incoming, "*.asset"))
            AssignBundle("Assets/Incoming/" + Path.GetFileName(meshAsset), bundleName);
        foreach (var texAsset in Directory.GetFiles(incoming, "*.png"))
            AssignBundle("Assets/Incoming/" + Path.GetFileName(texAsset), bundleName);

        Directory.CreateDirectory(job.outputDirectory);
        var manifest = BuildPipeline.BuildAssetBundles(
            job.outputDirectory,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            throw new InvalidOperationException("BuildAssetBundles returned null during PackFolder.");

        var built = Path.Combine(job.outputDirectory, bundleName);
        if (!File.Exists(built))
            throw new FileNotFoundException("Folder pack bundle was not written.", built);

        foreach (var prefabPath in packed)
        {
            var name = Path.GetFileNameWithoutExtension(prefabPath);
            AssertArtPrefab(built, name);
        }

        Debug.Log($"DrakesAssetForge packed folder bundle: {built} ({new FileInfo(built).Length} bytes) with {packed.Count} prefab(s).");
    }

    private static string SanitizeFileName(string name)
    {
        var chars = name.Trim().Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        var s = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(s) ? "item" : s;
    }

    /// <summary>
    /// Copies Texture2D assets from a per-item art.bundle into Incoming as
    /// <c>{prefabName}_diffuse</c> so folder packs keep the albedo for runtime stamping.
    /// </summary>
    private static void CopyDiffuseTexturesFromBundle(AssetBundle source, string prefabName)
    {
        var textures = source.LoadAllAssets<Texture2D>();
        if (textures == null || textures.Length == 0)
            return;

        Texture2D pick = null;
        foreach (var tex in textures)
        {
            if (tex == null)
                continue;
            var n = tex.name ?? "";
            if (n.IndexOf("diffuse", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.Equals("diffuse", StringComparison.OrdinalIgnoreCase))
            {
                pick = tex;
                break;
            }
        }

        if (pick == null)
            pick = textures.FirstOrDefault(t => t != null);

        if (pick == null)
            return;

        // Bundle textures are often non-readable — blit into a temporary RT then encode PNG.
        var label = SanitizeFileName(prefabName) + "_diffuse";
        var pngPath = Path.Combine(Application.dataPath, "Incoming", label + ".png");
        try
        {
            var rt = RenderTexture.GetTemporary(pick.width, pick.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(pick, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(pick.width, pick.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, pick.width, pick.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(pngPath, readable.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(readable);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("DrakesAssetForge could not blit diffuse from art.bundle: " + ex.Message);
            return;
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath("Assets/Incoming/" + label + ".png") as TextureImporter;
        if (importer != null)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        Debug.Log($"DrakesAssetForge packed diffuse '{label}' ({pick.width}x{pick.height}) from art.bundle blit.");
    }

    private static void ImportDiffusePng(string sourcePng, string prefabName)
    {
        var label = SanitizeFileName(prefabName) + "_diffuse";
        var dest = Path.Combine(Application.dataPath, "Incoming", label + ".png");
        File.Copy(sourcePng, dest, true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var assetPath = "Assets/Incoming/" + label + ".png";
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer != null)
        {
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        Debug.Log($"DrakesAssetForge packed diffuse '{label}' from {sourcePng}.");
    }

    private static GameObject FindPrefabInBundle(AssetBundle source, string prefabName)
    {
        var leaf = Path.GetFileNameWithoutExtension(prefabName.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf))
            leaf = prefabName;

        // 1) Exact LoadAsset (full path or short name).
        var direct = source.LoadAsset<GameObject>(prefabName);
        if (direct != null)
            return direct;

        direct = source.LoadAsset<GameObject>(leaf);
        if (direct != null)
            return direct;

        // 2) Match GetAllAssetNames() paths like assets/drake/locksmit/masterkey.prefab
        var assetNames = source.GetAllAssetNames();
        if (assetNames != null)
        {
            foreach (var assetName in assetNames)
            {
                var assetLeaf = Path.GetFileNameWithoutExtension(assetName.Replace('\\', '/'));
                if (!assetLeaf.Equals(leaf, StringComparison.OrdinalIgnoreCase) &&
                    !assetName.EndsWith("/" + leaf + ".prefab", StringComparison.OrdinalIgnoreCase) &&
                    assetName.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var loaded = source.LoadAsset<GameObject>(assetName);
                if (loaded != null)
                    return loaded;
            }
        }

        // 3) Scan loaded GameObjects by name.
        var all = source.LoadAllAssets<GameObject>();
        if (all == null)
            return null;

        foreach (var go in all)
        {
            if (go != null && go.name.Equals(leaf, StringComparison.OrdinalIgnoreCase))
                return go;
        }

        foreach (var go in all)
        {
            if (go == null)
                continue;
            if (go.name.IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0 ||
                leaf.IndexOf(go.name, StringComparison.OrdinalIgnoreCase) >= 0)
                return go;
        }

        return null;
    }

    /// <summary>
    /// Keep the authored hierarchy (skull + shaft + bit) — only strip non-mesh junk / missing scripts.
    /// Do not rebuild from MeshFilters; that breaks relative skull placement.
    /// </summary>
    private static GameObject PreserveOriginalVisual(GameObject source)
    {
        var root = UnityEngine.Object.Instantiate(source);
        root.name = "art";

        // Bottom-up: remove missing Valheim scripts that block SaveAsPrefabAsset.
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (var i = transforms.Length - 1; i >= 0; i--)
        {
            var t = transforms[i];
            if (t == null)
                continue;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }

        StripJunkVisualChildren(root);

        var components = root.GetComponentsInChildren<Component>(true);
        foreach (var c in components)
        {
            if (c == null)
                continue;
            if (c is Transform)
                continue;
            if (c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)
                continue;
            UnityEngine.Object.DestroyImmediate(c);
        }

        // Drop MeshFilters whose vertex buffers never loaded (streamed .resS missing).
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null)
                continue;
            if (filter.sharedMesh != null && MeshHasVertexData(filter.sharedMesh))
                continue;
            var go = filter.gameObject;
            UnityEngine.Object.DestroyImmediate(filter);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
                UnityEngine.Object.DestroyImmediate(mr);
            if (go != root && go.GetComponentsInChildren<Transform>(true).Length <= 1)
                UnityEngine.Object.DestroyImmediate(go);
        }

        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned == null)
                continue;
            if (skinned.sharedMesh != null && MeshHasVertexData(skinned.sharedMesh))
                continue;
            var go = skinned.gameObject;
            UnityEngine.Object.DestroyImmediate(skinned);
            if (go != root && go.GetComponentsInChildren<Transform>(true).Length <= 1)
                UnityEngine.Object.DestroyImmediate(go);
        }

        var meshCount = root.GetComponentsInChildren<MeshFilter>(true).Length
                        + root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
        if (meshCount == 0)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return null;
        }

        // Clear materials — runtime ArtItemLoader applies Valheim mats.
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != null)
                renderer.sharedMaterials = Array.Empty<Material>();
        }

        return root;
    }

    /// <summary>
    /// LockSmith fat packs include helper meshes (NurbsPath @ huge scale, "key backup").
    /// Strip those only when other real meshes remain — PublicKey2's sole visual IS NurbsPath.
    /// </summary>
    private static void StripJunkVisualChildren(GameObject root)
    {
        var meshBearing = CountMeshBearingChildren(root);
        var toDestroy = new List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.gameObject == null || t.gameObject == root)
                continue;

            var n = t.gameObject.name ?? "";
            var isBackup = n.IndexOf("backup", StringComparison.OrdinalIgnoreCase) >= 0;
            var isNurbs = n.Equals("NurbsPath", StringComparison.OrdinalIgnoreCase);

            // Drop backup / Nurbs helpers only when other meshes remain — skullKey's sole
            // mesh is named "key backup" and must be kept.
            if ((isBackup || isNurbs) && meshBearing > 1)
            {
                Debug.Log(
                    $"DrakesAssetForge stripping junk visual child '{n}' " +
                    $"(scale {t.localScale}, meshBearing={meshBearing}).");
                toDestroy.Add(t.gameObject);
            }
        }

        foreach (var go in toDestroy)
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
    }

    private static int CountMeshBearingChildren(GameObject root)
    {
        var count = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter != null && filter.sharedMesh != null)
                count++;
        }

        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned != null && skinned.sharedMesh != null)
                count++;
        }

        return count;
    }

    private static bool MeshHasVertexData(Mesh mesh)
    {
        if (mesh == null || mesh.vertexCount <= 0)
            return false;

        // Readable: confirm CPU buffers exist (streamed .resS can report a count with empty arrays).
        if (!mesh.isReadable)
            return true; // pack via Instantiate + CreateAsset (no CPU readback)

        try
        {
            var verts = mesh.vertices;
            return verts != null && verts.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Build a brand-new hierarchy with only mesh visuals — fallback when preserve fails.
    /// </summary>
    private static GameObject BuildCleanArtVisual(GameObject source)
    {
        var root = new GameObject("art");
        var added = 0;

        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !MeshHasVertexData(filter.sharedMesh))
                continue;
            var child = new GameObject(string.IsNullOrWhiteSpace(filter.gameObject.name) ? "mesh" : filter.gameObject.name);
            child.transform.SetParent(root.transform, false);
            CopyLocalTransform(filter.transform, child.transform, source.transform);
            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = filter.sharedMesh;
            var mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterials = Array.Empty<Material>();
            added++;
        }

        foreach (var skinned in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned == null || skinned.sharedMesh == null || !MeshHasVertexData(skinned.sharedMesh))
                continue;
            var child = new GameObject(string.IsNullOrWhiteSpace(skinned.gameObject.name) ? "skinned" : skinned.gameObject.name);
            child.transform.SetParent(root.transform, false);
            CopyLocalTransform(skinned.transform, child.transform, source.transform);
            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = skinned.sharedMesh;
            smr.sharedMaterials = Array.Empty<Material>();
            smr.localBounds = skinned.localBounds;
            added++;
        }

        if (added == 0)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return null;
        }

        return root;
    }

    private static void CopyLocalTransform(Transform from, Transform to, Transform spaceRoot)
    {
        // Bake world pose relative to the source prefab root into the clean child.
        var world = from.localToWorldMatrix;
        var rootWorld = spaceRoot.localToWorldMatrix;
        var local = rootWorld.inverse * world;
        to.localPosition = local.GetColumn(3);
        to.localRotation = Quaternion.LookRotation(
            local.GetColumn(2),
            local.GetColumn(1));
        to.localScale = new Vector3(
            local.GetColumn(0).magnitude,
            local.GetColumn(1).magnitude,
            local.GetColumn(2).magnitude);
    }

    private static int s_meshSerial;

    /// <summary>
    /// AssetBundle-loaded meshes are not project assets — clone them into Incoming so the new art.bundle owns them.
    /// Materials are cleared; runtime ArtItemLoader applies Valheim materials.
    /// </summary>
    private static void BakeMeshesAndMaterials(GameObject root, string prefabLabel)
    {
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (!MeshHasVertexData(filter.sharedMesh))
                throw new InvalidOperationException(
                    $"MeshFilter '{filter.gameObject.name}' on '{prefabLabel}' has no embedded vertex data " +
                    $"(likely streamed .resS). Strip it or re-export the source with embedded meshes.");
            filter.sharedMesh = SaveMeshAsset(filter.sharedMesh, prefabLabel, filter.gameObject.name);
        }

        foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skinned == null || skinned.sharedMesh == null)
                continue;
            if (!MeshHasVertexData(skinned.sharedMesh))
                throw new InvalidOperationException(
                    $"SkinnedMesh '{skinned.gameObject.name}' on '{prefabLabel}' has no embedded vertex data.");
            skinned.sharedMesh = SaveMeshAsset(skinned.sharedMesh, prefabLabel, skinned.gameObject.name);
            skinned.sharedMaterials = Array.Empty<Material>();
        }

        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer != null)
                renderer.sharedMaterials = Array.Empty<Material>();
        }
    }

    /// <summary>
    /// Deep-copy vertex channels into a unique project Mesh asset so AssetBundle build embeds bytes
    /// (no .resS dependency) and PackFolder never overwrites another prefab's mesh_N.asset.
    /// </summary>
    private static Mesh SaveMeshAsset(Mesh source, string prefabLabel, string childName)
    {
        var serial = ++s_meshSerial;
        var label = SanitizeFileName(prefabLabel);
        var child = SanitizeFileName(string.IsNullOrWhiteSpace(childName) ? source.name : childName);
        var assetName = label + "_" + child + "_" + serial;
        var path = "Assets/Incoming/" + assetName + ".asset";
        if (File.Exists(Path.Combine(Application.dataPath, "Incoming", assetName + ".asset")))
            path = "Assets/Incoming/" + assetName + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset";

        Mesh copy;
        if (source.isReadable)
        {
            copy = CloneMeshFromReadable(source);
            copy.name = assetName;
        }
        else
        {
            // Non-readable AssetBundle meshes refuse CPU readback and CreateAsset keeps an
            // external .resS pointer that is missing at Valheim runtime (black spike / empty mesh).
            throw new InvalidOperationException(
                $"Mesh '{source.name}' on '{prefabLabel}/{childName}' is not readable " +
                $"(vertexCount={source.vertexCount}). Cannot embed streamed .resS data. " +
                "Export an OBJ/FBX (art/mesh.obj) and compile that instead.");
        }

        AssetDatabase.CreateAsset(copy, path);
        var loaded = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (loaded == null || loaded.vertexCount <= 0)
            throw new InvalidOperationException("Failed to embed mesh at " + path);

        // Instantiate+CreateAsset can keep an external .resS pointer — that becomes the black spike at runtime.
        if (MeshHasExternalStream(loaded, out var streamPath, out var streamSize))
            throw new InvalidOperationException(
                $"Mesh '{source.name}' on '{prefabLabel}/{childName}' still references streamed data " +
                $"'{streamPath}' (size={streamSize}). Source vertex bytes are not embedded. " +
                "Export an OBJ/FBX and compile that instead.");

        Debug.Log(
            $"DrakesAssetForge embedded mesh '{loaded.name}' verts={loaded.vertexCount} " +
            $"readable={loaded.isReadable} → {path}");
        return loaded;
    }

    private static bool MeshHasExternalStream(Mesh mesh, out string path, out long size)
    {
        path = "";
        size = 0;
        try
        {
            var so = new SerializedObject(mesh);
            var stream = so.FindProperty("m_StreamData");
            if (stream == null)
                return false;
            var pathProp = stream.FindPropertyRelative("path");
            var sizeProp = stream.FindPropertyRelative("size");
            path = pathProp != null ? pathProp.stringValue ?? "" : "";
            if (sizeProp != null)
                size = sizeProp.propertyType == SerializedPropertyType.Integer
                    ? sizeProp.longValue
                    : sizeProp.longValue;
            return !string.IsNullOrEmpty(path) && size > 0;
        }
        catch
        {
            return false;
        }
    }

    private static Mesh CloneMeshFromReadable(Mesh source)
    {
        var readableVerts = source.vertices;
        if (readableVerts == null || readableVerts.Length == 0)
            throw new InvalidOperationException(
                $"Cannot clone mesh '{source.name}' - readable but vertex buffers empty.");

        var copy = new Mesh
        {
            name = source.name,
            indexFormat = source.indexFormat,
        };

        copy.vertices = readableVerts;
        copy.normals = source.normals;
        copy.tangents = source.tangents;
        copy.colors = source.colors;
        copy.colors32 = source.colors32;
        copy.uv = source.uv;
        copy.uv2 = source.uv2;
        copy.uv3 = source.uv3;
        copy.uv4 = source.uv4;
        copy.boneWeights = source.boneWeights;
        copy.bindposes = source.bindposes;

        copy.subMeshCount = source.subMeshCount;
        for (var i = 0; i < source.subMeshCount; i++)
            copy.SetIndices(source.GetIndices(i), source.GetTopology(i), i, true);

        FinalizeClonedMesh(copy);
        return copy;
    }

    private static void FinalizeClonedMesh(Mesh copy)
    {
        copy.RecalculateBounds();
        if (copy.normals == null || copy.normals.Length != copy.vertexCount)
            copy.RecalculateNormals();
    }
}
}
