using System.Collections.Generic;
using DrakesForge.Format.Json;

namespace DrakesForge.Format;

/// <summary>forgepack.json at the root of a pack folder.</summary>
public sealed class ForgePack
{
    public const int CurrentFormat = 1;
    public const string FileName = "forgepack.json";
    public const string ItemsFolder = "items";

    public int Format { get; set; } = CurrentFormat;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Thunderstore website_url.</summary>
    public string Website { get; set; } = "";
    /// <summary>Loaded by its own mod's embedded Forge (C# mod export); the shared runtime leaves it alone.</summary>
    public bool Embedded { get; set; }
}

public enum RecipeKind
{
    /// <summary>New item cloned from an ItemDrop prefab.</summary>
    Item,
    /// <summary>New build piece cloned from a Piece prefab.</summary>
    Piece,
    /// <summary>Changes the look of an existing vanilla prefab in place. No new id.</summary>
    Reskin
}

/// <summary>
/// One item/piece/reskin. Everything vanilla is referenced by name and resolved at load from the
/// player's own install; only files under the pack (textures, models, icons) ship.
/// </summary>
public sealed class ItemRecipe
{
    public string Id { get; set; } = "";
    public RecipeKind Kind { get; set; } = RecipeKind.Item;
    /// <summary>Vanilla prefab cloned (Item/Piece) or modified (Reskin).</summary>
    public string Base { get; set; } = "";
    public string? Name { get; set; }
    public string? Description { get; set; }

    public LookRecipe Look { get; set; } = new();

    /// <summary>Component type name → field path (dots for nested) → value. e.g. WearNTear → m_health → 1200.</summary>
    public Dictionary<string, Dictionary<string, JsonValue>> Fields { get; set; } = new();

    public List<BehaviourRecipe> Behaviours { get; set; } = new();
    public CraftRecipe? Craft { get; set; }
    public SnapRecipe? Snap { get; set; }

    /// <summary>File the recipe was read from. Not serialized.</summary>
    public string? SourcePath { get; set; }
}

public sealed class LookRecipe
{
    public MeshSource? Mesh { get; set; }
    public List<MaterialOverride> Materials { get; set; } = new();
    /// <summary>Pack-relative PNG. Null keeps the base icon.</summary>
    public string? Icon { get; set; }

    public bool IsEmpty => Mesh == null && Materials.Count == 0 && Icon == null;
}

/// <summary>Replace the base's visible mesh with another vanilla prefab's or a pack model file.</summary>
public sealed class MeshSource
{
    public string? Prefab { get; set; }
    /// <summary>Pack-relative .glb.</summary>
    public string? File { get; set; }
}

/// <summary>
/// Changes material slots. Matches by original material <see cref="Target"/> name, or flattened
/// <see cref="Slot"/> index (renderers in hierarchy order). Neither set = every slot.
/// </summary>
public sealed class MaterialOverride
{
    /// <summary>
    /// Target for chest/leg armour's body material (ItemDrop m_armorMaterial, shader Custom/Player): the look
    /// painted onto the player when worn. Its textures are _ChestTex, _LegsTex, _ChestBumpMap, _ChestMetal, …
    /// </summary>
    public const string ArmorTarget = "@armor";

    public string? Target { get; set; }
    public int? Slot { get; set; }

    /// <summary>Borrow the material from this vanilla prefab (first material, or <see cref="FromMaterial"/>).</summary>
    public string? FromPrefab { get; set; }
    /// <summary>Borrow a material by name (alone, or narrowed to <see cref="FromPrefab"/>).</summary>
    public string? FromMaterial { get; set; }

    public string? Shader { get; set; }
    /// <summary>#RRGGBB or #RRGGBBAA applied to _Color.</summary>
    public string? Tint { get; set; }
    /// <summary>Shader property → pack-relative PNG.</summary>
    public Dictionary<string, string> Textures { get; set; } = new();
    public Dictionary<string, float> Floats { get; set; } = new();
    public Dictionary<string, string> Colors { get; set; } = new();
}

/// <summary>A Forge-provided component configured from data (glow, …).</summary>
public sealed class BehaviourRecipe
{
    public string Type { get; set; } = "";
    public Dictionary<string, JsonValue> Settings { get; set; } = new();
}

public sealed class CraftRecipe
{
    /// <summary>Crafting station: workbench, forge, stonecutter, artisan, blackforge, galdr, cauldron, or a prefab name.</summary>
    public string? Station { get; set; }
    public int StationLevel { get; set; } = 1;
    /// <summary>Pieces only: Hammer, Hoe, Cultivator, or a piece table prefab.</summary>
    public string? Tool { get; set; }
    /// <summary>Pieces only: hammer tab (Building, Furniture, Crafting, Misc, or a custom name).</summary>
    public string? Category { get; set; }
    public List<Requirement> Requirements { get; set; } = new();
}

public sealed class Requirement
{
    public string Item { get; set; } = "";
    public int Amount { get; set; } = 1;
    public int AmountPerLevel { get; set; }
    public bool Recover { get; set; } = true;
}

public enum SnapMode
{
    /// <summary>Use the base's snap points.</summary>
    Keep,
    /// <summary>Remove the base's and use <see cref="SnapRecipe.Points"/>.</summary>
    Replace,
    /// <summary>Base's plus <see cref="SnapRecipe.Points"/>.</summary>
    Add
}

public sealed class SnapRecipe
{
    public SnapMode Mode { get; set; } = SnapMode.Keep;
    public List<Vec3> Points { get; set; } = new();
}

public struct Vec3
{
    public float X;
    public float Y;
    public float Z;

    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
