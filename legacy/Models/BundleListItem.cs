namespace DrakeAssetForge.Models;

public sealed class BundleListItem
{
    public required string BundleId { get; init; }
    public required string FullPath { get; init; }
    public long SizeBytes { get; init; }
    public int DependencyCount { get; init; }

    public string SizeDisplay =>
        SizeBytes >= 1_048_576
            ? $"{SizeBytes / 1_048_576.0:0.0} MB"
            : $"{SizeBytes / 1024.0:0} KB";
}
