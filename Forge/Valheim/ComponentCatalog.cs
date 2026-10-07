using Mono.Cecil;

namespace DrakesForge.Valheim;

/// <summary>
/// What can be added to a prefab: every Valheim script in assembly_valheim (fields read from the type definition,
/// with type defaults since a new component has no saved values yet) and the common Unity components.
/// </summary>
public static class ComponentCatalog
{
    /// <summary>Unity components worth adding, with the settings people actually change.</summary>
    public static readonly IReadOnlyList<string> UnityComponents = new[] { "Rigidbody", "BoxCollider", "SphereCollider", "CapsuleCollider", "MeshCollider", "Light" };

    public static IReadOnlyList<string> ValheimScripts(AssetSession session) =>
        session.Run(s => s.Components.MonoBehaviourNames());

    /// <summary>Editable settings of a component that isn't on the prefab yet.</summary>
    public static IReadOnlyList<FieldNode> Defaults(AssetSession session, string type)
    {
        if (UnityDefaults(type) is { } unity)
            return unity;
        return session.Run(s => s.Components.Defaults(type));
    }

    private static IReadOnlyList<FieldNode>? UnityDefaults(string type)
    {
        FieldNode N(string name, FieldKind kind, object value, string typeName) =>
            new()
            {
                Name = name, Path = name, Label = ComponentReader.Labelize(name), Kind = kind, Value = value, TypeName = typeName,
                CSharpType = typeName switch { "Single" => "float", "Boolean" => "bool", "Int32" => "int", var t => "global::UnityEngine." + t }
            };
        var trigger = N("isTrigger", FieldKind.Bool, false, "Boolean");
        var center = N("center", FieldKind.Vector3, new[] { 0f, 0f, 0f }, "Vector3");
        return type switch
        {
            "Rigidbody" => new[]
            {
                N("mass", FieldKind.Number, 1.0, "Single"), N("drag", FieldKind.Number, 0.0, "Single"),
                N("angularDrag", FieldKind.Number, 0.05, "Single"), N("useGravity", FieldKind.Bool, true, "Boolean"),
                N("isKinematic", FieldKind.Bool, false, "Boolean")
            },
            "BoxCollider" => new[] { trigger, center, N("size", FieldKind.Vector3, new[] { 1f, 1f, 1f }, "Vector3") },
            "SphereCollider" => new[] { trigger, center, N("radius", FieldKind.Number, 0.5, "Single") },
            "CapsuleCollider" => new[]
            {
                trigger, center, N("radius", FieldKind.Number, 0.5, "Single"), N("height", FieldKind.Number, 2.0, "Single"),
                N("direction", FieldKind.Integer, 1.0, "Int32")
            },
            "MeshCollider" => new[] { trigger, N("convex", FieldKind.Bool, false, "Boolean") },
            "Light" => new[]
            {
                N("color", FieldKind.Color, new[] { 1f, 0.85f, 0.6f, 1f }, "Color"), N("intensity", FieldKind.Number, 1.0, "Single"),
                N("range", FieldKind.Number, 6.0, "Single")
            },
            _ => null
        };
    }
}

internal sealed partial class ComponentReader
{
    private List<string>? _scriptNames;

    public IReadOnlyList<string> MonoBehaviourNames()
    {
        if (_scriptNames != null)
            return _scriptNames;
        _scriptNames = new List<string>();
        if (_module == null)
            return _scriptNames;
        foreach (var type in _module.Types)
            if (!type.IsAbstract && type.IsClass && !type.HasGenericParameters && DerivesFromMonoBehaviour(type))
                _scriptNames.Add(type.Name);
        _scriptNames.Sort(StringComparer.OrdinalIgnoreCase);
        return _scriptNames;
    }

    private static bool DerivesFromMonoBehaviour(TypeDefinition type)
    {
        for (var t = type.BaseType; t != null; t = Resolve(t)?.BaseType)
            if (t.FullName == "UnityEngine.MonoBehaviour")
                return true;
        return false;
    }

    public IReadOnlyList<FieldNode> Defaults(string typeName)
    {
        var type = FindType(typeName);
        return type == null ? Array.Empty<FieldNode>() : DefaultChildren(type, "", 0);
    }

    private List<FieldNode> DefaultChildren(TypeDefinition type, string prefix, int depth)
    {
        var nodes = new List<FieldNode>();
        // Base classes first, the way Unity serializes them.
        var chain = new List<TypeDefinition>();
        for (var t = type; t != null && !t.FullName.StartsWith("UnityEngine.", StringComparison.Ordinal) && t.FullName != "System.Object"; t = Resolve(t.BaseType))
            chain.Insert(0, t);

        foreach (var t in chain)
        {
            var initial = FieldInitializers(t);
            foreach (var field in t.Fields)
            {
                if (field.IsStatic || field.IsNotSerialized || field.HasConstant)
                    continue;
                if (!field.IsPublic && !field.CustomAttributes.Any(a => a.AttributeType.Name == "SerializeField"))
                    continue;
                if (DefaultNode(field, prefix, depth, initial.TryGetValue(field.Name, out var v) ? v : null) is { } node)
                    nodes.Add(node);
            }
        }

        return nodes;
    }

    /// <summary>
    /// Field initializers ("public float m_baseMass = 20f;") compile into the constructor, so read the
    /// constants it stores: numbers, bools, strings, enums and new Vector3/Color(...) literals.
    /// </summary>
    private static Dictionary<string, object> FieldInitializers(TypeDefinition type)
    {
        var values = new Dictionary<string, object>();
        var ctor = type.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && !m.HasParameters);
        if (ctor?.HasBody != true)
            return values;
        var stack = new List<object?>();
        foreach (var ins in ctor.Body.Instructions)
        {
            var code = ins.OpCode.Code;
            switch (code)
            {
                case Mono.Cecil.Cil.Code.Ldc_R4: stack.Add((double)(float)ins.Operand); break;
                case Mono.Cecil.Cil.Code.Ldc_R8: stack.Add((double)ins.Operand); break;
                case Mono.Cecil.Cil.Code.Ldstr: stack.Add((string)ins.Operand); break;
                case Mono.Cecil.Cil.Code.Ldc_I4: stack.Add((double)(int)ins.Operand); break;
                case Mono.Cecil.Cil.Code.Ldc_I4_S: stack.Add((double)(sbyte)ins.Operand); break;
                case Mono.Cecil.Cil.Code.Ldc_I4_M1: stack.Add(-1.0); break;
                case >= Mono.Cecil.Cil.Code.Ldc_I4_0 and <= Mono.Cecil.Cil.Code.Ldc_I4_8:
                    stack.Add((double)(code - Mono.Cecil.Cil.Code.Ldc_I4_0)); break;
                case Mono.Cecil.Cil.Code.Newobj when ins.Operand is MethodReference m &&
                                                     m.DeclaringType.FullName is "UnityEngine.Vector3" or "UnityEngine.Color" or "UnityEngine.Vector2" &&
                                                     m.Parameters.Count <= stack.Count:
                {
                    var args = stack.Skip(stack.Count - m.Parameters.Count).ToList();
                    stack.RemoveRange(stack.Count - m.Parameters.Count, m.Parameters.Count);
                    if (args.All(a => a is double))
                    {
                        var f = args.Select(a => (float)(double)a!).ToList();
                        if (m.DeclaringType.Name == "Color" && f.Count == 3)
                            f.Add(1f);
                        stack.Add(f.ToArray());
                    }
                    else
                        stack.Add(null);
                    break;
                }
                case Mono.Cecil.Cil.Code.Stfld when ins.Operand is FieldReference fr:
                    if (stack.Count > 0 && stack[^1] is { } value && fr.DeclaringType.FullName == type.FullName)
                        values[fr.Name] = value;
                    stack.Clear();
                    break;
                case Mono.Cecil.Cil.Code.Ldarg_0:
                case Mono.Cecil.Cil.Code.Nop:
                    break;
                default:
                    stack.Clear(); // anything else (method calls, new lists): not a plain constant
                    break;
            }
        }

        return values;
    }

    private FieldNode? DefaultNode(FieldDefinition field, string prefix, int depth, object? initial)
    {
        var path = prefix.Length == 0 ? field.Name : prefix + "." + field.Name;
        var reference = field.FieldType;
        FieldNode Node(FieldKind kind, object? value, IReadOnlyList<FieldNode>? children = null, IReadOnlyList<EnumOption>? options = null, bool flags = false) => new()
        {
            Name = field.Name, Path = path, Label = Labelize(field.Name), Kind = kind, TypeName = reference.Name, Value = value, CSharpType = CSharpName(reference),
            Children = children ?? Array.Empty<FieldNode>(), EnumOptions = options, IsFlags = flags
        };

        if (reference.IsArray || reference.Name.StartsWith("List`", StringComparison.Ordinal))
            return Node(FieldKind.List, null);

        switch (reference.FullName)
        {
            case "System.Boolean": return Node(FieldKind.Bool, initial is double b && b != 0);
            case "System.String": return Node(FieldKind.Text, initial as string ?? "");
            case "System.Single":
            case "System.Double": return Node(FieldKind.Number, initial as double? ?? 0.0);
            case "System.Int32":
            case "System.Int64":
            case "System.Int16":
            case "System.Byte":
            case "System.UInt32": return Node(FieldKind.Integer, initial as double? ?? 0.0);
            case "UnityEngine.Color": return Node(FieldKind.Color, initial as float[] ?? new[] { 0f, 0f, 0f, 0f });
            case "UnityEngine.Vector3": return Node(FieldKind.Vector3, initial as float[] ?? new[] { 0f, 0f, 0f });
            case "UnityEngine.Vector2": return Node(FieldKind.Vector2, initial as float[] ?? new[] { 0f, 0f });
        }

        var definition = Resolve(reference);
        if (definition == null)
            return null;
        if (definition.IsEnum)
        {
            var options = definition.Fields.Where(f => f.HasConstant).Select(f => new EnumOption(f.Name, Convert.ToInt64(f.Constant))).ToList();
            return Node(FieldKind.Enum, initial as double? ?? 0.0, options: options,
                flags: definition.CustomAttributes.Any(a => a.AttributeType.Name == "FlagsAttribute"));
        }

        // Links to assets/other components (prefabs, sprites, effects): not editable here.
        for (var t = definition; t != null; t = Resolve(t.BaseType))
            if (t.FullName == "UnityEngine.Object")
                return Node(FieldKind.Reference, "none");

        if (depth < 4 && !definition.IsPrimitive && definition.CustomAttributes.Any(a => a.AttributeType.Name == "SerializableAttribute"))
        {
            var children = DefaultChildren(definition, path, depth + 1);
            return children.Count == 0 ? null : Node(FieldKind.Group, null, children);
        }

        return null;
    }
}
