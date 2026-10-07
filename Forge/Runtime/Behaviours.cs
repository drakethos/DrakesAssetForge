using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>Forge-provided behaviours configured from recipe data (no user C# needed).</summary>
internal static class Behaviours
{
    public static void Apply(GameObject prefab, List<BehaviourRecipe> behaviours, List<string> warnings)
    {
        foreach (var behaviour in behaviours)
        {
            switch (behaviour.Type.ToLowerInvariant())
            {
                case "glow":
                    AddGlow(prefab, behaviour.Settings, warnings);
                    break;
                default:
                    warnings.Add($"unknown behaviour '{behaviour.Type}' (available: glow)");
                    break;
            }
        }
    }

    /// <summary>{ "type": "glow", "color": "#FFB066", "intensity": 1, "range": 4, "offset": [0, 1, 0], "nightOnly": false }</summary>
    private static void AddGlow(GameObject prefab, Dictionary<string, JsonValue> s, List<string> warnings)
    {
        if (prefab.transform.Find(GameNames.GlowChild) != null)
        {
            warnings.Add("only one glow per item");
            return;
        }

        var glow = new GameObject(GameNames.GlowChild);
        glow.transform.SetParent(prefab.transform, false);
        glow.transform.localPosition = Vec(s, "offset", new Vector3(0f, 1f, 0f));

        var light = glow.AddComponent<Light>();
        light.type = LightType.Point;
        light.shadows = LightShadows.None;
        light.intensity = Num(s, "intensity", 1f);
        light.range = Num(s, "range", 4f);
        var colorText = s.TryGetValue("color", out var c) ? c.AsString() : null;
        if (colorText == null)
            light.color = new Color(1f, 0.69f, 0.4f);
        else if (RecipeSerializer.TryParseColor(colorText, out var rgba))
            light.color = new Color(rgba[0], rgba[1], rgba[2]);
        else
            warnings.Add($"glow.color '{colorText}' is not a color");

        if (s.TryGetValue("nightOnly", out var night) && night.AsBool() == true)
            glow.AddComponent<ForgeGlow>().NightOnly = true;
    }

    private static float Num(Dictionary<string, JsonValue> s, string key, float fallback) =>
        s.TryGetValue(key, out var v) && v.AsNumber() is { } n ? (float)n : fallback;

    private static Vector3 Vec(Dictionary<string, JsonValue> s, string key, Vector3 fallback)
    {
        if (!s.TryGetValue(key, out var v) || !v.IsArray || v.Count != 3 || v.Items.Any(i => i.AsNumber() == null))
            return fallback;
        return new Vector3((float)v.Items[0].NumberValue, (float)v.Items[1].NumberValue, (float)v.Items[2].NumberValue);
    }
}

/// <summary>Turns a glow light on only at night. Lives on placed/dropped instances.</summary>
public sealed class ForgeGlow : MonoBehaviour
{
    public bool NightOnly;

    private Light? _light;
    private float _nextCheck;

    private void Awake() => _light = GetComponent<Light>();

    private void Update()
    {
        if (!NightOnly || _light == null || Time.time < _nextCheck)
            return;
        _nextCheck = Time.time + 2f;
        _light.enabled = EnvMan.IsNight();
    }
}
