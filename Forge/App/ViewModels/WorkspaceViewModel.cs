using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format;

namespace DrakesForge.App.ViewModels;

public sealed partial class PackListItem : ObservableObject
{
    public PackListItem(ItemRecipe recipe) => Recipe = recipe;

    public ItemRecipe Recipe { get; }
    [ObservableProperty] private Bitmap? _icon;
    public string Title => Recipe.Name ?? (Recipe.Kind == RecipeKind.Reskin ? $"{Recipe.Base} (reskin)" : Recipe.Id);
    public string Subtitle => Recipe.Kind == RecipeKind.Reskin ? $"reskin of {Recipe.Base}" : $"{Recipe.Base} · {Recipe.Id}";
    public string Group => Recipe.Kind switch { RecipeKind.Piece => "Pieces", RecipeKind.Item => "Items", _ => "Reskins" };

    public void Renamed()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}

/// <summary>Step 3: the pack's items on the left, viewport in the middle, inspector on the right.</summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public WorkspaceViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<PackListItem> Items { get; } = new();
    public ObservableCollection<VanillaTile> Sources { get; } = new();
    public ObservableCollection<string> Files { get; } = new();

    [ObservableProperty] private PackListItem? _selected;
    [ObservableProperty] private ItemEditorViewModel? _editor;
    [ObservableProperty] private string _libraryTab = "Pack";
    /// <summary>The material/mesh picker, when open (shown over the workspace).</summary>
    [ObservableProperty] private AssetPickerViewModel? _picker;

    public bool LibPack => LibraryTab == "Pack";
    public bool LibSources => LibraryTab == "Sources";
    public bool LibFiles => LibraryTab == "Files";
    public bool IsEmpty => Items.Count == 0;

    partial void OnLibraryTabChanged(string value)
    {
        OnPropertyChanged(nameof(LibPack));
        OnPropertyChanged(nameof(LibSources));
        OnPropertyChanged(nameof(LibFiles));
        if (value == "Files")
            ReloadFiles();
    }

    public void Reload(ItemRecipe? select = null)
    {
        Editor?.SaveNow();
        Items.Clear();
        Sources.Clear();
        var pack = _main.Pack;
        if (pack == null)
        {
            Editor = null;
            return;
        }

        foreach (var recipe in pack.Recipes.OrderBy(r => r.Kind).ThenBy(r => r.Name ?? r.Id))
        {
            var item = new PackListItem(recipe);
            Items.Add(item);
            LoadIcon(item);
        }

        if (_main.Vanilla != null)
            foreach (var name in pack.Sources)
                if (_main.Vanilla.Catalog.ByName.TryGetValue(name, out var entry))
                {
                    var tile = new VanillaTile(entry, _main.Browse);
                    tile.EnsureIcon(_main.Vanilla);
                    Sources.Add(tile);
                }

        ReloadFiles();
        OnPropertyChanged(nameof(IsEmpty));
        Selected = Items.FirstOrDefault(i => i.Recipe == select) ?? Items.FirstOrDefault();
    }

    private void LoadIcon(PackListItem item)
    {
        var pack = _main.Pack;
        if (item.Recipe.Look.Icon != null && Services.Images.LoadFile(pack?.FullPath(item.Recipe.Look.Icon)) is { } custom)
        {
            item.Icon = Services.Images.ToBitmap(custom);
            return;
        }

        if (_main.Vanilla?.Catalog.ByName.TryGetValue(item.Recipe.Base, out var entry) == true)
            _main.Vanilla.RequestIcon(entry, b => item.Icon = b);
    }

    private void ReloadFiles()
    {
        Files.Clear();
        var pack = _main.Pack;
        if (pack == null)
            return;
        foreach (var file in pack.ShippedFiles().Select(f => Path.GetRelativePath(pack.Root, f).Replace('\\', '/')).Where(f => !f.StartsWith("items/") && f != ForgePack.FileName).OrderBy(f => f))
            Files.Add(file);
    }

    partial void OnSelectedChanged(PackListItem? value)
    {
        Editor?.SaveNow();
        Editor = value != null ? new ItemEditorViewModel(value.Recipe, _main) : null;
    }

    public void RecipeRenamed(ItemRecipe recipe)
    {
        var item = Items.FirstOrDefault(i => i.Recipe == recipe);
        if (item == null)
            return;
        item.Renamed();
        LoadIcon(item);
    }

    [RelayCommand]
    private void SetLibrary(string tab) => LibraryTab = tab;

    [RelayCommand]
    private void AddMore() => _main.GoTo(Step.Browse);

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Selected == null || _main.Pack == null)
            return;
        _main.Pack.DeleteRecipe(Selected.Recipe);
        _main.RecipeSaved($"removed {Selected.Recipe.Id}");
        Reload();
    }

    [RelayCommand]
    private void RemoveSource(VanillaTile tile)
    {
        _main.Pack?.Sources.Remove(tile.Name);
        _main.Pack?.SaveState();
        Sources.Remove(tile);
    }
}
