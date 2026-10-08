# Changelog

## 0.4.0

- `look.mesh.file`: a pack's own static `.glb` replaces the base mesh. Each submesh gets its base colour texture; rigged models use their rest pose. Warnings are logged, and the base mesh stays if the file can't be read.
- `look.hold`: offsets on an item's held pose (position, rotation in degrees, scale). Applies to the item's attach child, so held and dropped items both follow it; placed and dropped copies sync on hot reload.
- Fix: the model file and borrowed meshes sit under the item's visual root, so held items show them.
- Fix: a material override on a model file matches its slot by name (`glb default`, or the file's material name), so textures and tints apply.

## 0.3.0

- Fix: costs naming a missing item no longer leave an empty requirement (which crashed removing the piece): a pack item's display name is mapped to its id, anything unknown is left out with a warning, and empty entries are stripped after registration.
- `look.parts` (kitbashing): other vanilla prefabs' meshes placed on the model, each with position, rotation, scale, an optional mesh-name filter and its own material overrides. On items they sit under `attach`, so they show held and dropped.
- `look.scale`: scales the whole model (items: the held/dropped visual; pieces: the root, collision included).
- Fix: `hideMesh` now hides only the model's own meshes, never parts or sprites.

## 0.2.1

- Packaging release: version aligned with Drakes Asset Forge 0.2.1. No functional changes since 0.2.0.

## 0.2.0

- `look.sprites`: flat images (alpha cut out, one or two sided) placed on the prefab; `look.hideMesh` hides the model's own mesh.
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
