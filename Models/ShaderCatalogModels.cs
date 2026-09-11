namespace DrakeAssetForge.Models;

public enum ShaderPropertyKind
{
    Color = 0,
    Vector = 1,
    Float = 2,
    Range = 3,
    Texture = 4,
    Unknown = 99,
}

public enum MaterialAuthoringMode
{
    Donor,
    Custom,
}

public sealed class ShaderPropertySchema
{
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public ShaderPropertyKind Kind { get; init; }
    public string KindLabel => Kind.ToString();
}

public sealed class ShaderCatalogEntry
{
    public required string FindName { get; init; }
    public string SoftRefDisplayName { get; init; } = "";
    public string SoftRefPath { get; init; } = "";
    public List<ShaderPropertySchema> Properties { get; init; } = new();

    public string ListLabel =>
        string.IsNullOrWhiteSpace(SoftRefDisplayName) || SoftRefDisplayName.Equals(FindName, StringComparison.OrdinalIgnoreCase)
            ? FindName
            : $"{FindName}  ({SoftRefDisplayName})";
}

public sealed class ShaderCatalogDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset BuiltUtc { get; set; }
    public string ValheimPath { get; set; } = "";
    public List<ShaderCatalogEntry> Shaders { get; set; } = new();
}

public sealed class MaterialPropertyValue
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ShaderPropertyKind Kind { get; set; }
    public string Value { get; set; } = "";
    public string TextureRef { get; set; } = "";
}

public sealed class MaterialDocument
{
    public MaterialAuthoringMode Mode { get; set; } = MaterialAuthoringMode.Donor;
    public string ShaderName { get; set; } = "";
    public string? SeededFromSoftRefMaterial { get; set; }
    public List<MaterialPropertyValue> Properties { get; set; } = new();
    public DateTimeOffset ModifiedUtc { get; set; }
}
