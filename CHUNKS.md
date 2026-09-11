# Drake Asset Forge — testable chunks

Mapped to the UI flow canvas (`drake-asset-forge-ui-flow`): **Spy vs Work**, one shell, three panes.

```
Spy in Catalog → Pin/select donor → Clone → Project work → Export
```

## Phase A — Catalog (Spy)

### A1 — SoftRef load + filters (done)
- Auto-load SoftRef `manifest_extended` on startup (from `environment.props` / File menu)
- Views: Items / Pieces / Prefabs / Icons / Recipes / Raw
- Subcategory + hide `_res`
- Resolve item → bundle container PathId

**Test:** `SwordIron` resolves to Prefab container + PathId.

### A2 — App shell + Catalog three-pane (done)
- Nav: **Catalog | Project | Export**
- Catalog workspace: **Browser · Preview · Inspector**
- Selection bar: Pin / Unpin / Clone into Project… (clone = stub → Project screen)
- Pins tray (in-memory)
- Inspector read-only tabs; **Prefab sheet live** from SoftRef
- Project + Export screens = stubs only

**Test:** Auto SoftRef → Items → select SwordIron → Prefab inspector fills → Pin → Open bundle shows container hit.

### A3 — Catalog preview (done)
- Center pane shows SoftRef **icon/sprite** preview (SpriteAtlas crop via RenderDataKey GUID) or Texture2D
- Selecting an Item prefab auto-loads matching `_icons/<name>.png`
- Refuse dumping full atlas sheets without a valid crop
- Selecting a Texture/Sprite row in bundle objects previews that asset
- Mesh rows show basic vert stats (no 3D yet)

**Test:** Items → `SwordIron` → Preview shows 64×64 icon (not 4096 sheet). Icons view → same.

### A4 — Catalog field spy (feeds Clone seed) (done)
- Generic **Valheim script property spy** (not ItemDrop-hardcoded)
- Prefab scan → MonoBehaviours matched to `assembly_valheim.dll` fingerprints (skips Unity Transform/Rigidbody/VFX noise)
- Inspector **Scripts** sheet: pick script → flattened properties
- Recipe SoftRef assets still on Recipe sheet
- Uses `AssetsTools.NET.MonoCecil` + Mono.Cecil against Managed

**Test:** Items → SwordIron → Scripts shows ZNetView / ZSyncTransform / ItemDrop; ItemDrop has slash 55. Pieces → woodwall → Piece / WearNTear.

## Phase B — Project (Work)

### B1 — Project tree + owned item JSON (done)
- Clone SoftRef donor → `%LocalAppData%/DrakeAssetForge/Projects/Default/Items/<id>/item.json`
- Project screen: same three-pane shell (owned list · preview · inspector)
- Seeds **all matched Valheim scripts** into item.json; Scripts sheet editable + Save; Recipe seed read-only for now

**Test:** Catalog → SwordIron → Clone into Project → Scripts list includes ItemDrop/ZNetView → edit → Save.

### B2 — Material / shader sheet (done)
- SoftRef shader catalog → `Shader.Find` names + property schemas (cached under LocalAppData)
- Material sheet: **Donor** vs **Custom**, shader picker, property grid
- Writes `material.json` beside owned `item.json` (textures = path refs for later art import)
- Never packs Valheim shader bytecode

**Test:** SoftRef load builds catalog → Project item → Material → uncheck Donor → pick `Custom/Creature` → Save → `material.json` has props.

**Later (not now):** Expand the Material/shaders tab with a **visual look preview** once owned materials + 3D mesh preview exist — so you can see how the shader/material reads on the item, not just property grids. Depends on art import + preview work; skip until then.

### B3 — Art import hooks (done)
- Project **Art** sheet: attach mesh (FBX), icon (PNG), diffuse/albedo (PNG)
- Files copy into `Items/<id>/art/`; paths stored in `art.json` beside `item.json`
- Preview prefers custom icon when set; otherwise SoftRef donor icon
- Compile to AssetBundle is **not** this chunk (see C2)

**Test:** Edit in Project → lands on **Art** sheet → Browse icon PNG → Preview swaps; list shows `art: icon`; Open folder shows `art/`.

## Phase C — Runtime proof + Export

### C1 — In-game smoke (was Chunk 4)
- Hand/minimal Jotunn mod: clone + Shader.Find + texture

### C2 — Silent Unity art bundle (was Chunk 5)
- FBX/PNG → art-only AssetBundle

### C3 — Full export (was Chunk 6)
- Generate Jotunn mod folder (C# + bundle + csproj)

## Explicit non-goals until noted

- ItemForge JSON runtime as product output
- Full Valheim Unity rip
- Packing vanilla assets into mods
- Mock_\* as default pipeline
- Full 3D model preview in Catalog (mesh vert stats only today — optional later chunk)
- Material/shaders tab **look preview** (needs materials + 3D preview first — note under B2)
