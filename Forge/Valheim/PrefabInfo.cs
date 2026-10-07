using System.Numerics;

namespace DrakesForge.Valheim;

/// <summary>What a vanilla prefab is made of, read without decoding meshes or textures.</summary>
public sealed class PrefabInfo
{
    public required string Name { get; init; }
    /// <summary>Script class names on the root object, in order (ZNetView, Piece, WearNTear, …).</summary>
    public required IReadOnlyList<string> Scripts { get; init; }
    /// <summary>Every serialized setting of each root script, with vanilla values.</summary>
    public IReadOnlyList<ComponentInfo> Components { get; init; } = Array.Empty<ComponentInfo>();
    public required IReadOnlyList<RendererInfo> Renderers { get; init; }
    /// <summary>Lights (fire glow, ward light), in hierarchy order.</summary>
    public IReadOnlyList<EffectInfo> Lights { get; init; } = Array.Empty<EffectInfo>();
    /// <summary>Particle effects (flames, sparks, ward bubble).</summary>
    public IReadOnlyList<EffectInfo> Particles { get; init; } = Array.Empty<EffectInfo>();
    public bool HasIcon { get; init; }
    /// <summary>Build cost of a vanilla piece (null for items: their recipes live in ObjectDB, not the prefab).</summary>
    public PieceCost? PieceCost { get; init; }
    /// <summary>Chest/legs armour: the material Valheim paints onto the player's body when worn (ItemDrop m_armorMaterial).</summary>
    public MaterialInfo? ArmorMaterial { get; init; }
    /// <summary>"_snappoint" children, local to the root, in Unity's axes.</summary>
    public IReadOnlyList<Vector3> SnapPoints { get; init; } = Array.Empty<Vector3>();

    internal AssetRef? IconSprite { get; init; }

    /// <summary>Material slots in the order Forge Runtime counts them (renderers in hierarchy order).</summary>
    public IEnumerable<MaterialInfo> MaterialSlots => Renderers.SelectMany(r => r.Materials);
}

public sealed class PieceCost
{
    /// <summary>Hammer tab as Jotunn names it (Misc, Crafting, Building, HeavyBuild, Furniture, …).</summary>
    public required string Category { get; init; }
    /// <summary>Crafting station prefab name, or null when none is needed.</summary>
    public string? Station { get; init; }
    public required IReadOnlyList<(string Item, int Amount, int PerLevel, bool Recover)> Resources { get; init; }
}

public sealed class EffectInfo
{
    public required string Path { get; init; }
    /// <summary>Light colour, or a particle system's start colour (0-1 RGBA).</summary>
    public required float[] Color { get; init; }
    public float Intensity { get; init; }
    public float Range { get; init; }
}

public sealed class RendererInfo
{
    public required string Path { get; init; }
    public required bool Skinned { get; init; }
    /// <summary>False for lower LOD levels and inactive objects (destroyed/worn variants).</summary>
    public required bool Visible { get; init; }
    /// <summary>Part of the worn look (under attach_skin).</summary>
    public bool Worn { get; init; }
    /// <summary>Drawn when the item is worn.</summary>
    public bool VisibleWorn { get; init; }
    public required Matrix4x4 ToRoot { get; init; }
    public required string MeshName { get; init; }
    public required IReadOnlyList<MaterialInfo> Materials { get; init; }

    internal AssetRef? Mesh { get; init; }
}

public sealed class MaterialInfo
{
    public required string Name { get; init; }
    public required string Shader { get; init; }
    /// <summary>_Color, linear 0–1 RGBA (white if the shader has none).</summary>
    public required float[] Color { get; init; }
    public string? MainTextureName { get; init; }
    public IReadOnlyDictionary<string, float> Floats { get; init; } = new Dictionary<string, float>();
    public IReadOnlyList<string> TextureSlots { get; init; } = Array.Empty<string>();
    /// <summary>Texture slot → texture name, for slots that have one.</summary>
    public IReadOnlyDictionary<string, string> Textures { get; init; } = new Dictionary<string, string>();
    /// <summary>Colour properties and their values (_Color, _EmissionColor…).</summary>
    public IReadOnlyDictionary<string, float[]> Colors { get; init; } = new Dictionary<string, float[]>();

    internal IReadOnlyDictionary<string, AssetRef> TextureRefs { get; init; } = new Dictionary<string, AssetRef>();

    internal AssetRef? MainTexture { get; init; }
}

/// <summary>A loaded asset reference inside an <see cref="AssetSession"/>; only valid for that session.</summary>
internal sealed record AssetRef(AssetsTools.NET.Extra.AssetsFileInstance File, long PathId);
