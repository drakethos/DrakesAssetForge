# Drakes Asset Forge

Make Valheim items, build pieces and reskins **without Unity**. Browse your install, restyle
meshes and materials, change components, snap points, sprites, fire and glow — then push into a
running game or publish a tiny data pack.

This Hexium / Gale package ships the **Windows desktop app** as a self-contained
`DrakesAssetForge.exe` (not a BepInEx plugin DLL), plus the package icon and store files.

## Install (Gale / Hexium)

1. Install this package into a Valheim profile (BepInEx + Jotunn come with the usual setup;
   **Forge Runtime** is a dependency and installs automatically).
2. Open the profile folder → `BepInEx/plugins/DrakeMods-DrakesAssetForge/`.
3. Run **`DrakesAssetForge.exe`**.
4. In ⚙ Settings, point the app at Valheim and this same mod-manager profile so **Push to game**
   and **Install Forge Runtime** target the right place.

You still need Valheim installed on the machine (the app reads it; it never changes game files).

## What you get

- Self-contained **exe** — no separate .NET install.
- Package **icon** for the Hexium / Gale mod list.
- A **modpack-style** install: this package pulls **Forge Runtime** (and Jotunn) so the profile is
  ready to load packs you push or publish.

## Portable zip

GitHub Releases also attach `DrakesAssetForge-<version>-win-x64.zip` if you prefer to unzip the
app somewhere outside the profile. Same exe either way.

## Forge Runtime

Data packs need [Drakes Forge Runtime](https://github.com/drakethos/DrakesAssetForge/releases)
(also on Hexium as `DrakeMods-DrakesForgeRuntime`). C# mod exports can compile Forge in and skip
the runtime.

## Links

- Source & docs: https://github.com/drakethos/DrakesAssetForge
- Changelog: see `CHANGELOG.md` in this package
