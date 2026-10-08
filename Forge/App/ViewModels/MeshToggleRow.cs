using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakesForge.App.ViewModels;

/// <summary>One mesh (renderer) of the base model. Ticked = hidden: it stops drawing in game and in the preview.</summary>
public sealed partial class MeshToggleRow : ObservableObject
{
    private readonly Action _changed;

    public MeshToggleRow(string path, string meshName, bool hidden, Action changed)
    {
        Path = path;
        MeshName = meshName;
        _isHidden = hidden;
        _changed = changed;
    }

    /// <summary>Path under the prefab root ("rock_a/mesh"); empty for a mesh on the root itself.</summary>
    public string Path { get; }
    public string MeshName { get; }
    public string Label => Path.Length == 0 ? "(root mesh)" : Path;

    [ObservableProperty] private bool _isHidden;

    partial void OnIsHiddenChanged(bool value) => _changed();
}
