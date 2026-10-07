using System;
using System.Collections.Generic;
using System.Linq;
using DrakesForge.Format;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// Removes and adds components on the prefab ("a ward without warding" = remove PrivateArea).
/// Done once when the item is built: removed components can't be restored by a hot reload, so changing
/// these lists needs a game restart.
/// </summary>
internal static class ComponentChanges
{
    private static readonly Dictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);

    public static void Apply(GameObject prefab, ItemRecipe recipe, List<string> warnings)
    {
        foreach (var name in recipe.RemoveComponents)
        {
            var type = Resolve(name);
            if (type == null)
            {
                warnings.Add($"components.remove: no component type '{name}'");
                continue;
            }

            var found = prefab.GetComponents(type);
            if (found.Length == 0)
            {
                warnings.Add($"components.remove: the prefab has no {name}");
                continue;
            }

            foreach (var component in found)
                UnityEngine.Object.DestroyImmediate(component);
            if (prefab.GetComponent(type) != null)
                warnings.Add($"components.remove: Unity kept {name} (another component requires it); remove that one too");
        }

        foreach (var add in recipe.AddComponents)
        {
            var type = Resolve(add.Type);
            if (type == null)
            {
                warnings.Add($"components.add: no component type '{add.Type}'");
                continue;
            }

            if (prefab.GetComponent(type) == null)
                prefab.AddComponent(type);
        }
    }

    /// <summary>"Rigidbody", "BoxCollider", "Container", "Vagon"… across Unity and every loaded mod/game assembly.</summary>
    public static Type? Resolve(string name)
    {
        if (TypeCache.TryGetValue(name, out var cached))
            return cached;

        Type? found = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            found = types.FirstOrDefault(t => t != null && (t.Name == name || t.FullName == name) && typeof(Component).IsAssignableFrom(t) && !t.IsAbstract);
            // Prefer the game's own and Unity's types over a mod's same-named one.
            if (found != null && (assembly.GetName().Name is "assembly_valheim" || assembly.GetName().Name.StartsWith("UnityEngine", StringComparison.Ordinal)))
                break;
        }

        TypeCache[name] = found;
        return found;
    }
}
