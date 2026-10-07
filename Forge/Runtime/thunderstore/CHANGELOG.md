# Changelog

## Unreleased

- `effects`: recolour every light and particle effect (fire, ward glow), scale light brightness and reach.
- `components`: `remove` scripts (a ward without warding) and `add` Unity components or Valheim scripts.
- Colours accept a strength, `#RRGGBB*k`, for HDR emission; setting `_EmissionColor` turns emission on.

## 0.1.0

- First release: loads Forge packs (items, build pieces, reskins) from BepInEx/plugins and the Forge app's push folder.
- Looks: borrow meshes and materials from any vanilla prefab, tint, gloss/metallic, textures (incl. normal maps), icons.
- Armour body material (`@armor`): retexture what chest and leg armour paints onto the player.
- Any component field, Forge behaviours (glow), costs, crafting stations, hammer tabs, snap points.
- Hot reload of installed and pushed packs while the game runs.
- Embeddable: C# mod exports compile Forge in; their packs are skipped here so nothing registers twice.
