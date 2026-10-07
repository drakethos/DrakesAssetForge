using System.IO.Compression;

namespace DrakeAssetForge.Services;

/// <summary>Folder project copy + .daf zip pack/unpack (project.json + Items/ + Imports/).</summary>
public static class ProjectPackageService
{
    public static void CopyProject(string sourceRoot, string destRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException("Source project folder was not found.");
        if (string.Equals(
                Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(destRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Source and destination are the same folder.");

        Directory.CreateDirectory(destRoot);
        CopyDirectory(sourceRoot, destRoot);
        var marker = Path.Combine(destRoot, "project.json");
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, """
                {
                  "name": "Imported",
                  "schemaVersion": 1
                }
                """);
        }
    }

    public static void ExportDaf(string projectRoot, string dafPath)
    {
        if (!Directory.Exists(projectRoot))
            throw new DirectoryNotFoundException("Project folder was not found.");
        if (File.Exists(dafPath))
            File.Delete(dafPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dafPath))!);
        ZipFile.CreateFromDirectory(projectRoot, dafPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    public static void ImportDaf(string dafPath, string destRoot)
    {
        if (!File.Exists(dafPath))
            throw new FileNotFoundException("Package not found.", dafPath);

        if (Directory.Exists(destRoot) && Directory.EnumerateFileSystemEntries(destRoot).Any())
            throw new InvalidOperationException("Destination folder is not empty. Pick an empty folder.");

        Directory.CreateDirectory(destRoot);
        ZipFile.ExtractToDirectory(dafPath, destRoot);
        var marker = Path.Combine(destRoot, "project.json");
        if (!File.Exists(marker))
        {
            File.WriteAllText(marker, """
                {
                  "name": "Imported",
                  "schemaVersion": 1
                }
                """);
        }

        Directory.CreateDirectory(Path.Combine(destRoot, "Items"));
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, Path.Combine(dest, name), overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(dir);
            if (name is ".git" or "bin" or "obj")
                continue;
            CopyDirectory(dir, Path.Combine(dest, name));
        }
    }
}
