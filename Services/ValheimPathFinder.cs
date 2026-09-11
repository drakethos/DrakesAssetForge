using System.Xml.Linq;

namespace DrakeAssetForge.Services;

public static class ValheimPathFinder
{
    public static string? TryFindFromEnvironmentProps(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (dir != null)
        {
            var props = Path.Combine(dir.FullName, "environment.props");
            if (File.Exists(props))
            {
                var path = ReadValheimGamePath(props);
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    return path;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static string? ReadValheimGamePath(string environmentPropsPath)
    {
        try
        {
            var doc = XDocument.Load(environmentPropsPath);
            var value = doc.Descendants("ValheimGamePath").FirstOrDefault()?.Value?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    public static string? GetSoftRefRoot(string valheimInstall)
    {
        var softRef = Path.Combine(valheimInstall, "valheim_Data", "StreamingAssets", "SoftRef");
        return Directory.Exists(softRef) ? softRef : null;
    }

    public static string? GetManagedDirectory(string valheimInstall)
    {
        var managed = Path.Combine(valheimInstall, "valheim_Data", "Managed");
        return Directory.Exists(managed) ? managed : null;
    }

    public static string? GetBundlesDirectory(string softRefRoot)
    {
        var bundles = Path.Combine(softRefRoot, "Bundles");
        return Directory.Exists(bundles) ? bundles : null;
    }

    public static bool LooksLikeValheimInstall(string path)
    {
        return Directory.Exists(Path.Combine(path, "valheim_Data"));
    }
}
