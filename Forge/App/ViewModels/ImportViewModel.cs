using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

public enum ImportMode
{
    NewItem,
    Reskin,
    Source
}

public sealed partial class ImportRow : ObservableObject
{
    private readonly ImportViewModel _owner;

    public ImportRow(VanillaEntry entry, ImportMode mode, string name, string id, ImportViewModel owner)
    {
        Entry = entry;
        _mode = mode;
        _name = name;
        _id = id;
        _owner = owner;
    }

    public VanillaEntry Entry { get; }
    public string Subtitle => Entry.Category.Length == 0 ? Entry.Kind.ToString() : $"{Entry.Kind} · {Entry.Category}";
    public bool IsPiece => Entry.Kind == VanillaKind.Piece;
    /// <summary>Filled in once the prefab is read (cost to copy, snap points).</summary>
    public PrefabInfo? Info { get; set; }

    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private ImportMode _mode;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _id;
    [ObservableProperty] private bool _copyCost = true;
    [ObservableProperty] private string _costText = "";

    public bool IsNew
    {
        get => Mode == ImportMode.NewItem;
        set { if (value) Mode = ImportMode.NewItem; }
    }

    public bool IsReskin
    {
        get => Mode == ImportMode.Reskin;
        set { if (value) Mode = ImportMode.Reskin; }
    }

    public bool IsSource
    {
        get => Mode == ImportMode.Source;
        set { if (value) Mode = ImportMode.Source; }
    }

    public string Help => Mode switch
    {
        ImportMode.NewItem => "A new item with its own ID, starting as a copy you can change.",
        ImportMode.Reskin => "Changes how the vanilla one looks. No new item; every copy in the world changes.",
        _ => "Not an item. Keep it in the pack to borrow its mesh or materials."
    };

    partial void OnModeChanged(ImportMode value)
    {
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(IsReskin));
        OnPropertyChanged(nameof(IsSource));
        OnPropertyChanged(nameof(Help));
        _owner.UpdateSummary();
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveRow(this);
}

/// <summary>Step 2: decide what each shortlisted prefab becomes, then write the recipes.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public ImportViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<ImportRow> Rows { get; } = new();

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _title = "";

    public void Reload()
    {
        Rows.Clear();
        var pack = _main.Pack;
        var vanilla = _main.Vanilla;
        if (pack == null || vanilla == null)
            return;

        Title = $"Import {pack.WorkingList.Count} into {pack.Manifest.Name}";
        var prefix = Regex.Replace(pack.Manifest.Id, "[^A-Za-z0-9]", "").ToLowerInvariant();
        foreach (var name in pack.WorkingList)
        {
            if (!vanilla.Catalog.ByName.TryGetValue(name, out var entry))
                continue;
            var row = new ImportRow(entry, ImportMode.NewItem, Pretty(name), UniqueId($"{prefix}_{name.ToLowerInvariant()}"), this);
            Rows.Add(row);
            vanilla.RequestIcon(entry, b => row.Icon = b);
            _ = FillInfo(row, vanilla);
        }

        UpdateSummary();
    }

    private static async Task FillInfo(ImportRow row, Services.VanillaService vanilla)
    {
        try
        {
            var preview = await vanilla.LoadPreviewAsync(row.Entry);
            row.Info = preview.Info;
            if (preview.Info.PieceCost is { } cost)
                row.CostText = $"Copy build cost: {string.Join(", ", cost.Resources.Select(r => $"{r.Item} ×{r.Amount}"))} at {cost.Station ?? "no station"}";
        }
        catch (Exception)
        {
            // the row still imports; cost just isn't copied
        }
    }

    public void UpdateSummary()
    {
        var n = Rows.Count(r => r.Mode == ImportMode.NewItem);
        var re = Rows.Count(r => r.Mode == ImportMode.Reskin);
        var src = Rows.Count(r => r.Mode == ImportMode.Source);
        Summary = $"{n} new · {re} reskin{(re == 1 ? "" : "s")} · {src} source{(src == 1 ? "" : "s")}";
    }

    public void RemoveRow(ImportRow row)
    {
        Rows.Remove(row);
        UpdateSummary();
    }

    [RelayCommand]
    private void Back() => _main.GoTo(Step.Browse);

    [RelayCommand]
    private void ImportAll()
    {
        var pack = _main.Pack;
        if (pack == null || Rows.Count == 0)
            return;

        ItemRecipe? first = null;
        foreach (var row in Rows)
        {
            switch (row.Mode)
            {
                case ImportMode.Source:
                    if (!pack.Sources.Contains(row.Entry.Name))
                        pack.Sources.Add(row.Entry.Name);
                    break;
                case ImportMode.Reskin:
                {
                    var recipe = new ItemRecipe { Id = row.Entry.Name, Base = row.Entry.Name, Kind = RecipeKind.Reskin };
                    if (pack.Recipes.All(r => r.Id != recipe.Id))
                    {
                        pack.SaveRecipe(recipe);
                        first ??= recipe;
                    }

                    break;
                }
                default:
                {
                    var recipe = new ItemRecipe
                    {
                        Id = UniqueId(Regex.Replace(row.Id.Trim(), "[^A-Za-z0-9_]", "_"), againstRows: false),
                        Base = row.Entry.Name,
                        Kind = row.IsPiece ? RecipeKind.Piece : RecipeKind.Item,
                        Name = string.IsNullOrWhiteSpace(row.Name) ? null : row.Name.Trim()
                    };
                    if (row.IsPiece)
                        recipe.Craft = CraftFor(row);
                    pack.SaveRecipe(recipe);
                    first ??= recipe;
                    break;
                }
            }

            pack.WorkingList.Remove(row.Entry.Name);
        }

        pack.SaveState();
        _main.Browse.Reload();
        _main.Workspace.Reload(first);
        _main.GoTo(Step.Workspace);
        _main.RecipeSaved($"{Rows.Count} imported");
    }

    /// <summary>New pieces need a hammer entry; copy the base's cost when asked so they aren't free.</summary>
    private static CraftRecipe CraftFor(ImportRow row)
    {
        var craft = new CraftRecipe { Tool = "Hammer", Category = "Misc" };
        if (row.Info?.PieceCost is { } cost)
        {
            craft.Category = cost.Category;
            if (row.CopyCost)
            {
                craft.Station = cost.Station;
                craft.Requirements.AddRange(cost.Resources.Select(r => new Requirement { Item = r.Item, Amount = r.Amount, AmountPerLevel = r.PerLevel, Recover = r.Recover }));
            }
        }

        return craft;
    }

    private string UniqueId(string id, bool againstRows = true)
    {
        var pack = _main.Pack;
        var taken = new HashSet<string>(pack?.Recipes.Select(r => r.Id) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        if (againstRows)
            taken.UnionWith(Rows.Select(r => r.Id));
        if (!taken.Contains(id) && _main.Vanilla?.Catalog.ByName.ContainsKey(id) != true)
            return id;
        for (var i = 2; ; i++)
            if (!taken.Contains($"{id}_{i}"))
                return $"{id}_{i}";
    }

    /// <summary>"iron_grate" → "Iron Grate", "SwordBronze" → "Sword Bronze".</summary>
    public static string Pretty(string prefab)
    {
        var spaced = Regex.Replace(prefab.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
        return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
