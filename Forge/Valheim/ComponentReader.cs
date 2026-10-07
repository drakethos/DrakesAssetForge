using System.Text.RegularExpressions;
using AssetsTools.NET;
using Mono.Cecil;

namespace DrakesForge.Valheim;

public enum FieldKind
{
    Integer,
    Number,
    Bool,
    Text,
    Enum,
    Color,
    Vector2,
    Vector3,
    Group,
    /// <summary>Arrays and lists: shown read-only (count) for now.</summary>
    List,
    /// <summary>Links to other assets (effects, prefabs, sprites): read-only.</summary>
    Reference
}

public sealed record EnumOption(string Name, long Value);

/// <summary>One serialized field of a Valheim script, with its vanilla value and enough type info for a proper editor.</summary>
public sealed class FieldNode
{
    public required string Name { get; init; }
    /// <summary>Dotted path from the component, as recipes store it ("m_itemData.m_shared.m_maxDurability").</summary>
    public required string Path { get; init; }
    public required string Label { get; init; }
    public required FieldKind Kind { get; init; }
    /// <summary>C# type name from the game DLL, for tooltips.</summary>
    public required string TypeName { get; init; }
    /// <summary>double (numbers/enums), bool, string, or float[] (colors/vectors).</summary>
    public object? Value { get; init; }
    public IReadOnlyList<EnumOption>? EnumOptions { get; init; }
    public bool IsFlags { get; init; }
    public IReadOnlyList<FieldNode> Children { get; init; } = Array.Empty<FieldNode>();
    public int Count { get; init; }
}

public sealed class ComponentInfo
{
    public required string Name { get; init; }
    public required IReadOnlyList<FieldNode> Fields { get; init; }
}

/// <summary>
/// Turns a MonoBehaviour's serialized data into <see cref="FieldNode"/>s. Values come from the bundle;
/// types (enums and their names, flags) come from assembly_valheim via Mono.Cecil.
/// </summary>
internal sealed class ComponentReader
{
    private const int MaxDepth = 6;
    private static readonly HashSet<string> EngineFields = new() { "m_GameObject", "m_Enabled", "m_Script", "m_Name", "m_EditorHideFlags", "m_EditorClassIdentifier" };

    private readonly ModuleDefinition? _module;

    public ComponentReader(string managedDirectory)
    {
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDirectory);
            var path = Path.Combine(managedDirectory, "assembly_valheim.dll");
            if (File.Exists(path))
                _module = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver }).MainModule;
        }
        catch (Exception)
        {
            // without type info, enums show as numbers
        }
    }

    public IReadOnlyList<FieldNode> Read(string scriptName, AssetTypeValueField behaviour) =>
        ReadChildren(behaviour, FindType(scriptName), "", 0);

    private TypeDefinition? FindType(string name)
    {
        if (_module == null)
            return null;
        return _module.GetType(name) ?? _module.Types.FirstOrDefault(t => t.Name == name);
    }

    private List<FieldNode> ReadChildren(AssetTypeValueField parent, TypeDefinition? type, string prefix, int depth)
    {
        var nodes = new List<FieldNode>();
        foreach (var field in parent.Children)
        {
            if (depth == 0 && EngineFields.Contains(field.FieldName))
                continue;
            if (Read(field, type, prefix, depth) is { } node)
                nodes.Add(node);
        }

        return nodes;
    }

    private FieldNode? Read(AssetTypeValueField field, TypeDefinition? owner, string prefix, int depth)
    {
        var name = field.FieldName;
        var path = prefix.Length == 0 ? name : prefix + "." + name;
        var def = FindField(owner, name);
        var typeName = def?.FieldType.Name ?? field.TypeName;
        var template = field.TemplateField;

        FieldNode Node(FieldKind kind, object? value = null, IReadOnlyList<FieldNode>? children = null, int count = 0, IReadOnlyList<EnumOption>? options = null, bool flags = false) => new()
        {
            Name = name, Path = path, Label = Labelize(name), Kind = kind, TypeName = typeName, Value = value,
            Children = children ?? Array.Empty<FieldNode>(), Count = count, EnumOptions = options, IsFlags = flags
        };

        if (field.TypeName.StartsWith("PPtr<", StringComparison.Ordinal))
            return Node(FieldKind.Reference, field["m_PathID"].AsLong != 0 ? "linked" : "none");

        if (template.IsArray || (field.Children.Count == 1 && field.Children[0].TemplateField.IsArray))
        {
            var array = template.IsArray ? field : field.Children[0];
            return Node(FieldKind.List, count: array.AsArray.size);
        }

        switch (field.TypeName)
        {
            case "ColorRGBA":
                return Node(FieldKind.Color, new[] { field["r"].AsFloat, field["g"].AsFloat, field["b"].AsFloat, field["a"].AsFloat });
            case "Vector3f":
                return Node(FieldKind.Vector3, new[] { field["x"].AsFloat, field["y"].AsFloat, field["z"].AsFloat });
            case "Vector2f":
                return Node(FieldKind.Vector2, new[] { field["x"].AsFloat, field["y"].AsFloat });
        }

        // Unity often stores bools as a byte; the game's own type says what it really is.
        if (typeName == "Boolean" && template.ValueType is AssetValueType.Bool or AssetValueType.UInt8 or AssetValueType.Int8)
            return Node(FieldKind.Bool, field.AsLong != 0 || (template.ValueType == AssetValueType.Bool && field.AsBool));

        switch (template.ValueType)
        {
            case AssetValueType.Bool:
                return Node(FieldKind.Bool, field.AsBool);
            case AssetValueType.String:
                return Node(FieldKind.Text, field.AsString);
            case AssetValueType.Float:
                // Round-trip the float's own digits: 0.8f, not 0.800000011920929.
                return Node(FieldKind.Number, double.Parse(field.AsFloat.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture));
            case AssetValueType.Double:
                return Node(FieldKind.Number, field.AsDouble);
            case AssetValueType.Int8:
            case AssetValueType.UInt8:
            case AssetValueType.Int16:
            case AssetValueType.UInt16:
            case AssetValueType.Int32:
            case AssetValueType.UInt32:
            case AssetValueType.Int64:
            case AssetValueType.UInt64:
            {
                var value = (double)field.AsLong;
                var enumType = Resolve(def?.FieldType);
                if (enumType is { IsEnum: true })
                {
                    var options = enumType.Fields.Where(f => f.HasConstant).Select(f => new EnumOption(f.Name, Convert.ToInt64(f.Constant))).ToList();
                    var flags = enumType.CustomAttributes.Any(a => a.AttributeType.Name == "FlagsAttribute");
                    return Node(FieldKind.Enum, value, options: options, flags: flags);
                }

                return Node(FieldKind.Integer, value);
            }
        }

        if (field.Children.Count > 0 && depth < MaxDepth)
        {
            var children = ReadChildren(field, Resolve(def?.FieldType), path, depth + 1);
            return children.Count == 0 ? null : Node(FieldKind.Group, children: children);
        }

        return null;
    }

    private static FieldDefinition? FindField(TypeDefinition? type, string name)
    {
        for (var t = type; t != null; t = Resolve(t.BaseType))
            if (t.Fields.FirstOrDefault(f => f.Name == name && !f.IsStatic) is { } f)
                return f;
        return null;
    }

    private static TypeDefinition? Resolve(TypeReference? reference)
    {
        try
        {
            return reference?.Resolve();
        }
        catch (Exception)
        {
            return null; // UnityEngine types etc. that aren't next to assembly_valheim
        }
    }

    /// <summary>"m_maxDurability" → "Max durability", "m_itemData" → "Item data".</summary>
    public static string Labelize(string field)
    {
        var name = field.StartsWith("m_", StringComparison.Ordinal) ? field[2..] : field;
        name = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ").Replace('_', ' ').Trim();
        if (name.Length == 0)
            return field;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i == 0 ? char.ToUpperInvariant(w[0]) + w[1..] : (w.All(char.IsUpper) ? w : w.ToLowerInvariant()));
        return string.Join(' ', words);
    }
}
