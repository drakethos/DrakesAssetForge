using Mono.Cecil;

namespace DrakeAssetForge.Services;

/// <summary>
/// Maps SoftRef MonoBehaviour field signatures → Valheim type names via assembly_valheim.dll.
/// </summary>
public sealed class ValheimScriptNameResolver : IDisposable
{
    private readonly List<(string Name, string FullName, string Assembly, HashSet<string> Fields)> _monoBehaviours = new();
    private readonly List<(string Name, string FullName, string Assembly, HashSet<string> Fields)> _scriptableObjects = new();

    public ValheimScriptNameResolver(string managedDirectory)
    {
        foreach (var dll in new[] { "assembly_valheim.dll", "Assembly-CSharp.dll" })
        {
            var path = Path.Combine(managedDirectory, dll);
            if (!File.Exists(path))
                continue;
            try
            {
                LoadAssembly(path, Path.GetFileNameWithoutExtension(dll));
            }
            catch
            {
                // skip unreadable
            }
        }
    }

    public string? ResolveMonoBehaviourName(IReadOnlyCollection<string> customFieldNames) =>
        Resolve(_monoBehaviours, customFieldNames);

    public string? ResolveScriptableObjectName(IReadOnlyCollection<string> customFieldNames) =>
        Resolve(_scriptableObjects, customFieldNames);

    public void Dispose()
    {
        _monoBehaviours.Clear();
        _scriptableObjects.Clear();
    }

    private void LoadAssembly(string path, string assemblyName)
    {
        using var asm = AssemblyDefinition.ReadAssembly(path);
        foreach (var type in asm.MainModule.Types)
        {
            var kind = GetUnityBaseKind(type);
            if (kind == null)
                continue;

            var fields = GetSerializedFieldNames(type);
            if (fields.Count == 0)
                continue;

            var entry = (type.Name, type.FullName, assemblyName, fields);
            if (kind == "MB")
                _monoBehaviours.Add(entry);
            else
                _scriptableObjects.Add(entry);
        }
    }

    private static string? Resolve(
        List<(string Name, string FullName, string Assembly, HashSet<string> Fields)> catalog,
        IReadOnlyCollection<string> customFieldNames)
    {
        if (customFieldNames.Count == 0)
            return null;

        var observed = new HashSet<string>(customFieldNames, StringComparer.Ordinal);
        (string Name, int Score)? best = null;

        foreach (var entry in catalog)
        {
            if (!entry.Fields.SetEquals(observed))
            {
                // Allow soft match when type fields are a subset (base-class padding) or equal after ignore.
                if (!entry.Fields.IsSubsetOf(observed) && !observed.IsSubsetOf(entry.Fields))
                    continue;
                if (entry.Fields.Count == 0)
                    continue;
            }

            // Prefer exact set equality, then largest overlap.
            var overlap = entry.Fields.Count(observed.Contains);
            var score = entry.Fields.SetEquals(observed)
                ? 1_000_000 + overlap
                : overlap * 10 - Math.Abs(entry.Fields.Count - observed.Count);

            if (best == null || score > best.Value.Score)
                best = (entry.Name, score);
        }

        // Require a strong match so random Unity leftovers don't get wrong labels.
        if (best == null || best.Value.Score < customFieldNames.Count)
            return null;

        return best.Value.Name;
    }

    private static HashSet<string> GetSerializedFieldNames(TypeDefinition type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t != null; t = ResolveBase(t))
        {
            if (t.Name is "MonoBehaviour" or "ScriptableObject" or "Behaviour" or "Component" or "Object")
                break;

            foreach (var field in t.Fields)
            {
                if (!IsUnitySerialized(field))
                    continue;
                names.Add(field.Name);
            }
        }

        return names;
    }

    private static bool IsUnitySerialized(FieldDefinition field)
    {
        if (field.IsStatic || field.IsLiteral || field.IsInitOnly)
            return false;
        if (field.IsNotSerialized)
            return false;
        if (field.IsPublic)
            return true;
        return field.CustomAttributes.Any(a =>
            a.AttributeType.Name is "SerializeField" or "SerializeReference");
    }

    private static string? GetUnityBaseKind(TypeDefinition type)
    {
        try
        {
            var b = type.BaseType;
            while (b != null)
            {
                if (b.Name == "MonoBehaviour")
                    return "MB";
                if (b.Name == "ScriptableObject")
                    return "SO";
                var res = b.Resolve();
                if (res == null)
                    return null;
                b = res.BaseType;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static TypeDefinition? ResolveBase(TypeDefinition type)
    {
        try
        {
            return type.BaseType?.Resolve();
        }
        catch
        {
            return null;
        }
    }
}
