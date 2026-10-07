using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

/// <summary>One row on the Export screen checklist (which items ship to the mod + hooks).</summary>
public partial class ExportItemRow : ObservableObject
{
    public ExportItemRow(OwnedItemDocument item, bool includeInExport, string status)
    {
        Item = item;
        Id = item.Id;
        DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Id : item.DisplayName;
        GroupPath = item.GroupPath;
        Status = status;
        _includeInExport = includeInExport;
    }

    public OwnedItemDocument Item { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public string GroupPath { get; }
    public string Status { get; }

    public string GroupLabel =>
        string.IsNullOrWhiteSpace(GroupPath) ? "(root)" : GroupPath;

    [ObservableProperty]
    private bool _includeInExport;
}
