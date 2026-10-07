using DrakesForge.Format;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>
/// A pack on disk is the project: forgepack.json + items/*.json + the user's files, exactly what ships.
/// App-only state (working list, source-only prefabs) lives in .forge/ and is never pushed or published.
/// </summary>
public sealed class PackProject
{
    private const string StateFolder = ".forge";
    private const string StateFile = "project.json";

    private PackProject(string root, ForgePack manifest)
    {
        Root = root;
        Manifest = manifest;
    }

    public string Root { get; }
    public ForgePack Manifest { get; }
    public List<ItemRecipe> Recipes { get; } = new();
    /// <summary>Vanilla prefabs the user shortlisted in the browser.</summary>
    public List<string> WorkingList { get; } = new();
    /// <summary>Vanilla prefabs imported as "Source only": things to borrow meshes/materials from.</summary>
    public List<string> Sources { get; } = new();

    public static string DefaultPacksDirectory =>
        AppSettings.PacksFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DrakesAssetForge", "Packs");

    public static PackProject Create(string root, string id, string name, string author)
    {
        if (File.Exists(Path.Combine(root, ForgePack.FileName)))
            return Open(root);

        Directory.CreateDirectory(Path.Combine(root, ForgePack.ItemsFolder));
        var project = new PackProject(root, new ForgePack { Id = id, Name = name, Author = author, Version = "0.1.0" });
        project.SaveManifest();
        project.SaveState();
        return project;
    }

    public static PackProject Open(string root)
    {
        var loaded = PackReader.Load(root);
        var project = new PackProject(root, loaded.Manifest);
        project.Recipes.AddRange(loaded.Recipes);

        var statePath = Path.Combine(root, StateFolder, StateFile);
        if (File.Exists(statePath))
        {
            try
            {
                var state = JsonValue.Parse(File.ReadAllText(statePath));
                project.WorkingList.AddRange(Strings(state["working"]));
                project.Sources.AddRange(Strings(state["sources"]));
            }
            catch (FormatException)
            {
                // app state is disposable
            }
        }

        return project;
    }

    public void SaveManifest() =>
        File.WriteAllText(Path.Combine(Root, ForgePack.FileName), RecipeSerializer.WritePack(Manifest));

    public void SaveState()
    {
        var dir = Path.Combine(Root, StateFolder);
        Directory.CreateDirectory(dir);
        var state = JsonValue.NewObject().Set("working", Array(WorkingList)).Set("sources", Array(Sources));
        File.WriteAllText(Path.Combine(dir, StateFile), state.ToJson());
    }

    public string RecipePath(ItemRecipe recipe) =>
        recipe.SourcePath ?? Path.Combine(Root, ForgePack.ItemsFolder, recipe.Id + ".json");

    public void SaveRecipe(ItemRecipe recipe)
    {
        var path = RecipePath(recipe);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RecipeSerializer.WriteRecipe(recipe));
        recipe.SourcePath = path;
        if (!Recipes.Contains(recipe))
            Recipes.Add(recipe);
    }

    public void DeleteRecipe(ItemRecipe recipe)
    {
        Recipes.Remove(recipe);
        var path = RecipePath(recipe);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Copies a user file into the pack; returns the pack-relative path recipes use.</summary>
    public string AddFile(string sourcePath, string folder)
    {
        var dir = Path.Combine(Root, folder);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(dir, name);
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            File.Copy(sourcePath, target, true);
        return folder + "/" + name;
    }

    public string? FullPath(string? packRelative) =>
        string.IsNullOrEmpty(packRelative) ? null : Path.Combine(Root, packRelative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Installs the pack (minus .forge), unzipped, into <paramref name="target"/>: a profile's plugins folder
    /// or Forge's dev folder. Only changed files are copied; files no longer in the pack are removed.
    /// Forge Runtime hot-reloads it while the game runs.
    /// </summary>
    public string Push(PushTarget target)
    {
        var dest = target.PackFolder(Manifest);
        Directory.CreateDirectory(dest);

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ShippedFiles())
        {
            var relative = Path.GetRelativePath(Root, file);
            wanted.Add(relative);
            var to = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (!File.Exists(to) || File.GetLastWriteTimeUtc(to) != File.GetLastWriteTimeUtc(file) || new FileInfo(to).Length != new FileInfo(file).Length)
            {
                File.Copy(file, to, true);
                File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(file));
            }
        }

        foreach (var stale in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
            if (!wanted.Contains(Path.GetRelativePath(dest, stale)))
                File.Delete(stale);
        return dest;
    }

    /// <summary>Other copies of this pack (same id) in the target's folder, which would conflict in game.</summary>
    public List<string> OtherCopies(PushTarget target)
    {
        var mine = Path.GetFullPath(target.PackFolder(Manifest));
        var copies = new List<string>();
        foreach (var root in PackReader.FindPackRoots(target.Folder, maxDepth: 2))
        {
            if (string.Equals(Path.GetFullPath(root), mine, StringComparison.OrdinalIgnoreCase))
                continue;
            var problems = new List<string>();
            try
            {
                var other = RecipeSerializer.ReadPack(File.ReadAllText(Path.Combine(root, ForgePack.FileName)), problems);
                if (string.Equals(other.Id, Manifest.Id, StringComparison.OrdinalIgnoreCase))
                    copies.Add(root);
            }
            catch (IOException)
            {
                // unreadable: not ours to judge
            }
        }

        return copies;
    }

    /// <summary>Everything that goes into a published pack.</summary>
    public IEnumerable<string> ShippedFiles() =>
        Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(Root, f).StartsWith(StateFolder, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Strings(JsonValue? array) =>
        array?.Items.Select(i => i.AsString()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!) ?? Enumerable.Empty<string>();

    private static JsonValue Array(IEnumerable<string> values)
    {
        var arr = JsonValue.NewArray();
        foreach (var v in values)
            arr.Add(v);
        return arr;
    }
}
