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
    }

    public PushTarget Target { get; }
    public string Label => Target.Label;
    public string Folder => Target.Folder;
    public string RuntimeText => Target.HasRuntime ? "Forge Runtime ✓" : "Forge Runtime missing";
    public string JotunnText => Target.HasJotunn ? "Jotunn ✓" : "Jotunn missing";
    public bool RuntimeOk => Target.HasRuntime;
    public bool JotunnOk => Target.HasJotunn;
    public bool CanInstallRuntime => !Target.HasRuntime && PushTargets.BundledRuntime() != null;

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            _owner.Select(this);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(RuntimeText));
        OnPropertyChanged(nameof(RuntimeOk));
        OnPropertyChanged(nameof(CanInstallRuntime));
    }

    [RelayCommand]
    private void InstallRuntime() => _owner.InstallRuntime(this);

    [RelayCommand]
    private void Open() => System.Diagnostics.Process.Start("explorer.exe", Target.Folder);
}

/// <summary>Where packs live and where "Push to game" installs them.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        _packsFolder = PackProject.DefaultPacksDirectory;
        foreach (var target in PushTargets.Detect(main.Vanilla?.Catalog.Install.Root))
            Targets.Add(new PushTargetRow(target, this));
        if (AppSettings.PushFolder is { } saved && Targets.All(t => !Same(t.Folder, saved)))
            Targets.Insert(Targets.Count - 1, new PushTargetRow(PushTargets.Custom(saved), this));

        var current = Targets.FirstOrDefault(t => AppSettings.PushFolder != null ? Same(t.Folder, AppSettings.PushFolder) : t.Target.IsDevFolder);
        if (current != null)
            current.IsSelected = true;
        UpdateDuplicates();
    }

    public ObservableCollection<PushTargetRow> Targets { get; } = new();
    public ObservableCollection<string> Duplicates { get; } = new();

    [ObservableProperty] private string _packsFolder;
    [ObservableProperty] private string _note = "";

    public PushTargetRow? Selected => Targets.FirstOrDefault(t => t.IsSelected);
    public string Destination => _main.Pack != null && Selected != null ? Selected.Target.PackFolder(_main.Pack.Manifest) : "";
    public bool HasDuplicates => Duplicates.Count > 0;

    public void Select(PushTargetRow row)
    {
        foreach (var other in Targets.Where(t => t != row))
            other.IsSelected = false;
        AppSettings.PushFolder = row.Target.IsDevFolder ? null : row.Target.Folder;
        AppSettings.PushLabel = row.Target.IsDevFolder ? null : row.Target.Label;
        AppSettings.Save();
        _main.PushTargetChanged();
        OnPropertyChanged(nameof(Destination));
        UpdateDuplicates();
        Note = !row.Target.HasRuntime
            ? "Forge Runtime isn't in this profile, so pushed packs won't load. Install it below (Jotunn is needed too)."
            : row.Target.HasJotunn ? "" : "Jotunn isn't in this profile; install it with your mod manager.";
    }

    private void UpdateDuplicates()
    {
        Duplicates.Clear();
        if (_main.Pack != null && Selected != null)
            foreach (var copy in _main.Pack.OtherCopies(Selected.Target))
                Duplicates.Add(copy);
        OnPropertyChanged(nameof(HasDuplicates));
    }

    public void InstallRuntime(PushTargetRow row)
    {
        try
        {
            var dir = PushTargets.InstallRuntime(row.Target);
            row.Refresh();
            Note = $"Installed Forge Runtime to {dir}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note = $"Couldn't install: {ex.Message}";
        }
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

    private static bool Same(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
