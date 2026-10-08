# Changelog

Two things release from this repo, each with its own version and tag:

- **Drakes Asset Forge** (the desktop app): tags `app-v*`
- **Drakes Forge Runtime** (the BepInEx mod packs need): tags `runtime-v*`, changelog in [Forge/Runtime/thunderstore/CHANGELOG.md](Forge/Runtime/thunderstore/CHANGELOG.md)

## App 0.3.0

Data packs made with 0.3.0 need **Forge Runtime 0.3.0** (their manifest says so).

- Fix: a build/craft cost naming an item by display name ("Piece of Paper"), wrong case ("wood") or a typo crashed the game when the piece was removed (empty refund in `Piece.DropResources`). Cost rows now warn as you type and save the item id; the Publish check and `validate` say what to write; the plain C# export writes the id or leaves the cost out with a TODO. The pack's own items are suggested in the cost autocomplete.
- Fix: **Remove** in the pack list did nothing: the open editor saved the item straight back. Removing now discards the open editor first, and a removed item can never be re-saved. Right-click selects the row under the cursor, so the menu acts on that item.
- **Kitbashing**: Parts on the Look tab add other Valheim prefabs' meshes to your item or piece (a skull on a key, a lantern on a pole), each with its own position, rotation, scale and materials (listed as "Part n · …" in the material list). Drag parts by their purple handle in the viewport, with the same axis locks and edge/grid snapping as snap points; "Only meshes" keeps part of a prefab. **Model scale** resizes the whole model (items: held and dropped; pieces: placed, collision included). Plain C# export and `render`/`validate` support them. Needs Forge Runtime with parts support.
- Pack list: **Duplicate** (Ctrl+D), **Copy** (Ctrl+C) and **Paste** (Ctrl+V), also on right-click. Copies get a free id (`_copy`, `_copy2`…); a reskin's copy becomes its own item or piece. Paste works across packs and brings the item's images along.
- Command line: `inspect`, `validate`, `render` (preview PNG), `new-pack`, and `export-code` flags, so packs can be made and checked without the UI.
- Plain C# export: **Images inside the DLL** (flatten-proof), **Use DrakeModsLibs helpers** (Libs 0.10 `DrakeModsLibs.Forge`), and **Looks only** for mods that register their own items (RenameIt's paper uses all three).
- Sprite sizes and positions no longer show float noise (0.6000000238) after reopening.

## App 0.2.1

Data packs made with 0.2.1 need **Forge Runtime 0.2.1** (their manifest says so).

- **Hexium / Gale package** for the desktop app: self-contained `DrakesAssetForge.exe` (not a plugin DLL), 256×256 `icon.png`, Thunderstore-compatible `manifest.json` / README / CHANGELOG. Install into a profile, then run the exe from `BepInEx/plugins/DrakeMods-DrakesAssetForge/`. Tagged **Tools** + **Modpack**; depends on Forge Runtime 0.2.1 and Jotunn so the profile is ready to push/test packs.
- App and Forge Runtime version numbers and the data-pack runtime dependency pin aligned at 0.2.1. Portable `DrakesAssetForge-*-win-x64.zip` still ships on the GitHub Release.

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
