using System.Globalization;
using System.Text;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.Services;

/// <summary>How the plain C# export is written.</summary>
public sealed class LiteOptions
{
    public const string LibsDependency = "DrakeMods-DrakeModsLibs-0.10.0";

    /// <summary>Call DrakeModsLibs.Forge instead of writing a ForgeLite helper next to the items.</summary>
    public bool UseLibs { get; init; }
    /// <summary>Images go inside the DLL (EmbeddedResource) instead of a folder beside it.</summary>
    public bool Embed { get; init; }
    /// <summary>Only the looks (Build/Dress/Icon per recipe); the mod registers its items itself.</summary>
    public bool LookOnly { get; init; }
    /// <summary>Namespace of the generated code (default: the project's, or the pack id).</summary>
    public string? Namespace { get; init; }
    /// <summary>Write generated files under this subfolder of the project (e.g. "Paper/Generated").</summary>
    public string? Subfolder { get; init; }
}

/// <summary>What the generator needs to know about the game: each base prefab's scripts and fields, and added components' fields.</summary>
public sealed class LiteTypeInfo
{
    public Dictionary<string, PrefabInfo> Bases { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, IReadOnlyList<FieldNode>> Added { get; } = new(StringComparer.Ordinal);

    public static async Task<LiteTypeInfo> LoadAsync(IEnumerable<ItemRecipe> recipes,
        Func<string, Task<PrefabInfo?>> baseInfo, Func<string, Task<IReadOnlyList<FieldNode>>> defaults)
    {
        var info = new LiteTypeInfo();
        foreach (var recipe in recipes)
        {
            if (!info.Bases.ContainsKey(recipe.Base) && await baseInfo(recipe.Base) is { } prefab)
                info.Bases[recipe.Base] = prefab;
            foreach (var add in recipe.AddComponents)
                if (!info.Added.ContainsKey(add.Type))
                    info.Added[add.Type] = await defaults(add.Type);
        }

        return info;
    }

    /// <summary>The setting a recipe override points at, for its C# type.</summary>
    public FieldNode? Field(ItemRecipe recipe, string component, string path)
    {
        IEnumerable<FieldNode>? fields = null;
        if (Bases.TryGetValue(recipe.Base, out var prefab))
            fields = prefab.Components.FirstOrDefault(c => c.Name == component)?.Fields;
        if (fields == null && Added.TryGetValue(component, out var added))
            fields = added;
        return fields == null ? null : Find(fields, path.Split('.'), 0);
    }

    private static FieldNode? Find(IEnumerable<FieldNode> nodes, string[] parts, int index)
    {
        var node = nodes.FirstOrDefault(n => n.Name == parts[index]);
        if (node == null || index == parts.Length - 1)
            return node;
        return Find(node.Children, parts, index + 1);
    }
}

/// <summary>
/// The "plain C#" export: each recipe becomes straight-line Jotunn code (clone, typed field assignments, materials,
/// fire colours, snap points) plus one small helper file. No Forge, no pack JSON at runtime; only PNGs in Assets\.
/// Generated files are rewritten on every export; Customize\&lt;Item&gt;.cs and the project files are written once.
/// </summary>
public static class LiteCodeWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly HashSet<string> UnityComponents = new(ComponentCatalog.UnityComponents) { "Collider", "MeshFilter", "MeshRenderer", "Transform", "Light", "AudioSource" };
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

    public static string DefaultFolder(PackProject pack) => CodeProjectWriter.DefaultFolder(pack) + "-plain";

    public static CodeExportResult Write(PackProject pack, string folder, bool intoExisting, LiteTypeInfo types, PushTarget? deployTo, string? valheimPath,
        LiteOptions? options = null)
    {
        var o = options ?? new LiteOptions();
        Directory.CreateDirectory(folder);
        var name = PushTarget.Sanitize(pack.Manifest.Id);
        var ns = o.Namespace ?? (intoExisting ? CodeProjectWriter.ReadNamespace(folder) ?? name : name);
        var author = PushTarget.Sanitize(pack.Manifest.Author.Length > 0 ? pack.Manifest.Author : "Unknown");
        var result = new CodeExportResult { Folder = folder, ProjectFile = intoExisting ? null : Path.Combine(folder, name + ".csproj") };
        // Everything generated goes under the optional subfolder (e.g. "Paper/Generated"); project files stay at the root.
        var root = o.Subfolder is { Length: > 0 } sub ? Path.Combine(folder, sub) : folder;

        var helper = Path.Combine(root, "Lite", "ForgeLite.g.cs");
        if (o.UseLibs)
        {
            if (File.Exists(helper))
                File.Delete(helper);
        }
        else
        {
            CodeProjectWriter.Write(result, helper, HelperSource().Replace("__NS__", ns));
        }

        SyncAssets(pack, Path.Combine(root, "Assets"), result);

        var itemsDir = Path.Combine(root, "Items");
        if (Directory.Exists(itemsDir))
            foreach (var stale in Directory.GetFiles(itemsDir, "*.g.cs"))
                File.Delete(stale);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builds = new StringBuilder();
        foreach (var recipe in pack.Recipes.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase))
        {
            var type = CodeProjectWriter.TypeName(recipe.Name ?? recipe.Id, used);
            if (o.LookOnly)
            {
                CodeProjectWriter.Write(result, Path.Combine(itemsDir, type + ".g.cs"), LookSource(ns, type, recipe, types, o));
                continue;
            }

            CodeProjectWriter.Write(result, Path.Combine(itemsDir, type + ".g.cs"), ItemSource(ns, type, recipe, types, o));
            CodeProjectWriter.WriteOnce(result, Path.Combine(root, "Customize", type + ".cs"), CustomizeStub(ns, type, recipe));
            builds.Append($"\n            Build(\"{CodeProjectWriter.Escape(recipe.Id)}\", Items.{type}.Build);");
        }

        // Looks-only: the mod registers its own items and calls Items.X.Build/Dress itself.
        if (o.LookOnly)
        {
            var registry = Path.Combine(root, "ForgeLiteItems.g.cs");
            if (File.Exists(registry))
                File.Delete(registry);
            if (intoExisting)
                CodeProjectWriter.Write(result, Path.Combine(root, "FORGE-PLAIN.md"), HookupGuide(ns, name, o));
            return result;
        }

        CodeProjectWriter.Write(result, Path.Combine(root, "ForgeLiteItems.g.cs"), $$"""
            // <auto-generated>
            // Regenerated by Drakes Asset Forge on every export. Put your code in Customize\<Item>.cs.
            // </auto-generated>
            #nullable enable
            using System;
            using Jotunn.Managers;
            {{(o.UseLibs ? "" : $"using {ns}.Lite;")}}

            namespace {{ns}};

            internal static class ForgeLiteItems
            {
                /// <summary>Call once from your plugin's Awake(). Items are built when Valheim's own prefabs are ready.</summary>
                internal static void Register(string assetsFolder)
                {
                    {{(o.UseLibs ? "// Images load from inside the DLL (or beside it) through DrakeModsLibs.Forge." : "ForgeLite.AssetsFolder = assetsFolder;")}}
                    PrefabManager.OnVanillaPrefabsAvailable += BuildAll;
                }

                private static void BuildAll()
                {
                    PrefabManager.OnVanillaPrefabsAvailable -= BuildAll;{{builds}}
                }

                private static void Build(string id, Action build)
                {
                    try
                    {
                        build();
                    }
                    catch (Exception ex)
                    {
                        Jotunn.Logger.LogError($"{id}: failed to build: {ex}");
                    }
                }
            }
            """);

        if (intoExisting)
        {
            CodeProjectWriter.Write(result, Path.Combine(root, "FORGE-PLAIN.md"), HookupGuide(ns, name, o));
        }
        else
        {
            var csproj = CodeProjectWriter.Csproj(name, ns, author, pack.Manifest.Version, "Assets");
            if (o.Embed)
                csproj = csproj.Replace("<None Include=\"Assets\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />",
                    "<!-- Images live inside the DLL: nothing to lose when a mod manager flattens folders. -->\n    <EmbeddedResource Include=\"Assets\\**\\*.png;Assets\\**\\*.jpg\" />");
            if (o.UseLibs)
                csproj = csproj.Replace("<Reference Include=\"assembly_valheim\"",
                    "<Reference Include=\"DrakeModsLibs\" HintPath=\"$(BepInExPath)\\plugins\\DrakeMods-DrakeModsLibs\\DrakeModsLibs.dll\" Private=\"false\" />\n    <Reference Include=\"assembly_valheim\"");
            CodeProjectWriter.WriteOnce(result, Path.Combine(folder, name + ".csproj"), csproj);
            CodeProjectWriter.WriteOnce(result, Path.Combine(folder, name + "Plugin.cs"), Plugin(name, ns, author, pack.Manifest.Version, o.UseLibs));
            CodeProjectWriter.WriteOnce(result, Path.Combine(folder, "environment.props"), CodeProjectWriter.EnvironmentProps(valheimPath, deployTo));
            CodeProjectWriter.WriteOnce(result, Path.Combine(folder, ".gitignore"), "bin/\nobj/\nenvironment.props\n*.user\n");
            CodeProjectWriter.WriteOnce(result, Path.Combine(folder, "README.md"), CodeProjectWriter.ReadPackFile(pack, "README.md") ?? ThunderstoreFiles.DefaultReadme(pack));
            CodeProjectWriter.Write(result, Path.Combine(folder, "manifest.json"), ThunderstoreFiles.Manifest(pack, o.UseLibs ? ThunderstoreFiles.CodeModDependencies.Append(LiteOptions.LibsDependency) : ThunderstoreFiles.CodeModDependencies));
            CodeProjectWriter.CopyIfExists(pack, "CHANGELOG.md", folder, result);
            CodeProjectWriter.CopyIfExists(pack, "icon.png", folder, result);
        }

        return result;
    }

    private static string HelperSource()
    {
        using var stream = typeof(LiteCodeWriter).Assembly.GetManifestResourceStream("ForgeLite.template")
                           ?? throw new InvalidOperationException("ForgeLite.template is missing from this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The pack's images (textures, icons), same relative paths, without the Thunderstore icon.</summary>
    private static void SyncAssets(PackProject pack, string dest, CodeExportResult result)
    {
        Directory.CreateDirectory(dest);
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in pack.ShippedFiles())
        {
            var relative = Path.GetRelativePath(pack.Root, file);
            if (relative == "icon.png" || !ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                continue;
            wanted.Add(relative);
            var to = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, true);
        }

        foreach (var stale in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
            if (!wanted.Contains(Path.GetRelativePath(dest, stale)))
                File.Delete(stale);
        result.Written.Add(dest + Path.DirectorySeparatorChar);
    }

    // ---- one item ----

    private static string ItemSource(string ns, string type, ItemRecipe recipe, LiteTypeInfo types, LiteOptions o)
    {
        var b = new StringBuilder();
        void L(string line = "") => b.Append(line.Length == 0 ? "" : "        " + line).Append('\n');

        var craft = recipe.Craft;
        var requirements = craft == null || craft.Requirements.Count == 0
            ? "System.Array.Empty<RequirementConfig>()"
            : "new[]\n            {\n" + string.Join(",\n", craft.Requirements.Select(r =>
                $"                new RequirementConfig({Str(r.Item)}, {r.Amount}, {r.AmountPerLevel}, {Bool(r.Recover)})")) + "\n            }";

        switch (recipe.Kind)
        {
            case RecipeKind.Item:
                L($"var custom = new CustomItem(Id, {Str(recipe.Base)}, new ItemConfig");
                L("{");
                L($"    Name = {NullableStr(recipe.Name)},");
                L($"    Description = {NullableStr(recipe.Description)},");
                L($"    CraftingStation = ForgeLite.Station({NullableStr(craft?.Station)}),");
                L($"    MinStationLevel = {craft?.StationLevel ?? 1},");
                L($"    Requirements = {requirements}");
                L("});");
                L("var prefab = custom.ItemPrefab;");
                break;
            case RecipeKind.Piece:
                L($"var custom = new CustomPiece(Id, {Str(recipe.Base)}, new PieceConfig");
                L("{");
                L($"    Name = {NullableStr(recipe.Name)},");
                L($"    Description = {NullableStr(recipe.Description)},");
                L($"    PieceTable = ForgeLite.PieceTable({NullableStr(craft?.Tool)}),");
                L($"    Category = {NullableStr(craft?.Category)},");
                L($"    CraftingStation = ForgeLite.Station({NullableStr(craft?.Station)}),");
                L($"    Requirements = {requirements}");
                L("});");
                L("var prefab = custom.PiecePrefab;");
                break;
            default:
                L("// Reskin: edits the vanilla prefab itself.");
                L($"var prefab = PrefabManager.Instance.GetPrefab({Str(recipe.Base)});");
                L("if (prefab == null)");
                L("{");
                L($"    Jotunn.Logger.LogWarning(\"{CodeProjectWriter.Escape(recipe.Base)} isn't in this Valheim version.\");");
                L("    return;");
                L("}");
                break;
        }

        Dress(recipe, types, l => L(l), lookOnly: false);

        L();
        L($"Customize.{type}.OnBuilt(prefab);");
        if (recipe.Kind == RecipeKind.Item)
            L("ItemManager.Instance.AddItem(custom);");
        else if (recipe.Kind == RecipeKind.Piece)
            L("PieceManager.Instance.AddPiece(custom);");

        return Dialect(ns, type, o, $$"""
            // <auto-generated>
            // "{{CodeProjectWriter.Escape(recipe.Name ?? recipe.Id)}}": {{recipe.Kind.ToString().ToLowerInvariant()}} from {{recipe.Base}}.
            // Regenerated by Drakes Asset Forge on every export; don't edit. Your code goes in Customize\{{type}}.cs.
            // </auto-generated>
            #nullable enable
            using Jotunn.Configs;
            using Jotunn.Entities;
            using Jotunn.Managers;
            using UnityEngine;
            using {{ns}}.Lite;

            namespace {{ns}}.Items;

            internal static class {{type}}
            {
                public const string Id = "{{CodeProjectWriter.Escape(recipe.Id)}}";

                internal static void Build()
                {
            {{b.ToString().TrimEnd('\n')}}
                }
            }
            """);
    }

    /// <summary>
    /// Everything after the prefab exists, in Forge's order: structure, look, names, settings, snap, fire, glow.
    /// Looks-only leaves out names (the mod sets them), the icon (exposed as <c>Icon</c>) and sprites (<c>Build</c>).
    /// </summary>
    private static void Dress(ItemRecipe recipe, LiteTypeInfo types, Action<string> line, bool lookOnly)
    {
        void L(string text = "") => line(text);

        if (recipe.RemoveComponents.Count > 0 || recipe.AddComponents.Count > 0)
        {
            L();
            L("// Components");
            foreach (var name in recipe.RemoveComponents)
                L($"ForgeLite.Remove<{ComponentType(name)}>(prefab);");
            foreach (var add in recipe.AddComponents)
                L($"ForgeLite.Add<{ComponentType(add.Type)}>(prefab);");
        }

        var look = recipe.Look;
        if (!look.IsEmpty && (!lookOnly || look.Mesh != null || look.Materials.Count > 0 || look.HideMesh || look.HasScale || look.Parts.Count > 0))
        {
            L();
            L("// Look");
            if (look.Mesh?.Prefab is { } mesh)
                L($"ForgeLite.BorrowMesh(prefab, {Str(mesh)});");
            if (look.Mesh?.File is { } file)
                L($"// TODO: model file {file} isn't supported by the plain export yet.");
            foreach (var ov in look.Materials)
                MaterialCall(ov, L);
            if (look.Icon != null && !lookOnly)
                L($"ForgeLite.Icon(prefab, {Str(look.Icon)});");
            // After materials, as in Forge: overrides never touch the sprites.
            if (look.HideMesh)
                L("ForgeLite.HideMesh(prefab);");
            // Kitbash: model scale, then other prefabs' meshes (each restyled on its own).
            if (look.HasScale)
                L($"ForgeLite.Scale(prefab, {V(look.Scale)});");
            for (var i = 0; i < look.Parts.Count; i++)
            {
                var part = look.Parts[i];
                L($"var part{i} = ForgeLite.AddPart(prefab, {Str(part.Prefab)}, {NullableStr(part.Child)}, {V(part.Position)}, {V(part.Rotation)}, {V(part.Scale)});");
                if (part.Materials.Count == 0)
                    continue;
                L($"if (part{i} != null)");
                L("{");
                foreach (var ov in part.Materials)
                    MaterialCall(ov, l => L("    " + l.Replace("\n", "\n    ")), $"part{i}");
                L("}");
            }
            foreach (var s in lookOnly ? new List<SpriteRecipe>() : look.Sprites)
                L($"ForgeLite.AddSprite(prefab.transform, {Str(s.File)}, {F(s.Width)}, {F(s.Height)}, new Vector3({F(s.Position.X)}, {F(s.Position.Y)}, {F(s.Position.Z)}), " +
                  $"new Vector3({F(s.Rotation.X)}, {F(s.Rotation.Y)}, {F(s.Rotation.Z)}), {Bool(s.DoubleSided)});");
        }

        if (!lookOnly && (recipe.Name != null || recipe.Description != null))
        {
            L();
            L("// Name");
            var drop = recipe.Kind == RecipeKind.Piece ? null : "prefab.GetComponent<ItemDrop>()";
            if (drop != null)
            {
                L($"if ({drop} is {{ }} drop)");
                L("{");
                if (recipe.Name != null)
                    L($"    drop.m_itemData.m_shared.m_name = {Str(recipe.Name)};");
                if (recipe.Description != null)
                    L($"    drop.m_itemData.m_shared.m_description = {Str(recipe.Description)};");
                L("}");
            }

            if (recipe.Kind != RecipeKind.Item)
            {
                L("if (prefab.GetComponent<Piece>() is { } piece)");
                L("{");
                if (recipe.Name != null)
                    L($"    piece.m_name = {Str(recipe.Name)};");
                if (recipe.Description != null)
                    L($"    piece.m_description = {Str(recipe.Description)};");
                L("}");
            }
        }

        if (recipe.Fields.Count > 0)
        {
            L();
            L("// Settings");
            var index = 0;
            foreach (var component in recipe.Fields)
            {
                var variable = "c" + index++;
                L($"if (prefab.GetComponent<{ComponentType(component.Key)}>() is {{ }} {variable})");
                L("{");
                foreach (var field in component.Value)
                {
                    var node = types.Field(recipe, component.Key, field.Key);
                    var literal = node == null ? null : Literal(node, field.Value);
                    if (literal == null)
                        L($"    // TODO {component.Key}.{field.Key} = {field.Value.ToJson(false)} (couldn't map this setting to C#)");
                    else
                        L($"    {variable}.{field.Key} = {literal};");
                }

                L("}");
            }
        }

        if (recipe.Snap is { Mode: not SnapMode.Keep } snap)
        {
            L();
            L("// Snap points");
            var points = string.Join(", ", snap.Points.Select(p => $"new Vector3({F(p.X)}, {F(p.Y)}, {F(p.Z)})"));
            L($"ForgeLite.SnapPoints(prefab, {Bool(snap.Mode == SnapMode.Replace)}{(points.Length > 0 ? ", " + points : "")});");
        }

        if (recipe.Effects is { IsEmpty: false } fx)
        {
            L();
            L("// Fire & lights");
            L($"ForgeLite.Effects(prefab, {ColorOrNull(fx.LightColor)}, {F(fx.LightIntensity ?? 1)}, {F(fx.LightRange ?? 1)}, {ColorOrNull(fx.FlameTint)});");
        }

        foreach (var glow in recipe.Behaviours.Where(x => x.Type.Equals("glow", StringComparison.OrdinalIgnoreCase)))
        {
            var s = glow.Settings;
            var color = s.GetValueOrDefault("color")?.AsString() ?? "#FFB066";
            var offset = s.GetValueOrDefault("offset") is { IsArray: true, Count: 3 } o
                ? $"new Vector3({F(o.Items[0].NumberValue)}, {F(o.Items[1].NumberValue)}, {F(o.Items[2].NumberValue)})"
                : "new Vector3(0f, 1f, 0f)";
            L();
            L("// Glow");
            L($"ForgeLite.Glow(prefab, {Color(color) ?? "Color.white"}, {F(s.GetValueOrDefault("intensity")?.AsNumber() ?? 1)}, " +
              $"{F(s.GetValueOrDefault("range")?.AsNumber() ?? 4)}, {offset}, {Bool(s.GetValueOrDefault("nightOnly")?.AsBool() ?? false)});");
        }
    }

    private static void MaterialCall(MaterialOverride ov, Action<string> line, string target = "prefab")
    {
        var edits = new List<string>();
        if (ov.Tint != null && Color(ov.Tint) is { } tint)
            edits.Add($"m.SetColor(\"_Color\", {tint});");
        foreach (var c in ov.Colors)
            if (Color(c.Value) is { } color)
                edits.Add(c.Key == "_EmissionColor" ? $"ForgeLite.Emission(m, {color});" : $"m.SetColor({Str(c.Key)}, {color});");
        foreach (var f in ov.Floats)
            edits.Add($"m.SetFloat({Str(f.Key)}, {F(f.Value)});");
        foreach (var t in ov.Textures)
            edits.Add($"ForgeLite.SetTexture(m, {Str(t.Key)}, {Str(t.Value)});");

        var edit = edits.Count == 0 ? "null" : "m =>\n        {\n" + string.Join("\n", edits.Select(e => "            " + e)) + "\n        }";
        var label = ov.Target == MaterialOverride.ArmorTarget ? "worn on the body" : ov.Target ?? (ov.Slot.HasValue ? $"slot {ov.Slot}" : "all materials");
        line($"// Material: {label}");
        if (ov.Target == MaterialOverride.ArmorTarget)
            line($"ForgeLite.ArmorMaterial({target}, {NullableStr(ov.FromPrefab)}, {NullableStr(ov.FromMaterial)}, {NullableStr(ov.Shader)}, {edit});");
        else
            line($"ForgeLite.Material({target}, {NullableStr(ov.Target)}, {(ov.Slot.HasValue ? ov.Slot.Value.ToString(Inv) : "null")}, " +
                 $"{NullableStr(ov.FromPrefab)}, {NullableStr(ov.FromMaterial)}, {NullableStr(ov.Shader)}, {edit});");
    }

    /// <summary>A recipe value as a C# expression of the field's type, or null when it can't be expressed.</summary>
    private static string? Literal(FieldNode node, JsonValue value)
    {
        var type = node.CSharpType;
        if (type == null)
            return null;
        switch (node.Kind)
        {
            case FieldKind.Bool:
                return value.AsBool() is { } b ? Bool(b) : null;
            case FieldKind.Text:
                return value.AsString() is { } s ? Str(s) : null;
            case FieldKind.Number:
            case FieldKind.Integer:
                if (value.AsNumber() is not { } n)
                    return null;
                return type switch
                {
                    "float" => F(n),
                    "double" => n.ToString("R", Inv) + "d",
                    "int" => ((long)n).ToString(Inv),
                    "long" => ((long)n).ToString(Inv) + "L",
                    "uint" => ((long)n).ToString(Inv) + "u",
                    _ => $"({type}){((long)n).ToString(Inv)}"
                };
            case FieldKind.Enum:
            {
                if (value.AsNumber() is { } raw)
                    return $"({type}){(long)raw}";
                var names = (value.AsString() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var options = names.Select(name => node.EnumOptions?.FirstOrDefault(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
                if (options.Any(o => o == null))
                    return null;
                return options.Count == 0 ? $"({type})0" : string.Join(" | ", options.Select(o => $"{type}.{o!.Name}"));
            }
            case FieldKind.Color:
                return Color(value.AsString());
            case FieldKind.Vector2:
            case FieldKind.Vector3:
            {
                if (!value.IsArray || value.Items.Any(i => i.AsNumber() == null))
                    return null;
                var parts = value.Items.Select(i => F(i.NumberValue)).ToList();
                return node.Kind == FieldKind.Vector3 && parts.Count == 3 ? $"new Vector3({string.Join(", ", parts)})"
                    : node.Kind == FieldKind.Vector2 && parts.Count == 2 ? $"new Vector2({string.Join(", ", parts)})"
                    : null;
            }
            default:
                return null;
        }
    }

    private static string ComponentType(string name) =>
        UnityComponents.Contains(name) ? "global::UnityEngine." + name : "global::" + name;

    private static string CustomizeStub(string ns, string type, ItemRecipe recipe) => $$"""
        using UnityEngine;

        namespace {{ns}}.Customize;

        /// <summary>
        /// Your code for "{{CodeProjectWriter.Escape(recipe.Name ?? recipe.Id)}}" ({{recipe.Id}}: {{recipe.Kind.ToString().ToLowerInvariant()}} from {{recipe.Base}}).
        /// Runs after Items\{{type}}.g.cs has built it, just before it's registered with Jotunn.
        /// Drakes Asset Forge creates this file once and never overwrites it.
        /// </summary>
        internal static class {{type}}
        {
            internal static void OnBuilt(GameObject prefab)
            {
                // prefab.GetComponent<Piece>().m_comfort = 2;
                // prefab.AddComponent<MyBehaviour>();
            }
        }
        """;

    private static string Plugin(string name, string ns, string author, string version, bool libs) => $$"""
        using System.IO;
        using BepInEx;

        namespace {{ns}};

        /// <summary>
        /// {{name}}: items from Drakes Asset Forge as plain C# (Items\), with your code in Customize\.
        /// Created once by Drakes Asset Forge; it's yours to change.
        /// </summary>
        [BepInPlugin(Guid, Name, Version)]
        [BepInDependency("com.jotunn.jotunn")]{{(libs ? "\n[BepInDependency(\"com.drakemods.libs\")]" : "")}}
        public sealed class {{name}}Plugin : BaseUnityPlugin
        {
            public const string Guid = "{{author.ToLowerInvariant()}}.{{name.ToLowerInvariant()}}";
            public const string Name = "{{name}}";
            public const string Version = "{{version}}";

            private void Awake()
            {
                ForgeLiteItems.Register(Path.Combine(Path.GetDirectoryName(Info.Location)!, "Assets"));
            }
        }
        """;

    private static string HookupGuide(string ns, string name, LiteOptions o)
    {
        var folder = o.Subfolder is { Length: > 0 } sub ? sub.Replace('/', '\\') + "\\" : "";
        var files = o.LookOnly
            ? $"`{folder}Items\\*.g.cs` (one class per recipe: `Build(parent, scale)` adds its sprites, `Dress(prefab)` the rest, `Icon`)"
            : $"`{folder}Items\\*.g.cs`, `{folder}ForgeLiteItems.g.cs`" + (o.UseLibs ? "" : $", `{folder}Lite\\ForgeLite.g.cs`");
        var register = o.LookOnly
            ? "Your own code registers the items and calls, for example, `Items.PaperSheet.Build(decor.transform, scale)` and `Items.PaperSheet.Icon`."
            : $"In your plugin's `Awake()`:\n\n```csharp\n{ns}.ForgeLiteItems.Register(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Info.Location)!, \"Assets\"));\n```";
        var images = o.Embed
            ? $"In your `.csproj`, put the images inside the DLL (safe when Hexium/Gale flatten folders):\n\n```xml\n<ItemGroup>\n  <EmbeddedResource Include=\"{folder}Assets\\**\\*.png;{folder}Assets\\**\\*.jpg\" />\n</ItemGroup>\n```"
            : $"In your `.csproj`, ship the images beside the DLL:\n\n```xml\n<ItemGroup>\n  <None Include=\"{folder}Assets\\**\\*\" CopyToOutputDirectory=\"PreserveNewest\" />\n</ItemGroup>\n```";
        var refs = o.UseLibs
            ? "References: DrakeModsLibs 0.10+ (its `DrakeModsLibs.Forge` helpers), Jotunn, assembly_valheim, UnityEngine.CoreModule."
            : "References (most Jotunn mods have them): Jotunn, assembly_valheim, UnityEngine, UnityEngine.CoreModule,\n   UnityEngine.ImageConversionModule, UnityEngine.ParticleSystemModule, UnityEngine.PhysicsModule,\n   and Valheim's `netstandard.dll` from valheim_Data\\Managed.";
        return $"""
            # Drakes Asset Forge items (plain C#)

            Regenerated on every export: {files} and `{folder}Assets\` (the pack's images).{(o.LookOnly ? "" : $" Yours, created once: `{folder}Customize\\*.cs`.")}

            1. {register}

            2. {images}

            3. {refs}

            No Forge Runtime and no pack files are needed.
            """;
    }

    /// <summary>
    /// Looks-only: for a mod that registers its items itself. Per recipe: <c>Build(parent, scale)</c> adds the sprites
    /// (sizes and positions × scale), <c>Dress(prefab)</c> applies the rest (materials, mesh, settings, snap, fire, glow),
    /// <c>Icon</c> is the recipe's icon.
    /// </summary>
    private static string LookSource(string ns, string type, ItemRecipe recipe, LiteTypeInfo types, LiteOptions o)
    {
        var build = new StringBuilder();
        foreach (var s in recipe.Look.Sprites)
            build.Append($"        ForgeLite.AddSprite(parent, {Str(s.File)}, {F(s.Width)} * scale, {F(s.Height)} * scale, " +
                         $"new Vector3({F(s.Position.X)}, {F(s.Position.Y)}, {F(s.Position.Z)}) * scale, " +
                         $"new Vector3({F(s.Rotation.X)}, {F(s.Rotation.Y)}, {F(s.Rotation.Z)}), {Bool(s.DoubleSided)});\n");

        var dress = new StringBuilder();
        Dress(recipe, types, l => dress.Append(l.Length == 0 ? "" : "        " + l).Append('\n'), lookOnly: true);
        var dressBody = dress.ToString().Trim('\n');

        var icon = recipe.Look.Icon != null
            ? $"\n\n    /// <summary>The recipe's icon ({recipe.Look.Icon}).</summary>\n    internal static Sprite? Icon => ForgeLite.IconSprite({Str(recipe.Look.Icon)});"
            : "";
        var dressMethod = dressBody.Length == 0
            ? ""
            : $"\n\n    /// <summary>Materials, mesh, settings, snap points, fire and glow on the prefab itself.</summary>\n    internal static void Dress(GameObject prefab)\n    {{\n{dressBody}\n    }}";

        return Dialect(ns, type, o, $$"""
            // <auto-generated>
            // "{{CodeProjectWriter.Escape(recipe.Name ?? recipe.Id)}}": look of {{recipe.Kind.ToString().ToLowerInvariant()}} {{recipe.Id}} (base {{recipe.Base}}).
            // Regenerated by Drakes Asset Forge on every export; don't edit. Change the recipe and export again.
            // </auto-generated>
            #nullable enable
            using UnityEngine;
            using {{ns}}.Lite;

            namespace {{ns}}.Items;

            internal static class {{type}}
            {
                public const string Id = "{{CodeProjectWriter.Escape(recipe.Id)}}";

                /// <summary>Adds the recipe's sprites under <paramref name="parent"/>; sizes and positions are multiplied by <paramref name="scale"/>.</summary>
                internal static void Build(Transform parent, float scale = 1f)
                {
            {{build.ToString().TrimEnd('\n')}}
                }{{dressMethod}}{{icon}}
            }
            """);
    }

    /// <summary>
    /// Generated code calls <c>ForgeLite.*</c> (the helper file written next to it). With "Use DrakeModsLibs" the same
    /// calls go to <c>DrakeModsLibs.Forge.ForgeLook</c>, whose image calls also take the mod's assembly.
    /// </summary>
    private static string Dialect(string ns, string type, LiteOptions o, string code)
    {
        if (!o.UseLibs)
            return code;
        code = code
            .Replace("ForgeLite.IconSprite(", "ForgeTextures.Sprite(Owner, ")
            .Replace("ForgeLite.Icon(", "ForgeLook.Icon(Owner, ")
            .Replace("ForgeLite.SetTexture(", "ForgeLook.SetTexture(Owner, ")
            .Replace("ForgeLite.AddSprite(", "ForgeLook.AddSprite(Owner, ")
            .Replace("ForgeLite.", "ForgeLook.")
            .Replace($"using {ns}.Lite;", "using System.Reflection;\nusing DrakeModsLibs.Forge;");
        // Images are looked up in this mod's DLL first (embedded), then beside it.
        // Source line endings depend on the checkout (CRLF/LF): normalise before inserting.
        code = code.Replace("\r\n", "\n");
        var marker = $"internal static class {type}\n{{\n";
        return code.Replace(marker, marker + $"    private static Assembly Owner => typeof({type}).Assembly;\n\n");
    }

    // ---- literals ----

    private static string V(Vec3 v) => $"new Vector3({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
    private static string F(double v) => ((float)v).ToString("R", Inv) + "f";
    private static string Bool(bool b) => b ? "true" : "false";
    private static string Str(string s) => "\"" + CodeProjectWriter.Escape(s).Replace("\n", "\\n").Replace("\r", "") + "\"";
    private static string NullableStr(string? s) => s == null ? "null" : Str(s);

    private static string? Color(string? hex) =>
        RecipeSerializer.TryParseColor(hex, out var c) ? $"new Color({F(c[0])}, {F(c[1])}, {F(c[2])}, {F(c[3])})" : null;

    private static string ColorOrNull(string? hex) => Color(hex) ?? "null";
}
