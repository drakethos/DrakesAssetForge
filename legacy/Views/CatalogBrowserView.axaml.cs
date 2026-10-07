using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DrakeAssetForge.Models;
using DrakeAssetForge.ViewModels;

namespace DrakeAssetForge.Views;

public partial class CatalogBrowserView : UserControl
{
    public CatalogBrowserView()
    {
        InitializeComponent();
    }

    private void OnCatalogLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.Row.DataContext is SoftRefAssetEntry asset)
            vm.RequestCatalogThumbnail(asset);
    }

    private void OnCatalogGridContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not DataGrid)
            return;

        if (e.Source is Control source)
        {
            var row = source.FindAncestorOfType<DataGridRow>();
            if (row?.DataContext is SoftRefAssetEntry asset)
                vm.SelectedAsset = asset;
        }
    }

    private void OnPinsContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || e.Source is not Control source)
            return;

        var item = source.DataContext as SoftRefAssetEntry
                   ?? source.FindAncestorOfType<ListBoxItem>()?.DataContext as SoftRefAssetEntry;
        if (item == null)
            return;

        vm.SelectedPin = item;
        vm.SelectedAsset = item;
        vm.RequestCatalogThumbnail(item);
    }
}
