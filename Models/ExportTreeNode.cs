using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

/// <summary>
/// Export checklist node. Folders cascade IncludeInExport to children; children can still be toggled individually.
/// </summary>
public partial class ExportTreeNode : ObservableObject
{
    private bool _suppressCascade;

    public ExportTreeNode(string name, string groupPath, OwnedItemDocument? item = null, string status = "")
    {
        Name = name;
        GroupPath = groupPath ?? "";
        Item = item;
        Status = status;
        EditSubtitle = status;
    }

    public string Name { get; }
    public string GroupPath { get; }
    public OwnedItemDocument? Item { get; }
    public ExportTreeNode? Parent { get; private set; }
    public bool IsFolder => Item == null;
    public bool IsItem => Item != null;
    public string Status { get; private set; }
    public ObservableCollection<ExportTreeNode> Children { get; } = new();

    /// <summary>True / false / null (mixed) for folder checkboxes.</summary>
    [ObservableProperty]
    private bool? _isChecked = true;

    [ObservableProperty]
    private bool _isExpanded = true;

    public string EditSubtitle { get; private set; }

    public bool IsSuppressingCascade => _suppressCascade;

    /// <summary>Folder checkboxes cascade when the user toggles them (not when syncing from children).</summary>
    public bool ShouldCascadeCheck => IsFolder;

    public string Subtitle =>
        IsFolder
            ? DescribeFolderState()
            : (string.IsNullOrWhiteSpace(Status) ? "item" : Status);

    public void AddChild(ExportTreeNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public void SetStatus(string status)
    {
        Status = status;
        EditSubtitle = status;
        OnPropertyChanged(nameof(Subtitle));
    }

    public void RefreshSubtitle() => OnPropertyChanged(nameof(Subtitle));

    public IEnumerable<ExportTreeNode> EnumerateItems()
    {
        if (IsItem)
        {
            yield return this;
            yield break;
        }

        foreach (var child in Children)
        {
            foreach (var nested in child.EnumerateItems())
                yield return nested;
        }
    }

    public IEnumerable<ExportTreeNode> EnumerateAll()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var nested in child.EnumerateAll())
                yield return nested;
        }
    }

    public void RefreshFolderCheckFromChildren()
    {
        if (!IsFolder || Children.Count == 0)
            return;

        _suppressCascade = true;
        try
        {
            var states = Children.Select(GetEffectiveCheck).ToList();
            if (states.All(s => s == true))
                IsChecked = true;
            else if (states.All(s => s == false))
                IsChecked = false;
            else
                IsChecked = null;
        }
        finally
        {
            _suppressCascade = false;
        }

        OnPropertyChanged(nameof(Subtitle));
        Parent?.RefreshFolderCheckFromChildren();
    }

    public void ApplyCheckCascade(bool value)
    {
        _suppressCascade = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _suppressCascade = false;
        }

        foreach (var child in Children)
            child.ApplyCheckCascade(value);

        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_suppressCascade || IsItem || value is null)
            return;

        foreach (var child in Children)
            child.ApplyCheckCascade(value.Value);

        OnPropertyChanged(nameof(Subtitle));
    }

    private static bool? GetEffectiveCheck(ExportTreeNode node)
    {
        if (node.IsItem)
            return node.IsChecked == true;
        if (node.Children.Count == 0)
            return node.IsChecked;
        var states = node.Children.Select(GetEffectiveCheck).ToList();
        if (states.All(s => s == true))
            return true;
        if (states.All(s => s == false))
            return false;
        return null;
    }

    private string DescribeFolderState()
    {
        var items = EnumerateItems().ToList();
        var on = items.Count(i => i.IsChecked == true);
        var mixed = IsChecked is null ? " · mixed" : "";
        return $"{on}/{items.Count} included{mixed}";
    }
}
