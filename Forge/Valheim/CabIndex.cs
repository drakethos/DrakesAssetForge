using AssetsTools.NET.Extra;

namespace DrakesForge.Valheim;

/// <summary>
/// Maps the internal "CAB-…" file names that cross-bundle pointers use to SoftRef bundle files.
/// Built by reading every bundle's header once (no decompression), then cached per Valheim build.
/// </summary>
internal sealed class CabIndex
{
    private readonly Dictionary<string, string> _cabToBundle;

    private CabIndex(Dictionary<string, string> map) => _cabToBundle = map;

    /// <param name="externalPath">"archive:/CAB-x/CAB-x" as stored in an assets file's externals.</param>
    public string? BundleFor(string externalPath)
    {
        var name = externalPath.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return _cabToBundle.TryGetValue(name, out var bundle) ? bundle : null;
    }

    public static CabIndex LoadOrBuild(ValheimInstall install)
    {
        var manifest = Path.Combine(install.SoftRefRoot, "manifest");
        var stamp = File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest).Ticks.ToString() : "0";
        var cacheFile = Path.Combine(ForgePaths.CacheDirectory, "cab-index.txt");

        if (TryLoad(cacheFile, stamp) is { } cached)
            return cached;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundlePath in Directory.EnumerateFiles(install.BundlesDirectory))
        {
            if (bundlePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
                continue;
            var am = new AssetsManager();
            try
            {
                var bundle = am.LoadBundleFile(bundlePath, false);
                foreach (var name in bundle.file.GetAllFileNames())
                    map.TryAdd(name, Path.GetFileName(bundlePath));
            }
            catch (Exception)
            {
                // not a bundle; skip
            }
            finally
            {
                am.UnloadAll(true);
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllLines(cacheFile, new[] { stamp }.Concat(map.Select(kv => kv.Key + "\t" + kv.Value)));
        }
        catch (IOException)
        {
            // cache is an optimisation only
        }

        return new CabIndex(map);
    }

    private static CabIndex? TryLoad(string cacheFile, string stamp)
    {
        try
        {
            if (!File.Exists(cacheFile))
                return null;
            var lines = File.ReadAllLines(cacheFile);
            if (lines.Length == 0 || lines[0] != stamp)
                return null;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                var tab = line.IndexOf('\t');
                if (tab > 0)
                    map[line[..tab]] = line[(tab + 1)..];
            }

            return new CabIndex(map);
        }
        catch (IOException)
        {
            return null;
        }
    }
}

public static class ForgePaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DrakesAssetForge");

    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

    /// <summary>Folder Forge Runtime watches; "Push to game" writes packs here.</summary>
    public static string PushDirectory => Path.Combine(DataDirectory, "Push");
}
