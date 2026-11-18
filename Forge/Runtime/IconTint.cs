using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// A tinted copy of an icon sprite. Icons often sit in an atlas, so the sprite's own rectangle is copied out first,
/// then each pixel is multiplied by the colour (white leaves the icon as it is).
/// </summary>
internal static class IconTint
{
    public static Sprite Apply(Sprite source, Color tint)
    {
        var rect = source.textureRect;
        var width = Mathf.RoundToInt(rect.width);
        var height = Mathf.RoundToInt(rect.height);
        var texture = source.texture;

        // Blit only the sprite's rectangle into a readable copy.
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        var scale = new Vector2(rect.width / texture.width, rect.height / texture.height);
        var offset = new Vector2(rect.x / texture.width, rect.y / texture.height);
        Graphics.Blit(texture, target, scale, offset);

        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);

        var pixels = copy.GetPixels();
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(pixels[i].r * tint.r, pixels[i].g * tint.g, pixels[i].b * tint.b, pixels[i].a * tint.a);
        copy.SetPixels(pixels);
        copy.Apply();

        return Sprite.Create(copy, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
    }
}
