using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

/// <summary>
/// Command-line: re-extract/pack a Project folder group (e.g. keys) with embedded meshes,
/// then ship <c>{group}.bundle</c> + wire JSON into a mod/plugin Assets/Items/{group}/ folder.
/// </summary>
public static class KeysFolderRepackCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var opts = Parse(args);
        if (opts.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        opts = ApplyDefaults(opts);
        if (string.IsNullOrWhiteSpace(opts.ProjectRoot))
        {
            PrintHelp();
            Console.Error.WriteLine("Missing --project (or pass --source under a known LockSmith install).");
            return 2;
        }

        if (!Directory.Exists(opts.ProjectRoot))
        {
            Console.Error.WriteLine("Project not found: " + opts.ProjectRoot);
            return 2;
        }

        string? sourceBundle = null;
        if (!string.IsNullOrWhiteSpace(opts.Source))
        {
            sourceBundle = ResolveSourceFile(opts.Source!);
            if (sourceBundle == null)
            {
                Console.Error.WriteLine("Source bundle not found: " + opts.Source);
                return 2;
            }
        }

        var unity = ResolveUnity(opts.UnityExe);
        if (unity == null)
        {
            Console.Error.WriteLine(
                "Unity.exe not found. Pass --unity \"C:\\Program Files\\Unity 6000.x\\Editor\\Unity.exe\" " +
                "or save it once in Asset Forge Export settings.");
            return 2;
        }

        var template = UnityArtCompiler.FindTemplateSource();
        if (template == null)
        {
            Console.Error.WriteLine("UnityTemplate missing next to DrakeAssetForge.");
            return 2;
        }

        var store = new ProjectStore(opts.ProjectRoot);
        store.EnsureCreated();
        var group = string.IsNullOrWhiteSpace(opts.Group) ? "keys" : opts.Group.Trim();
        var items = store.LoadAllItems()
            .Where(i => ValheimProjectSync.TopGroupName(i).Equals(group, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (items.Count == 0)
        {
            Console.Error.WriteLine($"No Project items in group '{group}' under {opts.ProjectRoot}\\Items.");
            return 2;
        }

        Console.WriteLine($"project={opts.ProjectRoot}");
        Console.WriteLine($"source={(sourceBundle ?? "(per-item art.json / Imports)")}");
        Console.WriteLine($"unity={unity}");
        Console.WriteLine($"template={template}");
        Console.WriteLine($"group={group} items={items.Count}: {string.Join(", ", items.Select(i => i.Id))}");

        var progress = new Progress<string>(m => Console.WriteLine("  " + m));

        foreach (var item in items)
        {
            var artBundle = Path.Combine(item.FolderPath, "art.bundle");
            if (opts.Force && File.Exists(artBundle))
            {
                File.Delete(artBundle);
                Console.WriteLine($"deleted stale {artBundle}");
            }

            if (File.Exists(artBundle) && new FileInfo(artBundle).Length > 1000 && !opts.Force)
            {
                Console.WriteLine($"SKIP prepare {item.Id} (art.bundle {new FileInfo(artBundle).Length:N0} bytes)");
                continue;
            }

            var art = store.LoadArt(item);
            var prepared = await PrepareItemArtAsync(
                store, item, art, unity, template, progress, sourceBundle, opts.PreferMesh);
            if (!prepared.Success)
            {
                Console.Error.WriteLine($"FAIL {item.Id}: {prepared.Message}");
                if (!string.IsNullOrWhiteSpace(prepared.LogPath) && File.Exists(prepared.LogPath))
                    Console.Error.WriteLine(Tail(prepared.LogPath, 40));
                return 1;
            }

            Console.WriteLine($"OK {item.Id}: {prepared.Message}");
        }

        var packOutput = ValheimProjectSync.FolderBundlePath(store, group);
        Directory.CreateDirectory(Path.GetDirectoryName(packOutput)!);
        var entries = items
            .Select(i =>
            {
                var art = store.LoadArt(i);
                var diffuse = art.IncludeDiffuse
                    ? ProjectStore.ResolveArtAbsolutePath(i, art.DiffusePath)
                    : null;
                return (Path.Combine(i.FolderPath, "art.bundle"), i.Id, diffuse);
            })
            .ToList();

        foreach (var (path, id, _) in entries)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"Missing art.bundle for pack: {id} → {path}");
                return 1;
            }

            Console.WriteLine($"  pack entry {id} ({new FileInfo(path).Length:N0})");
        }

        Console.WriteLine($"PACK → {packOutput}");
        var pack = await UnityArtCompiler.PackFolderAsync(
            unity,
            template,
            new UnityArtPackFolderRequest
            {
                OutputBundlePath = packOutput,
                BundleName = group,
                Entries = entries,
            },
            progress);

        if (!pack.Success)
        {
            Console.Error.WriteLine("PACK FAIL: " + pack.Message);
            if (!string.IsNullOrWhiteSpace(pack.LogPath) && File.Exists(pack.LogPath))
                Console.Error.WriteLine(Tail(pack.LogPath, 60));
            return 1;
        }

        Console.WriteLine("PACK OK: " + pack.Message);

        var codeProject = store.TryLoadValheimProjectPath() ?? opts.ProjectRoot;
        var sync = ValheimProjectSync.Sync(store, codeProject);
        Console.WriteLine("SYNC: " + sync.Message);

        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deployRoot in ResolveDeployTargets(opts, sourceBundle))
        {
            var destKeys = NormalizeKeysDeployFolder(deployRoot, group);
            if (!shipped.Add(Path.GetFullPath(destKeys)))
                continue;
            Directory.CreateDirectory(destKeys);
            ShipFolderPack(store, group, items, destKeys);
            Console.WriteLine(
                $"SHIPPED → {destKeys}\\{group}.bundle ({new FileInfo(Path.Combine(destKeys, group + ".bundle")).Length:N0} bytes)");
        }

        Console.WriteLine("DONE");
        return 0;
    }

    private static async Task<UnityArtCompileResult> PrepareItemArtAsync(
        ProjectStore store,
        OwnedItemDocument item,
        ArtDocument art,
        string unity,
        string template,
        IProgress<string> progress,
        string? sourceOverride,
        bool preferMesh)
    {
        var meshAbs = ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath);
        var source = sourceOverride
                     ?? store.ResolveSourceBundlePath(art.SourceBundlePath)
                     ?? store.ResolveProjectRelativePath(art.SourceBundlePath);

        async Task<UnityArtCompileResult> CompileMeshAsync()
        {
            Console.WriteLine($"COMPILE {item.Id} mesh={meshAbs}");
            var compiled = await UnityArtCompiler.CompileAsync(
                unity,
                template,
                new UnityArtCompileRequest
                {
                    MeshPath = meshAbs!,
                    DiffusePath = ProjectStore.ResolveArtAbsolutePath(item, art.DiffusePath),
                    OutputBundlePath = Path.Combine(item.FolderPath, "art.bundle"),
                    BundleName = "art",
                },
                progress);
            if (compiled.Success)
            {
                art.NeedsBundleExtract = false;
                store.SaveArt(item, art);
            }

            return compiled;
        }

        async Task<UnityArtCompileResult> ExtractAsync()
        {
            if (source == null || string.IsNullOrWhiteSpace(art.SourcePrefabName))
            {
                return new UnityArtCompileResult
                {
                    Success = false,
                    Message =
                        $"missing source bundle or prefab for '{item.Id}'. " +
                        "Pass --source path\\to\\Assets\\drake or set art.json sourceBundlePath.",
                };
            }

            var prefabName = BundleProjectImporter.ResolvePrefabNameInBundle(source, art.SourcePrefabName)
                             ?? art.SourcePrefabName;
            Console.WriteLine(
                $"EXTRACT {item.Id} from {source} prefab={prefabName}");
            var extracted = await UnityArtCompiler.ExtractPrefabAsync(
                unity,
                template,
                new UnityArtExtractRequest
                {
                    SourceBundlePath = source,
                    PrefabName = prefabName,
                    OutputBundlePath = Path.Combine(item.FolderPath, "art.bundle"),
                    BundleName = "art",
                },
                progress);

            if (extracted.Success)
            {
                art.NeedsBundleExtract = false;
                art.IncludeMesh = true;
                store.SaveArt(item, art);
            }

            return extracted;
        }

        // Streamed .resS meshes (publickey2) need OBJ/FBX; everything else extracts from --source.
        if (preferMesh && meshAbs != null && art.IncludeMesh)
            return await CompileMeshAsync();

        if (source != null && !string.IsNullOrWhiteSpace(art.SourcePrefabName))
        {
            var extracted = await ExtractAsync();
            if (extracted.Success)
                return extracted;

            if (meshAbs != null && art.IncludeMesh)
            {
                Console.WriteLine(
                    $"EXTRACT failed for {item.Id} ({extracted.Message}); falling back to mesh {meshAbs}");
                return await CompileMeshAsync();
            }

            return extracted;
        }

        if (meshAbs != null && art.IncludeMesh)
            return await CompileMeshAsync();

        return await ExtractAsync();
    }

    private static void ShipFolderPack(
        ProjectStore store,
        string groupName,
        IReadOnlyList<OwnedItemDocument> items,
        string destFolder)
    {
        Directory.CreateDirectory(destFolder);

        foreach (var file in Directory.EnumerateFiles(destFolder, "*.json"))
            TryDelete(file);
        foreach (var file in Directory.EnumerateFiles(destFolder, "*.png"))
            TryDelete(file);

        var srcBundle = ValheimProjectSync.FolderBundlePath(store, groupName);
        var destBundle = Path.Combine(destFolder, groupName + ".bundle");
        File.Copy(srcBundle, destBundle, overwrite: true);
        TryDelete(destBundle + ".manifest");

        foreach (var item in items)
        {
            var art = store.LoadArt(item);
            var spawn = string.IsNullOrWhiteSpace(art.PrefabName) ? item.Id : art.PrefabName.Trim();
            var scale = art.Scale <= 0 || float.IsNaN(art.Scale) || float.IsInfinity(art.Scale) ? 1f : art.Scale;
            var useDonor = art.UseDonorVisual;
            var wire = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = spawn,
                sourceId = item.Id,
                donor = string.IsNullOrWhiteSpace(item.Donor.PrefabName) ? "LeatherScraps" : item.Donor.PrefabName,
                displayName = string.IsNullOrWhiteSpace(art.PrefabName)
                    ? (string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName)
                    : spawn,
                scale,
                description = string.IsNullOrWhiteSpace(art.Description) ? null : art.Description.Trim(),
                existingMaterials = Array.Empty<string>(),
                useDonorVisual = useDonor,
                artPrefab = useDonor ? "donor" : item.Id,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(destFolder, item.Id + ".json"), wire);

            var icon = art.IncludeIcon ? ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) : null;
            if (icon != null)
                File.Copy(icon, Path.Combine(destFolder, item.Id + ".png"), overwrite: true);
        }
    }

    private static IEnumerable<string> ResolveDeployTargets(Options opts, string? sourceBundle)
    {
        if (!string.IsNullOrWhiteSpace(opts.Deploy))
            yield return opts.Deploy!;

        // When --source is .../Assets/drake, default ship beside it: .../Assets/Items/keys
        if (string.IsNullOrWhiteSpace(opts.Deploy) && !string.IsNullOrWhiteSpace(sourceBundle))
        {
            var fromSource = DeployBesideSource(sourceBundle!);
            if (fromSource != null)
                yield return fromSource;
        }

        var gale = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "com.kesomannen.gale",
            "valheim",
            "profiles",
            "drakeTest",
            "BepInEx",
            "plugins",
            "DrakeMods-LockSmith");
        if (opts.AlsoGale && Directory.Exists(gale))
            yield return gale;
    }

    private static string? DeployBesideSource(string sourceBundle)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(sourceBundle));
        if (string.IsNullOrWhiteSpace(dir))
            return null;
        var leaf = Path.GetFileName(dir);
        // .../Assets/drake → .../Assets/Items/keys
        if (leaf.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(dir, "Items", "keys");
        // .../plugins/DrakeMods-LockSmith/Assets → already handled
        return Path.Combine(dir, "Items", "keys");
    }

    private static string NormalizeKeysDeployFolder(string deployRoot, string group)
    {
        var full = Path.GetFullPath(deployRoot.TrimEnd('\\', '/'));
        var leaf = Path.GetFileName(full);
        if (leaf.Equals(group, StringComparison.OrdinalIgnoreCase))
            return full;
        if (leaf.Equals("Items", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(full, group);
        if (leaf.Equals("Assets", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(full, "Items", group);
        if (File.Exists(full) || leaf.Equals("drake", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetDirectoryName(full)!;
            if (Path.GetFileName(parent).Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(parent, "Items", group);
        }

        return Path.Combine(full, "Assets", "Items", group);
    }

    private static string? ResolveSourceFile(string raw)
    {
        var path = Path.GetFullPath(raw.Trim().Trim('"'));
        if (File.Exists(path))
            return path;
        // Allow passing the Assets folder — look for extensionless "drake"
        if (Directory.Exists(path))
        {
            var drake = Path.Combine(path, "drake");
            if (File.Exists(drake))
                return drake;
            var bundle = Path.Combine(path, "drake.bundle");
            if (File.Exists(bundle))
                return bundle;
            var assetsDrake = Path.Combine(path, "Assets", "drake");
            if (File.Exists(assetsDrake))
                return assetsDrake;
        }

        return null;
    }

    private static string? ResolveUnity(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            return overridePath;

        var saved = UnityArtCompiler.TryLoadSavedUnityExe();
        if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved))
            return saved;

        return UnityArtCompiler.FindBestEditor(null, out _);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string Tail(string path, int lines)
    {
        try
        {
            return string.Join(Environment.NewLine, File.ReadLines(path).Reverse().Take(lines).Reverse());
        }
        catch
        {
            return "";
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
DrakeAssetForge repack-keys — extract/pack keys from a fat Assets/drake (or Imports) and ship keys.bundle.

Usage:
  DrakeAssetForge.exe repack-keys --source <Assets/drake> [options]

Options:
  --source <path>    Fat bundle: file (Assets/drake), Assets folder, or plugin root
  --project <path>   Asset Forge / LockSmith project (has Items/keys). Default: last project / LockSmith
  --group <name>     Folder group to pack (default: keys)
  --deploy <path>    Destination (plugin / Assets / Items / keys). Default: beside --source → Assets/Items/keys
  --also-gale        Also ship to Gale profile drakeTest DrakeMods-LockSmith
  --force            Rebuild every per-item art.bundle from --source (or mesh.obj fallback)
  --prefer-mesh      Prefer art/mesh.obj over extract when both exist
  --unity <exe>      Unity.exe path
  --help             Show this help

Examples:
  DrakeAssetForge.exe repack-keys --source "C:\Users\house\AppData\Roaming\com.kesomannen.gale\valheim\profiles\drakeTest\BepInEx\plugins\DrakeMods-LockSmith\Assets\drake" --force

  DrakeAssetForge.exe repack-keys --source "...\DrakeMods-LockSmith\Assets" --project C:\Modding\Vahleim\Mod\LockSmith --force
""");
    }

    private sealed class Options
    {
        public string? ProjectRoot { get; init; }
        public string? Source { get; init; }
        public string Group { get; init; } = "keys";
        public string? Deploy { get; init; }
        public string? UnityExe { get; init; }
        public bool Force { get; init; }
        public bool AlsoGale { get; init; }
        public bool PreferMesh { get; init; }
        public bool ShowHelp { get; init; }
    }

    private static Options ApplyDefaults(Options opts)
    {
        var project = opts.ProjectRoot;
        if (string.IsNullOrWhiteSpace(project))
            project = AppSettings.TryLoadLastProjectPath();
        if (string.IsNullOrWhiteSpace(project) || !Directory.Exists(project))
        {
            var lockSmith = @"C:\Modding\Vahleim\Mod\LockSmith";
            if (Directory.Exists(lockSmith) && File.Exists(Path.Combine(lockSmith, "project.json")))
                project = lockSmith;
        }

        // If --source is under a LockSmith-like tree that has Items/, prefer that as project.
        if (!string.IsNullOrWhiteSpace(opts.Source))
        {
            var resolved = ResolveSourceFile(opts.Source!);
            if (resolved != null)
            {
                var walk = new DirectoryInfo(Path.GetDirectoryName(resolved)!);
                while (walk != null)
                {
                    if (File.Exists(Path.Combine(walk.FullName, "project.json")) &&
                        Directory.Exists(Path.Combine(walk.FullName, "Items")))
                    {
                        // Prefer explicit --project; only fill when empty.
                        if (string.IsNullOrWhiteSpace(opts.ProjectRoot))
                            project = walk.FullName;
                        break;
                    }

                    walk = walk.Parent;
                }
            }
        }

        return new Options
        {
            ProjectRoot = project,
            Source = opts.Source,
            Group = opts.Group,
            Deploy = opts.Deploy,
            UnityExe = opts.UnityExe,
            Force = opts.Force,
            AlsoGale = opts.AlsoGale,
            PreferMesh = opts.PreferMesh,
            ShowHelp = opts.ShowHelp,
        };
    }

    private static Options Parse(string[] args)
    {
        string? project = null;
        string? source = null;
        string group = "keys";
        string? deploy = null;
        string? unity = null;
        var force = false;
        var alsoGale = false;
        var preferMesh = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-h" or "--help" or "/?")
                help = true;
            else if (a is "--project" or "-p")
                project = Next(args, ref i);
            else if (a is "--source" or "-s")
                source = Next(args, ref i);
            else if (a is "--group" or "-g")
                group = Next(args, ref i) ?? "keys";
            else if (a is "--deploy" or "-d")
                deploy = Next(args, ref i);
            else if (a is "--unity" or "-u")
                unity = Next(args, ref i);
            else if (a is "--force" or "-f")
                force = true;
            else if (a is "--also-gale")
                alsoGale = true;
            else if (a is "--prefer-mesh")
                preferMesh = true;
            else if (!a.StartsWith('-') &&
                     !a.Equals("repack-keys", StringComparison.OrdinalIgnoreCase))
            {
                // Bare path: treat as --source when it looks like a bundle / Assets folder.
                if (source == null && (File.Exists(a) || Directory.Exists(a)))
                    source = a;
                else if (project == null)
                    project = a;
            }
        }

        return new Options
        {
            ProjectRoot = project,
            Source = source,
            Group = group,
            Deploy = deploy,
            UnityExe = unity,
            Force = force,
            AlsoGale = alsoGale,
            PreferMesh = preferMesh,
            ShowHelp = help,
        };
    }

    private static string? Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            return null;
        i++;
        return args[i];
    }
}
