# Drakes Asset Forge

Make Valheim items, build pieces and reskins **without Unity**: browse the game, pick a starting point, restyle it,
change its stats, and push it straight into your game while it's running.

Packs never contain Valheim's models or textures. Meshes and materials are borrowed **by name** from the player's own
install when the game loads, so a pack is a few small text files plus whatever PNGs you made.

![Browse Valheim](docs/screenshots/1-browse.png)

## What you can do

- **Browse Valheim**: every item and build piece in your install with a real 3D preview, its materials and shaders,
  its icon, its components and its vanilla build cost. Add what you want to work from to a working list.
- **Import**: each one becomes a *new item* (its own ID, copies the vanilla cost), a *reskin* (changes the vanilla
  one everywhere), or a *source* to borrow meshes and materials from.
- **Restyle**: borrow any prefab's mesh or material from a visual picker, tint with a colour picker, gloss/metallic,
  replace or export any texture slot (normal maps too), render an icon from the viewport.
  Armour shows as worn, including the textures it paints onto the player's body.
- **Change anything**: every setting on the base's scripts (ItemDrop, Piece, WearNTear, Door, Container…) with its
  vanilla value, proper controls (dropdowns for enums, toggles, colours), search, and one-click reset. Costs, crafting
  station, hammer tab, snap points, and a glow light.
- **Push to game**: installs the pack into your Gale / r2modman / Thunderstore Mod Manager profile and hot-reloads it
  while Valheim runs.
- **Publish** as either:
  - a **data pack**: a Thunderstore-ready zip (README, CHANGELOG, icon made from your items) that needs
    [Forge Runtime](#forge-runtime), or
  - a **C# mod project**: Forge compiled into your own mod (no runtime dependency) with a `Customize\<Item>.cs` file
    per item for your own code. Re-export any time; your code is kept.

| | |
|---|---|
| ![Workspace](docs/screenshots/3-workspace-look.png) | ![Components](docs/screenshots/3d-components.png) |
| ![Material picker](docs/screenshots/3f-material-picker.png) | ![Armour body textures](docs/screenshots/5b-dress-body.png) |
| ![Publish](docs/screenshots/4-publish.png) | ![Settings](docs/screenshots/6-settings.png) |

## Get it

Download **`DrakesAssetForge-<version>-win-x64.zip`** from [Releases](https://github.com/drakethos/DrakesAssetForge/releases),
unzip anywhere, run `DrakesAssetForge.exe`. No .NET install needed.

You need Valheim installed (the app reads it; it never changes it) and a mod-manager profile with BepInEx and
Jotunn to test in. ⚙ Settings can install Forge Runtime into that profile for you.

## Forge Runtime

The small (≈80 KB) BepInEx mod that loads data packs. Players install it once, plus any packs.
Releases are attached to `runtime-v*` [releases](https://github.com/drakethos/DrakesAssetForge/releases);
details in [Forge/Runtime/thunderstore/README.md](Forge/Runtime/thunderstore/README.md).

## Build from source

```
dotnet run --project Forge/App            # the app
dotnet build Forge/Forge.slnx             # everything
dotnet test Forge/Format.Tests
```

Building `Forge/Runtime` needs game references: locally from `environment.props` (Valheim path + your mod profile;
see [Forge/README.md](Forge/README.md)), in CI from the Pfhoenix reference package + Jotunn (`-p:ForgeRefsDir=…`).

| Folder | |
|---|---|
| `Forge/App` | the desktop app (Avalonia) |
| `Forge/Valheim` | reads the player's install: catalog, meshes, materials, icons, component fields; software renderer |
| `Forge/Format` | pack/recipe format (no dependencies), shared by app and runtime |
| `Forge/Runtime` | Forge Runtime (BepInEx), also compiled into C# mod exports |
| `Forge/samples` | example pack |
| `legacy/` | the previous Catalog/Project/Export app, kept for reference (not built) |

Format, recipe keys and the C# export layout: [Forge/README.md](Forge/README.md).

## Releasing

One workflow, two release trains, picked by tag (version must match the project file):

| Tag | Builds | Publishes |
|---|---|---|
| `app-v0.1.0` | `Forge/App` self-contained win-x64 (with Forge Runtime + source bundled) | GitHub Release |
| `runtime-v0.1.0` | `Forge/Runtime` Thunderstore package | GitHub Release; Thunderstore and Hexium only when enabled |

Store uploads are off until you turn them on with repository variables `PUBLISH_THUNDERSTORE=true` /
`PUBLISH_HEXIUM=true` (secrets `THUNDERSTORE_TOKEN`, `HEXIUM_TOKEN`).

## License

MIT, see [LICENSE](LICENSE).
