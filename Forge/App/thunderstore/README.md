# Drakes Asset Forge Tool (.exe)

**This package is the desktop TOOL** — the Windows program (`DrakesAssetForge.exe`) authors use to
create Valheim items, build pieces, reskins, and data packs.

It is **not** [Forge Runtime](#related-packages) and **not** a gameplay mod. Players who only want
finished content install author packs (plus Runtime), not this Tools package.

Hexium / Thunderstore identity: **`DrakeMods-DrakesAssetForgeTool`** (distinct from
`DrakeMods-DrakesForgeRuntime`).

![Browse Valheim](screenshots/1-browse.png)

## What this tool does

Make Valheim items, pieces and reskins **without Unity**: browse your install, restyle meshes and
materials, edit components, snap points, sprites, fire and glow — then push into a running game or
publish a tiny data pack.

| | |
|---|---|
| ![Workspace](screenshots/3-workspace-look.png) | ![Components](screenshots/3d-components.png) |
| ![Material picker](screenshots/3f-material-picker.png) | ![Fire and glow](screenshots/7-ward-fire.png) |
| ![Sprite banner](screenshots/8-sprite-banner.png) | ![Snap tool](screenshots/9-snap-tool.png) |
| ![Publish](screenshots/4-publish.png) | ![Settings](screenshots/6-settings.png) |

## Install (Gale / Hexium)

1. Install this **Tools** package into a Valheim profile (BepInEx + Jotunn come with the usual
   setup; **Forge Runtime** is listed as a dependency so push/test works in that profile).
2. Open the profile folder → `BepInEx/plugins/DrakeMods-DrakesAssetForgeTool/`.
3. Run **`DrakesAssetForge.exe`**. Keep **`DrakesAssetForge.pdb`** next to the exe (required by
   the self-contained native host / Hexium packaging checks).
4. In ⚙ Settings, point the app at Valheim and this same mod-manager profile.

You still need Valheim installed on the machine (the app reads it; it never changes game files).

## What is in this package

- `DrakesAssetForge.exe` — the Forge tool (self-contained Windows app; no separate .NET install)
- `DrakesAssetForge.pdb` — required sidecar next to the exe
- Store metadata: `manifest.json` (`name`: `DrakesAssetForgeTool`), `icon.png` (TOOL / .EXE badge),
  this README, `CHANGELOG.md`
- Screenshots under `screenshots/`
- Bundled `runtime/` + `forge-src/` helpers the app uses for install/export

## Related packages

| Package | Role | Identity |
|---|---|---|
| **Drakes Asset Forge Tool** (this) | Authoring **program** (`.exe`) | `DrakeMods-DrakesAssetForgeTool` · [app releases](https://github.com/drakethos/DrakesAssetForge/releases?q=app-v) |
| **Drakes Forge Runtime** | BepInEx mod that **loads** Forge data packs | `DrakeMods-DrakesForgeRuntime` · [runtime releases](https://github.com/drakethos/DrakesAssetForge/releases?q=runtime-v) |
| **Data packs / content mods** | What you publish from the tool for players | Made in-app (Publish → data pack or C# / plain C#) |

Forge Runtime is updated only when the loader itself changes — not on every Tools / app release.
This Tools package depends on the current published Runtime so profiles stay ready for push and test.

## Portable zip (optional)

GitHub Releases also attach `DrakesAssetForge-<version>-win-x64.zip` if you prefer to unzip the
app outside a mod profile. Same exe + pdb either way:
https://github.com/drakethos/DrakesAssetForge/releases

## Links

- Source & docs: https://github.com/drakethos/DrakesAssetForge
- App changelog: see `CHANGELOG.md` in this package
- Issues / ideas: https://github.com/drakethos/DrakesAssetForge/issues
