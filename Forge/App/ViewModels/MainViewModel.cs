using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrakesForge.App.Services;

namespace DrakesForge.App.ViewModels;

public enum Step
{
    Browse,
    Import,
    Workspace,
    Publish
}

public sealed partial class StepChip : ObservableObject
{
    public StepChip(Step step, string label, MainViewModel main)
    {
        Step = step;
        Label = label;
        GoCommand = new RelayCommand(() => main.GoTo(step));
    }

    public Step Step { get; }
    public string Label { get; }
    public IRelayCommand GoCommand { get; }
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>Shell: the open pack, which step is showing, and Push to game.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public MainViewModel()
    {
        Browse = new BrowseViewModel(this);
        Import = new ImportViewModel(this);
        Workspace = new WorkspaceViewModel(this);
        Publish = new PublishViewModel(this);
        Steps = new ObservableCollection<StepChip>
        {
            new(Step.Browse, "1 · Browse Valheim", this),
            new(Step.Import, "2 · Import", this),
            new(Step.Workspace, "3 · Work", this),
            new(Step.Publish, "4 · Publish", this)
        };
        _page = Browse;
    }

    public BrowseViewModel Browse { get; }
    public ImportViewModel Import { get; }
    public WorkspaceViewModel Workspace { get; }
    public PublishViewModel Publish { get; }
    public ObservableCollection<StepChip> Steps { get; }

    [ObservableProperty] private VanillaService? _vanilla;
    [ObservableProperty] private string? _setupMessage;
    [ObservableProperty] private PackProject? _pack;
    [ObservableProperty] private Step _step;
    [ObservableProperty] private object _page;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _livePush = true;
    [ObservableProperty] private bool _showNewPack;
    [ObservableProperty] private string _newPackName = "";

    public string PackTitle => Pack == null ? "No pack" : $"{Pack.Manifest.Name}  ·  v{Pack.Manifest.Version}";
    public string ValheimLabel => Vanilla == null ? "Valheim not found" : $"Valheim · {Vanilla.Catalog.Entries.Count:N0} prefabs";

    /// <summary>Opens Valheim and the last pack (or a starter pack). Call once after the window exists.</summary>
    public void Initialize()
    {
        AppSettings.Load();
        OpenValheim(AppSettings.ValheimPath);

        var last = AppSettings.LastPack;
        if (last != null && File.Exists(Path.Combine(last, Format.ForgePack.FileName)))
            OpenPack(last);
        else
            CreatePack("My First Pack");
        GoTo(Step.Browse);
    }

    public void OpenValheim(string? path)
    {
        Vanilla?.Dispose();
        Vanilla = VanillaService.TryOpen(path, out var error);
        SetupMessage = Vanilla == null ? error : null;
        if (Vanilla != null && path != null)
        {
            AppSettings.ValheimPath = path;
            AppSettings.Save();
        }

        OnPropertyChanged(nameof(ValheimLabel));
        Browse.Reload();
    }

    public void OpenPack(string root)
    {
        try
        {
            Pack = PackProject.Open(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't open pack: {ex.Message}";
            return;
        }

        AppSettings.LastPack = root;
        AppSettings.Save();
        Status = $"Opened {Pack.Manifest.Name} ({Pack.Root})";
    }

    public void CreatePack(string name)
    {
        var id = Regex.Replace(name, "[^A-Za-z0-9]", "");
        if (id.Length == 0)
            id = "MyPack";
        var root = Path.Combine(PackProject.DefaultPacksDirectory, id);
        Pack = PackProject.Create(root, id, name, Environment.UserName);
        AppSettings.LastPack = root;
        AppSettings.Save();
        Status = $"Pack folder: {root}";
    }

    partial void OnPackChanged(PackProject? value)
    {
        OnPropertyChanged(nameof(PackTitle));
        Browse.Reload();
        Workspace.Reload();
        Publish.Reload();
    }

    public void GoTo(Step step)
    {
        if (step == Step.Import && Pack?.WorkingList.Count == 0)
        {
            Status = "Add things to your working list first (the + on a tile).";
            step = Step.Browse;
        }

        Step = step;
        Page = step switch
        {
            Step.Import => Import,
            Step.Workspace => Workspace,
            Step.Publish => Publish,
            _ => Browse
        };
        if (step == Step.Import)
            Import.Reload();
        if (step == Step.Publish)
            Publish.Reload();
        foreach (var chip in Steps)
            chip.IsCurrent = chip.Step == step;
    }

    /// <summary>A recipe was saved; with live push on, the running game picks it up.</summary>
    public void RecipeSaved(string what)
    {
        if (LivePush && Pack != null)
            PushNow($"Saved {what}, pushed to game");
        else
            Status = $"Saved {what}";
        Publish.Dirty = true;
    }

    [RelayCommand]
    private void PushToGame() => PushNow("Pushed");

    private void PushNow(string label)
    {
        if (Pack == null)
            return;
        try
        {
            var target = CurrentPushTarget;
            var dest = Pack.Push(target);

            // A copy left in Forge's own dev folder from earlier pushes would win over the profile copy in game.
            if (!target.IsDevFolder)
            {
                var stale = PushTargets.Dev().PackFolder(Pack.Manifest);
                if (Directory.Exists(stale))
                    Directory.Delete(stale, true);
            }
            var copies = Pack.OtherCopies(target);
            Status = copies.Count > 0
                ? $"{label} to {target.Label}, but another copy of this pack is in that profile ({copies[0]}). Remove it in Settings."
                : $"{label} to {target.Label} · {DateTime.Now:HH:mm:ss} → {dest}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status = $"Push failed: {ex.Message}";
        }
    }

    /// <summary>Where Push installs packs: the user's pick, else the profile this app lives in, else Forge's dev folder.</summary>
    public PushTarget CurrentPushTarget => PushTargets.Current();

    public string PushTargetLabel => $"Push installs to {CurrentPushTarget.Label}";

    public void PushTargetChanged() => OnPropertyChanged(nameof(PushTargetLabel));

    [ObservableProperty] private SettingsViewModel? _settings;

    [RelayCommand]
    private void OpenSettings() => Settings = new SettingsViewModel(this);

    [RelayCommand]
    private void ToggleNewPack() => ShowNewPack = !ShowNewPack;

    [RelayCommand]
    private void ConfirmNewPack()
    {
        if (string.IsNullOrWhiteSpace(NewPackName))
            return;
        CreatePack(NewPackName.Trim());
        NewPackName = "";
        ShowNewPack = false;
        GoTo(Step.Browse);
    }

    [RelayCommand]
    private async Task OpenPackFolder()
    {
        var folder = await Dialogs.PickFolderAsync("Open a pack folder (contains forgepack.json)");
        if (folder == null)
            return;
        if (!File.Exists(Path.Combine(folder, Format.ForgePack.FileName)))
        {
            Status = "That folder has no forgepack.json.";
            return;
        }

        OpenPack(folder);
        GoTo(Step.Workspace);
    }

    [RelayCommand]
    private async Task ChooseValheim()
    {
        var folder = await Dialogs.PickFolderAsync("Choose your Valheim folder");
        if (folder != null)
            OpenValheim(folder);
    }

    [RelayCommand]
    private void RevealPack()
    {
        if (Pack != null)
            System.Diagnostics.Process.Start("explorer.exe", Pack.Root);
    }

    public void Dispose() => Vanilla?.Dispose();
}
