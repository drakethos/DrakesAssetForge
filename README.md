# Drakes Asset Forge

Drakes Asset Forge is a tool for Valheim asset creation. **No bulk SoftRef/Valheim ripping** — spy Catalog data, clone a donor, attach your own (or imported/modified) art, then Export a Jotunn mod.

**Current release: `0.2.0`** — Catalog + Project + art export / folder packs work; full auto mod scaffolding (CHUNKS C3) and polish still ahead. Expect rough edges.

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
Catalog (SoftRef spy) → Project (edit) → Import other mods to rework (optional) → Export
```

**Export** = make `art.bundle` for checked items (extract imported prefabs / compile FBX as needed) **then** wire `Assets/Items` + `ArtItemHooks.g.cs` into the code project.

Imported or user-modified prefabs/FBX/skins **are** shippable content. SoftRef catalog meshes are never bulk-dumped into bundles. Pure donor + script items with no custom art still Export hooks/`item.json` only.

### Export selection

On **Export**, check folders/items (folder check cascades; individuals override). **Select all / none**, or **Use Project selection**. Unchecked items are removed from the mod on the next Export.

### Projects

- **File → New / Open / Save Project As…** — folder with `project.json` + `Items/`
- **Export / Import Project Package…** — `.daf` zip of the project tree
- Last opened project is remembered in `%LocalAppData%/DrakeAssetForge/settings.json`

### Import bundles / old mods

Import is for **inspecting and reworking** other mods (or your own old bundles) — not the ship step.

- **Import Asset Bundle…** — pick Unity bundles (including **extensionless** SoftRef/mod files)
- **Import Mod Assets Folder…** — open a mod `Assets/` folder (e.g. `…/DrakeMods-LockSmith/Assets` with `ploam` / `drake`) and pull every UnityFS bundle
- **Import Mod Items Folder…** — open a synced `Assets/Items` tree (`item.json` + optional `art.bundle`)
- Multi-prefab bundles land as `bundle(needs extract)`; **Export** (or File → Extract) uses Unity to pack each into a standard `art` prefab bundle

Vanilla SoftRef **names** are skipped on import so Catalog bulk does not flood the project. Once an item is owned (imported mod prefab or attached FBX), Export (re)bundles it.

## Making art.bundles

Needs a Unity 6 editor close to Valheim's version (`valheim_Data/boot.config`). Set Unity on the Export screen.

- Attached FBX → Export compiles into `art.bundle`
- Imported multi-prefab → Export extracts named prefab into `art.bundle`
- Imported single-prefab / prior compile → Export ships the existing `art.bundle`
- **Rebuild selected art.bundle** force-recompiles the Project selection’s FBX (then run Export to wire)

## Phase C1 (in-game smoke)

Build and launch with Jotunn on the `drakeTest` profile:

```powershell
dotnet build DrakesAssetForge\Smoke\DrakesAssetForgeSmoke.csproj -c Debug
```

The build copies `DrakesAssetForgeSmoke.dll` into the profile plugins folder. In Valheim: `spawn <owned item id>` (from the Project list). See [CHUNKS.md](CHUNKS.md).

## License

MIT — see [LICENSE](LICENSE).
