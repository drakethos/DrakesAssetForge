using System.Collections.Generic;
using DrakesForge.Format;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>Recolours fire and glow: every Light and ParticleSystem on the prefab (not Forge's own glow).</summary>
internal static class Effects
{
    /// <summary>Pre-Forge light and particle settings, so hot reload starts from vanilla.</summary>
    public sealed class Snapshot
    {
        public readonly List<(Light Light, Color Color, float Intensity, float Range)> Lights = new();
        public readonly List<(ParticleSystem System, ParticleSystem.MinMaxGradient Start, ParticleSystem.MinMaxGradient Lifetime)> Particles = new();
    }

    public static Snapshot Capture(GameObject prefab)
    {
        var snapshot = new Snapshot();
        foreach (var light in Lights(prefab))
            snapshot.Lights.Add((light, light.color, light.intensity, light.range));
        foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            snapshot.Particles.Add((ps, ps.main.startColor, ps.colorOverLifetime.color));
        return snapshot;
    }

    public static void Restore(Snapshot snapshot)
    {
        foreach (var (light, color, intensity, range) in snapshot.Lights)
        {
            if (light == null)
                continue;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
        }

        foreach (var (ps, start, lifetime) in snapshot.Particles)
        {
            if (ps == null)
                continue;
            var main = ps.main;
            main.startColor = start;
            var col = ps.colorOverLifetime;
            col.color = lifetime;
        }
    }

    public static void Apply(GameObject prefab, EffectsRecipe? effects, List<string> warnings)
    {
        if (effects == null || effects.IsEmpty)
            return;

        var lights = Lights(prefab);
        var particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
        if ((effects.LightColor != null || effects.LightIntensity != null || effects.LightRange != null) && lights.Count == 0)
            warnings.Add("effects: this prefab has no lights");
        if (effects.FlameTint != null && particles.Length == 0)
            warnings.Add("effects.flameTint: this prefab has no particle effects");

        foreach (var light in lights)
        {
            if (RecipeSerializer.TryParseColor(effects.LightColor, out var c))
                light.color = new Color(c[0], c[1], c[2], light.color.a);
            if (effects.LightIntensity is { } intensity)
                light.intensity *= intensity;
            if (effects.LightRange is { } range)
                light.range *= range;
        }

        if (!RecipeSerializer.TryParseColor(effects.FlameTint, out var tint))
            return;
        var tintColor = new Color(tint[0], tint[1], tint[2], 1f);
        foreach (var ps in particles)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(tintColor.r, tintColor.g, tintColor.b, main.startColor.color.a));

            // Fire usually fades through a colour gradient: keep its alpha (the fade), swap its colours.
            var col = ps.colorOverLifetime;
            if (col.enabled)
            {
                var gradient = col.color.mode == ParticleSystemGradientMode.Gradient ? col.color.gradient : null;
                var recoloured = new Gradient();
                recoloured.SetKeys(
                    new[] { new GradientColorKey(tintColor, 0f), new GradientColorKey(tintColor, 1f) },
                    gradient?.alphaKeys ?? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(recoloured);
            }
        }
    }

    private static List<Light> Lights(GameObject prefab)
    {
        var glow = prefab.transform.Find(GameNames.GlowChild);
        var result = new List<Light>();
        foreach (var light in prefab.GetComponentsInChildren<Light>(true))
            if (glow == null || !light.transform.IsChildOf(glow))
                result.Add(light);
        return result;
    }
}
