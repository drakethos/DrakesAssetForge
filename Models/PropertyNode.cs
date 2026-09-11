using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

/// <summary>Unity-style collapsible inspector node (group or leaf value).</summary>
public partial class PropertyNode : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _fullPath = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isGroup;

    public ObservableCollection<PropertyNode> Children { get; } = new();

    public bool IsLeaf => !IsGroup;

    public string Header =>
        IsGroup
            ? (string.IsNullOrWhiteSpace(Value) ? Name : $"{Name}  {Value}")
            : Name;
}

public static class PropertyTreeBuilder
{
    /// <summary>
    /// Builds a collapsible tree from flat dotted paths (e.g. ItemDrop.m_itemData.m_shared.m_name).
    /// </summary>
    public static ObservableCollection<PropertyNode> FromFlatRows(
        IEnumerable<FieldRow> rows,
        string? stripRootPrefix = null,
        int expandDepth = 1)
    {
        var root = new PropertyNode { Name = "(root)", IsGroup = true, IsExpanded = true };

        foreach (var row in rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
        {
            var path = row.Path;
            if (!string.IsNullOrEmpty(stripRootPrefix) &&
                path.StartsWith(stripRootPrefix + ".", StringComparison.OrdinalIgnoreCase))
            {
                path = path[(stripRootPrefix.Length + 1)..];
            }
            else if (!string.IsNullOrEmpty(stripRootPrefix) &&
                     path.Equals(stripRootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Skip the "… +N more" truncation markers as leaves under parent.
            var segments = SplitPath(path);
            if (segments.Count == 0)
                continue;

            var current = root;
            var accumulated = string.IsNullOrEmpty(stripRootPrefix) ? "" : stripRootPrefix;

            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var isLast = i == segments.Count - 1;

                string nextPath;
                if (current.Name.Equals("Array", StringComparison.OrdinalIgnoreCase) &&
                    segment.Display.StartsWith("Element ", StringComparison.Ordinal) &&
                    TryParseElementIndex(segment.Display, out var elementIndex))
                {
                    // SoftRef paths use Array[0], not Array.Array[0]
                    nextPath = $"{current.FullPath}[{elementIndex}]";
                }
                else
                {
                    nextPath = string.IsNullOrEmpty(accumulated)
                        ? segment.Raw
                        : accumulated + "." + segment.Raw;
                }

                accumulated = nextPath;

                var child = current.Children.FirstOrDefault(c =>
                    c.FullPath.Equals(accumulated, StringComparison.OrdinalIgnoreCase));

                if (child == null)
                {
                    child = new PropertyNode
                    {
                        Name = segment.Display,
                        FullPath = accumulated,
                        IsGroup = !isLast,
                        IsExpanded = i < expandDepth,
                        Value = isLast ? row.Value : "",
                    };
                    current.Children.Add(child);
                }
                else if (isLast)
                {
                    // Path exists as both group and leaf summary (e.g. Array = [14] then Array[0]…).
                    if (child.IsGroup && !string.IsNullOrWhiteSpace(row.Value))
                        child.Value = row.Value;
                    else if (!child.IsGroup)
                        child.Value = row.Value;
                }
                else
                {
                    child.IsGroup = true;
                }

                current = child;
            }
        }

        // Promote: if a node has Value like [N] and children, keep as group with badge.
        NormalizeGroups(root, expandDepth, depth: -1);
        return root.Children;
    }

    public static IEnumerable<EditableFieldRow> FlattenLeaves(IEnumerable<PropertyNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!node.IsGroup)
            {
                yield return new EditableFieldRow { Path = node.FullPath, Value = node.Value };
            }
            else
            {
                // Group-only summary rows (Array = [14]) still useful to persist.
                if (!string.IsNullOrWhiteSpace(node.Value) && node.Value.StartsWith('['))
                    yield return new EditableFieldRow { Path = node.FullPath, Value = node.Value };

                foreach (var leaf in FlattenLeaves(node.Children))
                    yield return leaf;
            }
        }
    }

    private static bool TryParseElementIndex(string display, out int index)
    {
        index = 0;
        const string prefix = "Element ";
        return display.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(display[prefix.Length..], out index);
    }

    private static void NormalizeGroups(PropertyNode node, int expandDepth, int depth)
    {
        foreach (var child in node.Children)
        {
            if (child.Children.Count > 0)
            {
                child.IsGroup = true;
                child.IsExpanded = depth + 1 < expandDepth;
            }

            NormalizeGroups(child, expandDepth, depth + 1);
        }
    }

    private static List<PathSegment> SplitPath(string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<PathSegment>(parts.Length);
        foreach (var part in parts)
        {
            // Array[0] → Array / Element 0 so the [N] summary row becomes the parent folder.
            var bracket = part.IndexOf('[');
            if (bracket >= 0 && part.EndsWith(']'))
            {
                var head = bracket == 0 ? "Array" : part[..bracket];
                var indexText = part[(bracket + 1)..^1];
                if (string.IsNullOrEmpty(head))
                    head = "Array";

                if (head.Equals("Array", StringComparison.OrdinalIgnoreCase))
                    result.Add(new PathSegment("Array", "Array"));

                if (int.TryParse(indexText, out var index))
                    result.Add(new PathSegment($"{head}[{index}]", $"Element {index}"));
                else
                    result.Add(new PathSegment(part, part));
                continue;
            }

            if (part.Equals("Array", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new PathSegment("Array", "Array"));
                continue;
            }

            if (part.Equals("…", StringComparison.Ordinal) || part.EndsWith('…'))
            {
                result.Add(new PathSegment(part, "…"));
                continue;
            }

            result.Add(new PathSegment(part, part));
        }

        return result;
    }

    private readonly record struct PathSegment(string Raw, string Display);
}
