# Drakes Asset Forge

Drakes Asset Forge is a powerful tool for Valheim asset creation. **No asset ripping** — just view Valheim SoftRef data and start making assets (clone donor → edit fields → attach FBX/PNG → export a Jotunn mod).

Repo: [drakethos/DrakesAssetForge](https://github.com/drakethos/DrakesAssetForge)

## Run (from this folder)

```powershell
dotnet run --project DrakeAssetForge.csproj
```

Or from the DrakesWorkshop suite checkout (sibling folder):

```powershell
dotnet run --project DrakesAssetForge
```

Valheim path auto-fills from suite `environment.props` (`ValheimGamePath`) when present.

## Workflow

```
Spy in Catalog → Edit in Project → Art / Material / Scripts → Export (Compile + Sync)
```

Art is **optional**. Items with only scripts/properties sync and register in-game without `art.bundle`.

### Export selection

On **Export**, check which project items Sync into the Valheim mod (`Assets/Items` + `ArtItemHooks.g.cs`). Use **Select all / none**, or **Use Project selection** after Ctrl/Shift multi-selecting on Project. Unchecked items are removed from the mod on the next Sync.

### Projects

- **File → New / Open / Save Project As…** — folder with `project.json` + `Items/`
- **Export / Import Project Package…** — `.daf` zip of the project tree
- Last opened project is remembered in `%LocalAppData%/DrakeAssetForge/settings.json`

### Import bundles / old mods

- **Import Asset Bundle…** — pick Unity bundles (including **extensionless** SoftRef/mod files)
- **Import Mod Assets Folder…** — open a mod `Assets/` folder (e.g. `…/DrakeMods-LockSmith/Assets` with `ploam` / `drake`) and pull every UnityFS bundle
- **Import Mod Items Folder…** — open a synced `Assets/Items` tree (`item.json` + optional `art.bundle`); falls back to Assets-folder discovery if none
- Multi-prefab bundles land as items with `bundle(needs extract)`; **Extract Imported Bundles…** uses Unity to pack each into a standard `art` prefab bundle

## Current slice — Phase C2 (art bundle)

Attach an FBX, then **Export → Compile art bundle**. Needs a Unity 6 editor close to Valheim's version (`valheim_Data/boot.config`). Output is `art.bundle` next to the item. Smoke loads that mesh on spawn.

## Phase C1 (in-game smoke)

Build and launch with Jotunn on the `drakeTest` profile:

```powershell
dotnet build DrakesAssetForge\Smoke\DrakesAssetForgeSmoke.csproj -c Debug
```

The build copies `DrakesAssetForgeSmoke.dll` into the profile plugins folder. In Valheim: `spawn <owned item id>` (from the Project list). See [CHUNKS.md](CHUNKS.md).

## License

MIT — see [LICENSE](LICENSE).
