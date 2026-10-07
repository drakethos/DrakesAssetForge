using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format;
using DrakesForge.Format.Json;

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

    // Duplicate / copy / paste -------------------------------------------------------------------

    private const string ClipboardKind = "drakesAssetForge.recipe";

    /// <summary>A copy of the selected item under a new id, selected for editing. A reskin's copy becomes a new item/piece.</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (Selected == null || _main.Pack == null)
            return;
        Add(Clone(Selected.Recipe), $"duplicated {Selected.Recipe.Id}");
    }

    /// <summary>Puts the selected item on the clipboard (as recipe JSON, with where its files live).</summary>
    [RelayCommand]
    private async Task Copy()
    {
        if (Selected == null || _main.Pack == null)
            return;
        var wrapper = JsonValue.NewObject()
            .Set("kind", ClipboardKind)
            .Set("packRoot", _main.Pack.Root)
            .Set("recipe", JsonValue.Parse(RecipeSerializer.WriteRecipe(Selected.Recipe)));
        await Services.Dialogs.CopyTextAsync(wrapper.ToJson());
        _main.Status = $"Copied {Selected.Title}. Paste here or in another pack (Ctrl+V).";
    }

    /// <summary>Adds the item on the clipboard (from this or another pack, or a plain recipe JSON) under a free id.</summary>
    [RelayCommand]
    private async Task Paste()
    {
        var pack = _main.Pack;
        if (pack == null || await Services.Dialogs.PasteTextAsync() is not { } text)
            return;
        JsonValue json;
        try
        {
            json = JsonValue.Parse(text);
        }
        catch (FormatException)
        {
            _main.Status = "The clipboard doesn't hold an item (copy one in the pack list first).";
            return;
        }

        var recipeJson = json["kind"]?.AsString() == ClipboardKind ? json["recipe"] : json;
        if (recipeJson is not { IsObject: true } || recipeJson["id"] == null || recipeJson["base"] == null)
        {
            _main.Status = "The clipboard doesn't hold an item (copy one in the pack list first).";
            return;
        }

        var problems = new List<string>();
        var recipe = RecipeSerializer.ReadRecipe(recipeJson.ToJson(), problems);
        var from = json["packRoot"]?.AsString();
        var copied = from != null && !string.Equals(Path.GetFullPath(from), Path.GetFullPath(pack.Root), StringComparison.OrdinalIgnoreCase)
            ? CopyFiles(recipe, from, pack)
            : 0;
        var clash = pack.Recipes.Any(r => r.Id == recipe.Id);
        Add(clash ? Clone(recipe) : recipe, $"pasted {recipe.Id}{(copied > 0 ? $" with {copied} file(s)" : "")}");
    }

    private void Add(ItemRecipe recipe, string what)
    {
        _main.Pack!.SaveRecipe(recipe);
        _main.RecipeSaved(what);
        Reload(recipe);
    }

    /// <summary>A deep copy with a free id ("_copy", "_copy2"…) and "(copy)" on its name.</summary>
    private ItemRecipe Clone(ItemRecipe source)
    {
        var copy = RecipeSerializer.ReadRecipe(RecipeSerializer.WriteRecipe(source), new List<string>());
        copy.SourcePath = null;
        if (copy.Kind == RecipeKind.Reskin)
        {
            // Two reskins of one prefab would fight; the copy becomes its own item or piece.
            var isPiece = _main.Vanilla?.Catalog.ByName.TryGetValue(copy.Base, out var entry) == true && entry.Kind == Valheim.VanillaKind.Piece;
            copy.Kind = isPiece ? RecipeKind.Piece : RecipeKind.Item;
            copy.Name ??= copy.Base;
        }

        var stem = System.Text.RegularExpressions.Regex.Replace(copy.Id, "_copy\\d*$", "");
        var id = stem + "_copy";
        for (var n = 2; _main.Pack!.Recipes.Any(r => r.Id == id) || File.Exists(Path.Combine(_main.Pack.Root, ForgePack.ItemsFolder, id + ".json")); n++)
            id = stem + "_copy" + n;
        copy.Id = id;
        if (copy.Name != null && !copy.Name.EndsWith("(copy)", StringComparison.Ordinal))
            copy.Name += " (copy)";
        return copy;
    }

    /// <summary>Brings the PNGs a pasted recipe uses from its own pack (same paths; existing files are kept).</summary>
    private static int CopyFiles(ItemRecipe recipe, string fromRoot, Services.PackProject to)
    {
        var files = new List<string?> { recipe.Look.Icon };
        files.AddRange(recipe.Look.Materials.SelectMany(m => m.Textures.Values));
        files.AddRange(recipe.Look.Sprites.Select(s => s.File));
        files.AddRange(recipe.Look.Parts.SelectMany(p => p.Materials).SelectMany(m => m.Textures.Values));
        var count = 0;
        foreach (var relative in files.Where(f => !string.IsNullOrEmpty(f)).Distinct())
        {
            var source = Path.Combine(fromRoot, relative!.Replace('/', Path.DirectorySeparatorChar));
            var target = to.FullPath(relative)!;
            if (!File.Exists(source) || File.Exists(target))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            count++;
        }

        return count;
    }

    [RelayCommand]
    private void RemoveSource(VanillaTile tile)
    {
        _main.Pack?.Sources.Remove(tile.Name);
        _main.Pack?.SaveState();
        Sources.Remove(tile);
    }
}
