using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using DrakesForge.Format.Json;

namespace DrakesForge.Format;

/// <summary>Reads and writes forgepack.json and item recipes. Unknown keys are ignored; bad values become problems.</summary>
public static class RecipeSerializer
{
    public static ForgePack ReadPack(string json, List<string> problems)
    {
        var root = ParseObject(json, problems);
        var pack = new ForgePack();
        if (root == null)
            return pack;

        pack.Format = (int)(root["format"]?.AsNumber() ?? ForgePack.CurrentFormat);
        pack.Id = Str(root, "id") ?? "";
        pack.Name = Str(root, "name") ?? pack.Id;
        pack.Version = Str(root, "version") ?? "1.0.0";
        pack.Author = Str(root, "author") ?? "";
        pack.Description = Str(root, "description") ?? "";
        pack.Website = Str(root, "website") ?? "";
        pack.Embedded = root["embedded"]?.AsBool() ?? false;

        if (pack.Format > ForgePack.CurrentFormat)
            problems.Add($"Pack format {pack.Format} is newer than this Forge ({ForgePack.CurrentFormat}). Update Forge Runtime.");
        if (string.IsNullOrWhiteSpace(pack.Id))
            problems.Add("Pack is missing \"id\".");
        return pack;
    }

    public static string WritePack(ForgePack pack)
    {
        var root = JsonValue.NewObject()
            .Set("format", pack.Format)
            .Set("id", pack.Id)
            .Set("name", pack.Name)
            .Set("version", pack.Version)
            .Set("author", pack.Author);
        if (!string.IsNullOrEmpty(pack.Description))
            root.Set("description", pack.Description);
        if (!string.IsNullOrEmpty(pack.Website))
            root.Set("website", pack.Website);
        if (pack.Embedded)
            root.Set("embedded", true);
        return root.ToJson();
    }

    public static ItemRecipe ReadRecipe(string json, List<string> problems)
    {
        var root = ParseObject(json, problems);
        var recipe = new ItemRecipe();
        if (root == null)
            return recipe;

        recipe.Id = Str(root, "id") ?? "";
        recipe.Base = Str(root, "base") ?? "";
        recipe.Name = Str(root, "name");
        recipe.Description = Str(root, "description");

        var kind = Str(root, "kind");
        if (kind != null)
        {
            if (TryEnum<RecipeKind>(kind, out var parsed))
                recipe.Kind = parsed;
            else
                problems.Add($"Unknown kind \"{kind}\" (item, piece, reskin).");
        }

        if (root["look"] is { IsObject: true } look)
            recipe.Look = ReadLook(look, problems);

        if (root["fields"] is { IsObject: true } fields)
        {
            foreach (var component in fields.Properties)
            {
                if (!component.Value.IsObject)
                {
                    problems.Add($"fields.{component.Key} must be an object of field → value.");
                    continue;
                }

                var map = new Dictionary<string, JsonValue>();
                foreach (var field in component.Value.Properties)
                    map[field.Key] = field.Value;
                recipe.Fields[component.Key] = map;
            }
        }

        if (root["behaviours"] is { IsArray: true } behaviours)
        {
            foreach (var b in behaviours.Items)
            {
                var type = b.IsObject ? Str(b, "type") : null;
                if (type == null)
                {
                    problems.Add("Each behaviour needs a \"type\".");
                    continue;
                }

                var behaviour = new BehaviourRecipe { Type = type };
                foreach (var p in b.Properties)
                    if (p.Key != "type")
                        behaviour.Settings[p.Key] = p.Value;
                recipe.Behaviours.Add(behaviour);
            }
        }

        if (root["craft"] is { IsObject: true } craft)
            recipe.Craft = ReadCraft(craft, problems);

        if (root["snap"] is { IsObject: true } snap)
            recipe.Snap = ReadSnap(snap, problems);

        if (root["components"] is { IsObject: true } components)
        {
            foreach (var r in components["remove"]?.Items ?? Array.Empty<JsonValue>())
                if (r.AsString() is { Length: > 0 } name)
                    recipe.RemoveComponents.Add(name);
            foreach (var a in components["add"]?.Items ?? Array.Empty<JsonValue>())
            {
                var type = a.AsString() ?? (a.IsObject ? Str(a, "type") : null);
                if (string.IsNullOrWhiteSpace(type))
                    problems.Add("components.add entries need a \"type\".");
                else
                    recipe.AddComponents.Add(new ComponentAdd { Type = type! });
            }
        }

        if (root["effects"] is { IsObject: true } effects)
        {
            recipe.Effects = new EffectsRecipe
            {
                LightColor = Str(effects, "lightColor"),
                LightIntensity = effects["lightIntensity"]?.AsNumber() is { } li ? (float)li : null,
                LightRange = effects["lightRange"]?.AsNumber() is { } lr ? (float)lr : null,
                FlameTint = Str(effects, "flameTint")
            };
        }

        Validate(recipe, problems);
        return recipe;
    }

    public static string WriteRecipe(ItemRecipe recipe)
    {
        var root = JsonValue.NewObject()
            .Set("id", recipe.Id)
            .Set("kind", recipe.Kind.ToString().ToLowerInvariant())
            .Set("base", recipe.Base);
        SetIf(root, "name", recipe.Name);
        SetIf(root, "description", recipe.Description);

        if (!recipe.Look.IsEmpty)
            root.Set("look", WriteLook(recipe.Look));

        if (recipe.Fields.Count > 0)
        {
            var fields = JsonValue.NewObject();
            foreach (var component in recipe.Fields)
            {
                var map = JsonValue.NewObject();
                foreach (var field in component.Value)
                    map.Set(field.Key, field.Value);
                fields.Set(component.Key, map);
            }

            root.Set("fields", fields);
        }

        if (recipe.Behaviours.Count > 0)
        {
            var arr = JsonValue.NewArray();
            foreach (var b in recipe.Behaviours)
            {
                var obj = JsonValue.NewObject().Set("type", b.Type);
                foreach (var s in b.Settings)
                    obj.Set(s.Key, s.Value);
                arr.Add(obj);
            }

            root.Set("behaviours", arr);
        }

        if (recipe.Craft != null)
            root.Set("craft", WriteCraft(recipe.Craft));

        if (recipe.Snap != null)
        {
            var points = JsonValue.NewArray();
            foreach (var p in recipe.Snap.Points)
                points.Add(JsonValue.NewArray().Add(Tidy(p.X)).Add(Tidy(p.Y)).Add(Tidy(p.Z)));
            root.Set("snap", JsonValue.NewObject()
                .Set("mode", recipe.Snap.Mode.ToString().ToLowerInvariant())
                .Set("points", points));
        }

        if (recipe.RemoveComponents.Count > 0 || recipe.AddComponents.Count > 0)
        {
            var components = JsonValue.NewObject();
            if (recipe.RemoveComponents.Count > 0)
            {
                var remove = JsonValue.NewArray();
                foreach (var r in recipe.RemoveComponents)
                    remove.Add(r);
                components.Set("remove", remove);
            }

            if (recipe.AddComponents.Count > 0)
            {
                var add = JsonValue.NewArray();
                foreach (var a in recipe.AddComponents)
                    add.Add(JsonValue.NewObject().Set("type", a.Type));
                components.Set("add", add);
            }

            root.Set("components", components);
        }

        if (recipe.Effects is { IsEmpty: false } fx)
        {
            var effects = JsonValue.NewObject();
            SetIf(effects, "lightColor", fx.LightColor);
            if (fx.LightIntensity is { } li)
                effects.Set("lightIntensity", Tidy(li));
            if (fx.LightRange is { } lr)
                effects.Set("lightRange", Tidy(lr));
            SetIf(effects, "flameTint", fx.FlameTint);
            root.Set("effects", effects);
        }

        return root.ToJson();
    }

    /// <summary>Checks that don't need the game. The runtime adds its own (prefab exists, etc.).</summary>
    public static void Validate(ItemRecipe recipe, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(recipe.Id))
            problems.Add("Missing \"id\".");
        else if (recipe.Id.IndexOfAny(new[] { ' ', '(', ')', '/', '\\', '.' }) >= 0)
            problems.Add($"Id \"{recipe.Id}\" may not contain spaces, brackets, slashes or dots.");

        if (string.IsNullOrWhiteSpace(recipe.Base))
            problems.Add("Missing \"base\" (the vanilla prefab to start from).");

        if (recipe.Kind == RecipeKind.Reskin && recipe.Id != recipe.Base && !string.IsNullOrEmpty(recipe.Id) && recipe.Craft != null)
            problems.Add("Reskins keep the vanilla recipe; \"craft\" is ignored.");

        foreach (var component in recipe.Fields)
            foreach (var field in component.Value)
                if (field.Value.Kind == JsonKind.Null || (field.Value.Kind == JsonKind.String && field.Value.StringValue!.Trim().Length == 0))
                    problems.Add($"fields.{component.Key}.{field.Key} has no value.");

        if (recipe.Look.Mesh is { Prefab: null, File: null })
            problems.Add("look.mesh needs \"prefab\" or \"file\".");

        for (var i = 0; i < recipe.Look.Materials.Count; i++)
        {
            var m = recipe.Look.Materials[i];
            if (m.Tint != null && !TryParseColor(m.Tint, out _))
                problems.Add($"look.materials[{i}].tint \"{m.Tint}\" is not #RRGGBB or #RRGGBBAA.");
            foreach (var c in m.Colors)
                if (!TryParseColor(c.Value, out _))
                    problems.Add($"look.materials[{i}].colors.{c.Key} \"{c.Value}\" is not a color.");
        }

        if (recipe.Craft != null)
        {
            foreach (var r in recipe.Craft.Requirements)
            {
                if (string.IsNullOrWhiteSpace(r.Item))
                    problems.Add("craft.requirements: each entry needs \"item\".");
                if (r.Amount < 0)
                    problems.Add($"craft.requirements: {r.Item} amount can't be negative.");
            }
        }

        if (recipe.Effects is { } effects)
            foreach (var (key, value) in new[] { ("lightColor", effects.LightColor), ("flameTint", effects.FlameTint) })
                if (value != null && !TryParseColor(value, out _))
                    problems.Add($"effects.{key} \"{value}\" is not a color.");

        foreach (var removed in recipe.RemoveComponents)
            if (recipe.AddComponents.Any(a => a.Type == removed))
                problems.Add($"components: {removed} is both removed and added.");

        if (recipe.Snap != null && recipe.Snap.Mode != SnapMode.Keep && recipe.Kind == RecipeKind.Item)
            problems.Add("Snap points only apply to pieces.");
    }

    /// <summary>Parses #RGB, #RRGGBB or #RRGGBBAA into 0–1 floats.</summary>
    /// <summary>
    /// "#RRGGBB" / "#RRGGBBAA" / "#RGB", optionally "*k" for HDR brightness (emission, glow):
    /// "#66CCFF*2.5" scales RGB by 2.5 (alpha untouched).
    /// </summary>
    public static bool TryParseColor(string? raw, out float[] rgba)
    {
        rgba = new[] { 1f, 1f, 1f, 1f };
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var text = raw!.Trim();
        var star = text.IndexOf('*');
        if (star >= 0)
        {
            if (!float.TryParse(text.Substring(star + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || scale < 0 ||
                !TryParseColor(text.Substring(0, star), out rgba))
                return false;
            for (var i = 0; i < 3; i++)
                rgba[i] *= scale;
            return true;
        }

        var hex = text.TrimStart('#');
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        if (hex.Length != 6 && hex.Length != 8)
            return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;
        if (hex.Length == 6)
            value = (value << 8) | 0xFF;
        rgba = new[]
        {
            ((value >> 24) & 0xFF) / 255f,
            ((value >> 16) & 0xFF) / 255f,
            ((value >> 8) & 0xFF) / 255f,
            (value & 0xFF) / 255f
        };
        return true;
    }

    private static LookRecipe ReadLook(JsonValue look, List<string> problems)
    {
        var result = new LookRecipe { Icon = Str(look, "icon") };

        if (look["mesh"] is { IsObject: true } mesh)
            result.Mesh = new MeshSource { Prefab = Str(mesh, "prefab"), File = Str(mesh, "file") };

        if (look["materials"] is { IsArray: true } materials)
        {
            foreach (var m in materials.Items)
            {
                if (!m.IsObject)
                {
                    problems.Add("look.materials entries must be objects.");
                    continue;
                }

                var o = new MaterialOverride
                {
                    Target = Str(m, "target"),
                    Slot = m["slot"]?.AsNumber() is { } slot ? (int)slot : null,
                    Shader = Str(m, "shader"),
                    Tint = Str(m, "tint")
                };

                if (m["from"] is { } from)
                {
                    if (from.Kind == JsonKind.String)
                    {
                        o.FromPrefab = from.StringValue;
                    }
                    else if (from.IsObject)
                    {
                        o.FromPrefab = Str(from, "prefab");
                        o.FromMaterial = Str(from, "material");
                    }
                }

                if (m["textures"] is { IsObject: true } textures)
                    foreach (var t in textures.Properties)
                        if (t.Value.AsString() is { } path)
                            o.Textures[t.Key] = path;

                if (m["floats"] is { IsObject: true } floats)
                    foreach (var f in floats.Properties)
                        if (f.Value.AsNumber() is { } n)
                            o.Floats[f.Key] = (float)n;

                if (m["colors"] is { IsObject: true } colors)
                    foreach (var c in colors.Properties)
                        if (c.Value.AsString() is { } color)
                            o.Colors[c.Key] = color;

                result.Materials.Add(o);
            }
        }

        return result;
    }

    private static JsonValue WriteLook(LookRecipe look)
    {
        var obj = JsonValue.NewObject();
        if (look.Mesh != null)
        {
            var mesh = JsonValue.NewObject();
            SetIf(mesh, "prefab", look.Mesh.Prefab);
            SetIf(mesh, "file", look.Mesh.File);
            obj.Set("mesh", mesh);
        }

        if (look.Materials.Count > 0)
        {
            var arr = JsonValue.NewArray();
            foreach (var m in look.Materials)
            {
                var o = JsonValue.NewObject();
                SetIf(o, "target", m.Target);
                if (m.Slot.HasValue)
                    o.Set("slot", m.Slot.Value);
                if (m.FromMaterial != null)
                {
                    var from = JsonValue.NewObject();
                    SetIf(from, "prefab", m.FromPrefab);
                    from.Set("material", m.FromMaterial);
                    o.Set("from", from);
                }
                else
                {
                    SetIf(o, "from", m.FromPrefab);
                }

                SetIf(o, "shader", m.Shader);
                SetIf(o, "tint", m.Tint);
                if (m.Textures.Count > 0)
                {
                    var t = JsonValue.NewObject();
                    foreach (var kv in m.Textures)
                        t.Set(kv.Key, kv.Value);
                    o.Set("textures", t);
                }

                if (m.Floats.Count > 0)
                {
                    var f = JsonValue.NewObject();
                    foreach (var kv in m.Floats)
                        f.Set(kv.Key, Tidy(kv.Value));
                    o.Set("floats", f);
                }

                if (m.Colors.Count > 0)
                {
                    var c = JsonValue.NewObject();
                    foreach (var kv in m.Colors)
                        c.Set(kv.Key, kv.Value);
                    o.Set("colors", c);
                }

                arr.Add(o);
            }

            obj.Set("materials", arr);
        }

        SetIf(obj, "icon", look.Icon);
        return obj;
    }

    private static CraftRecipe ReadCraft(JsonValue craft, List<string> problems)
    {
        var result = new CraftRecipe
        {
            Station = Str(craft, "station"),
            StationLevel = (int)(craft["stationLevel"]?.AsNumber() ?? 1),
            Tool = Str(craft, "tool"),
            Category = Str(craft, "category")
        };

        if (craft["requirements"] is { IsArray: true } reqs)
        {
            foreach (var r in reqs.Items)
            {
                if (!r.IsObject)
                {
                    problems.Add("craft.requirements entries must be objects.");
                    continue;
                }

                result.Requirements.Add(new Requirement
                {
                    Item = Str(r, "item") ?? "",
                    Amount = (int)(r["amount"]?.AsNumber() ?? 1),
                    AmountPerLevel = (int)(r["perLevel"]?.AsNumber() ?? 0),
                    Recover = r["recover"]?.AsBool() ?? true
                });
            }
        }

        return result;
    }

    private static JsonValue WriteCraft(CraftRecipe craft)
    {
        var obj = JsonValue.NewObject();
        SetIf(obj, "station", craft.Station);
        if (craft.StationLevel != 1)
            obj.Set("stationLevel", craft.StationLevel);
        SetIf(obj, "tool", craft.Tool);
        SetIf(obj, "category", craft.Category);
        var arr = JsonValue.NewArray();
        foreach (var r in craft.Requirements)
        {
            var o = JsonValue.NewObject().Set("item", r.Item).Set("amount", r.Amount);
            if (r.AmountPerLevel != 0)
                o.Set("perLevel", r.AmountPerLevel);
            if (!r.Recover)
                o.Set("recover", false);
            arr.Add(o);
        }

        obj.Set("requirements", arr);
        return obj;
    }

    private static SnapRecipe ReadSnap(JsonValue snap, List<string> problems)
    {
        var result = new SnapRecipe();
        var mode = Str(snap, "mode");
        if (mode != null)
        {
            if (TryEnum<SnapMode>(mode, out var parsed))
                result.Mode = parsed;
            else
                problems.Add($"Unknown snap mode \"{mode}\" (keep, replace, add).");
        }

        if (snap["points"] is { IsArray: true } points)
        {
            foreach (var p in points.Items)
            {
                if (p.IsArray && p.Count == 3 && p.Items[0].AsNumber() is { } x && p.Items[1].AsNumber() is { } y && p.Items[2].AsNumber() is { } z)
                    result.Points.Add(new Vec3((float)x, (float)y, (float)z));
                else
                    problems.Add("snap.points entries must be [x, y, z].");
            }
        }

        return result;
    }

    private static JsonValue? ParseObject(string json, List<string> problems)
    {
        try
        {
            var root = JsonValue.Parse(json);
            if (root.IsObject)
                return root;
            problems.Add("Expected a JSON object at the top level.");
        }
        catch (FormatException ex)
        {
            problems.Add(ex.Message);
        }

        return null;
    }

    private static string? Str(JsonValue obj, string key) => obj[key]?.AsString();

    // float → double widening turns 0.65f into 0.6499999761581421; recipes are hand-editable, keep them tidy.
    private static double Tidy(float value) => Math.Round(value, 4);

    private static void SetIf(JsonValue obj, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            obj.Set(key, value!);
    }

    private static bool TryEnum<T>(string raw, out T value) where T : struct =>
        Enum.TryParse(raw, true, out value) && Enum.IsDefined(typeof(T), value);
}
