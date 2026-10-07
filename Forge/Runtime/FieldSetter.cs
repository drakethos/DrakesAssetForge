using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DrakesForge.Format;
using DrakesForge.Format.Json;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>
/// Sets component fields from recipe "fields": { "WearNTear": { "m_health": 1200 } }.
/// Dotted paths reach nested data ("m_itemData.m_shared.m_damages.m_slash"); structs are written back.
/// </summary>
internal static class FieldSetter
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Apply(GameObject prefab, Dictionary<string, Dictionary<string, JsonValue>> fields, List<string> warnings)
    {
        foreach (var component in fields)
        {
            var target = FindComponent(prefab, component.Key);
            if (target == null)
            {
                warnings.Add($"fields.{component.Key}: the prefab has no {component.Key} component");
                continue;
            }

            foreach (var field in component.Value)
            {
                try
                {
                    SetPath(target, field.Key.Split('.'), 0, field.Value);
                }
                catch (Exception ex)
                {
                    warnings.Add($"fields.{component.Key}.{field.Key}: {ex.Message}");
                }
            }
        }
    }

    private static Component? FindComponent(GameObject prefab, string typeName) =>
        prefab.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName)
        ?? prefab.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == typeName);

    // Returns obj so a boxed struct can be assigned back to its parent field.
    private static object SetPath(object obj, string[] path, int index, JsonValue value)
    {
        var field = FindField(obj.GetType(), path[index]);
        if (field == null && index == path.Length - 1)
        {
            // Unity components (Rigidbody.mass, BoxCollider.size, Light.color) expose properties, not fields.
            var property = obj.GetType().GetProperty(path[index], BindingFlags.Instance | BindingFlags.Public);
            if (property is { CanWrite: true })
            {
                property.SetValue(obj, Convert(value, property.PropertyType));
                return obj;
            }
        }

        if (field == null)
            throw new MissingFieldException($"{obj.GetType().Name} has no field '{path[index]}'");

        if (index == path.Length - 1)
        {
            field.SetValue(obj, Convert(value, field.FieldType));
            return obj;
        }

        var child = field.GetValue(obj) ?? throw new NullReferenceException($"'{path[index]}' is null");
        var updated = SetPath(child, path, index + 1, value);
        if (field.FieldType.IsValueType)
            field.SetValue(obj, updated);
        return obj;
    }

    private static FieldInfo? FindField(Type? type, string name)
    {
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, Flags);
            if (field != null)
                return field;
        }

        return null;
    }

    private static object? Convert(JsonValue value, Type type)
    {
        if (type == typeof(string))
            return value.AsString() ?? throw Mismatch(value, type);
        if (type == typeof(bool))
            return value.AsBool() ?? throw Mismatch(value, type);

        if (type.IsEnum)
        {
            if (value.AsString() is { } name)
                return Enum.Parse(type, name, true);
            if (value.AsNumber() is { } n)
                return Enum.ToObject(type, (int)n);
            throw Mismatch(value, type);
        }

        if (type.IsPrimitive)
        {
            var number = value.AsNumber() ?? throw Mismatch(value, type);
            return System.Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
        }

        if (type == typeof(Color))
        {
            if (RecipeSerializer.TryParseColor(value.AsString(), out var c))
                return new Color(c[0], c[1], c[2], c[3]);
            throw Mismatch(value, type);
        }

        if (type == typeof(Vector3))
        {
            if (value.IsArray && value.Count == 3 && value.Items.All(i => i.AsNumber() != null))
                return new Vector3((float)value.Items[0].NumberValue, (float)value.Items[1].NumberValue, (float)value.Items[2].NumberValue);
            throw Mismatch(value, type);
        }

        if (type == typeof(Vector2))
        {
            if (value.IsArray && value.Count == 2 && value.Items.All(i => i.AsNumber() != null))
                return new Vector2((float)value.Items[0].NumberValue, (float)value.Items[1].NumberValue);
            throw Mismatch(value, type);
        }

        throw new NotSupportedException($"can't set a {type.Name} from a recipe yet");
    }

    private static FormatException Mismatch(JsonValue value, Type type) =>
        new($"expected {Describe(type)}, got {value.ToJson(false)}");

    private static string Describe(Type type)
    {
        if (type.IsEnum)
            return $"one of {string.Join(", ", Enum.GetNames(type))}";
        if (type == typeof(Color))
            return "a color like \"#FF8800\"";
        if (type == typeof(Vector3))
            return "[x, y, z]";
        return type.Name;
    }
}
