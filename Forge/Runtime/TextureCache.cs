using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>PNG/JPG files from packs, loaded once per path. Cleared on hot reload so edited files are re-read.</summary>
internal sealed class TextureCache
{
    // Unity 6 adds a ReadOnlySpan overload net481 can't compile against; bind the byte[] one explicitly.
    private static readonly Func<Texture2D, byte[], bool> LoadImage = (Func<Texture2D, byte[], bool>)Delegate.CreateDelegate(
        typeof(Func<Texture2D, byte[], bool>),
        typeof(ImageConversion).GetMethod(nameof(ImageConversion.LoadImage), new[] { typeof(Texture2D), typeof(byte[]) })!);

    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sprite> _sprites = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="fullPath">Resolved path, or null if the recipe path escaped the pack.</param>
    /// <param name="label">The path as written in the recipe, for messages.</param>
    /// <param name="normalMap">
    /// Load as a normal map: linear color space, and repacked to Unity's (A = x, G = y) layout that its
    /// UnpackNormal expects, since a plain PNG isn't imported as a normal map at runtime.
    /// </param>
    public Texture2D? Load(string? fullPath, string label, List<string> warnings, bool normalMap = false)
    {
        if (fullPath == null)
        {
            warnings.Add($"'{label}' must be a path inside the pack");
            return null;
        }

        var key = normalMap ? fullPath + "|normal" : fullPath;
        if (_textures.TryGetValue(key, out var cached))
            return cached;
        if (!File.Exists(fullPath))
        {
            warnings.Add($"file '{label}' not found");
            return null;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, normalMap) { name = Path.GetFileNameWithoutExtension(fullPath) };
        try
        {
            if (!LoadImage(texture, File.ReadAllBytes(fullPath)))
            {
                warnings.Add($"'{label}' is not a readable PNG or JPG");
                return null;
            }
        }
        catch (IOException ex)
        {
            warnings.Add($"'{label}': {ex.Message}");
            return null;
        }

        if (normalMap)
        {
            var pixels = texture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, pixels[i].g, pixels[i].g, pixels[i].r);
            texture.SetPixels32(pixels);
            texture.Apply(true);
        }

        _textures[key] = texture;
        return texture;
    }

    /// <summary>A PNG/JPG held in memory (a model file's embedded texture). Not cached: the model is read once per apply.</summary>
    public Texture2D? LoadBytes(byte[] bytes, string label, List<string> warnings)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = label };
        if (!LoadImage(texture, bytes))
        {
            warnings.Add($"'{label}' is not a readable PNG or JPG");
            return null;
        }
        return texture;
    }

    public Sprite? LoadSprite(string? fullPath, string label, List<string> warnings)
    {
        if (fullPath != null && _sprites.TryGetValue(fullPath, out var cached))
            return cached;

        var texture = Load(fullPath, label, warnings);
        if (texture == null)
            return null;

        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = texture.name;
        _sprites[fullPath!] = sprite;
        return sprite;
    }

    public void Clear()
    {
        _textures.Clear();
        _sprites.Clear();
    }
}
