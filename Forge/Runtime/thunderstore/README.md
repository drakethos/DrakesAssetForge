# Drakes Forge Runtime

Loads **Forge packs**: Valheim items, build pieces and reskins made with
[Drakes Asset Forge](https://github.com/drakethos/DrakesAssetForge), built at load time from your own game's prefabs.

A pack is plain files (recipes + the author's PNGs). It never contains Valheim's models or textures: meshes and
materials are borrowed by name from your install, so packs are tiny and need no asset bundles.

## For players

Install this and the pack you want. That's it. Packs installed under `BepInEx/plugins` are found automatically.

## For pack makers

- Made with the Drakes Asset Forge desktop app (browse Valheim, import, edit, publish).
- Packs in the app's push folder hot-reload while the game runs: look, settings, snap points and costs update live.
- A pack exported as a **C# mod** compiles Forge into the mod itself and doesn't need this runtime.

Requires Jotunn.
