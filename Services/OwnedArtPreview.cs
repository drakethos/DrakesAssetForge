using Avalonia.Media.Imaging;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

/// <summary>
/// Preview helpers for owned Project items — prefer custom icon / source-bundle art, not the donor SoftRef icon.
/// </summary>
public static class OwnedArtPreview
{
    public sealed class Result
    {
        public Bitmap? Bitmap { get; init; }
        public string? MeshPath { get; init; }
        public string Caption { get; init; } = "";
        /// <summary>True when no custom art was found; caller may optionally show SoftRef donor.</summary>
        public bool UsedDonorFallback { get; init; }
    }

    /// <summary>
    /// Resolves the best visual for an owned item. Persists a pulled icon to art/icon.png when found.
    /// </summary>
    public static Result Resolve(ProjectStore store, OwnedItemDocument item, ArtDocument art)
    {
        // 1) Attached / previously extracted icon (or any image under art/)
        foreach (var iconFile in EnumerateLocalIconFiles(item, art))
        {
            var bmp = TryLoadBitmapFile(iconFile);
            if (bmp == null)
                continue;
            return new Result
            {
                Bitmap = bmp,
                Caption = $"Owned '{item.DisplayName}' · custom icon",
            };
        }

        // 2) Loose project icons (e.g. Assets/masterkey_icon.png)
        foreach (var loose in EnumerateProjectLooseIcons(store, item))
        {
            var bmp = TryLoadBitmapFile(loose);
            if (bmp == null)
                continue;
            TryPersistIconBytes(store, item, art, File.ReadAllBytes(loose));
            return new Result
            {
                Bitmap = bmp,
                Caption = $"Owned '{item.DisplayName}' · project icon",
            };
        }

        // 3) Texture from compiled art.bundle
        var artBundle = Path.Combine(item.FolderPath, "art.bundle");
        if (File.Exists(artBundle))
        {
            var fromArt = TryBitmapFromBundle(artBundle, item.Id, art.SourcePrefabName);
            if (fromArt != null)
            {
                TryPersistIcon(store, item, art, fromArt.Value.Bytes);
                return new Result
                {
                    Bitmap = fromArt.Value.Bitmap,
                    Caption = $"Owned '{item.DisplayName}' · art.bundle preview",
                };
            }
        }

        // 4) Texture from imported source pack (drake/ploam/…)
        var source = store.ResolveSourceBundlePath(art.SourceBundlePath, repairImports: false)
                     ?? store.ResolveProjectRelativePath(art.SourceBundlePath);
        if (source != null)
        {
            var fromSrc = TryBitmapFromBundle(source, item.Id, art.SourcePrefabName);
            if (fromSrc != null)
            {
                TryPersistIcon(store, item, art, fromSrc.Value.Bytes);
                return new Result
                {
                    Bitmap = fromSrc.Value.Bitmap,
                    Caption = $"Owned '{item.DisplayName}' · source pack texture",
                };
            }
        }

        // 5) Attached FBX path for orbit view (caller sets MeshPreviewPath)
        var mesh = ProjectStore.ResolveArtAbsolutePath(item, art.MeshPath);
        if (mesh != null)
        {
            return new Result
            {
                MeshPath = mesh,
                Caption = $"Owned '{item.DisplayName}' · FBX mesh preview",
            };
        }

        // Never recommend SoftRef donor for imported clones (LeatherScraps etc.) — that is not custom art.
        var imported = item.Donor.Kind.Equals("Imported", StringComparison.OrdinalIgnoreCase);
        return new Result
        {
            Caption = imported
                ? $"Owned '{item.DisplayName}' · no custom preview yet (extract/attach icon or compile art.bundle)."
                : $"Owned '{item.DisplayName}' · no custom preview yet (donor SoftRef icon available as last resort).",
            UsedDonorFallback = !imported,
        };
    }

    private static IEnumerable<string> EnumerateLocalIconFiles(OwnedItemDocument item, ArtDocument art)
    {
        var list = new List<string>();
        var primary = ProjectStore.ResolveArtAbsolutePath(item, art.IconPath);
        if (primary != null)
            list.Add(primary);

        var artFolder = Path.Combine(item.FolderPath, "art");
        if (Directory.Exists(artFolder))
        {
            foreach (var file in Directory.EnumerateFiles(artFolder)
                         .Where(IsImageFile)
                         .OrderBy(f => Path.GetFileName(f).Equals("icon.png", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                         .ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (primary != null && file.Equals(primary, StringComparison.OrdinalIgnoreCase))
                    continue;
                list.Add(file);
            }
        }

        return list;
    }

    private static IEnumerable<string> EnumerateProjectLooseIcons(ProjectStore store, OwnedItemDocument item)
    {
        var roots = new[]
        {
            Path.Combine(store.ProjectRoot, "Assets"),
            store.ProjectRoot,
        };
        var stems = BuildNameHints(item.Id, null)
            .Select(h => Path.GetFileNameWithoutExtension(h.Replace('\\', '/')))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var stem in stems)
            {
                foreach (var name in new[]
                         {
                             $"{stem}_icon.png", $"{stem}_icon.jpg",
                             $"{stem}.png", $"{stem}.jpg",
                             $"icon_{stem}.png",
                         })
                {
                    var path = Path.Combine(root, name);
                    if (File.Exists(path))
                        yield return path;
                }
            }
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static Bitmap? TryLoadBitmapFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return null;
            using var ms = new MemoryStream(bytes);
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    private static void TryPersistIcon(
        ProjectStore store,
        OwnedItemDocument item,
        ArtDocument art,
        byte[] pngBytes) =>
        TryPersistIconBytes(store, item, art, pngBytes);

    private static void TryPersistIconBytes(
        ProjectStore store,
        OwnedItemDocument item,
        ArtDocument art,
        byte[] bytes)
    {
        try
        {
            if (ProjectStore.ResolveArtAbsolutePath(item, art.IconPath) != null)
                return;
            if (bytes.Length == 0)
                return;

            var artFolder = store.GetArtAssetsFolder(item);
            Directory.CreateDirectory(artFolder);
            var dest = Path.Combine(artFolder, "icon.png");
            File.WriteAllBytes(dest, bytes);
            art.IconPath = "art/icon.png";
            art.IncludeIcon = true;
            store.SaveArt(item, art);
        }
        catch
        {
            // preview still works without persist
        }
    }

    private static (Bitmap Bitmap, byte[] Bytes)? TryBitmapFromBundle(
        string bundlePath,
        string itemId,
        string? sourcePrefabName)
    {
        try
        {
            var objects = BundleAssetLister.ListObjects(bundlePath, maxObjects: 8000);
            var hints = BuildNameHints(itemId, sourcePrefabName);

            var textures = objects
                .Where(o =>
                    o.TypeName.Equals("Texture2D", StringComparison.OrdinalIgnoreCase) ||
                    o.TypeName.Equals("Texture", StringComparison.OrdinalIgnoreCase) ||
                    o.TypeName.Equals("Sprite", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var ranked = textures
                .Select(o => (Obj: o, Score: ScoreName(o.Name, hints)))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Obj.Name.Length)
                .ToList();

            foreach (var hit in ranked)
            {
                var png = SoftRefPreviewService.TryExportTexturePng(bundlePath, hit.Obj.PathId);
                if (png == null || png.Length == 0)
                    continue;
                using var ms = new MemoryStream(png);
                return (new Bitmap(ms), png);
            }

            // Only dump an arbitrary texture when we had a strong name hint miss but the bundle
            // is a tiny single-item art.bundle (not a fat pack with dozens of sheets).
            if (textures.Count > 0 && textures.Count <= 4)
            {
                foreach (var tex in textures)
                {
                    var png = SoftRefPreviewService.TryExportTexturePng(bundlePath, tex.PathId);
                    if (png == null || png.Length == 0)
                        continue;
                    using var ms = new MemoryStream(png);
                    return (new Bitmap(ms), png);
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static List<string> BuildNameHints(string itemId, string? sourcePrefabName)
    {
        var hints = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return;
            var leaf = Path.GetFileNameWithoutExtension(s.Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(leaf))
                hints.Add(leaf);
            hints.Add(s);
        }

        Add(itemId);
        Add(sourcePrefabName);
        // masterkey_2 → masterkey
        var trimmed = itemId.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_');
        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            Add(trimmed);

        // LockSmith keys share color sheets (privatekey → ironkey, etc.)
        foreach (var alias in TextureAliasesFor(itemId, sourcePrefabName))
            Add(alias);

        return hints;
    }

    private static IEnumerable<string> TextureAliasesFor(string itemId, string? sourcePrefabName)
    {
        var leaf = Path.GetFileNameWithoutExtension((sourcePrefabName ?? itemId).Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(leaf))
            yield break;

        var key = leaf.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_');
        if (key.Equals("privatekey", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ironkey";
            yield return "brownkey";
        }
        else if (key.Equals("publickey", StringComparison.OrdinalIgnoreCase) ||
                 key.Equals("publickey2", StringComparison.OrdinalIgnoreCase))
        {
            yield return "goldkey";
            yield return "bluekey";
        }
        else if (key.Equals("masterkeycustom", StringComparison.OrdinalIgnoreCase))
        {
            yield return "masterkey";
            yield return "ironkey";
        }
        else if (key.Equals("keymaker", StringComparison.OrdinalIgnoreCase))
        {
            yield return "ironkey";
            yield return "masterkey";
        }
    }

    private static int ScoreName(string name, IReadOnlyList<string> hints)
    {
        var leaf = Path.GetFileNameWithoutExtension(name.Replace('\\', '/'));
        var score = 0;
        foreach (var hint in hints)
        {
            var hintLeaf = Path.GetFileNameWithoutExtension(hint.Replace('\\', '/'));
            if (leaf.Equals(hintLeaf, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 100);
            else if (leaf.Contains(hintLeaf, StringComparison.OrdinalIgnoreCase) && hintLeaf.Length >= 4)
                score = Math.Max(score, 70);
            else if (hintLeaf.Contains(leaf, StringComparison.OrdinalIgnoreCase) && leaf.Length >= 4)
                score = Math.Max(score, 40);
        }

        if (leaf.Contains("icon", StringComparison.OrdinalIgnoreCase))
            score += 10;
        return score;
    }
}
