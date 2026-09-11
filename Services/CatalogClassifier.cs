using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public static class CatalogClassifier
{
    public static SoftRefAssetEntry Enrich(string assetId, string bundleId, string pathInBundle)
    {
        var normalized = pathInBundle.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var ext = Path.GetExtension(normalized);
        var display = Path.GetFileNameWithoutExtension(fileName);

        var kind = ClassifyKind(normalized, ext);
        var (category, sub, noise) = ClassifyFolder(normalized, kind);

        return new SoftRefAssetEntry
        {
            AssetId = assetId,
            BundleId = bundleId,
            PathInBundle = pathInBundle,
            NormalizedPath = normalized,
            FileName = fileName,
            Extension = ext,
            DisplayName = display,
            Kind = kind,
            Category = category,
            SubCategory = sub,
            IsResourceNoise = noise,
        };
    }

    private static CatalogKind ClassifyKind(string path, string ext)
    {
        if (ext.Equals(".shader", StringComparison.OrdinalIgnoreCase))
            return CatalogKind.Shader;

        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return CatalogKind.Audio;

        if (ext.Equals(".mat", StringComparison.OrdinalIgnoreCase))
            return CatalogKind.Material;

        if (ext.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".dae", StringComparison.OrdinalIgnoreCase))
            return CatalogKind.Mesh;

        if (IsIconPath(path, ext))
            return CatalogKind.Icon;

        if (path.Contains("/GameElements/Recipes/", StringComparison.OrdinalIgnoreCase) ||
            (ext.Equals(".asset", StringComparison.OrdinalIgnoreCase) &&
             path.Contains("/Recipes/", StringComparison.OrdinalIgnoreCase)))
            return CatalogKind.Recipe;

        if (ext.Equals(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains("/GameElements/Items/", StringComparison.OrdinalIgnoreCase))
                return CatalogKind.ItemPrefab;
            if (path.Contains("/GameElements/Pieces/", StringComparison.OrdinalIgnoreCase))
                return CatalogKind.PiecePrefab;
            return CatalogKind.OtherPrefab;
        }

        return CatalogKind.Other;
    }

    private static bool IsIconPath(string path, string ext)
    {
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".tga", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Contains("/_icons/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/icons/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/Icons/", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Category, string SubCategory, bool Noise) ClassifyFolder(
        string path,
        CatalogKind kind)
    {
        var noise = path.Contains("/_res/", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("/_resources/", StringComparison.OrdinalIgnoreCase);

        if (TrySegmentAfter(path, "/GameElements/Items/", out var itemRest))
        {
            var sub = FirstSegment(itemRest);
            if (string.Equals(sub, "_icons", StringComparison.OrdinalIgnoreCase))
                return ("Items", "icons", false);
            if (string.Equals(sub, "_res", StringComparison.OrdinalIgnoreCase))
                return ("Items", "_res", true);
            return ("Items", string.IsNullOrEmpty(sub) ? "(root)" : sub, noise);
        }

        if (TrySegmentAfter(path, "/GameElements/Pieces/", out var pieceRest))
        {
            var sub = FirstSegment(pieceRest);
            if (string.Equals(sub, "_res", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sub, "icons", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sub, "_icons", StringComparison.OrdinalIgnoreCase))
                return ("Pieces", sub, string.Equals(sub, "_res", StringComparison.OrdinalIgnoreCase));
            return ("Pieces", string.IsNullOrEmpty(sub) ? "(root)" : sub, noise);
        }

        if (TrySegmentAfter(path, "/GameElements/Recipes/", out _))
            return ("Recipes", "", false);

        if (TrySegmentAfter(path, "/GameElements/", out var geRest))
            return ("GameElements", FirstSegment(geRest), noise);

        if (TrySegmentAfter(path, "/Characters/", out var charRest))
            return ("Characters", FirstSegment(charRest), noise);

        if (TrySegmentAfter(path, "/world/", out var worldRest))
            return ("World", FirstSegment(worldRest), noise);

        if (TrySegmentAfter(path, "/Effects/", out var fxRest))
            return ("Effects", FirstSegment(fxRest), noise);

        if (TrySegmentAfter(path, "/UI/", out var uiRest))
            return ("UI", FirstSegment(uiRest), noise);

        if (TrySegmentAfter(path, "/Audio/", out var audioRest))
            return ("Audio", FirstSegment(audioRest), noise);

        return (kind.ToString(), "", noise);
    }

    private static bool TrySegmentAfter(string path, string marker, out string rest)
    {
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            rest = "";
            return false;
        }

        rest = path[(idx + marker.Length)..];
        return true;
    }

    private static string FirstSegment(string rest)
    {
        if (string.IsNullOrEmpty(rest))
            return "";
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    public static bool MatchesView(SoftRefAssetEntry entry, CatalogViewMode mode, bool hideResourceNoise)
    {
        if (hideResourceNoise && entry.IsResourceNoise && mode != CatalogViewMode.Raw)
            return false;

        return mode switch
        {
            CatalogViewMode.Items => entry.Kind == CatalogKind.ItemPrefab,
            CatalogViewMode.Pieces => entry.Kind == CatalogKind.PiecePrefab,
            CatalogViewMode.Prefabs => entry.Kind is CatalogKind.ItemPrefab
                or CatalogKind.PiecePrefab
                or CatalogKind.OtherPrefab,
            CatalogViewMode.Icons => entry.Kind == CatalogKind.Icon,
            CatalogViewMode.Recipes => entry.Kind == CatalogKind.Recipe,
            CatalogViewMode.Raw => true,
            _ => true,
        };
    }
}
