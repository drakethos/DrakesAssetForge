using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using Avalonia.Media.Imaging;
using DrakeAssetForge.Models;

namespace DrakeAssetForge.Services;

public sealed class SoftRefPreviewResult
{
    public Bitmap? Bitmap { get; init; }
    public string Caption { get; init; } = "";
    public string? Error { get; init; }
}

public static class SoftRefPreviewService
{
    public static SoftRefPreviewResult PreviewTextureFromBundle(string bundlePath, long pathId)
    {
        var am = new AssetsManager();
        try
        {
            var afileInst = OpenFirstAssetsFile(am, bundlePath);
            if (afileInst == null)
                return Fail("Could not open assets file in bundle.");

            var info = afileInst.file.GetAssetInfo(pathId);
            if (info == null)
                return Fail($"No asset at PathId {pathId}.");

            return DecodeAsset(am, afileInst, info);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            am.UnloadAll();
        }
    }

    /// <summary>Decode a Texture2D from a bundle to PNG bytes (for import). Returns null on failure.</summary>
    public static byte[]? TryExportTexturePng(string bundlePath, long pathId)
    {
        var am = new AssetsManager();
        try
        {
            var afileInst = OpenFirstAssetsFile(am, bundlePath);
            if (afileInst == null)
                return null;

            var info = afileInst.file.GetAssetInfo(pathId);
            if (info == null)
                return null;

            var bf = am.GetBaseField(afileInst, info);
            var texture = TextureFile.ReadTextureFile(bf);
            if (texture.m_Width <= 0 || texture.m_Height <= 0 || texture.m_Width > 1024 || texture.m_Height > 1024)
                return null;

            var encData = texture.FillPictureData(afileInst);
            if (encData == null || encData.Length == 0)
                return null;

            var bgra = texture.DecodeTextureRaw(encData);
            if (bgra == null || bgra.Length == 0)
                return null;

            TextureOperations.FlipBGRA32Vertically(bgra, texture.m_Width, texture.m_Height);
            using var ms = new MemoryStream();
            TextureOperations.WriteRawImage(bgra, texture.m_Width, texture.m_Height, ms, ImageExportType.Png, 100);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            am.UnloadAll();
        }
    }

    public static SoftRefPreviewResult PreviewTextureByContainerPath(string bundlePath, string pathInBundle)
    {
        var entry = BundleAssetLister.FindContainerEntry(bundlePath, pathInBundle);
        if (entry == null)
            return Fail($"Container entry not found for '{pathInBundle}'.");

        return PreviewTextureFromBundle(bundlePath, entry.PathId);
    }

    public static SoftRefPreviewResult PreviewMeshStats(string bundlePath, long pathId)
    {
        var am = new AssetsManager();
        try
        {
            var afileInst = OpenFirstAssetsFile(am, bundlePath);
            if (afileInst == null)
                return Fail("Could not open assets file in bundle.");

            var info = afileInst.file.GetAssetInfo(pathId);
            if (info == null)
                return Fail($"No asset at PathId {pathId}.");

            var bf = am.GetBaseField(afileInst, info);
            var name = bf["m_Name"].IsDummy ? "(mesh)" : bf["m_Name"].AsString;
            var verts = TryReadInt(bf, "m_VertexCount");
            if (verts < 0)
                verts = TryReadInt(bf, "m_VertexData.m_VertexCount");

            return new SoftRefPreviewResult
            {
                Caption = $"Mesh '{name}' · verts≈{Math.Max(verts, 0)}",
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            am.UnloadAll();
        }
    }

    public static SoftRefAssetEntry? FindIconForItem(
        SoftRefAssetEntry item,
        IReadOnlyList<SoftRefAssetEntry> allAssets)
    {
        var name = item.DisplayName;
        return allAssets.FirstOrDefault(a =>
            a.Kind == CatalogKind.Icon &&
            a.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static SoftRefPreviewResult DecodeAsset(
        AssetsManager am,
        AssetsFileInstance afileInst,
        AssetFileInfo info)
    {
        var type = (AssetClassID)info.TypeId;
        var bf = am.GetBaseField(afileInst, info);
        var name = bf["m_Name"].IsDummy ? "(asset)" : bf["m_Name"].AsString;

        if (type == AssetClassID.Texture2D)
            return DecodeTexture2D(am, afileInst, info, name, crop: null, requireCrop: false);

        if (type == AssetClassID.Sprite)
            return DecodeSprite(am, afileInst, bf, name);

        return Fail($"Type {type} is not a texture/sprite preview source.");
    }

    private static SoftRefPreviewResult DecodeSprite(
        AssetsManager am,
        AssetsFileInstance afileInst,
        AssetTypeValueField spriteBf,
        string name)
    {
        var atlasId = spriteBf["m_SpriteAtlas"]["m_PathID"].AsLong;
        if (atlasId != 0)
        {
            var atlasHit = TryResolveAtlasSprite(am, afileInst, spriteBf, name, atlasId);
            if (atlasHit != null)
                return atlasHit;
        }

        // Standalone sprite with embedded texture pointer.
        var directTexId = spriteBf["m_RD"]["texture"]["m_PathID"].AsLong;
        if (directTexId != 0)
        {
            var texInfo = afileInst.file.GetAssetInfo(directTexId);
            if (texInfo != null)
            {
                var rect = ReadRect(spriteBf["m_RD"]["textureRect"]);
                return DecodeTexture2D(am, afileInst, texInfo, name, rect, requireCrop: rect != null);
            }
        }

        return Fail(
            $"Sprite '{name}' is atlas-packed but crop could not be resolved (won't show full sheet).");
    }

    private static SoftRefPreviewResult? TryResolveAtlasSprite(
        AssetsManager am,
        AssetsFileInstance afileInst,
        AssetTypeValueField spriteBf,
        string name,
        long atlasId)
    {
        var atlasInfo = afileInst.file.GetAssetInfo(atlasId);
        if (atlasInfo == null)
            return Fail($"SpriteAtlas PathId {atlasId} missing.");

        var atlasBf = am.GetBaseField(afileInst, atlasInfo);
        var atlasData = FindAtlasDataForSprite(atlasBf, spriteBf, name);
        if (atlasData == null)
            return Fail($"No atlas RenderData for sprite '{name}'.");

        var texId = atlasData["texture"]["m_PathID"].AsLong;
        if (texId == 0)
            return Fail($"Atlas entry for '{name}' has null texture.");

        var texInfo = afileInst.file.GetAssetInfo(texId);
        if (texInfo == null)
            return Fail($"Atlas texture PathId {texId} missing.");

        var crop = ReadRect(atlasData["textureRect"]);
        if (crop == null || crop.Value.W <= 0 || crop.Value.H <= 0)
            return Fail($"Atlas entry for '{name}' has empty textureRect.");

        return DecodeTexture2D(am, afileInst, texInfo, name, crop, requireCrop: true);
    }

    private static AssetTypeValueField? FindAtlasDataForSprite(
        AssetTypeValueField atlasBf,
        AssetTypeValueField spriteBf,
        string spriteName)
    {
        var map = atlasBf["m_RenderDataMap"]["Array"];
        if (map.IsDummy)
            return null;

        // Prefer exact RenderDataKey match (GUID + long).
        var key = spriteBf["m_RenderDataKey"];
        if (!key.IsDummy)
        {
            var keyGuid = key["first"];
            var keyLong = key["second"].AsLong;
            foreach (var entry in map.Children)
            {
                var entryKey = entry["first"];
                if (entryKey["second"].AsLong != keyLong)
                    continue;
                if (!GuidEquals(entryKey["first"], keyGuid))
                    continue;
                return entry["second"];
            }
        }

        // Fallback: packed name → packed sprite PathId → that sprite's RenderDataKey.
        try
        {
            var names = atlasBf["m_PackedSpriteNamesToIndex"]["Array"];
            var sprites = atlasBf["m_PackedSprites"]["Array"];
            for (var i = 0; i < names.Children.Count && i < sprites.Children.Count; i++)
            {
                if (!names.Children[i].AsString.Equals(spriteName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // We already have the sprite field; use its key was primary. If name matched index,
                // re-scan map using only the long can be ambiguous — instead use textureRect from
                // sprite m_RD when atlas texture is known via any map entry with this sprite's keyLong.
                var keyLong = spriteBf["m_RenderDataKey"]["second"].AsLong;
                foreach (var entry in map.Children)
                {
                    if (entry["first"]["second"].AsLong != keyLong)
                        continue;
                    var data = entry["second"];
                    var rect = ReadRect(data["textureRect"]);
                    // Prefer a small icon-sized rect over a near-full-sheet rect.
                    if (rect is { W: > 0 and <= 512, H: > 0 and <= 512 })
                        return data;
                }
            }
        }
        catch
        {
            // ignore fallback errors
        }

        return null;
    }

    private static bool GuidEquals(AssetTypeValueField a, AssetTypeValueField b)
    {
        try
        {
            if (a.IsDummy || b.IsDummy || a.Children.Count < 4 || b.Children.Count < 4)
                return false;

            // SoftRef GUID fields expose children named data[0]..data[3], not a nested "data" array.
            for (var i = 0; i < 4; i++)
            {
                if (a.Children[i].AsUInt != b.Children[i].AsUInt)
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SoftRefPreviewResult DecodeTexture2D(
        AssetsManager am,
        AssetsFileInstance afileInst,
        AssetFileInfo info,
        string label,
        (int X, int Y, int W, int H)? crop,
        bool requireCrop)
    {
        var bf = am.GetBaseField(afileInst, info);
        var texName = bf["m_Name"].IsDummy ? label : bf["m_Name"].AsString;
        var texture = TextureFile.ReadTextureFile(bf);
        if (texture.m_Width <= 0 || texture.m_Height <= 0)
            return Fail($"Texture '{texName}' has invalid size.");

        // Never show a giant atlas sheet as an "icon" preview.
        if (requireCrop)
        {
            if (crop == null || crop.Value.W <= 0 || crop.Value.H <= 0)
                return Fail($"Refusing to preview atlas '{texName}' without a crop rect.");

            var area = (long)crop.Value.W * crop.Value.H;
            var texArea = (long)texture.m_Width * texture.m_Height;
            if (texArea > 0 && area * 4 > texArea)
                return Fail($"Crop for '{label}' looks like the full atlas ({crop.Value.W}×{crop.Value.H} of {texture.m_Width}×{texture.m_Height}).");
        }

        // Straight Texture2D is fine; refuse dumping an entire atlas sheet into the preview pane.
        if (!requireCrop && (texture.m_Width > 1024 || texture.m_Height > 1024))
        {
            return Fail(
                $"'{texName}' is {texture.m_Width}×{texture.m_Height} (likely an atlas sheet). " +
                "Select the Sprite/icon entry so it can be cropped.");
        }

        var encData = texture.FillPictureData(afileInst);
        if (encData == null || encData.Length == 0)
            return Fail($"Texture '{texName}' has no picture data.");

        var bgra = texture.DecodeTextureRaw(encData);
        if (bgra == null || bgra.Length == 0)
            return Fail($"Texture '{texName}' decode returned empty.");

        TextureOperations.FlipBGRA32Vertically(bgra, texture.m_Width, texture.m_Height);

        int outW = texture.m_Width;
        int outH = texture.m_Height;
        byte[] pixels = bgra;

        if (crop is { } c && c.W > 0 && c.H > 0)
        {
            var x = Math.Clamp(c.X, 0, Math.Max(0, texture.m_Width - 1));
            var yUnity = Math.Clamp(c.Y, 0, Math.Max(0, texture.m_Height - 1));
            var w = Math.Clamp(c.W, 1, texture.m_Width - x);
            var h = Math.Clamp(c.H, 1, texture.m_Height - yUnity);
            // Unity rect is bottom-left origin; buffer was flipped to top-left.
            var yTop = texture.m_Height - yUnity - h;
            yTop = Math.Clamp(yTop, 0, Math.Max(0, texture.m_Height - h));

            pixels = new byte[w * h * 4];
            for (var row = 0; row < h; row++)
            {
                Buffer.BlockCopy(
                    bgra,
                    ((yTop + row) * texture.m_Width + x) * 4,
                    pixels,
                    row * w * 4,
                    w * 4);
            }

            outW = w;
            outH = h;
        }

        using var ms = new MemoryStream();
        TextureOperations.WriteRawImage(pixels, outW, outH, ms, ImageExportType.Png, 100);
        ms.Position = 0;
        var bitmap = new Bitmap(ms);

        var caption = crop == null
            ? $"{label} · {outW}×{outH}"
            : $"{label} · {outW}×{outH} (from atlas {texture.m_Width}×{texture.m_Height})";

        return new SoftRefPreviewResult
        {
            Bitmap = bitmap,
            Caption = caption,
        };
    }

    private static (int X, int Y, int W, int H)? ReadRect(AssetTypeValueField rect)
    {
        try
        {
            if (rect.IsDummy)
                return null;
            return (
                (int)Math.Round(rect["x"].AsFloat),
                (int)Math.Round(rect["y"].AsFloat),
                (int)Math.Round(rect["width"].AsFloat),
                (int)Math.Round(rect["height"].AsFloat));
        }
        catch
        {
            return null;
        }
    }

    private static AssetsFileInstance? OpenFirstAssetsFile(AssetsManager am, string bundlePath)
    {
        var bun = am.LoadBundleFile(bundlePath, true);
        var names = bun.file.GetAllFileNames();
        if (names.Count == 0)
            return null;

        var assetsIndex = 0;
        for (var i = 0; i < names.Count; i++)
        {
            if (!names[i].EndsWith(".resS", StringComparison.OrdinalIgnoreCase) &&
                !names[i].EndsWith(".resource", StringComparison.OrdinalIgnoreCase))
            {
                assetsIndex = i;
                break;
            }
        }

        return am.LoadAssetsFileFromBundle(bun, assetsIndex, false);
    }

    private static int TryReadInt(AssetTypeValueField bf, string path)
    {
        try
        {
            var f = bf[path];
            return f.IsDummy ? -1 : f.AsInt;
        }
        catch
        {
            return -1;
        }
    }

    private static SoftRefPreviewResult Fail(string error) =>
        new()
        {
            Caption = "Preview failed",
            Error = error,
        };
}
