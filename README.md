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
Spy in Catalog → Edit in Project → Art / Material / Scripts → Export (later)
```

## Current slice — Phase B3 (art import)

**Quick test:** Catalog → item → **Edit in Project** → Inspector **Art** (opens automatically) → **Browse** an icon PNG → center Preview should show your PNG. Use **Open folder** to see `art/` on disk.

See [CHUNKS.md](CHUNKS.md) for the full chunk roadmap.

## License

MIT — see [LICENSE](LICENSE).
