using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

public partial class MaterialPropertyRow : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private ShaderPropertyKind _kind;

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _textureRef = "";

    public bool IsTexture => Kind is ShaderPropertyKind.Texture;
    public bool IsColorOrFloat => Kind is not ShaderPropertyKind.Texture;

    public string KindLabel => Kind.ToString();

    partial void OnKindChanged(ShaderPropertyKind value)
    {
        OnPropertyChanged(nameof(IsTexture));
        OnPropertyChanged(nameof(IsColorOrFloat));
        OnPropertyChanged(nameof(KindLabel));
    }

    public MaterialPropertyValue ToValue() => new()
    {
        Name = Name,
        Description = Description,
        Kind = Kind,
        Value = Value,
        TextureRef = TextureRef,
    };

    public static MaterialPropertyRow FromValue(MaterialPropertyValue value) => new()
    {
        Name = value.Name,
        Description = value.Description,
        Kind = value.Kind,
        Value = value.Value,
        TextureRef = value.TextureRef,
    };
}
