using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;

namespace DrakesForge.App.ViewModels;

public sealed partial class PushTargetRow : ObservableObject
{
    private readonly SettingsViewModel _owner;

    public PushTargetRow(PushTarget target, SettingsViewModel owner)
    {
        Target = target;
        _owner = owner;
        _isPinned = AppSettings.PinnedPushFolders.Any(p => PushTarget.SameFolder(p, target.Folder));
    }

    public PushTarget Target { get; }
    public string Label => Target.Label;
    public string Folder => Target.Folder;
    public string RuntimeText => Target.HasRuntime ? "Forge Runtime ✓" : "Forge Runtime missing";
    public string JotunnText => Target.HasJotunn ? "Jotunn ✓" : "Jotunn missing";
    public bool RuntimeOk => Target.HasRuntime;
    public bool JotunnOk => Target.HasJotunn;
    /// <summary>Has everything a pushed pack needs, so it's worth listing before the rest.</summary>
    public bool IsReady => Target.HasRuntime && Target.HasJotunn;
    public bool CanInstallRuntime => !Target.HasRuntime && PushTargets.BundledRuntime() != null;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isPinned;

    public double PinOpacity => IsPinned ? 1 : 0.35;
    public string PinTip => IsPinned ? "Unpin: stop keeping this profile at the top" : "Pin: always list this profile at the top";

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _owner.Select(this);
    }

    partial void OnIsPinnedChanged(bool value)
    {
        OnPropertyChanged(nameof(PinOpacity));
        OnPropertyChanged(nameof(PinTip));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(RuntimeOk));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(CanInstallRuntime));
    }

    [RelayCommand]
    private void InstallRuntime() => _owner.InstallRuntime(this);

    [RelayCommand]
    private void TogglePin() => _owner.TogglePin(this);

    [RelayCommand]
    private void Open() => System.Diagnostics.Process.Start("explorer.exe", Target.Folder);
}

/// <summary>Where packs live and where "Push to game" installs them.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _showAll;
    // Marking the current target selected while building must not save it as the user's choice.
    private bool _building = true;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _packsFolder = PackProject.DefaultPacksDirectory;
        foreach (var target in PushTargets.Detect(main.Vanilla?.Catalog.Install.Root))
            Targets.Add(new PushTargetRow(target, this));

        // The target Push uses now may not be among the detected profiles (a saved folder, or the profile this app lives in). Add it.
        var current = PushTargets.Current();
        if (!current.IsDevFolder && Targets.All(t => !PushTarget.SameFolder(t.Folder, current.Folder)))
            Targets.Insert(Targets.Count - 1, new PushTargetRow(current, this));

        var selected = Targets.FirstOrDefault(t => current.IsDevFolder ? t.Target.IsDevFolder : PushTarget.SameFolder(t.Folder, current.Folder));
        if (selected != null)
            selected.IsSelected = true;
        _building = false;
        RefreshList();
        UpdateDuplicates();
    }

    public ObservableCollection<PushTargetRow> Targets { get; } = new();
    /// <summary>The rows the list shows: pinned and ready profiles first, the first few only until "Show more".</summary>
    public ObservableCollection<PushTargetRow> Visible { get; } = new();
    public ObservableCollection<string> Duplicates { get; } = new();

    [ObservableProperty] private string _packsFolder;
    [ObservableProperty] private string _note = "";

    public PushTargetRow? Selected => Targets.FirstOrDefault(t => t.IsSelected);
    public string Destination => _main.Pack != null && Selected != null ? Selected.Target.PackFolder(_main.Pack.Manifest) : "";
    public bool HasDuplicates => Duplicates.Count > 0;

    /// <summary>The toggle under the list is shown when there is something more to show (or less, once expanded).</summary>
    public bool CanToggle => _showAll ? Targets.Count > AppSettings.PushListSize : Targets.Count > Visible.Count;
    public string ToggleText => _showAll ? "Show fewer" : $"Show {Targets.Count - Visible.Count} more…";

    public void Select(PushTargetRow row)
    {
        if (_building)
            return;
        foreach (var other in Targets.Where(t => t != row))
            other.IsSelected = false;
        AppSettings.PushChosen = true;
        AppSettings.PushFolder = row.Target.IsDevFolder ? null : row.Target.Folder;
        AppSettings.PushLabel = row.Target.IsDevFolder ? null : row.Target.Label;
        AppSettings.Save();
        _main.PushTargetChanged();
        OnPropertyChanged(nameof(Destination));
        RefreshList();
        UpdateDuplicates();
        Note = !row.Target.HasRuntime
            ? "Forge Runtime isn't in this profile, so pushed packs won't load. Install it below (Jotunn is needed too)."
            : row.Target.HasJotunn ? "" : "Jotunn isn't in this profile; install it with your mod manager.";
    }

    [RelayCommand]
    private void ToggleShowAll()
    {
        _showAll = !_showAll;
        RefreshList();
    }

    public void TogglePin(PushTargetRow row)
    {
        var pins = AppSettings.PinnedPushFolders;
        if (row.IsPinned)
            pins.RemoveAll(p => PushTarget.SameFolder(p, row.Folder));
        else
            pins.Add(row.Folder);
        row.IsPinned = !row.IsPinned;
        AppSettings.Save();
        RefreshList();
    }

    /// <summary>
    /// Rebuilds <see cref="Visible"/>: pinned and selected rows, then the first few ready profiles, and everything else only after "Show more".
    /// Order is stable (pinned, then ready, then detection order), so a click never moves the row under the cursor.
    /// </summary>
    private void RefreshList()
    {
        var ordered = Targets
            .OrderBy(t => t.IsPinned ? 0 : 1)
            .ThenBy(t => t.IsReady ? 0 : 1)
            .ToList();
        var limit = AppSettings.PushListSize;
        var shown = _showAll
            ? ordered
            : ordered.Where(t => t.IsPinned || t.IsSelected)
                .Concat(ordered.Where(t => t.IsReady && !t.IsPinned && !t.IsSelected).Take(limit))
                .ToList();

        Visible.Clear();
        foreach (var row in shown)
            Visible.Add(row);
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ToggleText));
    }

    public void InstallRuntime(PushTargetRow row)
    {
        try
        {
            var dir = PushTargets.InstallRuntime(row.Target);
            row.Refresh();
            RefreshList();
            Note = $"Installed Forge Runtime to {dir}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note = $"Couldn't install: {ex.Message}";
        }
    }

    private void UpdateDuplicates()
    {
        Duplicates.Clear();
        if (_main.Pack != null && Selected != null)
            foreach (var copy in _main.Pack.OtherCopies(Selected.Target))
                Duplicates.Add(copy);
        OnPropertyChanged(nameof(HasDuplicates));
    }

    [RelayCommand]
    private async Task ChoosePacksFolder()
    {
        var folder = await Dialogs.PickFolderAsync("Where should new packs be created?");
        if (folder == null)
            return;
        PacksFolder = folder;
        AppSettings.PacksFolder = folder;
        AppSettings.Save();
        Note = "New packs will be created here. Existing packs stay where they are (open them with Open pack folder).";
    }

    [RelayCommand]
    private void OpenPacksFolder()
    {
        Directory.CreateDirectory(PacksFolder);
        System.Diagnostics.Process.Start("explorer.exe", PacksFolder);
    }

    [RelayCommand]
    private async Task AddCustomTarget()
    {
        var folder = await Dialogs.PickFolderAsync("Choose a mod profile, its BepInEx folder, or a plugins folder");
        if (folder == null)
            return;
        var row = new PushTargetRow(PushTargets.Custom(folder), this);
        Targets.Insert(Math.Max(0, Targets.Count - 1), row);
        row.IsSelected = true;
    }

    /// <summary>Deletes other copies of this pack in the selected profile (they'd load instead of, or as well as, yours).</summary>
    [RelayCommand]
    private void RemoveDuplicates()
    {
        foreach (var dir in Duplicates.ToList())
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Note = $"Couldn't remove {dir}: {ex.Message}";
            }
        }

        UpdateDuplicates();
    }

    [RelayCommand]
    private void Close() => _main.Settings = null;
}
