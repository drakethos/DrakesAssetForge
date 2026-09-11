namespace DrakeAssetForge.Models;

public sealed class FieldRow
{
    public required string Path { get; init; }
    public required string Value { get; init; }
}

public sealed class SpyScriptComponent
{
    public required string ClassName { get; init; }
    public required string PathId { get; init; }
    public required IReadOnlyList<FieldRow> Fields { get; init; }
    public bool IsMatchedValheimScript { get; init; }

    public string DisplayLabel =>
        IsMatchedValheimScript ? ClassName : $"{ClassName} (unmatched)";
}

public sealed class PrefabPropertySpyResult
{
    public string? Error { get; init; }
    public string PrefabName { get; init; } = "";
    public IReadOnlyList<SpyScriptComponent> Scripts { get; init; } = Array.Empty<SpyScriptComponent>();
}

public sealed class AssetPropertySpyResult
{
    public string? Error { get; init; }
    public string AssetName { get; init; } = "";
    public string SoftRefPath { get; init; } = "";
    public string ClassName { get; init; } = "";
    public IReadOnlyList<FieldRow> Fields { get; init; } = Array.Empty<FieldRow>();
}

public sealed class RecipeRequirementRow
{
    public required string ItemName { get; init; }
    public required string Token { get; init; }
    public required int Amount { get; init; }
    public required int AmountPerLevel { get; init; }

    public string Display =>
        AmountPerLevel > 0
            ? $"{ItemName} × {Amount} (+{AmountPerLevel}/lvl)"
            : $"{ItemName} × {Amount}";
}
