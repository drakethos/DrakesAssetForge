using System.Text.Json.Serialization;

namespace DrakeAssetForge.Models;

public sealed class OwnedItemDocument
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DonorRef Donor { get; set; } = new();
    public List<OwnedScriptSeed> Scripts { get; set; } = new();
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public OwnedRecipeSeed? Recipe { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    [JsonIgnore]
    public string DocumentPath { get; set; } = "";

    /// <summary>Relative group under Items/ (e.g. Keys). Empty means the project root.</summary>
    [JsonIgnore]
    public string GroupPath { get; set; } = "";

    /// <summary>Populated when loading from disk (art.json).</summary>
    [JsonIgnore]
    public bool HasMeshArt { get; set; }

    [JsonIgnore]
    public bool HasIconArt { get; set; }

    [JsonIgnore]
    public bool HasDiffuseArt { get; set; }

    [JsonIgnore]
    public bool HasCompiledArt { get; set; }

    [JsonIgnore]
    public bool NeedsBundleExtract { get; set; }

    [JsonIgnore]
    public bool IncludeInExport { get; set; } = true;

    [JsonIgnore]
    public string ArtBadge =>
        (IncludeInExport ? "" : "export:out ") +
        (HasMeshArt ? (HasCompiledArt ? "mesh " : "mesh(needs compile) ") : "") +
        (NeedsBundleExtract ? "bundle(needs extract) " : (!HasMeshArt && HasCompiledArt ? "bundle " : "")) +
        (HasIconArt ? "icon " : "") +
        (HasDiffuseArt ? "diffuse" : "");

    [JsonIgnore]
    public string ArtBadgeDisplay =>
        string.IsNullOrWhiteSpace(ArtBadge)
            ? "art: none — Export ships hooks only (no custom bundle)"
            : $"art: {ArtBadge.Trim()}";
}

public sealed class OwnedScriptSeed
{
    public string ClassName { get; set; } = "";
    public string PathId { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DonorRef
{
    public string SoftRefAssetId { get; set; } = "";
    public string SoftRefPath { get; set; } = "";
    public string BundleId { get; set; } = "";
    public string PrefabName { get; set; } = "";
    public string Kind { get; set; } = "";
}

public sealed class OwnedRecipeSeed
{
    public string SoftRefPath { get; set; } = "";
    public string RecipeName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OwnedRecipeRequirement> Requirements { get; set; } = new();
}

public sealed class OwnedRecipeRequirement
{
    public string ItemName { get; set; } = "";
    public string Token { get; set; } = "";
    public int Amount { get; set; }
    public int AmountPerLevel { get; set; }
}
