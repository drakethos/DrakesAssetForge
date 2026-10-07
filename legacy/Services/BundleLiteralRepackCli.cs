using DrakeAssetForge.Services;

namespace DrakeAssetForge.Services;

/// <summary>
/// CLI: one-for-one subset of a fat UnityFS into a slim keys (or filtered) bundle.
/// Does not re-import meshes or strip materials — keeps original asset bytes.
/// </summary>
public static class BundleLiteralRepackCli
{
    public static int Run(string[] args)
    {
        var opts = Parse(args);
        if (opts.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (string.IsNullOrWhiteSpace(opts.Source))
        {
            PrintHelp();
            Console.Error.WriteLine("Missing --source (fat UnityFS, e.g. Imports/drake.bundle or Assets/drake).");
            return 2;
        }

        var source = ResolveSource(opts.Source!);
        if (source == null)
        {
            Console.Error.WriteLine("Source bundle not found: " + opts.Source);
            return 2;
        }

        var output = opts.Output;
        if (string.IsNullOrWhiteSpace(output))
        {
            output = Path.Combine(
                Path.GetDirectoryName(source)!,
                "Items",
                "keys",
                "keys.bundle");
            // If source is …/Assets/drake → …/Assets/Items/keys/keys.bundle
            var leaf = Path.GetFileName(source);
            var parent = Path.GetDirectoryName(source)!;
            if (Path.GetFileName(parent).Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("drake", StringComparison.OrdinalIgnoreCase) ||
                leaf.Equals("drake.bundle", StringComparison.OrdinalIgnoreCase))
            {
                var assetsDir = Path.GetFileName(parent).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                    ? parent
                    : Path.GetDirectoryName(parent)!;
                if (Path.GetFileName(assetsDir).Equals("Assets", StringComparison.OrdinalIgnoreCase))
                    output = Path.Combine(assetsDir, "Items", "keys", "keys.bundle");
            }
        }

        output = Path.GetFullPath(output!);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        Console.WriteLine($"source={source} ({new FileInfo(source).Length:N0} bytes)");
        Console.WriteLine($"output={output}");
        Console.WriteLine($"filters=[{string.Join(", ", opts.Filters)}]");
        Console.WriteLine($"compress={!opts.NoCompress}");

        var result = BundleLiteralRepack.Repack(
            source,
            output,
            opts.Filters,
            compress: !opts.NoCompress,
            fixMasterKey: opts.FixMasterKey);

        if (!result.Success)
        {
            Console.Error.WriteLine("FAIL: " + result.Message);
            return 1;
        }

        Console.WriteLine(result.Message);
        foreach (var name in result.ContainerNames)
            Console.WriteLine("  + " + name);

        foreach (var deploy in opts.DeployTargets)
        {
            var dest = NormalizeKeysBundlePath(deploy);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(output, dest, overwrite: true);
            Console.WriteLine($"SHIPPED → {dest} ({new FileInfo(dest).Length:N0} bytes)");
        }

        Console.WriteLine("DONE");
        return 0;
    }

    private static string NormalizeKeysBundlePath(string deploy)
    {
        var full = Path.GetFullPath(deploy.Trim().Trim('"'));
        if (full.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(full).Equals("keys", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(full))
        {
            // treat as file path
            if (full.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                return full;
        }

        if (Directory.Exists(full) || !Path.HasExtension(full))
        {
            var leaf = Path.GetFileName(full.TrimEnd('\\', '/'));
            if (leaf.Equals("keys", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(full, "keys.bundle");
            if (leaf.Equals("Items", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(full, "keys", "keys.bundle");
            if (leaf.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(full, "Items", "keys", "keys.bundle");
            return Path.Combine(full, "Assets", "Items", "keys", "keys.bundle");
        }

        return full;
    }

    private static string? ResolveSource(string raw)
    {
        var path = Path.GetFullPath(raw.Trim().Trim('"'));
        if (File.Exists(path))
            return path;
        if (Directory.Exists(path))
        {
            foreach (var name in new[] { "drake", "drake.bundle", Path.Combine("Assets", "drake"), Path.Combine("Imports", "drake.bundle") })
            {
                var candidate = Path.Combine(path, name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
DrakeAssetForge repack-literal — one-for-one subset of a fat UnityFS into a slim keys bundle.

Copies matching container assets + their PPtr dependency closure. Always strips
MonoBehaviour / MonoScript (ItemDrop, ZNetView, …) so Unity does not spam
missing-script / "file 'none'" on load. Streamed .resS is slimmed to kept slices.

Usage:
  DrakeAssetForge.exe repack-literal --source <fat.bundle|Assets/drake> [options]

Options:
  --source <path>     Fat UnityFS (file) or folder containing Assets/drake / Imports/drake.bundle
  --out <path>        Output keys.bundle path (default: beside source → Assets/Items/keys/keys.bundle)
  --filter <text>     Substring filter (repeatable). Default: key, locksmit, keymaker
                      For MasterKey-only ship: --filter locksmit/masterkey.prefab --filter ironkey.png --filter locksmit/masterkey.jpg
  --deploy <path>     Also copy result to plugin / Assets / Items / keys (repeatable)
  --also-gale         Also ship to Gale profile drakeTest DrakeMods-LockSmith
  --fix-masterkey     Retarget NurbsPath → key backup, strip NurbsPath subtree, expose keyskull
  --no-compress       Write uncompressed UnityFS (debug)
  --help              Show this help

Examples:
  DrakeAssetForge.exe repack-literal --source "C:\Modding\Vahleim\Mod\LockSmith\Imports\drake.bundle" --out "C:\Modding\Vahleim\Mod\LockSmith\Assets\Items\keys\keys.bundle" --filter locksmit/masterkey.prefab --filter ironkey.png --filter locksmit/masterkey.jpg --fix-masterkey --also-gale
""");
    }

    private sealed class Options
    {
        public string? Source { get; init; }
        public string? Output { get; init; }
        public List<string> Filters { get; init; } = new();
        public List<string> DeployTargets { get; init; } = new();
        public bool AlsoGale { get; init; }
        public bool FixMasterKey { get; init; }
        public bool NoCompress { get; init; }
        public bool ShowHelp { get; init; }
    }

    private static Options Parse(string[] args)
    {
        string? source = null;
        string? output = null;
        var filters = new List<string>();
        var deploy = new List<string>();
        var alsoGale = false;
        var fixMasterKey = false;
        var noCompress = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Equals("repack-literal", StringComparison.OrdinalIgnoreCase))
                continue;
            if (a is "--help" or "-h" or "/?")
            {
                help = true;
                continue;
            }

            if (a.Equals("--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                source = args[++i];
                continue;
            }

            if (a.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                output = args[++i];
                continue;
            }

            if (a.Equals("--filter", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                filters.Add(args[++i]);
                continue;
            }

            if (a.Equals("--deploy", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                deploy.Add(args[++i]);
                continue;
            }

            if (a.Equals("--also-gale", StringComparison.OrdinalIgnoreCase))
            {
                alsoGale = true;
                continue;
            }

            if (a.Equals("--fix-masterkey", StringComparison.OrdinalIgnoreCase))
            {
                fixMasterKey = true;
                continue;
            }

            if (a.Equals("--no-compress", StringComparison.OrdinalIgnoreCase))
            {
                noCompress = true;
                continue;
            }
        }

        if (alsoGale)
        {
            var gale = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "com.kesomannen.gale",
                "valheim",
                "profiles",
                "drakeTest",
                "BepInEx",
                "plugins",
                "DrakeMods-LockSmith");
            if (Directory.Exists(gale))
                deploy.Add(gale);
        }

        return new Options
        {
            Source = source,
            Output = output,
            Filters = filters.Count > 0 ? filters : new List<string> { "key", "locksmit", "keymaker" },
            DeployTargets = deploy,
            AlsoGale = alsoGale,
            FixMasterKey = fixMasterKey,
            NoCompress = noCompress,
            ShowHelp = help,
        };
    }
}
