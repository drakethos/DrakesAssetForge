using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DrakeAssetForge.ViewModels;

namespace DrakeAssetForge.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PickOpenFileAsync = PickOpenFileAsync;
            vm.StartInitialLoad();
        }
    }

    private async Task<string?> PickOpenFileAsync(string title, IReadOnlyList<string> patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(title)
                {
                    Patterns = patterns.ToList(),
                },
            ],
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async void OnOpenValheimFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Valheim install folder",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        await vm.SetValheimPathAndReloadAsync(folder.Path.LocalPath);
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
