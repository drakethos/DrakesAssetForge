# Drakes Asset Forge Tool (.exe)

**This package is the desktop TOOL**: the Windows program (`DrakesAssetForge.exe`) you use to create
Valheim items, build pieces, reskins and data packs.

It is **not** [Forge Runtime](#related-packages) and **not** a gameplay mod. Players who only want
finished content install author packs (plus Runtime), not this Tools package.

![Browse Valheim](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/1-browse.png)

## Start here: video guide

**[How to use Drakes Asset Forge (YouTube)](https://www.youtube.com/watch?v=0QPxaB5X7yo)**

## What this tool does

Make Valheim items, pieces and reskins **without Unity**. Browse your install, restyle meshes and
materials, change components, snap points, sprites, fire and glow, then push into a running game or
publish a small data pack.

Packs never contain Valheim's models or textures. Meshes and materials are borrowed **by name** from
your own install when the game loads, so a pack is a few small text files plus any PNGs you made.

## How to use it

The app runs in four steps, shown as tabs at the top:

1. **Browse Valheim**: every item and build piece in your install, with a 3D preview, materials,
   icon, components and vanilla build cost. Add the ones you want to work from to your working list.
2. **Import**: each one becomes a **new item** (its own ID, copies the vanilla cost), a **reskin**
   (changes the vanilla one everywhere), or a **source** (kept only to borrow meshes and materials).
3. **Work**: per item, the *Look* tab picks its mesh, materials, tint, gloss and icon; *Components*
   edits any script setting; *Recipe* sets costs and crafting station; *Snap* sets where pieces
   connect. Edits auto-save.
4. **Publish**: make a data pack, a C# mod project, or plain C# code.

**Push to game** copies your pack into a mod profile and hot-reloads it while Valheim runs. Turn on
**Live push** to send every save straight to the game.

## What you can do

- **Restyle**: borrow any prefab's mesh or material from a visual picker, tint it, adjust gloss and
  metallic, replace or export any texture slot (normal maps too), and render an icon from the viewport.
  Armour shows as worn, including the textures it paints onto the player's body.
- **Change anything**: every setting on the base's scripts (ItemDrop, Piece, WearNTear, Door,
  Container and more) with its vanilla value, proper controls, search, and one-click reset.
- **Snap points**: auto-detected from the model's shape, or drag numbered points in the viewport.
- **Kitbashing**: build new shapes from pieces of Valheim with no modelling. Scale a key up, put a
  skull on it, give each part its own materials.
- **Sprites**: flat pictures (PNG with transparency) on any item or piece: a banner, a sign, a poster,
  or a note on the floor. Or hide the base model so the picture is the whole look.
- **Fire, light and glow**: recolour a brazier's flames and light, or make a material glow.
- **Remove and add components**: a ward without warding, or a Rigidbody, a light, or any Valheim
  script with the game's own defaults.
- **Publish** as either:
  - a **data pack**: a Thunderstore-ready zip that needs Forge Runtime, or
  - a **C# mod project**: Forge compiled into your own mod, with a `Customize\<Item>.cs` file per item
    for your own code. Re-export any time; your code is kept.
  - **plain C#**: each item as readable Jotunn code. No Forge and no pack files.

| | |
|---|---|
| ![Workspace](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/3-workspace-look.png) | ![Components](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/3d-components.png) |
| ![Material picker](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/3f-material-picker.png) | ![Fire and glow](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/7-ward-fire.png) |
| ![Sprite banner](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/8-sprite-banner.png) | ![Snap tool](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/9-snap-tool.png) |
| ![Publish](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/4-publish.png) | ![Settings](https://raw.githubusercontent.com/drakethos/DrakesAssetForge/main/docs/screenshots/6-settings.png) |

## Install (Gale / Hexium)

1. Install this **Tools** package into a Valheim profile. BepInEx and Jotunn come with the usual
   setup; **Forge Runtime** is listed as a dependency so push and test work in that profile.
2. Open the profile folder → `BepInEx/plugins/DrakeMods-DrakesAssetForgeTool/`.
3. Run **`DrakesAssetForge.exe`**. Keep **`DrakesAssetForge.pdb`** next to the exe.
4. In ⚙ **Settings**, point the app at Valheim and at this same mod-manager profile. Settings can
   also install Forge Runtime into that profile for you.

You still need Valheim installed on the machine. The app reads it and never changes game files.

## What is in this package

- `DrakesAssetForge.exe`: the Forge tool (self-contained Windows app, no separate .NET install)
- `DrakesAssetForge.pdb`: required sidecar next to the exe
- Store metadata: `manifest.json`, `icon.png`, this README, `CHANGELOG.md`
- Bundled `runtime/` and `forge-src/` helpers the app uses for install and export

## Related packages

| Package | Role | Identity |
|---|---|---|
| **Drakes Asset Forge Tool** (this) | Authoring **program** (`.exe`) | `DrakeMods-DrakesAssetForgeTool` · [app releases](https://github.com/drakethos/DrakesAssetForge/releases?q=app-v) |
| **Drakes Forge Runtime** | BepInEx mod that **loads** Forge data packs | `DrakeMods-DrakesForgeRuntime` · [runtime releases](https://github.com/drakethos/DrakesAssetForge/releases?q=runtime-v) |
| **Data packs / content mods** | What you publish from the tool for players | Made in-app (Publish → data pack, C# or plain C#) |

Forge Runtime updates only when the loader itself changes, not on every Tools or app release.

## Portable zip (optional)

GitHub Releases also attach `DrakesAssetForge-<version>-win-x64.zip` if you'd rather run the app
outside a mod profile. It's the same exe and pdb.

## Coming next

- **Model export**: any Valheim mesh with its textures as `.glb`, to open in Blender
- **Model import**: your own static `.glb` meshes in packs, loaded by Forge Runtime

## Links

- Video guide: https://www.youtube.com/watch?v=0QPxaB5X7yo
- Source and docs: https://github.com/drakethos/DrakesAssetForge
- Full guide and build notes: https://github.com/drakethos/DrakesAssetForge#readme
- App changelog: `CHANGELOG.md` in this package
- Issues and ideas: https://github.com/drakethos/DrakesAssetForge/issues
