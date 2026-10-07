using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.Format.Json;
using DrakesForge.Valheim;

namespace DrakesForge.App.ViewModels;

/// <summary>A script on the prefab: its settings plus Remove (with an "are you sure?").</summary>
public sealed partial class ComponentCard : FieldGroup
{
    private readonly ComponentsPanel _owner;

    public ComponentCard(ComponentsPanel owner, string name, string? subtitle, bool expanded, bool isAdded) : base(name, subtitle, expanded)
    {
        _owner = owner;
        IsAdded = isAdded;
    }

    public bool IsAdded { get; }
    public string RemoveWarning => ComponentsPanel.RemoveWarning(Label, IsAdded);
    public string ConfirmText => IsAdded ? "Take it off" : $"Remove {Label}";
    public bool HasNoSettings => Children.Count == 0;

    [ObservableProperty] private bool _isConfirming;

    [RelayCommand]
    private void AskRemove() => IsConfirming = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirming = false;

    [RelayCommand]
    private void ConfirmRemove()
    {
        IsConfirming = false;
        _owner.Remove(this);
    }
}

public sealed partial class RemovedComponent(ComponentsPanel owner, string name) : ObservableObject
{
    public string Name { get; } = name;

    [RelayCommand]
    private void Undo() => owner.Restore(Name);
}

public sealed partial class AddOption(ComponentsPanel owner, string name, string kind, string hint) : ObservableObject
{
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    public string Hint { get; } = hint;
    public bool HasHint => Hint.Length > 0;

    [RelayCommand]
    private Task Add() => owner.AddAsync(Name);
}

/// <summary>
/// The Components tab: every setting of every script on the base, editable in place, plus removing scripts
/// (a ward without its warding) and adding new ones (Rigidbody, colliders, any Valheim script).
/// Only values that differ from vanilla (or from the game's defaults, for added ones) are written to the recipe.
/// </summary>
public sealed partial class ComponentsPanel : ObservableObject
{
    // Plumbing scripts: rarely touched, shown last and collapsed.
    private static readonly HashSet<string> Plumbing = new() { "ZNetView", "ZSyncTransform" };

    private static readonly Dictionary<string, string> FriendlyGroups = new()
    {
        ["m_itemData"] = "Item data",
        ["m_shared"] = "Item settings"
    };

    /// <summary>What happens without each well-known script, so "Remove" says what it costs.</summary>
    private static readonly Dictionary<string, string> RemoveEffects = new()
    {
        ["ZNetView"] = "Without ZNetView it won't save or sync in multiplayer. This almost always breaks the prefab.",
        ["Piece"] = "Without Piece it can't be placed with the hammer or shown in the build menu.",
        ["ItemDrop"] = "Without ItemDrop it's no longer an item: it can't be picked up, held or crafted.",
        ["WearNTear"] = "Without WearNTear it takes no damage, needs no support and can't be hammered down.",
        ["PrivateArea"] = "Without PrivateArea it stops warding: no protected area, no permitted list.",
        ["Fireplace"] = "Without Fireplace it stops using fuel, and the fire can't be lit or put out.",
        ["Container"] = "Without Container it no longer opens or stores anything.",
        ["CraftingStation"] = "Without CraftingStation it no longer works as a crafting station.",
        ["Door"] = "Without Door it can't be opened or closed.",
        ["Bed"] = "Without Bed you can't sleep or set your spawn here.",
        ["Vagon"] = "Without Vagon it can't be attached and pulled.",
        ["ZSyncTransform"] = "Without ZSyncTransform its movement won't sync between players."
    };

    /// <summary>Shown first in "Add component".</summary>
    private static readonly (string Name, string Hint)[] Common =
    {
        ("Rigidbody", "physics: falls, can be pushed"),
        ("BoxCollider", "solid box"),
        ("SphereCollider", "solid sphere"),
        ("CapsuleCollider", "solid capsule"),
        ("MeshCollider", "collides with its own mesh"),
        ("Light", "a light source"),
        ("Container", "storage, like a chest"),
        ("Door", "opens and closes"),
        ("Fireplace", "burns fuel, like a campfire"),
        ("CraftingStation", "a crafting station"),
        ("Bed", "sleep / spawn point"),
        ("Vagon", "pull it like the cart"),
        ("PrivateArea", "a ward"),
        ("Piece", "placeable with the hammer"),
        ("ItemDrop", "an item you can pick up")
    };

    public static string RemoveWarning(string name, bool isAdded) => isAdded
        ? $"Take the {name} you added back off? Its settings are dropped."
        : RemoveEffects.TryGetValue(name, out var effect)
            ? effect + " Your changes to its settings are dropped."
            : $"The vanilla {name} script comes off this prefab. Other scripts that expect it may stop working.";

    private readonly Action _changed;
    private readonly Dictionary<string, FieldEditor> _editors = new();
    private readonly Dictionary<string, Dictionary<string, JsonValue>> _unmatched = new();
    private readonly List<ComponentInfo> _added = new();
    private readonly List<string> _removed = new();
    private Func<string, Task<IReadOnlyList<FieldNode>>>? _defaults;
    private Func<Task<IReadOnlyList<string>>>? _scripts;
    private IReadOnlyList<string> _scriptNames = Array.Empty<string>();
    private PrefabInfo? _info;
    private bool _building;

    public ComponentsPanel(Action changed) => _changed = changed;

    public ObservableCollection<FieldGroup> Components { get; } = new();
    public ObservableCollection<FieldEditor> Results { get; } = new();
    public ObservableCollection<RemovedComponent> Removed { get; } = new();
    public ObservableCollection<AddOption> AddResults { get; } = new();
    /// <summary>Overrides in the recipe that don't match a known setting (other objects, lists); kept as-is.</summary>
    public ObservableCollection<string> OtherOverrides { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _changedOnly;
    [ObservableProperty] private bool _showReadOnly;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private string _addSearch = "";

    public bool IsFiltering => Search.Trim().Length > 0 || ChangedOnly;
    public bool HasOtherOverrides => OtherOverrides.Count > 0;
    public bool HasRemoved => Removed.Count > 0;
    public IReadOnlyList<string> RemovedNames => _removed;
    public IReadOnlyList<string> AddedNames => _added.Select(a => a.Name).ToList();

    partial void OnSearchChanged(string value) => Filter();
    partial void OnChangedOnlyChanged(bool value) => Filter();
    partial void OnShowReadOnlyChanged(bool value) => Rebuild();
    partial void OnAddSearchChanged(string value) => FilterAdd();

    public async Task BuildAsync(PrefabInfo info, Dictionary<string, Dictionary<string, JsonValue>> overrides, IEnumerable<string> removed, IEnumerable<string> added,
        Func<string, Task<IReadOnlyList<FieldNode>>>? defaults, Func<Task<IReadOnlyList<string>>>? scripts)
    {
        _defaults = defaults;
        _scripts = scripts;
        var addedInfos = new List<ComponentInfo>();
        foreach (var name in added.Distinct())
            addedInfos.Add(new ComponentInfo { Name = name, Fields = await DefaultsOf(name) });

        _info = info;
        _removed.Clear();
        _removed.AddRange(removed.Distinct());
        _added.Clear();
        _added.AddRange(addedInfos);
        _building = true;
        _editors.Clear();
        _unmatched.Clear();
        foreach (var c in info.Components.Concat(_added))
            AddEditors(c);

        foreach (var component in overrides)
            foreach (var field in component.Value)
            {
                if (_editors.TryGetValue(component.Key + "|" + field.Key, out var editor) && !editor.IsReadOnly)
                    editor.Load(field.Value);
                else
                {
                    if (!_unmatched.TryGetValue(component.Key, out var map))
                        _unmatched[component.Key] = map = new Dictionary<string, JsonValue>();
                    map[field.Key] = field.Value;
                }
            }

        OtherOverrides.Clear();
        foreach (var c in _unmatched)
            foreach (var f in c.Value)
                OtherOverrides.Add($"{c.Key}.{f.Key} = {f.Value.ToJson(false)}");
        OnPropertyChanged(nameof(HasOtherOverrides));
        _building = false;
        Rebuild();
    }

    private async Task<IReadOnlyList<FieldNode>> DefaultsOf(string type)
    {
        if (_defaults == null)
            return Array.Empty<FieldNode>();
        try
        {
            return await _defaults(type);
        }
        catch (Exception)
        {
            return Array.Empty<FieldNode>(); // added, just without editable settings
        }
    }

    private void AddEditors(ComponentInfo c)
    {
        foreach (var leaf in Leaves(c.Fields))
            _editors[c.Name + "|" + leaf.Node.Path] = new FieldEditor(this, c.Name, leaf.Node, leaf.Crumb);
    }

    private IEnumerable<(FieldNode Node, string Crumb)> Leaves(IEnumerable<FieldNode> nodes, string crumb = "")
    {
        foreach (var n in nodes)
        {
            if (n.Kind == FieldKind.Group)
            {
                foreach (var leaf in Leaves(n.Children, crumb + GroupLabel(n) + " › "))
                    yield return leaf;
            }
            else
            {
                yield return (n, crumb);
            }
        }
    }

    private void Rebuild()
    {
        Components.Clear();
        Removed.Clear();
        foreach (var name in _removed)
            Removed.Add(new RemovedComponent(this, name));
        OnPropertyChanged(nameof(HasRemoved));
        if (_info == null)
            return;
        foreach (var c in _info.Components.Where(c => !_removed.Contains(c.Name)).OrderBy(c => Plumbing.Contains(c.Name)))
        {
            var card = new ComponentCard(this, c.Name, Plumbing.Contains(c.Name) ? "networking" : null, !Plumbing.Contains(c.Name), false);
            Fill(card, c.Name, c.Fields);
            Components.Add(card);
        }

        foreach (var c in _added)
        {
            var card = new ComponentCard(this, c.Name, "added", true, true);
            Fill(card, c.Name, c.Fields);
            Components.Add(card);
        }

        Recount();
        Filter();
    }

    private void Fill(FieldGroup group, string component, IEnumerable<FieldNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Kind == FieldKind.Group)
            {
                var child = new FieldGroup(GroupLabel(n), null, n.Name is "m_itemData" or "m_shared");
                Fill(child, component, n.Children);
                if (child.Children.Count == 0)
                    continue;
                group.Children.Add(child);
                group.AllEditors.AddRange(child.AllEditors);
            }
            else if (_editors.TryGetValue(component + "|" + n.Path, out var editor) && (ShowReadOnly || !editor.IsReadOnly))
            {
                group.Children.Add(editor);
                group.AllEditors.Add(editor);
            }
        }
    }

    private static string GroupLabel(FieldNode n) => FriendlyGroups.TryGetValue(n.Name, out var label) ? label : n.Label;

    private bool IsLive(FieldEditor e) => !_removed.Contains(e.Component);

    private void Filter()
    {
        OnPropertyChanged(nameof(IsFiltering));
        Results.Clear();
        if (!IsFiltering)
            return;
        var terms = Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var e in _editors.Values.Where(IsLive))
        {
            if (!ShowReadOnly && e.IsReadOnly)
                continue;
            if (ChangedOnly && !e.IsModified)
                continue;
            var hay = $"{e.Label} {e.Breadcrumb} {e.Node.Path} {e.Component}";
            if (terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase)))
                Results.Add(e);
            if (Results.Count >= 200)
                break;
        }
    }

    public void Changed(FieldEditor editor)
    {
        if (_building)
            return;
        Recount();
        if (ChangedOnly && !editor.IsModified)
            Results.Remove(editor);
        _changed();
    }

    private void Recount()
    {
        foreach (var g in Components)
            g.Recount();
        var n = _editors.Values.Where(IsLive).Count(e => e.IsModified);
        var parts = new List<string>();
        if (n > 0)
            parts.Add($"{n} setting{(n == 1 ? "" : "s")} changed");
        if (_removed.Count > 0)
            parts.Add($"{_removed.Count} removed");
        if (_added.Count > 0)
            parts.Add($"{_added.Count} added");
        Summary = parts.Count == 0 ? "Everything is vanilla. Change any value below." : string.Join(" · ", parts);
    }

    // ---- remove / add ----

    public void Remove(ComponentCard card)
    {
        if (card.IsAdded)
        {
            _added.RemoveAll(a => a.Name == card.Label);
            foreach (var key in _editors.Keys.Where(k => k.StartsWith(card.Label + "|", StringComparison.Ordinal)).ToList())
                _editors.Remove(key);
        }
        else if (!_removed.Contains(card.Label))
        {
            _removed.Add(card.Label);
        }

        Rebuild();
        _changed();
    }

    public void Restore(string name)
    {
        _removed.Remove(name);
        Rebuild();
        _changed();
    }

    private bool IsPresent(string name) =>
        _added.Any(a => a.Name == name) || (_info?.Components.Any(c => c.Name == name) == true && !_removed.Contains(name));

    public async Task AddAsync(string name)
    {
        IsAdding = false;
        if (IsPresent(name))
            return;
        if (_removed.Contains(name))
        {
            Restore(name); // the base had it all along: put the vanilla one back
            return;
        }

        var info = new ComponentInfo { Name = name, Fields = await DefaultsOf(name) };
        _added.Add(info);
        AddEditors(info);
        Rebuild();
        _changed();
    }

    [RelayCommand]
    private async Task OpenAdd()
    {
        AddSearch = "";
        IsAdding = true;
        FilterAdd();
        if (_scriptNames.Count == 0 && _scripts != null)
        {
            try
            {
                _scriptNames = await _scripts();
            }
            catch (Exception)
            {
                // without the game DLL only the common ones are offered
            }

            FilterAdd();
        }
    }

    [RelayCommand]
    private void CloseAdd() => IsAdding = false;

    private void FilterAdd()
    {
        AddResults.Clear();
        var term = AddSearch.Trim();
        var common = Common.Select(c => c.Name).ToHashSet();
        var unity = ComponentCatalog.UnityComponents.ToHashSet();
        var all = Common.Select(c => new AddOption(this, c.Name, unity.Contains(c.Name) ? "Unity" : "Valheim", c.Hint))
            .Concat(_scriptNames.Where(n => !common.Contains(n)).Select(n => new AddOption(this, n, "Valheim", "")));
        foreach (var o in all.Where(o => !IsPresent(o.Name) && (term.Length == 0 || o.Name.Contains(term, StringComparison.OrdinalIgnoreCase))).Take(term.Length == 0 ? 15 : 80))
            AddResults.Add(o);
    }

    /// <summary>What the recipe should store: only changed values, plus overrides we couldn't map.</summary>
    public Dictionary<string, Dictionary<string, JsonValue>> Collect()
    {
        var result = new Dictionary<string, Dictionary<string, JsonValue>>();
        foreach (var c in _unmatched.Where(c => !_removed.Contains(c.Key)))
            result[c.Key] = new Dictionary<string, JsonValue>(c.Value);
        foreach (var e in _editors.Values.Where(e => e.IsModified && IsLive(e)))
        {
            if (e.ToJson() is not { } json)
                continue;
            if (!result.TryGetValue(e.Component, out var map))
                result[e.Component] = map = new Dictionary<string, JsonValue>();
            map[e.Node.Path] = json;
        }

        return result;
    }

    [RelayCommand]
    private void ClearSearch() => Search = "";

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var e in _editors.Values.Where(e => e.IsModified).ToList())
            e.ResetCommand.Execute(null);
    }

    [RelayCommand]
    private void DropOtherOverrides()
    {
        _unmatched.Clear();
        OtherOverrides.Clear();
        OnPropertyChanged(nameof(HasOtherOverrides));
        _changed();
    }
}
