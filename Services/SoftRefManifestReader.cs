using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

/// <summary>
/// Parses Valheim SoftRef <c>manifest</c> / <c>manifest_extended</c> text files (YAML-like).
/// </summary>
public static class SoftRefManifestReader
{
    public sealed class SoftRefIndex
    {
        public required string SoftRefRoot { get; init; }
        public required string BundlesDirectory { get; init; }
        public IReadOnlyList<SoftRefAssetEntry> Assets { get; init; } = Array.Empty<SoftRefAssetEntry>();
        public IReadOnlyDictionary<string, IReadOnlyList<string>> BundleDependencies { get; init; }
            = new Dictionary<string, IReadOnlyList<string>>();
    }

    public static SoftRefIndex Load(string softRefRoot)
    {
        var bundlesDir = Path.Combine(softRefRoot, "Bundles");
        if (!Directory.Exists(bundlesDir))
            throw new DirectoryNotFoundException($"SoftRef Bundles folder not found: {bundlesDir}");

        var extended = Path.Combine(softRefRoot, "manifest_extended");
        var basic = Path.Combine(softRefRoot, "manifest");
        var manifestPath = File.Exists(extended) ? extended : basic;
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("SoftRef manifest not found.", manifestPath);

        var assets = new List<SoftRefAssetEntry>();
        var deps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        string? currentBundle = null;
        string? currentAssetId = null;
        string? currentAssetBundle = null;
        var inBundleDeps = false;
        var inAssetSection = false;

        foreach (var raw in File.ReadLines(manifestPath))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.Equals("bundle dependencies:", StringComparison.OrdinalIgnoreCase))
            {
                inBundleDeps = true;
                inAssetSection = false;
                currentBundle = null;
                continue;
            }

            // Asset listings in extended manifests appear as "- asset ID:"
            if (trimmed.StartsWith("- asset ID:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("- asset id:", StringComparison.OrdinalIgnoreCase))
            {
                inBundleDeps = false;
                inAssetSection = true;
                FlushAsset(assets, ref currentAssetId, ref currentAssetBundle, path: null);
                currentAssetId = trimmed.Split(':', 2)[1].Trim();
                currentAssetBundle = null;
                continue;
            }

            if (inAssetSection)
            {
                if (trimmed.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
                {
                    currentAssetBundle = trimmed.Split(':', 2)[1].Trim();
                    continue;
                }

                if (trimmed.StartsWith("path in bundle:", StringComparison.OrdinalIgnoreCase))
                {
                    var path = trimmed.Split(':', 2)[1].Trim();
                    FlushAsset(assets, ref currentAssetId, ref currentAssetBundle, path);
                    continue;
                }
            }

            if (inBundleDeps)
            {
                if (trimmed.StartsWith("- bundle:", StringComparison.OrdinalIgnoreCase))
                {
                    currentBundle = trimmed.Split(':', 2)[1].Trim();
                    if (!deps.ContainsKey(currentBundle))
                        deps[currentBundle] = new List<string>();
                    continue;
                }

                if (currentBundle != null &&
                    trimmed.StartsWith("- ", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("- bundle:", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.Equals("dependencies:", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("dependencies:", StringComparison.OrdinalIgnoreCase))
                {
                    var depId = trimmed[2..].Trim();
                    if (!string.IsNullOrWhiteSpace(depId))
                        deps[currentBundle].Add(depId);
                }
            }
        }

        FlushAsset(assets, ref currentAssetId, ref currentAssetBundle, path: null);

        return new SoftRefIndex
        {
            SoftRefRoot = softRefRoot,
            BundlesDirectory = bundlesDir,
            Assets = assets,
            BundleDependencies = deps.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value,
                StringComparer.OrdinalIgnoreCase),
        };
    }

    private static void FlushAsset(
        List<SoftRefAssetEntry> assets,
        ref string? assetId,
        ref string? bundleId,
        string? path)
    {
        if (assetId != null && bundleId != null && path != null)
        {
            assets.Add(CatalogClassifier.Enrich(assetId, bundleId, path));
        }

        if (path != null)
        {
            assetId = null;
            bundleId = null;
        }
    }

    public static IReadOnlyList<BundleListItem> ListBundleFiles(
        SoftRefIndex index)
    {
        var items = new List<BundleListItem>();
        foreach (var file in Directory.EnumerateFiles(index.BundlesDirectory))
        {
            var id = Path.GetFileName(file);
            if (id.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                continue;

            index.BundleDependencies.TryGetValue(id, out var deps);
            var info = new FileInfo(file);
            items.Add(new BundleListItem
            {
                BundleId = id,
                FullPath = file,
                SizeBytes = info.Length,
                DependencyCount = deps?.Count ?? 0,
            });
        }

        return items.OrderBy(b => b.BundleId, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
