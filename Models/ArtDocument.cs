namespace DrakeAssetForge.Models;

/// <summary>
/// User art paths for an owned item. Paths are usually relative to the item folder
/// (copied under <c>art/</c>). Compile to AssetBundle happens later (C2).
/// </summary>
public sealed class ArtDocument
{
    public string? MeshPath { get; set; }
    public string? IconPath { get; set; }
    public string? DiffusePath { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}
