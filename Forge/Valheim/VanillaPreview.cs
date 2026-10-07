using System.Numerics;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;

namespace DrakesForge.Valheim;

/// <summary>Top-down BGRA pixels (Avalonia's Bgra8888 layout).</summary>
public sealed class RgbaImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[] Bgra { get; init; }
}

/// <summary>One drawable piece of a model, already transformed into the prefab root's space.</summary>
public sealed class ModelPart
{
    public required string Name { get; init; }
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] Uvs { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Index into <see cref="VanillaModel.Materials"/>, which is also the Forge material slot.</summary>
    public required int MaterialSlot { get; init; }
}

public sealed class ModelMaterial
{
    public required MaterialInfo Info { get; init; }
    public RgbaImage? Albedo { get; init; }
}

public sealed class VanillaModel
{
    public required IReadOnlyList<ModelPart> Parts { get; init; }
    /// <summary>Armour and capes: the mesh as worn on the player (bind pose). Empty for everything else.</summary>
    public IReadOnlyList<ModelPart> WornParts { get; init; } = Array.Empty<ModelPart>();
    public required IReadOnlyList<ModelMaterial> Materials { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public int TriangleCount => Parts.Sum(p => p.Indices.Length / 3);
}

public sealed class VanillaPreview
{
    public required PrefabInfo Info { get; init; }
    public RgbaImage? Icon { get; init; }
    public VanillaModel? Model { get; init; }
}

/// <summary>Decodes what the browser shows: icon, meshes and albedo textures. Everything stays in memory.</summary>
public static class PreviewLoader
{
    public static VanillaPreview Load(AssetSession session, VanillaEntry entry, bool model = true, int maxTextureSize = 512) =>
        session.Run(s =>
        {
            var info = PrefabReader.Inspect(s, entry);
            return new VanillaPreview
            {
                Info = info,
                Icon = info.IconSprite != null ? TryIcon(s, info.IconSprite) : null,
                Model = model ? LoadModel(s, info, maxTextureSize) : null
            };
        });

    /// <summary>Just the icon (for browser thumbnails).</summary>
    public static RgbaImage? LoadIcon(AssetSession session, VanillaEntry entry) =>
        session.Run(s => PrefabReader.FindIcon(s, entry) is { } sprite ? TryIcon(s, sprite) : null);

    private static VanillaModel LoadModel(AssetSession s, PrefabInfo info, int maxTextureSize)
    {
        var problems = new List<string>();
        var materials = new List<ModelMaterial>();
        var albedoCache = new Dictionary<AssetRef, RgbaImage?>();
        foreach (var m in info.MaterialSlots)
        {
            RgbaImage? albedo = null;
            if (m.MainTexture != null && !albedoCache.TryGetValue(m.MainTexture, out albedo))
            {
                albedo = TryTexture(s, m.MainTexture, null, maxTextureSize, problems);
                albedoCache[m.MainTexture] = albedo;
            }

            materials.Add(new ModelMaterial { Info = m, Albedo = albedo });
        }

        return new VanillaModel
        {
            Parts = DecodeParts(s, info, r => r.Visible, problems),
            WornParts = DecodeParts(s, info, r => r.VisibleWorn, problems),
            Materials = materials,
            Problems = problems
        };
    }

    private static List<ModelPart> DecodeParts(AssetSession s, PrefabInfo info, Func<RendererInfo, bool> include, List<string> problems)
    {
        var parts = new List<ModelPart>();
        var slot = 0;
        foreach (var renderer in info.Renderers)
        {
            var firstSlot = slot;
            slot += renderer.Materials.Count;
            if (!include(renderer) || renderer.Mesh == null)
                continue;

            try
            {
                var mesh = MeshDecoder.Decode(s, renderer.Mesh);
                for (var sub = 0; sub < mesh.SubMeshes.Count; sub++)
                {
                    // Unity reuses the last material for extra submeshes.
                    var materialSlot = firstSlot + Math.Min(sub, Math.Max(renderer.Materials.Count - 1, 0));
                    parts.Add(mesh.ToPart(sub, renderer.ToRoot, $"{renderer.Path}#{sub}", materialSlot));
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IndexOutOfRangeException or ArgumentException)
            {
                problems.Add($"{renderer.MeshName}: {ex.Message}");
            }
        }

        return parts;
    }

    /// <summary>
    /// Decodes every texture slot of a material (for the texture editors and "Export PNG").
    /// Slots whose texture can't be read are left out.
    /// </summary>
    public static IReadOnlyDictionary<string, RgbaImage> LoadTextures(AssetSession session, MaterialInfo material, int maxSize) =>
        session.Run(s =>
        {
            var result = new Dictionary<string, RgbaImage>();
            foreach (var slot in material.TextureRefs)
                if (TryTexture(s, slot.Value, null, maxSize, null) is { } image)
                    result[slot.Key] = image;
            return (IReadOnlyDictionary<string, RgbaImage>)result;
        });

    private static RgbaImage? TryIcon(AssetSession s, AssetRef spriteRef)
    {
        try
        {
            var am = s.Manager;
            var sprite = am.GetBaseField(spriteRef.File, spriteRef.File.file.GetAssetInfo(spriteRef.PathId));

            // Atlas-packed sprites: the atlas's render data holds the real texture + crop.
            var atlasPtr = sprite["m_SpriteAtlas"];
            if (!atlasPtr.IsDummy && atlasPtr["m_PathID"].AsLong != 0)
            {
                var atlas = s.Ext(spriteRef.File, atlasPtr);
                if (atlas.baseField != null && FindAtlasData(atlas.baseField, sprite) is { } data)
                    return TextureFromPtr(s, atlas.file, data["texture"], ReadRect(data["textureRect"]));
            }

            var rd = sprite["m_RD"];
            return TextureFromPtr(s, spriteRef.File, rd["texture"], ReadRect(rd["textureRect"]));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static RgbaImage? TextureFromPtr(AssetSession s, AssetsFileInstance file, AssetTypeValueField ptr, (int X, int Y, int W, int H)? crop)
    {
        var tex = s.Ext(file, ptr, true);
        return tex.info == null ? null : TryTexture(s, new AssetRef(tex.file, tex.info.PathId), crop, 256, null);
    }

    private static AssetTypeValueField? FindAtlasData(AssetTypeValueField atlas, AssetTypeValueField sprite)
    {
        var key = sprite["m_RenderDataKey"];
        if (key.IsDummy)
            return null;
        var keyGuid = key["first"];
        var keyLong = key["second"].AsLong;
        foreach (var entry in atlas["m_RenderDataMap.Array"].Children)
        {
            var entryKey = entry["first"];
            if (entryKey["second"].AsLong != keyLong)
                continue;
            var guid = entryKey["first"];
            var same = true;
            for (var i = 0; i < 4 && i < guid.Children.Count && i < keyGuid.Children.Count; i++)
                same &= guid.Children[i].AsUInt == keyGuid.Children[i].AsUInt;
            if (same)
                return entry["second"];
        }

        return null;
    }

    private static (int X, int Y, int W, int H)? ReadRect(AssetTypeValueField rect) =>
        rect.IsDummy
            ? null
            : ((int)Math.Round(rect["x"].AsFloat), (int)Math.Round(rect["y"].AsFloat), (int)Math.Round(rect["width"].AsFloat), (int)Math.Round(rect["height"].AsFloat));

    private static RgbaImage? TryTexture(AssetSession s, AssetRef texRef, (int X, int Y, int W, int H)? crop, int maxSize, List<string>? problems)
    {
        var atlasKey = (texRef.File.name, texRef.PathId);
        if (crop is { W: > 0, H: > 0 } cached && s.CachedAtlas(atlasKey) is { } atlas)
            return Downscale(Crop(atlas, cached.X, atlas.Height - cached.Y - cached.H, cached.W, cached.H), maxSize);

        try
        {
            var am = s.Manager;
            var bf = am.GetBaseField(texRef.File, texRef.File.file.GetAssetInfo(texRef.PathId));
            var texture = TextureFile.ReadTextureFile(bf);
            if (texture.m_Width <= 0 || texture.m_Height <= 0)
                return null;
            var raw = texture.FillPictureData(texRef.File);
            if (raw == null || raw.Length == 0)
                return null;
            var bgra = texture.DecodeTextureRaw(raw, true);
            if (bgra == null || bgra.Length == 0)
                return null;

            // Unity stores rows bottom-up.
            var w = texture.m_Width;
            var h = texture.m_Height;
            var image = new RgbaImage { Width = w, Height = h, Bgra = FlipRows(bgra, w, h) };
            if (crop is { W: > 0, H: > 0 } c)
            {
                s.CacheAtlas(atlasKey, image);
                image = Crop(image, c.X, h - c.Y - c.H, c.W, c.H);
            }
            return Downscale(image, maxSize);
        }
        catch (Exception ex)
        {
            problems?.Add($"texture #{texRef.PathId}: {ex.Message}");
            return null;
        }
    }

    private static byte[] FlipRows(byte[] src, int w, int h)
    {
        var dst = new byte[src.Length];
        var row = w * 4;
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(src, y * row, dst, (h - 1 - y) * row, row);
        return dst;
    }

    private static RgbaImage Crop(RgbaImage img, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, img.Width - 1);
        y = Math.Clamp(y, 0, img.Height - 1);
        w = Math.Clamp(w, 1, img.Width - x);
        h = Math.Clamp(h, 1, img.Height - y);
        var dst = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(img.Bgra, ((y + row) * img.Width + x) * 4, dst, row * w * 4, w * 4);
        return new RgbaImage { Width = w, Height = h, Bgra = dst };
    }

    /// <summary>Box filter by an integer factor until it fits; previews don't need 2K textures.</summary>
    private static RgbaImage Downscale(RgbaImage img, int maxSize)
    {
        var factor = 1;
        while (img.Width / factor > maxSize || img.Height / factor > maxSize)
            factor *= 2;
        if (factor == 1)
            return img;

        var w = img.Width / factor;
        var h = img.Height / factor;
        var dst = new byte[w * h * 4];
        var n = factor * factor;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            int b = 0, g = 0, r = 0, a = 0;
            for (var dy = 0; dy < factor; dy++)
            for (var dx = 0; dx < factor; dx++)
            {
                var i = ((y * factor + dy) * img.Width + x * factor + dx) * 4;
                b += img.Bgra[i];
                g += img.Bgra[i + 1];
                r += img.Bgra[i + 2];
                a += img.Bgra[i + 3];
            }

            var o = (y * w + x) * 4;
            dst[o] = (byte)(b / n);
            dst[o + 1] = (byte)(g / n);
            dst[o + 2] = (byte)(r / n);
            dst[o + 3] = (byte)(a / n);
        }

        return new RgbaImage { Width = w, Height = h, Bgra = dst };
    }
}
