using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DrakeAssetForge.Services;

public sealed class UnityArtCompileRequest
{
    public required string MeshPath { get; init; }
    public string? DiffusePath { get; init; }
    public required string OutputBundlePath { get; init; }
    public required string BundleName { get; init; }
}

public sealed class UnityArtCompileResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? BundlePath { get; init; }
    public string? LogPath { get; init; }
}

public sealed class UnityArtExtractRequest
{
    public required string SourceBundlePath { get; init; }
    public required string PrefabName { get; init; }
    public required string OutputBundlePath { get; init; }
    public required string BundleName { get; init; }
}

public sealed class UnityArtPackFolderRequest
{
    public required string OutputBundlePath { get; init; }
    public required string BundleName { get; init; }
    public required IReadOnlyList<(string SourceArtBundle, string PrefabName, string? DiffusePath)> Entries { get; init; }
}

/// <summary>
/// Copies the shipped Unity template to LocalAppData and runs Unity batchmode.
/// The template never contains Valheim assets or Valheim shaders.
/// </summary>
public static class UnityArtCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Regex UnityVersionRegex = new(@"\d+\.\d+\.\d+[a-z]\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrakeAssetForge",
            "settings.json");

    public static string? TryLoadSavedUnityExe() => AppSettings.TryLoadUnityExe();

    public static void SaveUnityExe(string unityExe) => AppSettings.SaveUnityExe(unityExe);

    public static string CompilerRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrakeAssetForge",
            "UnityArtCompiler");

    public static string? DetectValheimUnityVersion(string? valheimPath)
    {
        if (string.IsNullOrWhiteSpace(valheimPath))
            return null;

        var boot = Path.Combine(valheimPath, "valheim_Data", "boot.config");
        if (!File.Exists(boot))
            return null;

        foreach (var line in File.ReadLines(boot))
        {
            var trimmed = line.Trim();
            if (trimmed.Length >= 8 && char.IsDigit(trimmed[0]) && trimmed.Contains('.'))
                return trimmed;
        }

        return null;
    }

    public static string? FindBestEditor(string? requiredVersion, out string note)
    {
        note = "";
        var editors = ListInstalledEditors();
        if (editors.Count == 0)
        {
            note = "No Unity Hub editors found under Program Files\\Unity\\Hub\\Editor.";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requiredVersion))
        {
            var exact = editors.FirstOrDefault(e =>
                e.Version.Equals(requiredVersion, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                note = $"Using Unity {exact.Version} (matches Valheim).";
                return exact.ExePath;
            }
        }

        var sameLine = editors
            .Where(e => e.Version.StartsWith("6000.", StringComparison.Ordinal))
            .OrderByDescending(e => e.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (sameLine != null)
        {
            note = requiredVersion == null
                ? $"Using Unity {sameLine.Version}."
                : $"Valheim is Unity {requiredVersion}. Closest install is {sameLine.Version} — bundle load may fail until you install the matching editor.";
            return sameLine.ExePath;
        }

        var any = editors.OrderByDescending(e => e.Version, StringComparer.OrdinalIgnoreCase).First();
        note = $"Only found Unity {any.Version}. Valheim needs {requiredVersion ?? "Unity 6"}. Install a matching editor before compiling.";
        return any.ExePath;
    }

    public static async Task<UnityArtCompileResult> CompileAsync(
        string unityExe,
        string templateSource,
        UnityArtCompileRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(unityExe))
            return Fail($"Unity.exe not found: {unityExe}");
        if (!File.Exists(request.MeshPath))
            return Fail($"Mesh not found: {request.MeshPath}");
        if (!Directory.Exists(templateSource))
            return Fail($"Unity template missing: {templateSource}");

        var editor = ReadEditorIdentity(unityExe);
        progress?.Report("Preparing silent Unity project…");
        PrepareProject(templateSource, editor);

        var outputDir = Path.Combine(CompilerRoot, "out");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        var job = new
        {
            meshPath = request.MeshPath.Replace('\\', '/'),
            diffusePath = request.DiffusePath?.Replace('\\', '/') ?? "",
            outputDirectory = outputDir.Replace('\\', '/'),
            bundleName = request.BundleName.ToLowerInvariant(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(CompilerRoot, "art-job.json"),
            JsonSerializer.Serialize(job, JsonOptions),
            cancellationToken);

        var logPath = Path.Combine(CompilerRoot, "Logs", "compile.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath))
            File.Delete(logPath);

        progress?.Report("Unity batchmode compiling art bundle (first run can take several minutes)…");

        var start = new ProcessStartInfo(unityExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-batchmode");
        start.ArgumentList.Add("-nographics");
        start.ArgumentList.Add("-quit");
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(CompilerRoot);
        start.ArgumentList.Add("-buildTarget");
        start.ArgumentList.Add("StandaloneWindows64");
        start.ArgumentList.Add("-logFile");
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add("-executeMethod");
        start.ArgumentList.Add("DrakesAssetForge.ArtBuild.ArtBundleBuilder.Build");

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start Unity.");
        await process.WaitForExitAsync(cancellationToken);

        var built = Path.Combine(outputDir, request.BundleName.ToLowerInvariant());
        if (process.ExitCode != 0 || !File.Exists(built))
        {
            return new UnityArtCompileResult
            {
                Success = false,
                LogPath = logPath,
                Message = $"Unity exited {process.ExitCode}. {TailLog(logPath)}",
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputBundlePath)!);
        File.Copy(built, request.OutputBundlePath, overwrite: true);
        var size = new FileInfo(request.OutputBundlePath).Length;
        return new UnityArtCompileResult
        {
            Success = true,
            BundlePath = request.OutputBundlePath,
            LogPath = logPath,
            Message = $"Wrote art.bundle ({size:N0} bytes) with prefab 'art'. Rebuild the smoke plugin and spawn the item to see it.",
        };
    }

    public static async Task<UnityArtCompileResult> ExtractPrefabAsync(
        string unityExe,
        string templateSource,
        UnityArtExtractRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(unityExe))
            return Fail($"Unity.exe not found: {unityExe}");
        if (!File.Exists(request.SourceBundlePath))
            return Fail($"Source bundle not found: {request.SourceBundlePath}");
        if (!Directory.Exists(templateSource))
            return Fail($"Unity template missing: {templateSource}");

        var editor = ReadEditorIdentity(unityExe);
        progress?.Report("Preparing silent Unity project for extract…");
        PrepareProject(templateSource, editor);

        var outputDir = Path.Combine(CompilerRoot, "out");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        var job = new
        {
            sourceBundle = request.SourceBundlePath.Replace('\\', '/'),
            prefabName = request.PrefabName,
            outputDirectory = outputDir.Replace('\\', '/'),
            bundleName = request.BundleName.ToLowerInvariant(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(CompilerRoot, "extract-job.json"),
            JsonSerializer.Serialize(job, JsonOptions),
            cancellationToken);

        var logPath = Path.Combine(CompilerRoot, "Logs", "extract.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath))
            File.Delete(logPath);

        progress?.Report("Unity batchmode extracting prefab into art.bundle…");

        var start = new ProcessStartInfo(unityExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-batchmode");
        start.ArgumentList.Add("-nographics");
        start.ArgumentList.Add("-quit");
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(CompilerRoot);
        start.ArgumentList.Add("-buildTarget");
        start.ArgumentList.Add("StandaloneWindows64");
        start.ArgumentList.Add("-logFile");
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add("-executeMethod");
        start.ArgumentList.Add("DrakesAssetForge.ArtBuild.ArtBundleBuilder.Extract");

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start Unity.");
        await process.WaitForExitAsync(cancellationToken);

        var built = Path.Combine(outputDir, request.BundleName.ToLowerInvariant());
        if (process.ExitCode != 0 || !File.Exists(built))
        {
            return new UnityArtCompileResult
            {
                Success = false,
                LogPath = logPath,
                Message = $"Unity extract exited {process.ExitCode}. {TailLog(logPath)}",
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputBundlePath)!);
        File.Copy(built, request.OutputBundlePath, overwrite: true);
        var size = new FileInfo(request.OutputBundlePath).Length;
        return new UnityArtCompileResult
        {
            Success = true,
            BundlePath = request.OutputBundlePath,
            LogPath = logPath,
            Message = $"Extracted art.bundle ({size:N0} bytes) from '{request.PrefabName}'.",
        };
    }

    public static async Task<UnityArtCompileResult> PackFolderAsync(
        string unityExe,
        string templateSource,
        UnityArtPackFolderRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(unityExe))
            return Fail($"Unity.exe not found: {unityExe}");
        if (!Directory.Exists(templateSource))
            return Fail($"Unity template missing: {templateSource}");
        if (request.Entries.Count == 0)
            return Fail("PackFolder has no entries.");

        foreach (var (source, name, _) in request.Entries)
        {
            if (!File.Exists(source))
                return Fail($"Missing per-item art.bundle for '{name}': {source}");
        }

        var editor = ReadEditorIdentity(unityExe);
        progress?.Report($"Preparing Unity pack for {request.BundleName}.bundle…");
        PrepareProject(templateSource, editor);

        var outputDir = Path.Combine(CompilerRoot, "out");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, true);
        Directory.CreateDirectory(outputDir);

        var job = new
        {
            outputDirectory = outputDir.Replace('\\', '/'),
            bundleName = request.BundleName.ToLowerInvariant(),
            entries = request.Entries.Select(e => new
            {
                sourceBundle = e.SourceArtBundle.Replace('\\', '/'),
                prefabName = e.PrefabName,
                diffusePath = string.IsNullOrWhiteSpace(e.DiffusePath) ? "" : e.DiffusePath!.Replace('\\', '/'),
            }).ToArray(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(CompilerRoot, "pack-folder-job.json"),
            JsonSerializer.Serialize(job, JsonOptions),
            cancellationToken);

        var logPath = Path.Combine(CompilerRoot, "Logs", "pack-folder.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        if (File.Exists(logPath))
            File.Delete(logPath);

        progress?.Report($"Unity packing {request.Entries.Count} prefab(s) into {request.BundleName}.bundle…");

        var start = new ProcessStartInfo(unityExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-batchmode");
        start.ArgumentList.Add("-nographics");
        start.ArgumentList.Add("-quit");
        start.ArgumentList.Add("-projectPath");
        start.ArgumentList.Add(CompilerRoot);
        start.ArgumentList.Add("-buildTarget");
        start.ArgumentList.Add("StandaloneWindows64");
        start.ArgumentList.Add("-logFile");
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add("-executeMethod");
        start.ArgumentList.Add("DrakesAssetForge.ArtBuild.ArtBundleBuilder.PackFolder");

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Failed to start Unity.");
        await process.WaitForExitAsync(cancellationToken);

        var built = Path.Combine(outputDir, request.BundleName.ToLowerInvariant());
        if (process.ExitCode != 0 || !File.Exists(built))
        {
            return new UnityArtCompileResult
            {
                Success = false,
                LogPath = logPath,
                Message = $"Unity PackFolder exited {process.ExitCode}. {TailLog(logPath)}",
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputBundlePath)!);
        File.Copy(built, request.OutputBundlePath, overwrite: true);
        var size = new FileInfo(request.OutputBundlePath).Length;
        return new UnityArtCompileResult
        {
            Success = true,
            BundlePath = request.OutputBundlePath,
            LogPath = logPath,
            Message = $"Packed {request.BundleName}.bundle ({size:N0} bytes) with {request.Entries.Count} prefab(s).",
        };
    }

    public static string? FindTemplateSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "UnityTemplate");
            var script = Path.Combine(candidate, "Assets", "Editor", "ArtBundleBuilder.cs");
            if (File.Exists(script))
                return candidate;
            dir = dir.Parent;
        }

        return null;
    }

    private static void PrepareProject(string templateSource, EditorIdentity editor)
    {
        Directory.CreateDirectory(CompilerRoot);
        CopyTemplateFiles(templateSource, CompilerRoot);

        var stampPath = Path.Combine(CompilerRoot, "editor-version.txt");
        var previous = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : "";
        if (!string.IsNullOrEmpty(previous) &&
            !previous.Equals(editor.Version, StringComparison.OrdinalIgnoreCase))
        {
            DeleteIfExists(Path.Combine(CompilerRoot, "Library"));
            DeleteIfExists(Path.Combine(CompilerRoot, "Temp"));
        }

        File.WriteAllText(stampPath, editor.Version);
        var withRevision = string.IsNullOrWhiteSpace(editor.Revision)
            ? editor.Version
            : $"{editor.Version} ({editor.Revision})";
        Directory.CreateDirectory(Path.Combine(CompilerRoot, "ProjectSettings"));
        File.WriteAllText(
            Path.Combine(CompilerRoot, "ProjectSettings", "ProjectVersion.txt"),
            $"m_EditorVersion: {editor.Version}{Environment.NewLine}m_EditorVersionWithRevision: {withRevision}{Environment.NewLine}");
    }

    private static void CopyTemplateFiles(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.StartsWith("Library", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("Temp", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("Logs", StringComparison.OrdinalIgnoreCase) ||
                rel.StartsWith("UserSettings", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static EditorIdentity ReadEditorIdentity(string unityExe)
    {
        // Hub layout is .../Editor/<version>/Editor/Unity.exe.
        // The standalone installer uses .../Unity <version>/Editor/Unity.exe, so the folder name is not a version.
        var version = ExtractUnityVersion(Directory.GetParent(unityExe)?.Parent?.Name);
        string? revision = null;

        try
        {
            var product = FileVersionInfo.GetVersionInfo(unityExe).ProductVersion ?? "";
            version ??= ExtractUnityVersion(product);
            var underscore = product.IndexOf('_');
            if (underscore >= 0 && underscore < product.Length - 1)
                revision = product[(underscore + 1)..].Trim();
        }
        catch
        {
            // Folder name is enough when the binary metadata is unreadable.
        }

        return new EditorIdentity(version ?? "6000.0.75f1", revision);
    }

    private static string? ExtractUnityVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = UnityVersionRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    private static IReadOnlyList<EditorInstall> ListInstalledEditors()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Unity", "Hub", "Editor"),
        };

        var found = new List<EditorInstall>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var exe = Path.Combine(dir, "Editor", "Unity.exe");
                if (File.Exists(exe))
                    found.Add(new EditorInstall(Path.GetFileName(dir), exe));
            }
        }

        return found;
    }

    private static string TailLog(string logPath)
    {
        if (!File.Exists(logPath))
            return "No Unity log was written.";

        var lines = File.ReadAllLines(logPath);
        var interesting = lines.Where(IsActionableLogLine).TakeLast(3).ToList();
        if (interesting.Count == 0)
            interesting = lines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(3).ToList();

        return string.Join(" ", interesting.Select(l => l.Trim()));
    }

    private static bool IsActionableLogLine(string line)
    {
        // Batchmode always logs this once, then activates the personal license anyway.
        if (line.Contains("Access token is unavailable", StringComparison.OrdinalIgnoreCase))
            return false;
        if (line.Contains("LogAssemblyErrors", StringComparison.OrdinalIgnoreCase))
            return false;

        return line.Contains("Exception", StringComparison.OrdinalIgnoreCase)
               || line.Contains("is not a valid", StringComparison.OrdinalIgnoreCase)
               || line.Contains("Failed", StringComparison.OrdinalIgnoreCase)
               || line.Contains("error CS", StringComparison.OrdinalIgnoreCase)
               || line.Contains("BuildAssetBundles", StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }

    private static UnityArtCompileResult Fail(string message) =>
        new() { Success = false, Message = message };

    private sealed record EditorInstall(string Version, string ExePath);

    private sealed record EditorIdentity(string Version, string? Revision);
}
