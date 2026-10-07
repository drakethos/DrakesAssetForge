using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

public partial class EditableFieldRow : ObservableObject
{
    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _value = "";
}
