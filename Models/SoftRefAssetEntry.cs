using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DrakeAssetForge.Models;

public partial class SoftRefAssetEntry : ObservableObject
{
    public required string AssetId { get; init; }
    public required string BundleId { get; init; }
    public required string PathInBundle { get; init; }

    public string NormalizedPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public CatalogKind Kind { get; init; } = CatalogKind.Other;
    public string Category { get; init; } = "Other";
    public string SubCategory { get; init; } = "";
    public bool IsResourceNoise { get; init; }

    [ObservableProperty]
    private Bitmap? _thumbnail;
}
