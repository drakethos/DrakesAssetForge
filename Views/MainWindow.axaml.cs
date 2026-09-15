using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using DrakeAssetForge.Services;
using DrakeAssetForge.ViewModels;

namespace DrakeAssetForge.Views;

public partial class MainWindow : Window
{
    private PixelPoint _normalPosition;
    private Size _normalSize = new(1400, 860);
    private bool _wasMaximizedBeforeMinimize;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        PropertyChanged += OnWindowPropertyChanged;
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                CaptureNormalBounds();
        };
        Resized += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                CaptureNormalBounds();
        };
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        RestoreWindowPlacement();

        if (DataContext is MainViewModel vm)
        {
            vm.PickOpenFileAsync = PickOpenFileAsync;
            vm.PickOpenFilesAsync = PickOpenFilesAsync;
            vm.PickSaveFileAsync = PickSaveFileAsync;
            vm.PickFolderAsync = PickFolderAsync;
            vm.ConfirmAsync = ConfirmAsync;
            vm.StartInitialLoad();
        }
    }

    private void RestoreWindowPlacement()
    {
        var saved = AppSettings.TryLoadWindowState();
        if (saved == null)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        var width = Math.Clamp(saved.Width, 800, 10000);
        var height = Math.Clamp(saved.Height, 500, 10000);
        Width = width;
        Height = height;
        _normalSize = new Size(width, height);
        _normalPosition = new PixelPoint((int)Math.Round(saved.X), (int)Math.Round(saved.Y));

        if (IsPositionOnAnyScreen(_normalPosition, width, height))
            Position = _normalPosition;
        else
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

        WindowState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
        _wasMaximizedBeforeMinimize = saved.Maximized;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            var oldState = e.OldValue is WindowState os ? os : WindowState.Normal;
            var newState = e.NewValue is WindowState ns ? ns : WindowState;

            if (oldState == WindowState.Normal && newState == WindowState.Maximized)
                CaptureNormalBounds();
            if (newState == WindowState.Minimized)
                _wasMaximizedBeforeMinimize = oldState == WindowState.Maximized;
            if (newState == WindowState.Normal)
                CaptureNormalBounds();
            return;
        }
    }

    private void CaptureNormalBounds()
    {
        if (WindowState != WindowState.Normal)
            return;
        _normalPosition = Position;
        _normalSize = new Size(Width, Height);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e) => SaveWindowPlacement();

    private void SaveWindowPlacement()
    {
        CaptureNormalBounds();

        var maximized = WindowState switch
        {
            WindowState.Maximized => true,
            WindowState.Minimized => _wasMaximizedBeforeMinimize,
            _ => false,
        };

        AppSettings.SaveWindowState(new AppSettings.WindowStateSettings
        {
            X = _normalPosition.X,
            Y = _normalPosition.Y,
            Width = _normalSize.Width > 0 ? _normalSize.Width : 1400,
            Height = _normalSize.Height > 0 ? _normalSize.Height : 860,
            Maximized = maximized,
        });
    }

    private bool IsPositionOnAnyScreen(PixelPoint position, double width, double height)
    {
        try
        {
            var screens = Screens?.All;
            if (screens == null || screens.Count == 0)
                return true;

            var rect = new PixelRect(position, new PixelSize((int)Math.Max(1, width), (int)Math.Max(1, height)));
            foreach (var screen in screens)
            {
                if (screen.WorkingArea.Intersects(rect))
                    return true;
            }
        }
        catch
        {
            return true;
        }

        return false;
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

    private async Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, IReadOnlyList<string> patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(title)
                {
                    Patterns = patterns.ToList(),
                },
            ],
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Cast<string>()
            .ToList();
    }

    private async Task<string?> PickSaveFileAsync(string title, IReadOnlyList<string> patterns, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType(title)
                {
                    Patterns = patterns.Select(p => p.StartsWith("*.", StringComparison.Ordinal) || p.StartsWith('.')
                        ? (p.StartsWith('*') ? p : "*" + p)
                        : "*." + p.TrimStart('.')).ToList(),
                },
            ],
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var accepted = false;
        var dialog = new Window
        {
            Title = "Drakes Asset Forge",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var yes = new Button { Content = "Delete", MinWidth = 88 };
        var no = new Button { Content = "Cancel", MinWidth = 88 };
        yes.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        no.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { no, yes },
                },
            },
        };
        await dialog.ShowDialog(this);
        return accepted;
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
