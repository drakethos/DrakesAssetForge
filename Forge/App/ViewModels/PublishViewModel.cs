using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;
using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public sealed class CheckRow
{
    public required bool Ok { get; init; }
    public required string Text { get; init; }
    public string Glyph => Ok ? "✓" : "!";
}

/// <summary>
/// Step 4: Thunderstore details (kept in the pack: README.md, CHANGELOG.md, icon.png), checks, and the two outputs:
/// a data-pack zip (needs Forge Runtime) or a C# mod project (Forge compiled in, with your own code per item).
/// </summary>
public sealed partial class PublishViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _loading;

    public PublishViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<CheckRow> Checks { get; } = new();
    public ObservableCollection<string> ShippedFiles { get; } = new();
    public ObservableCollection<string> CodeLog { get; } = new();

    [ObservableProperty] private string _packName = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _website = "";
    [ObservableProperty] private string _readme = "";
    [ObservableProperty] private string _changelogNote = "";
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _result = "";

    // Output
    [ObservableProperty] private string _output = "zip";
    [ObservableProperty] private string _codeFolder = "";
    [ObservableProperty] private bool _intoExisting;
    [ObservableProperty] private bool _building;

    public bool Dirty { get; set; }
    public bool OutputZip => Output == "zip";
    /// <summary>Either C# output: the project actions are shared.</summary>
    public bool OutputCode => Output is "code" or "plain";
    public bool OutputForgeCode => Output == "code";
    public bool OutputPlain => Output == "plain";
    public string DependencyText => Output switch
    {
        "zip" => "Players need: " + string.Join(", ", ThunderstoreFiles.DataPackDependencies),
        "plain" => "Players need: " + string.Join(", ", ThunderstoreFiles.CodeModDependencies) + " (plain C#, no Forge at all)",
        _ => "Players need: " + string.Join(", ", ThunderstoreFiles.CodeModDependencies) + " (Forge is compiled into your mod)"
    };

    partial void OnOutputChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(OutputZip));
        OnPropertyChanged(nameof(OutputCode));
        OnPropertyChanged(nameof(OutputForgeCode));
        OnPropertyChanged(nameof(OutputPlain));
        OnPropertyChanged(nameof(DependencyText));
        // Keep the two C# exports in separate default folders (their Customize files differ).
        if (_main.Pack is { } pack && !IntoExisting)
        {
            if (newValue == "plain" && CodeFolder == CodeProjectWriter.DefaultFolder(pack))
                CodeFolder = LiteCodeWriter.DefaultFolder(pack);
            else if (newValue == "code" && CodeFolder == LiteCodeWriter.DefaultFolder(pack))
                CodeFolder = CodeProjectWriter.DefaultFolder(pack);
        }
    }

    public void Reload()
    {
        var pack = _main.Pack;
        if (pack == null)
            return;
        _loading = true;
        PackName = pack.Manifest.Name;
        Version = pack.Manifest.Version;
        Author = pack.Manifest.Author;
        Description = pack.Manifest.Description;
        Website = pack.Manifest.Website;
        Readme = ReadPackFile("README.md") ?? "";
        if (CodeFolder.Length == 0)
            CodeFolder = CodeProjectWriter.DefaultFolder(pack);
        _loading = false;
        LoadIcon();
        RunChecks();
    }

    partial void OnPackNameChanged(string value) => SaveManifest();
    partial void OnVersionChanged(string value) => SaveManifest();
    partial void OnAuthorChanged(string value) => SaveManifest();
    partial void OnDescriptionChanged(string value) => SaveManifest();
    partial void OnWebsiteChanged(string value) => SaveManifest();

    partial void OnReadmeChanged(string value)
    {
        if (_loading || _main.Pack == null)
            return;
        WritePackFile("README.md", value);
        RunChecks();
    }

    private void SaveManifest()
    {
        var pack = _main.Pack;
        if (pack == null || _loading)
            return;
        pack.Manifest.Name = PackName.Trim();
        pack.Manifest.Version = Version.Trim();
        pack.Manifest.Author = Author.Trim();
        pack.Manifest.Description = Description.Trim();
        pack.Manifest.Website = Website.Trim();
        pack.SaveManifest();
        RunChecks();
    }

    private void RunChecks()
    {
        var pack = _main.Pack;
        Checks.Clear();
        ShippedFiles.Clear();
        if (pack == null)
            return;

        var loaded = PackReader.Load(pack.Root);
        Checks.Add(new CheckRow { Ok = loaded.Recipes.Count > 0, Text = $"{loaded.Recipes.Count} recipe(s)" });
        Checks.Add(new CheckRow { Ok = loaded.Problems.Count == 0, Text = loaded.Problems.Count == 0 ? "No recipe problems" : string.Join("\n", loaded.Problems) });

        var vanilla = _main.Vanilla;
        if (vanilla != null)
        {
            var missing = loaded.Recipes.Where(r => !vanilla.Catalog.ByName.ContainsKey(r.Base)).Select(r => r.Base).ToList();
            Checks.Add(new CheckRow { Ok = missing.Count == 0, Text = missing.Count == 0 ? "Every base prefab exists in your Valheim" : $"Not in this Valheim: {string.Join(", ", missing)}" });
            var badItems = loaded.Recipes.SelectMany(r => r.Craft?.Requirements ?? new List<Requirement>())
                .Select(q => q.Item).Where(i => !vanilla.Catalog.ByName.ContainsKey(i)).Distinct().ToList();
            Checks.Add(new CheckRow { Ok = badItems.Count == 0, Text = badItems.Count == 0 ? "Every cost item exists" : $"Unknown cost items: {string.Join(", ", badItems)}" });
        }

        Checks.Add(new CheckRow { Ok = Regex.IsMatch(pack.Manifest.Version, @"^\d+\.\d+\.\d+$"), Text = "Version looks like 1.2.3" });
        Checks.Add(new CheckRow { Ok = pack.Manifest.Description.Length is > 0 and <= 250, Text = "Description (1–250 characters)" });
        Checks.Add(new CheckRow { Ok = Icon != null, Text = "icon.png, 256×256" });
        Checks.Add(new CheckRow { Ok = Readme.Trim().Length > 0, Text = "README.md" });
        Checks.Add(new CheckRow { Ok = true, Text = "0 Valheim files in the pack: meshes and materials are borrowed by name" });

        foreach (var file in pack.ShippedFiles())
        {
            var info = new FileInfo(file);
            ShippedFiles.Add($"{Path.GetRelativePath(pack.Root, file).Replace('\\', '/'),-44} {info.Length / 1024.0:0.#} KB");
        }
    }

    // Thunderstore files -------------------------------------------------------------------------

    private void LoadIcon()
    {
        var path = _main.Pack != null ? Path.Combine(_main.Pack.Root, "icon.png") : null;
        Icon = Images.LoadFile(path) is { } img ? Images.ToBitmap(img) : null;
    }

    [RelayCommand]
    private async Task ChooseIcon()
    {
        var file = await Dialogs.PickFileAsync("Pack icon (any size; saved as 256×256 PNG)", "Images", "*.png", "*.jpg", "*.jpeg");
        if (file == null || _main.Pack == null || Images.LoadFile(file) is not { } img)
            return;
        Images.SavePng(Images.Resize(img, 256, 256), Path.Combine(_main.Pack.Root, "icon.png"));
        LoadIcon();
        RunChecks();
    }

    /// <summary>Builds icon.png from the items' own icons (custom icons first, else the vanilla base's).</summary>
    [RelayCommand]
    private void MakeIcon()
    {
        var pack = _main.Pack;
        if (pack == null)
            return;
        var icons = new List<RgbaImage>();
        foreach (var r in pack.Recipes.OrderBy(r => r.Kind == RecipeKind.Reskin).ThenBy(r => r.Name ?? r.Id))
        {
            var img = Images.LoadFile(pack.FullPath(r.Look.Icon)) ?? Images.LoadFile(Path.Combine(ForgePaths.CacheDirectory, "icons", r.Base + ".png"));
            if (img != null)
                icons.Add(img);
            if (icons.Count == 4)
                break;
        }

        if (icons.Count == 0)
        {
            Result = "No item icons to build from yet. Open the items once (so their icons load) or choose a PNG.";
            return;
        }

        Images.SavePng(Images.IconMontage(icons), Path.Combine(pack.Root, "icon.png"));
        LoadIcon();
        RunChecks();
    }

    [RelayCommand]
    private void GenerateReadme()
    {
        if (_main.Pack != null)
            Readme = ThunderstoreFiles.DefaultReadme(_main.Pack);
    }

    /// <summary>Adds the "what's new" note to CHANGELOG.md under the current version (once per version).</summary>
    private void RecordChangelog()
    {
        var note = ChangelogNote.Trim();
        if (note.Length == 0 || _main.Pack == null)
            return;
        var existing = ReadPackFile("CHANGELOG.md") ?? "# Changelog\n";
        var heading = $"## {Version.Trim()}";
        var entry = string.Join("\n", note.Split('\n').Select(l => l.TrimStart('-', ' ').Trim()).Where(l => l.Length > 0).Select(l => "- " + l));
        var text = existing.Contains(heading)
            ? existing.Replace(heading + "\n", heading + "\n" + entry + "\n")
            : Regex.Replace(existing, "^(# Changelog\\n)?", m => (m.Value.Length > 0 ? m.Value : "# Changelog\n") + "\n" + heading + "\n" + entry + "\n");
        WritePackFile("CHANGELOG.md", text);
        ChangelogNote = "";
    }

    [RelayCommand]
    private void BumpVersion()
    {
        var parts = Version.Split('.');
        if (parts.Length == 3 && int.TryParse(parts[2], out var patch))
            Version = $"{parts[0]}.{parts[1]}.{patch + 1}";
    }

    // Data pack zip ------------------------------------------------------------------------------

    [RelayCommand]
    private void Push() => _main.PushToGameCommand.Execute(null);

    [RelayCommand]
    private void SaveZip()
    {
        var pack = _main.Pack;
        if (pack == null)
            return;
        RecordChangelog();

        var name = PushTarget.Sanitize(pack.Manifest.Id);
        var author = PushTarget.Sanitize(pack.Manifest.Author.Length > 0 ? pack.Manifest.Author : "Unknown");
        var outDir = ReleasesFolder();
        var zipPath = Path.Combine(outDir, $"{author}-{name}-{pack.Manifest.Version}.zip");

        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

            // Pack files at the zip root: Thunderstore installs them to plugins/<Author>-<Name>/, where Forge Runtime finds them.
            foreach (var file in pack.ShippedFiles())
                if (Path.GetFileName(file) != "manifest.json")
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(pack.Root, file).Replace('\\', '/'));

            WriteEntry(zip, "manifest.json", ThunderstoreFiles.Manifest(pack, ThunderstoreFiles.DataPackDependencies));
            if (!File.Exists(Path.Combine(pack.Root, "README.md")))
                WriteEntry(zip, "README.md", ThunderstoreFiles.DefaultReadme(pack));
            Result = $"Saved {zipPath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Result = $"Couldn't write the zip: {ex.Message}";
        }

        RunChecks();
    }

    [RelayCommand]
    private void OpenReleases() => Process.Start("explorer.exe", ReleasesFolder());

    // C# mod -------------------------------------------------------------------------------------

    [RelayCommand]
    private void SetOutput(string output) => Output = output;

    [RelayCommand]
    private async Task ChooseCodeFolder()
    {
        var folder = await Dialogs.PickFolderAsync(IntoExisting ? "Your mod's project folder (where its .csproj is)" : "Folder for the new C# mod project");
        if (folder != null)
            CodeFolder = folder;
    }

    [RelayCommand]
    private async Task GenerateCode()
    {
        var pack = _main.Pack;
        if (pack == null)
            return;
        RecordChangelog();
        CodeLog.Clear();
        try
        {
            CodeExportResult result;
            if (OutputPlain)
            {
                if (_main.Vanilla is not { } vanilla)
                {
                    Result = "Plain C# needs Valheim found (Settings): the code is typed from the game's own scripts.";
                    return;
                }

                CodeLog.Add("Reading the game's scripts…");
                var types = await LiteTypeInfo.LoadAsync(pack.Recipes, vanilla.InspectAsync, vanilla.ComponentDefaultsAsync);
                CodeLog.Clear();
                result = LiteCodeWriter.Write(pack, CodeFolder, IntoExisting, types, _main.CurrentPushTarget, vanilla.Catalog.Install.Root);
            }
            else
            {
                result = CodeProjectWriter.Write(pack, CodeFolder, IntoExisting, _main.CurrentPushTarget, _main.Vanilla?.Catalog.Install.Root);
            }

            CodeLog.Add($"Updated {result.Written.Count} file(s) in {result.Folder}");
            foreach (var kept in result.Kept)
                CodeLog.Add($"kept yours: {Path.GetRelativePath(result.Folder, kept)}");
            CodeLog.Add((IntoExisting, OutputPlain) switch
            {
                (true, true) => "Added Items\\, Lite\\, ForgeLiteItems.g.cs, Customize\\ and Assets\\. FORGE-PLAIN.md says what to add to your plugin and csproj.",
                (true, false) => "Added Forge\\, ForgeHooks.g.cs, Customize\\ and Pack\\. FORGE.md says what to add to your plugin and csproj.",
                _ => "Open the .csproj in your IDE, or press Build. Your code goes in Customize\\<Item>.cs."
            });
            Result = $"C# project ready: {result.Folder}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Result = $"Couldn't write the project: {ex.Message}";
        }
    }

    /// <summary>Runs dotnet build in the project (needs the .NET SDK); the project's own target deploys to the test profile.</summary>
    [RelayCommand]
    private async Task BuildCode()
    {
        if (!Directory.Exists(CodeFolder) || Building)
            return;
        Building = true;
        CodeLog.Clear();
        CodeLog.Add("Building…");
        try
        {
            var psi = new ProcessStartInfo("dotnet", "build -nologo -v:q")
            {
                WorkingDirectory = CodeFolder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            CodeLog.Clear();
            var lines = output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Contains(": error ") || l.Contains("Deployed ") || l.Contains("Build succeeded") || l.Contains("Build FAILED")).Distinct().Take(30).ToList();
            foreach (var line in lines)
                CodeLog.Add(Regex.Replace(line, @"\s*\[[^\]]+\.csproj\]$", ""));
            Result = process.ExitCode == 0 ? "Build succeeded" : "Build failed (see below)";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            CodeLog.Clear();
            CodeLog.Add("Couldn't run dotnet. Install the .NET SDK, or open the project in Visual Studio / Rider.");
        }
        finally
        {
            Building = false;
        }
    }

    [RelayCommand]
    private void OpenCodeFolder()
    {
        if (Directory.Exists(CodeFolder))
            Process.Start("explorer.exe", CodeFolder);
    }

    [RelayCommand]
    private void OpenInIde()
    {
        var csproj = Directory.Exists(CodeFolder) ? Directory.GetFiles(CodeFolder, "*.csproj").FirstOrDefault() : null;
        if (csproj != null)
            Process.Start(new ProcessStartInfo(csproj) { UseShellExecute = true });
    }

    // Helpers -------------------------------------------------------------------------------------

    private static string ReleasesFolder()
    {
        var dir = Path.Combine(Path.GetDirectoryName(PackProject.DefaultPacksDirectory)!, "Releases");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private string? ReadPackFile(string name)
    {
        var path = _main.Pack != null ? Path.Combine(_main.Pack.Root, name) : null;
        return path != null && File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private void WritePackFile(string name, string text)
    {
        if (_main.Pack != null)
            File.WriteAllText(Path.Combine(_main.Pack.Root, name), text);
    }

    private static void WriteEntry(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(text);
    }
}
