using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DrakesForge.App.ViewModels;

namespace DrakesForge.App.Views;

public partial class WorkspaceView : UserControl
{
    public WorkspaceView() => InitializeComponent();

    /// <summary>Right-click selects the item under the cursor so Remove targets that row.</summary>
    private void OnPackListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm || e.Source is not Control source)
            return;

        var item = source.DataContext as PackListItem
                   ?? source.FindAncestorOfType<ListBoxItem>()?.DataContext as PackListItem;
        if (item != null)
            vm.Selected = item;
    }
}
