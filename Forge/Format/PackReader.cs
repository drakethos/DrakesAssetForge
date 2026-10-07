using System;
using System.Collections.Generic;
using System.IO;

namespace DrakesForge.Format;

public sealed class PackProblem
{
    public PackProblem(string file, string message)
    {
        File = file;
        Message = message;
    }

    public string File { get; }
    public string Message { get; }

    public override string ToString() => $"{System.IO.Path.GetFileName(File)}: {Message}";
}

public sealed class LoadedPack
{
    public LoadedPack(string root, ForgePack manifest)
    {
        Root = root;
        Manifest = manifest;
    }

    /// <summary>Folder holding forgepack.json. Recipe file paths resolve against it.</summary>
    public string Root { get; }
    public ForgePack Manifest { get; }
    public List<ItemRecipe> Recipes { get; } = new();
    public List<PackProblem> Problems { get; } = new();

    /// <summary>Pack-relative path (forward slashes) to an absolute path, or null if it escapes the pack.</summary>
    public string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var full = Path.GetFullPath(Path.Combine(Root, relative!.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}

/// <summary>Finds and reads pack folders from disk.</summary>
public static class PackReader
{
    /// <summary>
    /// Every folder under <paramref name="searchRoot"/> (inclusive, up to <paramref name="maxDepth"/> levels) containing
    /// forgepack.json. Walks folder by folder so one unreadable folder (locked, broken link) only skips itself.
    /// </summary>
    public static List<string> FindPackRoots(string searchRoot, Action<string, Exception>? onError = null, int maxDepth = 6)
    {
        var roots = new List<string>();
        if (!Directory.Exists(searchRoot))
            return roots;

        var pending = new Stack<(string Dir, int Depth)>();
        pending.Push((searchRoot, 0));
        while (pending.Count > 0)
        {
            var (dir, depth) = pending.Pop();
            try
            {
                if (File.Exists(Path.Combine(dir, ForgePack.FileName)))
                {
                    // A pack's own subfolders (items, textures) never hold another pack.
                    roots.Add(dir);
                    continue;
                }

                if (depth >= maxDepth)
                    continue;
                foreach (var sub in Directory.GetDirectories(dir))
                    pending.Push((sub, depth + 1));
            }
            catch (Exception ex)
            {
                onError?.Invoke(dir, ex);
            }
        }

        roots.Sort(StringComparer.OrdinalIgnoreCase);
        return roots;
    }

    public static LoadedPack Load(string packRoot)
    {
        var manifestPath = Path.Combine(packRoot, ForgePack.FileName);
        var problems = new List<string>();
        var manifest = File.Exists(manifestPath)
            ? RecipeSerializer.ReadPack(File.ReadAllText(manifestPath), problems)
            : new ForgePack { Id = Path.GetFileName(packRoot) };

        var pack = new LoadedPack(packRoot, manifest);
        foreach (var p in problems)
            pack.Problems.Add(new PackProblem(manifestPath, p));

        var itemsDir = Path.Combine(packRoot, ForgePack.ItemsFolder);
        if (!Directory.Exists(itemsDir))
            return pack;

        var files = Directory.GetFiles(itemsDir, "*.json", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var recipeProblems = new List<string>();
            ItemRecipe recipe;
            try
            {
                recipe = RecipeSerializer.ReadRecipe(File.ReadAllText(file), recipeProblems);
            }
            catch (IOException ex)
            {
                pack.Problems.Add(new PackProblem(file, ex.Message));
                continue;
            }

            recipe.SourcePath = file;
            foreach (var p in recipeProblems)
                pack.Problems.Add(new PackProblem(file, p));

            if (!string.IsNullOrWhiteSpace(recipe.Id) && !string.IsNullOrWhiteSpace(recipe.Base))
                pack.Recipes.Add(recipe);
        }

        return pack;
    }
}
