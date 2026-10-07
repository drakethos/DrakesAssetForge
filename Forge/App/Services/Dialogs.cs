using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DrakesForge.App.Services;

/// <summary>File/folder pickers. The main window registers itself; view models call these.</summary>
public static class Dialogs
{
    public static TopLevel? Owner { get; set; }

    public static async Task CopyTextAsync(string text)
    {
        if (Owner?.Clipboard is { } clipboard)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
    }

    public static async Task<string?> PasteTextAsync() =>
        Owner?.Clipboard is { } clipboard ? await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard) : null;

    public static async Task<string?> PickFolderAsync(string title)
    {
        if (Owner == null)
            return null;
        var result = await Owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
    {
        if (Owner == null)
            return null;
        var result = await Owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(filterName) { Patterns = patterns } }
        });
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }
}
