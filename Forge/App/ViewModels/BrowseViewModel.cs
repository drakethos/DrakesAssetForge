using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public sealed partial class VanillaTile : ObservableObject
{
    private readonly BrowseViewModel _owner;

    public VanillaTile(VanillaEntry entry, BrowseViewModel owner)
    {
        Entry = entry;
        _owner = owner;
    }

    public VanillaEntry Entry { get; }
    public string Name => Entry.Name;
    public string Subtitle => Entry.Category.Length == 0 ? Entry.Kind.ToString() : Entry.Category;

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private bool _inList;
    private bool _iconKnown;

    public string ToggleGlyph => InList ? "✓" : "+";
    partial void OnInListChanged(bool value) => OnPropertyChanged(nameof(ToggleGlyph));

    /// <summary>Asks again each time the tile is shown, which bumps it to the front of the icon queue.</summary>
    public void EnsureIcon(Services.VanillaService vanilla)
    {
        if (_iconKnown)
            return;
        vanilla.RequestIcon(Entry, bitmap =>
        {
            _iconKnown = true;
            Icon = bitmap;
        });
    }

    [RelayCommand]
    private void Toggle() => _owner.Toggle(this);
}

public sealed class CategoryOption
{
    public required string Label { get; init; }
    public VanillaKind? Kind { get; init; }
    public string? Category { get; init; }
    public bool IsHeader { get; init; }
    public int Count { get; init; }
    public string CountText => IsHeader ? "" : Count.ToString("N0");
}

/// <summary>Step 1: browse the player's Valheim, preview anything, shortlist what to build from.</summary>
public sealed partial class BrowseViewModel : ObservableObject
{
    private const int MaxResults = 240;

    private readonly MainViewModel _main;
    private readonly Dictionary<string, VanillaTile> _tiles = new();

    public BrowseViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<CategoryOption> Categories { get; } = new();
    public ObservableCollection<VanillaTile> Results { get; } = new();
    public ObservableCollection<VanillaTile> WorkingList { get; } = new();

    [ObservableProperty] private CategoryOption? _selectedCategory;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _resultNote = "";
    [ObservableProperty] private VanillaTile? _selected;
    [ObservableProperty] private VanillaPreviewViewModel? _preview;

    public string ImportLabel => $"Import {WorkingList.Count} into {_main.Pack?.Manifest.Name ?? "pack"}…";
    public bool HasWorkingList => WorkingList.Count > 0;

    public void Reload()
    {
        Categories.Clear();
        Results.Clear();
        WorkingList.Clear();
        _tiles.Clear();
        Preview = null;
        var vanilla = _main.Vanilla;
        if (vanilla == null)
            return;

        var entries = vanilla.Catalog.Entries;
        Categories.Add(new CategoryOption { Label = "Everything", Count = entries.Count });
        foreach (var kind in new[] { VanillaKind.Piece, VanillaKind.Item })
        {
            var ofKind = entries.Where(e => e.Kind == kind).ToList();
            Categories.Add(new CategoryOption { Label = kind == VanillaKind.Piece ? "PIECES" : "ITEMS", IsHeader = true });
            Categories.Add(new CategoryOption { Label = kind == VanillaKind.Piece ? "All pieces" : "All items", Kind = kind, Count = ofKind.Count });
            foreach (var g in ofKind.Where(e => e.Category.Length > 0).GroupBy(e => e.Category, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()))
                Categories.Add(new CategoryOption { Label = Capitalize(g.Key), Kind = kind, Category = g.Key, Count = g.Count() });
        }

        foreach (var name in _main.Pack?.WorkingList ?? new List<string>())
            if (vanilla.Catalog.ByName.TryGetValue(name, out var entry))
            {
                var tile = Tile(entry);
                tile.InList = true;
                WorkingList.Add(tile);
            }

        SelectedCategory = Categories.FirstOrDefault(c => c.Kind == VanillaKind.Piece && c.Category == null) ?? Categories[0];
        NotifyList();
    }

    partial void OnSelectedCategoryChanged(CategoryOption? value)
    {
        if (value is { IsHeader: true })
        {
            SelectedCategory = Categories[Categories.IndexOf(value) + 1];
            return;
        }

        Filter();
    }

    partial void OnSearchChanged(string value) => Filter();

    partial void OnSelectedChanged(VanillaTile? value)
    {
        if (value != null && _main.Vanilla != null)
            Preview = new VanillaPreviewViewModel(value.Entry, _main.Vanilla);
    }

    private void Filter()
    {
        var vanilla = _main.Vanilla;
        if (vanilla == null)
            return;

        var cat = SelectedCategory;
        var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matching = vanilla.Catalog.Entries
            .Where(e => terms.All(t => e.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var matches = matching
            .Where(e => cat?.Kind == null || e.Kind == cat.Kind)
            .Where(e => cat?.Category == null || string.Equals(e.Category, cat.Category, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Searching in the wrong category shouldn't look like "doesn't exist".
        var widened = matches.Count == 0 && terms.Length > 0 && matching.Count > 0;
        if (widened)
            matches = matching;

        Results.Clear();
        foreach (var entry in matches.Take(MaxResults))
        {
            var tile = Tile(entry);
            Results.Add(tile);
            tile.EnsureIcon(vanilla);
        }

        ResultNote = matches.Count > MaxResults
            ? $"Showing {MaxResults} of {matches.Count:N0}. Type to narrow it down."
            : widened
                ? $"Nothing in {cat?.Label}; showing {matches.Count:N0} from everything"
                : $"{matches.Count:N0} prefabs";
        if (Selected == null && Results.Count > 0)
            Selected = Results[0];
    }

    private VanillaTile Tile(VanillaEntry entry)
    {
        if (!_tiles.TryGetValue(entry.Name, out var tile))
        {
            tile = new VanillaTile(entry, this) { InList = _main.Pack?.WorkingList.Contains(entry.Name) == true };
            _tiles[entry.Name] = tile;
            if (_main.Vanilla != null)
                tile.EnsureIcon(_main.Vanilla);
        }

        return tile;
    }

    public void Toggle(VanillaTile tile)
    {
        var pack = _main.Pack;
        if (pack == null)
            return;

        if (tile.InList)
        {
            pack.WorkingList.Remove(tile.Name);
            WorkingList.Remove(tile);
        }
        else
        {
            pack.WorkingList.Add(tile.Name);
            WorkingList.Add(tile);
        }

        tile.InList = !tile.InList;
        pack.SaveState();
        NotifyList();
    }

    [RelayCommand]
    private void ToggleSelected()
    {
        if (Selected != null)
            Toggle(Selected);
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        if (Preview != null)
            Preview.Mode = mode;
    }

    [RelayCommand]
    private void StartImport() => _main.GoTo(Step.Import);

    private void NotifyList()
    {
        OnPropertyChanged(nameof(ImportLabel));
        OnPropertyChanged(nameof(HasWorkingList));
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
