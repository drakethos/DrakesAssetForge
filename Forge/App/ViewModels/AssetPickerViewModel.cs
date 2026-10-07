using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public enum PickerMode
{
    /// <summary>Pick a prefab, then one of its materials.</summary>
    Material,
    /// <summary>Pick a prefab whose mesh to show.</summary>
    Mesh,
    /// <summary>Pick a chest/leg armour whose body look (the material painted on the player) to use.</summary>
    ArmorLook
}

/// <summary>
/// Browse every Valheim prefab with its preview and pick a material (or mesh) from it.
/// Opens over the workspace; nothing is copied, the recipe just names what was picked.
/// </summary>
public sealed partial class AssetPickerViewModel : ObservableObject
{
    private const int MaxResults = 200;

    private readonly MainViewModel _main;
    private readonly Action<string, string?> _onPick;
    private readonly Dictionary<string, VanillaTile> _tiles = new();

    public AssetPickerViewModel(MainViewModel main, PickerMode mode, string title, Action<string, string?> onPick)
    {
        _main = main;
        Mode = mode;
        Title = title;
        _onPick = onPick;
        _scope = mode == PickerMode.ArmorLook ? "Items" : main.Pack?.Sources.Count > 0 ? "Sources" : "All";
        Filter();
    }

    public PickerMode Mode { get; }
    public string Title { get; }
    public bool IsMaterialMode => Mode == PickerMode.Material;
    public bool IsMeshMode => Mode == PickerMode.Mesh;
    public bool IsArmorMode => Mode == PickerMode.ArmorLook;
    public string PickLabel => Mode switch { PickerMode.Material => "Use this material", PickerMode.Mesh => "Use this mesh", _ => "Use this body look" };
    public string Hint => Mode switch
    {
        PickerMode.Material => "Pick a prefab, then one of its materials. Its texture, color and shader replace this slot's.",
        PickerMode.Mesh => "Pick a prefab whose shape to show. Its own materials come with it; restyle them afterwards.",
        _ => "Pick a chest or leg armour. Its body textures (what's painted on the player when worn) replace this one's."
    };

    public ObservableCollection<VanillaTile> Results { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _scope;
    [ObservableProperty] private VanillaTile? _selected;
    [ObservableProperty] private VanillaPreviewViewModel? _preview;
    [ObservableProperty] private MaterialRow? _selectedMaterial;
    [ObservableProperty] private string _note = "";

    public bool ScopeAll => Scope == "All";
    public bool ScopePieces => Scope == "Pieces";
    public bool ScopeItems => Scope == "Items";
    public bool ScopeSources => Scope == "Sources";
    public bool CanPick => Selected != null && Mode switch
    {
        PickerMode.Material => SelectedMaterial != null,
        PickerMode.ArmorLook => Preview?.HasArmor == true,
        _ => true
    };

    partial void OnSearchChanged(string value) => Filter();

    partial void OnScopeChanged(string value)
    {
        OnPropertyChanged(nameof(ScopeAll));
        OnPropertyChanged(nameof(ScopePieces));
        OnPropertyChanged(nameof(ScopeItems));
        OnPropertyChanged(nameof(ScopeSources));
        Filter();
    }

    partial void OnSelectedChanged(VanillaTile? value)
    {
        SelectedMaterial = null;
        Preview = value != null && _main.Vanilla != null ? new VanillaPreviewViewModel(value.Entry, _main.Vanilla) : null;
        if (Preview != null)
            Preview.PropertyChanged += (_, e) =>
            {
                // Pre-select when there's only one material to choose.
                if (e.PropertyName == nameof(VanillaPreviewViewModel.Materials) && Preview?.Materials.Count == 1)
                    SelectedMaterial = Preview.Materials[0];
                if (e.PropertyName == nameof(VanillaPreviewViewModel.HasArmor))
                    OnPropertyChanged(nameof(CanPick));
            };
        OnPropertyChanged(nameof(CanPick));
    }

    partial void OnSelectedMaterialChanged(MaterialRow? value) => OnPropertyChanged(nameof(CanPick));

    private void Filter()
    {
        var vanilla = _main.Vanilla;
        Results.Clear();
        if (vanilla == null)
            return;

        var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<VanillaEntry> pool = Scope switch
        {
            "Pieces" => vanilla.Catalog.Entries.Where(e => e.Kind == VanillaKind.Piece),
            "Items" when IsArmorMode => vanilla.Catalog.Entries.Where(e => e.Kind == VanillaKind.Item && e.Category.Equals("armor", StringComparison.OrdinalIgnoreCase)),
            "Items" => vanilla.Catalog.Entries.Where(e => e.Kind == VanillaKind.Item),
            "Sources" => (_main.Pack?.Sources ?? new List<string>()).Select(n => vanilla.Catalog.ByName.GetValueOrDefault(n)).Where(e => e != null).Select(e => e!),
            _ => vanilla.Catalog.Entries
        };
        var matches = pool.Where(e => terms.All(t => e.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var entry in matches.Take(MaxResults))
        {
            if (!_tiles.TryGetValue(entry.Name, out var tile))
                _tiles[entry.Name] = tile = new VanillaTile(entry, _main.Browse);
            tile.EnsureIcon(vanilla);
            Results.Add(tile);
        }

        Note = matches.Count > MaxResults ? $"{MaxResults} of {matches.Count:N0}, type to narrow" : $"{matches.Count:N0} prefabs";
        if (Scope == "Sources" && matches.Count == 0 && terms.Length == 0)
            Note = "No sources in this pack yet. Import something as \"Source only\", or switch to All.";
    }

    [RelayCommand]
    private void SetScope(string scope) => Scope = scope;

    [RelayCommand]
    private void Pick()
    {
        if (!CanPick)
            return;
        _onPick(Selected!.Name, IsMaterialMode ? SelectedMaterial!.Name : null);
        _main.Workspace.Picker = null;
    }

    [RelayCommand]
    private void Cancel() => _main.Workspace.Picker = null;
}
