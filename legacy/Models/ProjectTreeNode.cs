using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

/// <summary>
/// One row in the Project tree. Folders are art-bundle style groups; leaves are owned items.
/// </summary>
public partial class ProjectTreeNode : ObservableObject
{
    private static readonly IBrush DropBrush = SolidColorBrush.Parse("#443D8BFF");
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public ProjectTreeNode(string name, string groupPath, OwnedItemDocument? item = null)
    {
        Name = name;
        GroupPath = groupPath ?? "";
        Item = item;
        EditName = name;
    }

    public string Name { get; private set; }
    public string GroupPath { get; }
    public OwnedItemDocument? Item { get; }
    public bool IsFolder => Item == null;
    public bool IsItem => Item != null;
    public ObservableCollection<ProjectTreeNode> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropTargetBrush))]
    private bool _isDropTarget;

    public IBrush DropTargetBrush => IsDropTarget ? DropBrush : TransparentBrush;

    public string Subtitle =>
        IsFolder
            ? (Children.Count == 0 ? "empty group" : $"{Children.Count} item(s)")
            : Item!.ArtBadgeDisplay;

    public string KindLabel => IsFolder ? "Group" : "Item";

    public void SetName(string name)
    {
        Name = name;
        EditName = name;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Subtitle));
    }

    public void RefreshSubtitle() => OnPropertyChanged(nameof(Subtitle));
}
