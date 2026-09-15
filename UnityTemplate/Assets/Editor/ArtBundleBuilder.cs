using System;
using System.IO;
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

        AssertArtPrefab(built);
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

    private static void AssertArtPrefab(string bundlePath)
    {
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
            throw new InvalidOperationException("Could not reopen the built bundle to verify the art prefab.");

        try
        {
            var art = bundle.LoadAsset<GameObject>("art");
            if (art == null)
                throw new InvalidOperationException("Bundle is missing the 'art' prefab.");
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
    /// Loads a prefab from an existing Unity asset bundle and repacks it as the standard <c>art</c> prefab bundle.
    /// Job file: extract-job.json beside the Unity project.
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
            throw new InvalidOperationException("Could not load source bundle: " + job.sourceBundle);

        GameObject template = null;
        try
        {
            var prefabName = string.IsNullOrWhiteSpace(job.prefabName) ? "art" : job.prefabName;
            template = source.LoadAsset<GameObject>(prefabName);
            if (template == null)
            {
                // Try leaf match against all GameObject assets.
                var all = source.LoadAllAssets<GameObject>();
                foreach (var go in all)
                {
                    if (go != null && go.name.Equals(prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        template = go;
                        break;
                    }
                }
            }

            if (template == null)
                throw new InvalidOperationException("Prefab '" + prefabName + "' was not found in the source bundle.");

            var instance = UnityEngine.Object.Instantiate(template);
            instance.name = "art";
            const string prefabPath = "Assets/Incoming/art.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            if (saved == null)
                throw new InvalidOperationException("Failed to save Assets/Incoming/art.prefab.");
        }
        finally
        {
            source.Unload(true);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        var bundleName = string.IsNullOrWhiteSpace(job.bundleName) ? "art" : job.bundleName.ToLowerInvariant();
        AssignBundle("Assets/Incoming/art.prefab", bundleName);

        Directory.CreateDirectory(job.outputDirectory);
        var manifest = BuildPipeline.BuildAssetBundles(
            job.outputDirectory,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
            throw new InvalidOperationException("BuildAssetBundles returned null during extract.");

        var built = Path.Combine(job.outputDirectory, bundleName);
        if (!File.Exists(built))
            throw new FileNotFoundException("Extracted bundle was not written.", built);

        AssertArtPrefab(built);
        Debug.Log($"DrakesAssetForge extracted art prefab: {built} ({new FileInfo(built).Length} bytes)");
    }
}
}
