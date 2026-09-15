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

    /// <summary>In-game prefab / spawn id. Empty means the project item id (often Custom_…).</summary>
    public string? PrefabName { get; set; }

    /// <summary>Item description. Empty means the donor prefab description.</summary>
    public string? Description { get; set; }

    /// <summary>Uniform scale applied to the art prefab when it is parented onto the clone.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>When false, the compiled mesh bundle is not copied to the mod. Missing means include.</summary>
    public bool IncludeMesh { get; set; } = true;

    /// <summary>When false, icon.png is not copied to the mod. Missing means include.</summary>
    public bool IncludeIcon { get; set; } = true;

    /// <summary>When false, the next compile omits the diffuse PNG. Missing means include.</summary>
    public bool IncludeDiffuse { get; set; } = true;

    /// <summary>
    /// When false, Sync / hook codegen skips this item. Missing in older art.json means include.
    /// </summary>
    public bool IncludeInExport { get; set; } = true;

    /// <summary>Project-relative path to a fat source Unity bundle (under Imports/) for extract-to-art.bundle.</summary>
    public string? SourceBundlePath { get; set; }

    /// <summary>Prefab name inside <see cref="SourceBundlePath"/> to extract as the standard <c>art</c> prefab.</summary>
    public string? SourcePrefabName { get; set; }

    /// <summary>True when a multi-prefab source was imported but not yet extracted into art.bundle.</summary>
    public bool NeedsBundleExtract { get; set; }

    public DateTimeOffset ModifiedUtc { get; set; }
}
