using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

/// <summary>
/// Lazy SoftRef / file thumbnails for catalog rows and project tree items.
/// </summary>
public sealed class SoftRefThumbnailCache
{
    private readonly ConcurrentDictionary<string, Bitmap?> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, string?>? _resolveBundle;
    private IReadOnlyList<SoftRefAssetEntry> _allAssets = Array.Empty<SoftRefAssetEntry>();

    public void Configure(Func<string, string?> resolveBundle, IReadOnlyList<SoftRefAssetEntry> allAssets)
    {
        _resolveBundle = resolveBundle;
        _allAssets = allAssets;
    }

    public void RequestCatalogThumb(SoftRefAssetEntry asset)
    {
        if (asset.Thumbnail != null)
            return;
        if (_byKey.TryGetValue(asset.AssetId, out var cached))
        {
            asset.Thumbnail = cached;
            return;
        }

        if (!_inflight.TryAdd(asset.AssetId, 0))
            return;

        var resolve = _resolveBundle;
        var all = _allAssets;
        _ = Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
                bmp = LoadCatalogBitmap(asset, resolve, all);
                _byKey[asset.AssetId] = bmp;
            }
            catch
            {
                _byKey[asset.AssetId] = null;
            }
            finally
            {
                _inflight.TryRemove(asset.AssetId, out _);
            }

            Dispatcher.UIThread.Post(() => asset.Thumbnail = bmp);
        });
    }

    public void RequestProjectThumb(ProjectTreeNode node, ProjectStore store)
    {
        if (node.IsFolder || node.Item == null || node.Thumbnail != null)
            return;

        var item = node.Item;
        var key = "owned:" + item.Id;
        if (_byKey.TryGetValue(key, out var cached))
        {
            node.Thumbnail = cached;
            return;
        }

        if (!_inflight.TryAdd(key, 0))
            return;

        var resolve = _resolveBundle;
        var all = _allAssets;
        var art = store.LoadArt(item);
        var donorName = item.Donor.PrefabName;
        var allowDonorThumb = !item.Donor.Kind.Equals("Imported", StringComparison.OrdinalIgnoreCase);
        ProjectStore storeCapture = store;
        OwnedItemDocument itemCapture = item;
        ArtDocument artCapture = art;

        _ = Task.Run(() =>
        {
            Bitmap? bmp = null;
            try
            {
                var custom = OwnedArtPreview.Resolve(storeCapture, itemCapture, artCapture);
                if (custom.Bitmap != null)
                    bmp = custom.Bitmap;
                else if (allowDonorThumb &&
                         custom.UsedDonorFallback &&
                         !string.IsNullOrWhiteSpace(donorName))
                {
                    // SoftRef donor only for true SoftRef clones — never LeatherScraps on imports.
                    var donor = all.FirstOrDefault(a =>
                        a.Kind == CatalogKind.ItemPrefab &&
                        a.DisplayName.Equals(donorName, StringComparison.OrdinalIgnoreCase));
                    if (donor != null)
                        bmp = LoadCatalogBitmap(donor, resolve, all);
                }

                _byKey[key] = bmp;
            }
            catch
            {
                _byKey[key] = null;
            }
            finally
            {
                _inflight.TryRemove(key, out _);
            }

            Dispatcher.UIThread.Post(() => node.Thumbnail = bmp);
        });
    }

    public void InvalidateOwned(string itemId)
    {
        if (_byKey.TryRemove("owned:" + itemId, out var bmp))
            bmp?.Dispose();
    }

    public void InvalidateAllOwned()
    {
        foreach (var key in _byKey.Keys.Where(k => k.StartsWith("owned:", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_byKey.TryRemove(key, out var bmp))
                bmp?.Dispose();
        }
    }

    private static Bitmap? LoadCatalogBitmap(
        SoftRefAssetEntry asset,
        Func<string, string?>? resolveBundle,
        IReadOnlyList<SoftRefAssetEntry> all)
    {
        SoftRefAssetEntry? iconAsset = asset.Kind == CatalogKind.Icon || IsTexturePath(asset)
            ? asset
            : SoftRefPreviewService.FindIconForItem(asset, all);
        if (iconAsset == null || resolveBundle == null)
            return null;

        var bundlePath = resolveBundle(iconAsset.BundleId);
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            return null;

        var result = SoftRefPreviewService.PreviewTextureByContainerPath(bundlePath, iconAsset.PathInBundle);
        return result.Error == null ? result.Bitmap : null;
    }

    private static bool IsTexturePath(SoftRefAssetEntry asset) =>
        asset.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
        asset.Extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
        asset.NormalizedPath.Contains("/texture", StringComparison.OrdinalIgnoreCase);
}
