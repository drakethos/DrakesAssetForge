namespace DrakesForge.Valheim;

public enum VanillaKind
{
    Item,
    Piece
}

/// <summary>A vanilla prefab the player can browse. Name is the prefab name recipes refer to ("iron_grate").</summary>
public sealed class VanillaEntry
{
    public required string Name { get; init; }
    public required VanillaKind Kind { get; init; }
    /// <summary>Folder under Items/ or Pieces/ ("weapons", "armor", …); empty for top-level pieces.</summary>
    public required string Category { get; init; }
    public required string BundleId { get; init; }
    public required string PathInBundle { get; init; }

    public override string ToString() => $"{Name} ({Kind}, {Category})";
}

/// <summary>
/// Index of Valheim's SoftRef manifest: which bundle holds which asset, and bundle dependencies.
/// Parsing the manifest is fast; no bundle is opened here.
/// </summary>
public sealed class VanillaCatalog
{
    // Asset folders that hold parts of prefabs, not browsable prefabs.
    private static readonly string[] NoiseFolders = { "_res", "_icons", "icons", "combined_meshes", "effects", "model" };

    private readonly Dictionary<string, string[]> _dependencies;

    private VanillaCatalog(ValheimInstall install, List<VanillaEntry> entries, Dictionary<string, string[]> dependencies)
    {
        Install = install;
        Entries = entries;
        _dependencies = dependencies;
        ByName = entries.GroupBy(e => e.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public ValheimInstall Install { get; }
    public IReadOnlyList<VanillaEntry> Entries { get; }
    public IReadOnlyDictionary<string, VanillaEntry> ByName { get; }

    public IReadOnlyList<string> DependenciesOf(string bundleId) =>
        _dependencies.TryGetValue(bundleId, out var deps) ? deps : Array.Empty<string>();

    public string BundlePath(string bundleId) => Path.Combine(Install.BundlesDirectory, bundleId);

    public static VanillaCatalog Load(ValheimInstall install)
    {
        var extended = Path.Combine(install.SoftRefRoot, "manifest_extended");
        var manifest = File.Exists(extended) ? extended : Path.Combine(install.SoftRefRoot, "manifest");

        var entries = new List<VanillaEntry>();
        var deps = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? depBundle = null;
        string? assetBundle = null;
        var section = 0; // 1 = bundle dependencies, 2 = assets

        foreach (var raw in File.ReadLines(manifest))
        {
            var line = raw.Trim();
            if (line.Equals("bundle dependencies:", StringComparison.OrdinalIgnoreCase))
            {
                section = 1;
                continue;
            }

            if (line.StartsWith("- asset ID:", StringComparison.OrdinalIgnoreCase))
            {
                section = 2;
                assetBundle = null;
                continue;
            }

            if (section == 1)
            {
                if (line.StartsWith("- bundle:", StringComparison.OrdinalIgnoreCase))
                {
                    depBundle = Value(line);
                    deps[depBundle] = new List<string>();
                }
                else if (depBundle != null && line.StartsWith("- ", StringComparison.Ordinal))
                {
                    deps[depBundle].Add(line[2..].Trim());
                }
            }
            else if (section == 2)
            {
                if (line.StartsWith("bundle:", StringComparison.OrdinalIgnoreCase))
                    assetBundle = Value(line);
                else if (line.StartsWith("path in bundle:", StringComparison.OrdinalIgnoreCase) && assetBundle != null)
                    TryAdd(entries, assetBundle, Value(line));
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return new VanillaCatalog(install, entries, deps.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
    }

    private static void TryAdd(List<VanillaEntry> entries, string bundle, string path)
    {
        if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return;

        VanillaKind kind;
        string rest;
        if (After(path, "/GameElements/Items/", out rest))
            kind = VanillaKind.Item;
        else if (After(path, "/GameElements/Pieces/", out rest))
            kind = VanillaKind.Piece;
        else
            return;

        var segments = rest.Split('/');
        if (segments.Any(s => NoiseFolders.Contains(s, StringComparer.OrdinalIgnoreCase)))
            return;

        entries.Add(new VanillaEntry
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Kind = kind,
            Category = segments.Length > 1 ? segments[0] : "",
            BundleId = bundle,
            PathInBundle = path
        });
    }

    private static bool After(string path, string marker, out string rest)
    {
        var i = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        rest = i < 0 ? "" : path[(i + marker.Length)..];
        return i >= 0;
    }

    private static string Value(string line) => line[(line.IndexOf(':') + 1)..].Trim();
}
