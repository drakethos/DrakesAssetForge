using Jotunn.Configs;
using UnityEngine;

namespace DrakesForge.Runtime;

/// <summary>Friendly recipe names → Valheim prefab names, plus helpers for matching Unity object names.</summary>
internal static class GameNames
{
    /// <summary>Child objects Forge adds to a prefab. Restore and instance sync find them by these names.</summary>
    public const string VisualChild = "forge_visual";
    public const string GlowChild = "forge_glow";
    public const string SpritesChild = "forge_sprites";

    public static string? Station(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        switch (name!.Trim().ToLowerInvariant())
        {
            case "none": return null;
            case "workbench": return CraftingStations.Workbench;
            case "forge": return CraftingStations.Forge;
            case "stonecutter": return CraftingStations.Stonecutter;
            case "cauldron": return CraftingStations.Cauldron;
            case "artisan":
            case "artisantable": return CraftingStations.ArtisanTable;
            case "blackforge": return CraftingStations.BlackForge;
            case "galdr":
            case "galdrtable": return CraftingStations.GaldrTable;
            case "meadketill": return CraftingStations.MeadKetill;
            case "preptable":
            case "foodpreparationtable": return CraftingStations.FoodPreparationTable;
            default: return name;
        }
    }

    public static string PieceTable(string? name)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "hammer": return PieceTables.Hammer;
            case "hoe": return PieceTables.Hoe;
            case "cultivator": return PieceTables.Cultivator;
            case "servingtray": return PieceTables.ServingTray;
            default: return name!;
        }
    }

    /// <summary>"iron_grate(Clone)" or "iron_grate (1)" → "iron_grate".</summary>
    public static string PrefabName(string objectName)
    {
        var cut = objectName.IndexOf('(');
        return (cut < 0 ? objectName : objectName.Substring(0, cut)).Trim();
    }

    /// <summary>"bronze (Instance)" → "bronze".</summary>
    public static string MaterialName(Material material) =>
        material.name.Replace(" (Instance)", "").Trim();
}
