# Changelog

Two things release from this repo, each with its own version and tag:

- **Drakes Asset Forge** (the desktop app): tags `app-v*`
- **Drakes Forge Runtime** (the BepInEx mod packs need): tags `runtime-v*`, changelog in [Forge/Runtime/thunderstore/CHANGELOG.md](Forge/Runtime/thunderstore/CHANGELOG.md)

## App 0.2.1

Data packs made with 0.2.1 need **Forge Runtime 0.2.1** (their manifest says so).

- Packaging release cut from the 0.2.0 line: no functional changes since `app-v0.2.0` / `runtime-v0.2.0`. App and Forge Runtime version numbers and the data-pack runtime dependency pin are aligned at 0.2.1.

## App 0.2.0

Data packs made with 0.2.0 need **Forge Runtime 0.2.0** (their manifest says so).

- Sprites: put flat images (PNG with transparency) on any item or piece: signs, banners, posters, a note lying on the floor. Size, position, rotation, one or both sides, with "Stand up" / "Lie flat" presets and a live preview; optionally hide the base model so the sprite is the whole look.
- Snap points, redone: auto-detect from the model's shape (floors and walls get their face corners, beams their ends, anything else its 8 corners) plus one-click bottom/top corners, edge middles and centre line; drag numbered points in the viewport, locked to X, Y, Z or level, sticking to the model's edges and middle or a grid; every point is named ("bottom-left corner", "top edge middle") with a front/right/up compass in the viewport; Delete removes the selected point.
- Fire & lights on the Look tab: light colour, brightness and reach, flame colour, for anything with fire or lights (braziers, wards, torches).
- Material glow: emission colour plus strength (colours accept `#RRGGBB*k` for HDR).
- Components: remove any script with an "are you sure?" that says what breaks, undo from the Removed list; add Rigidbody, colliders, lights or any of Valheim's ~380 scripts, starting from the game's own defaults.
- Publish: **Plain C#** output. Each item becomes readable Jotunn code with typed settings, plus one helper file, with no Forge and no pack files. Also `export-code <pack> <out> --plain`.

## App 0.1.0

First release of the rewritten app (the earlier Catalog/Project/Export tool lives in `legacy/`).

- Browse every Valheim item and piece with a real 3D preview, materials and shaders, icons, components and build cost, read straight from your install.
- Import wizard: new item, reskin, or source-only; new pieces copy the vanilla build cost.
- Workspace: borrow meshes and materials with a visual picker, tint with a colour picker, per-texture-slot replace/export, worn view and body textures for armour, every component setting editable with vanilla values and reset, recipe, snap points, glow.
- Push to game: installs the pack unzipped into a chosen Gale / r2modman / Thunderstore Mod Manager profile; hot-reloads while playing.
- Publish: Thunderstore-ready data pack zip (README, CHANGELOG, icon made from your items), or a C# mod project with Forge compiled in and a Customize file per item for your own code.
