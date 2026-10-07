# Changelog

Two things release from this repo, each with its own version and tag:

- **Drakes Asset Forge** (the desktop app): tags `app-v*`
- **Drakes Forge Runtime** (the BepInEx mod packs need): tags `runtime-v*`, changelog in [Forge/Runtime/thunderstore/CHANGELOG.md](Forge/Runtime/thunderstore/CHANGELOG.md)

## App 0.1.0

First release of the rewritten app (the earlier Catalog/Project/Export tool lives in `legacy/`).

- Browse every Valheim item and piece with a real 3D preview, materials and shaders, icons, components and build cost, read straight from your install.
- Import wizard: new item, reskin, or source-only; new pieces copy the vanilla build cost.
- Workspace: borrow meshes and materials with a visual picker, tint with a colour picker, per-texture-slot replace/export, worn view and body textures for armour, every component setting editable with vanilla values and reset, recipe, snap points, glow.
- Push to game: installs the pack unzipped into a chosen Gale / r2modman / Thunderstore Mod Manager profile; hot-reloads while playing.
- Publish: Thunderstore-ready data pack zip (README, CHANGELOG, icon made from your items), or a C# mod project with Forge compiled in and a Customize file per item for your own code.
