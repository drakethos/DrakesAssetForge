using System.Xml.Linq;

namespace DrakesForge.Valheim;

/// <summary>Locates a Valheim install and its SoftRef asset store.</summary>
public sealed class ValheimInstall
{
    private ValheimInstall(string root, string softRef)
    {
        Root = root;
        SoftRefRoot = softRef;
    }

    public string Root { get; }
    public string SoftRefRoot { get; }
    public string BundlesDirectory => Path.Combine(SoftRefRoot, "Bundles");
    public string ManagedDirectory => Path.Combine(Root, "valheim_Data", "Managed");

    public static ValheimInstall? TryOpen(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var softRef = Path.Combine(root, "valheim_Data", "StreamingAssets", "SoftRef");
        return Directory.Exists(Path.Combine(softRef, "Bundles")) ? new ValheimInstall(root, softRef) : null;
    }

    /// <summary>Common Steam locations, then any environment.props above <paramref name="startDirectory"/>.</summary>
    public static ValheimInstall? Find(string? startDirectory = null)
    {
        foreach (var candidate in Candidates(startDirectory))
            if (TryOpen(candidate) is { } install)
                return install;
        return null;
    }

    private static IEnumerable<string> Candidates(string? startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var props = Path.Combine(dir.FullName, "environment.props");
            if (!File.Exists(props))
                continue;
            string? path = null;
            try
            {
                path = XDocument.Load(props).Descendants("ValheimGamePath").FirstOrDefault()?.Value.Trim();
            }
            catch (Exception)
            {
                // unreadable props: keep looking
            }

            if (!string.IsNullOrEmpty(path))
                yield return path;
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(programFilesX86, "Steam", "steamapps", "common", "Valheim");
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            yield return Path.Combine(drive.Name, "SteamLibrary", "steamapps", "common", "Valheim");
            yield return Path.Combine(drive.Name, "Steam", "steamapps", "common", "Valheim");
        }
    }
}
