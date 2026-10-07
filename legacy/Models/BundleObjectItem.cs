namespace DrakeAssetForge.Models;

public sealed class BundleObjectItem
{
    public required long PathId { get; init; }
    public required int TypeId { get; init; }
    public required string TypeName { get; init; }
    public required string Name { get; init; }
}
