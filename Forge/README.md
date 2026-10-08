# Forge v2 — app, packs, runtime

A **pack** is a folder of plain files. **Forge Runtime** (one BepInEx plugin) builds it in-game from the
player's own Valheim prefabs. No Unity, no AssetBundles, no Jotunn mocks, no Valheim files shipped.
The **Forge app** is where you make packs.

```
dotnet run --project Forge/App          # the app
dotnet build Forge/Forge.slnx           # everything (also deploys the runtime to the test profile)
dotnet test Forge/Format.Tests
```

| Folder | What |
|---|---|
| `App/` | **Drakes Asset Forge** (Avalonia). Browse Valheim → Import → Workspace → Publish. |
| `Valheim/` | Reads the player's install (SoftRef bundles): catalog, prefab structure, meshes, materials/shaders, icons, piece costs, snap points, and a software renderer for previews. Read-only. |
| `Format/` | Pack/recipe model + JSON (netstandard2.0, no dependencies). Used by the app; compiled into the runtime. |
| `Runtime/` | `DrakesForgeRuntime.dll` BepInEx plugin. Builds to the profile in `environment.props` (Gale `drakeTest`). |
| `Format.Tests/` | Format tests. |
| `Probe/` | Dev tool: `dotnet run --project Forge/Probe -- iron_grate --render --props` prints what the reader sees and writes preview PNGs to `%TEMP%\forge-probe`. |
| `samples/BronzeBuilds/` | Example pack. Debug builds of the runtime copy it into the push folder. Open it in the app with *Open pack folder…*. |

## The app

1. **Browse Valheim**: every item and piece in your install, with a real 3D preview (model, materials and shaders, icon), components and build cost. **+** adds to the working list.
2. **Import**: each listed prefab becomes a **New item** (own ID; pieces copy the vanilla build cost), a **Reskin** (same ID, new look), or a **Source** (kept around to borrow meshes and materials from).
3. **Workspace**: per item, *Look* (mesh from another prefab, per-material borrow/tint/gloss/metallic/texture, icon from PNG or rendered from the viewport), *Components* (any field, plus Forge behaviours like glow), *Recipe*, *Snap* (auto-detect from the shape, drag numbered points in the viewport with axis locks and edge/grid snapping). Edits auto-save; with **Live push** on, every save goes straight to a running game.
4. **Publish**: checks, what ships, and a Thunderstore-ready zip in `Documents\DrakesAssetForge\Releases`.

Packs live in `Documents\DrakesAssetForge\Packs\<id>` by default (⚙ Settings › Packs folder) (a `.forge/` folder in each holds app-only state and never ships).
Icons are cached in `%LocalAppData%\DrakesAssetForge\cache`.

`DrakesAssetForge screenshot <folder>` walks the flow headlessly on a scratch pack and saves PNGs of each screen.

These projects don't use the suite `Directory.Build.props`/`.targets`, so there's no DrakeModsLibs reference: the runtime is its own Thunderstore package.

## Where packs load from

- **Installed:** any folder under `BepInEx/plugins` that has a `forgepack.json` (a pack published to Thunderstore).
- **Push folder:** `%LocalAppData%\DrakesAssetForge\Push` (config `Development.PushFolders`), the app's default push target.

**Push to game** (app ⚙ Settings) installs the pack unzipped into a chosen mod profile, `BepInEx\plugins\<Author>-<Pack>` (Gale, r2modman, Thunderstore Mod Manager and plain BepInEx installs are detected), or into the push folder above. Every loaded pack's folder is watched: saving **hot-reloads** while you play (look, fields, snap points, glow, costs; placed/dropped copies update, re-equip armour). A new id, or a changed `kind`/`base`, needs a restart. If the same pack id is in both the push folder and a profile, the push-folder copy wins.

## C# mod export

Publish › **C# mod project** writes a BepInEx project with Forge compiled in from source (namespaced into your mod, ~80 KB; no Forge Runtime dependency):

| File | Owner |
|---|---|
| `Forge\`, `ForgeHooks.g.cs`, `Pack\` (marked `"embedded": true`), `manifest.json` | regenerated on every export |
| `<Name>.csproj`, `<Name>Plugin.cs`, `Customize\<Item>.cs`, `README.md`, `environment.props` | written once, then yours |

`Customize\<Item>.cs` gets `OnBuilt(ForgeBuiltContext item)`: runs right after Forge builds the item (and after each hot reload), with `item.Prefab`, `item.Shared` (ItemDrop settings), `item.Piece`, `item.AddComponentOnce<T>()`.
*Into my existing mod* adds only `Forge\`, hooks, `Customize\`, `Pack\` and a `FORGE.md` with the one `ForgeHost.Start(...)` line to add. `dotnet build` deploys to the profile in `environment.props` (`-p:ForgeDeploy=false` to skip); Release builds also make the Thunderstore zip.
A mod's embedded Forge claims its pack id, so the shared runtime skips any data-pack copy of the same pack.

Publish › **Plain C#** instead writes each item as straight-line Jotunn code (`Items\<Item>.g.cs`, typed field
assignments, materials, sprites, fire colours, snap points) plus one helper (`Lite\ForgeLite.g.cs`) and the images in
`Assets\`. No Forge and no pack files; Jotunn is the only dependency. `Customize\<Item>.cs` gets `OnBuilt(GameObject prefab)`.

Plain C# options (Publish checkboxes, or CLI flags):

| Option | Flag | What it does |
|---|---|---|
| Images inside the DLL | `--embed` | images become embedded resources; nothing to lose when Hexium/Gale flatten folders |
| Use DrakeModsLibs helpers | `--libs` | generated code calls `DrakeModsLibs.Forge` (Libs 0.10+) instead of writing `Lite\ForgeLite.g.cs` |
| Looks only | `--look-only` | for mods that register items themselves: per recipe `Build(parent, scale)` (sprites), `Dress(prefab)` (materials, mesh, settings…), `Icon` |
| | `--namespace N`, `--folder sub/dir` | namespace of the generated code; write it under a subfolder of the project |

RenameIt's paper is the reference use: `DrakesRenameIt/Forge/Paper` (the pack) generates `Paper/Generated` with
`--look-only --libs --embed --into`.

## Command line

```
DrakesAssetForge inspect <prefab> [--json]          scripts, materials, snap points, size, lights
DrakesAssetForge validate <pack folder>             recipe problems, missing files, unknown bases
DrakesAssetForge render <pack> <id> <out.png> [--yaw deg --pitch deg --size px --worn]
DrakesAssetForge new-pack <folder> <id> <name> <author>
DrakesAssetForge export-code <pack> <out> [--plain] [--libs] [--embed] [--look-only] [--into] [--namespace N] [--folder dir]
DrakesAssetForge screenshot <folder>
```

These read Valheim the same way the app does, so packs can be written as JSON by hand or by a script, checked, and
previewed without opening the UI.

## Pack layout

```
BronzeBuilds/
  forgepack.json          { "format": 1, "id": "BronzeBuilds", "name": "…", "version": "0.1.0", "author": "…" }
  items/*.json            one recipe per file (subfolders OK)
  textures/*.png          your files; recipe paths are relative to the pack root
  README.md, CHANGELOG.md, icon.png   Thunderstore files (edited on the Publish screen)
```

## Recipe

`//` line comments are allowed.

```jsonc
{
  "id": "drake_bronze_gate",        // new prefab name (reskin: same as base)
  "kind": "piece",                  // item | piece | reskin
  "base": "iron_grate",             // vanilla prefab to clone (or modify, for reskin)
  "name": "Bronze Gate",
  "description": "…",

  "look": {
    "mesh": { "prefab": "wood_gate" },          // show another prefab's mesh (static meshes, LOD0); or { "file": "models/box.glb" } for your own .glb
    "materials": [
      {
        "target": "iron_grate_bars",            // match by material name…  or "slot": 0 …  or neither = all slots
        "from": "SwordBronze",                  // borrow its first material, or { "prefab": "X", "material": "name" }, or { "material": "name" }
        "shader": "Custom/Piece",
        "tint": "#C48A48",                      // _Color
        "textures": { "_MainTex": "textures/bronze_patina.png" },
        "floats": { "_Metallic": 0.7 },
        "colors": { "_EmissionColor": "#66AAFF*2.5" }   // "*k" = strength (HDR glow); setting emission turns it on
      }
    ],
    "sprites": [                                // flat images: pivot bottom centre, facing +Z (forward), alpha cut out
      { "file": "textures/banner.png", "size": [0.6, 1.2], "position": [0, 0.5, 0.12], "rotation": [0, 0, 0], "doubleSided": true }
    ],
    "hideMesh": false,                          // true: only parts and sprites show (colliders stay)
    "hideMeshes": ["New/Cube.030_Cube.001"],    // base meshes to remove, by renderer path (run inspect <prefab> to list them)
    "scale": 1.5,                               // whole model (or [x, y, z]); items: held + dropped, pieces: incl. collision
    "parts": [                                  // kitbash: other prefabs' meshes, positioned in this prefab's space
      { "prefab": "TrophySkeleton", "child": "skull",     // child: only meshes whose name contains it (optional)
        "position": [0, 0.03, 0.5], "rotation": [0, 180, 0], "scale": 0.55,
        "materials": [ { "target": "Skeleton", "tint": "#B8E0A0" } ] }   // overrides for this part only
    ],
    "icon": "textures/gate_icon.png"
  },

  "effects": {                                  // every Light and particle effect (fire, sparks, ward glow)
    "lightColor": "#66AAFF", "lightIntensity": 1.5, "lightRange": 1.0,   // intensity/range multiply vanilla
    "flameTint": "#4C8CFF"                      // keeps each effect's fade
  },

  "components": {                               // structural; needs a game restart to change
    "remove": ["PrivateArea"],                  // a ward without warding
    "add": [ { "type": "Rigidbody" } ]          // Unity components or any Valheim script; set them up under "fields"
  },

  "fields": {                                   // any component field, dotted paths for nested data
    "WearNTear": { "m_health": 1200, "m_materialType": "Iron" },
    "ItemDrop": { "m_itemData.m_shared.m_damages.m_slash": 60 }
  },

  "behaviours": [
    { "type": "glow", "color": "#FFB066", "intensity": 1, "range": 4, "offset": [0, 1, 0], "nightOnly": true }
  ],

  "craft": {
    "tool": "Hammer",                           // pieces: Hammer | Hoe | Cultivator
    "category": "Building",                     // pieces: hammer tab (unknown name = new tab)
    "station": "forge",                         // workbench, forge, stonecutter, artisan, blackforge, galdr, cauldron, none
    "stationLevel": 1,                          // items
    "requirements": [ { "item": "Bronze", "amount": 4, "perLevel": 0, "recover": true } ]
  },

  "snap": { "mode": "add", "points": [[0, 2, 0]] }   // keep | replace | add (pieces only)
}
```

Borrowing (`from`, `mesh.prefab`) always takes the **vanilla** look, even if another recipe reskins that prefab.

## Not yet

- `look.mesh.file` (.glb models): next runtime milestone (runtime glTF loader).
- Restoring component fields on hot reload: a removed field keeps its last value until restart.
- Item crafting recipes aren't read from vanilla yet (piece costs are); set item costs in the Recipe tab.
- UI design canvas: https://claude.ai/artifact/W3avdoNYCwB8nNKHM9oLMs
