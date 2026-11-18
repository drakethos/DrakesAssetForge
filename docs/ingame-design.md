# Forge Bench: Asset Forge in game (design)

Working name **DrakesForgeBench**. Third mod of the family: **Forge Runtime** (loads packs), **Asset Forge app** (full editor), **Forge Bench** (this: a small in-game editor). Bench depends on Forge Runtime and DrakeModsLibs (UI only). It is client-only and ships separately. Nothing here goes into the Runtime package.

## The one idea that makes this cheap

The pack folder is already the bridge. The app writes `forgepack.json` + `items/*.json`; the Runtime watches those folders and hot-reloads. Bench does the same thing from inside the game:

```
Bench UI  --RecipeSerializer.WriteRecipe-->  Packs/BenchPack/items/x.json  --DevWatcher-->  Runtime.Reload  -->  prefab updated live
```

`DrakesForge.Format` is public (`WritePack`, `WriteRecipe`, `ReadRecipe`, `Validate`), so Bench writes the exact files the app reads. A pack made in game opens in the app for publishing, and a pack from the app opens in Bench. No second format. "You need the editor to go outside" holds: Bench never exports a zip or C# project, and never reads or writes Valheim's files.

Everything Bench edits is a recipe:

| Bench feature | Recipe field it writes (already supported) |
|---|---|
| Swap material from another item | `look.materials[].fromPrefab / fromMaterial / target / slot` |
| Which part of a multi-mesh object | `target` (material name), `slot`, `look.hideMeshes`, `parts[].child` |
| Kitbash | `look.parts[]` (prefab, child, position, rotation, scale, materials) |
| Item in hand | `look.holdPosition / holdRotation / holdScale` (HoldPose) |
| Quick stats | `fields["ItemDrop"]["m_itemData.m_shared.m_damages.m_slash"]` etc. |
| Name, description, recipe | `name`, `description`, `craft.station`, `craft.requirements` |
| Collision, scale | `look.collision`, `look.scale` |

## The hard constraint: new ids need a restart

`ForgeRegistry.Reload` says it plainly: a new id, or changed `kind`/`base`, needs a restart. Registration happens once at `OnVanillaPrefabsAvailable`. So there are two kinds of edit:

1. **Edit an existing prefab** (reskin recipe, or a recipe the Runtime already built): live, today, no Runtime change.
2. **Create a new item/piece id**: needs registration after the world is loaded.

Plan for 2, in order of effort:

- **v1 (no Runtime change): "Save, then Restart Valheim."** Bench writes the recipe and shows a "needs restart" chip. Honest and shippable.
- **v2 (small Runtime minor): `ForgeLive.Register(recipe)`.** Offline/world-owner only. Builds the clone through the same `Register` path, then adds it to `ZNetScene` named prefabs, `ObjectDB` items, and the hammer's `PieceTable`, and refreshes the piece list. This is the spike to prove first; it is the only Runtime change and it stays dormant until Bench calls it (no bloat for packs-only installs). If it proves flaky, v1 stays.

New ids and multiplayer: a new prefab id means every client needs the pack, so Bench's new-id output is offline/dedicated-with-pack only. That is why the prefab builder has a second output that needs nothing.

## Prefab builder: two outputs from one editing session

You pick **Prefab mode** in an enhanced hammer menu, place a plot, and build with normal pieces under relaxed rules. Then you choose what the prefab becomes:

**A. Kit (blueprint).** A saved list of vanilla pieces with local transforms (`kits/<name>.json`, own small format, owned by Bench). Placing a kit spawns the real vanilla pieces. Works on any server with no mod on other clients, and the pieces stay functional (doors, chests, stations). Same family as PlanBuild; Bench's differences are the plot, the relaxed placement, and costing.

**B. Baked piece.** The members become `look.parts[]` on one new piece (`kind: piece`), collision `mesh` or `box`, cost from the auto-cost. One prefab, one hover name, one snap set. Decorative only: Parts copies static meshes, so a baked door does not open and a baked chest holds nothing. Needs the pack on every client.

Material swaps follow the same split: in a kit they are cosmetic and re-applied locally by Bench from a ZDO tag (other clients without Bench see vanilla); baked, they are in the recipe and real for everyone with the pack. The UI says this next to the swap, not in docs.

### Plot and relaxed placement

- **Plot**: a square marker (default 12 m, 4 to 32) that levels the ground under it and clears the area for placement. Done = plot disappears, terrain stays (or *Restore ground* if the terrain-restore spike works).
- **Relaxed rules in Prefab mode only** (Harmony patches that early-out unless the mode flag is set, so they cost nothing otherwise): placement status forced valid, overlap/clipping check skipped, structural-support collapse suppressed for pieces tagged as prototype members, free rotation and fine nudge (grid 0.5 / 0.1 / free), no cost charged while prototyping.
- Members are tagged in ZDO (`drake_proto`, prefab id) so that Done can gather them, and so a crash mid-build does not lose them. On Done untagged pieces are left alone.

### Costing

Cost is computed, then editable:

- **Auto**: sum of each member's `Piece.m_resources`, grouped by item. Door + wall + roof shows as one list.
- **Modes**: *Sum*, *Sum ×0.75* style multiplier, *Per-item override* (click a row, type an amount), *Free* (admin).
- For a baked piece this becomes `craft.requirements`; for a kit it is charged on placement (or free for admins).

## In-game item editor

Click an item (inventory or an item stand) → **Edit**. The player stands in a viewer pose, reusing the apron fitting-room pattern (`ApronTuner`: still player, orbiting camera, left panel, Front/Side/Back). Differences:

- Panel tabs: **Look**, **Hold**, **Stats**, **Recipe**. Hold shows the item in the hand with arrow-key/drag nudges writing `holdPosition/Rotation/Scale`.
- **Stats** is a short list per item type, with a stepper and a slider each, vanilla value shown, reset per row. Weapons: slash/pierce/blunt/fire/frost/lightning/poison/spirit, block power, durability, weight, attack stamina, knockback. Armour: armour, durability, weight. Food: health, stamina, burn time. Not a component inspector; anything else is "open in the app".
- **Description / weapon type** are plain fields. The recipe tab lists station + requirements with the same editor as the prefab costing.
- Edits write to the recipe and hot reload immediately, so you see the change on the model in your hand.

### Material and part picker

One picker, used by items and prefab members:

1. Pick the **target**: a list of the object's parts (mesh names / material slots), highlighted in the viewport when hovered.
2. Pick a **source**: search the items/pieces list (with icons), then its **sub-materials** if it has more than one. "Borrow from: Bronze Sword → SwordBronze".
3. Optional tint, gloss, metallic (existing `MaterialOverride` fields).

No file paths anywhere. Sources are limited to prefabs the player's game knows.

## Restrictions

- **Single player**: always on (world owner).
- **Server**: only when the local player is an admin (Libs `DrakePlayerIdentity`/permissions), and only features that need nothing from other clients are enabled: Kits and admin-only edits. Anything that needs a new id on clients shows as "needs the pack on all players".
- Bench writes only inside its own pack folder; never reads Valheim files, never lists the disk (the in-game UI has no file browser, per the plan).

## Mod layout

```
DrakesForgeBench/
  BenchPlugin.cs          BepInEx entry, config, hotkey, admin gate
  Packs/                  BenchPackStore (writes forgepack.json + items/*.json via Format)
  Editing/                RecipeDraft (undo/redo over an ItemRecipe), StatsCatalog, Picker data
  Prefab/                 PlotTool, PrototypeMode (patches), Kit (save/load/place), AutoCost, Bake (kit -> recipe)
  Item/                   ItemEditSession (viewer pose, hold nudge)
  UI/                     UITK views on Libs UkWindow/UkControls; no hand-placed pixels
  Spikes/                 ForgeLive (if it stays out of Runtime), terrain restore
```

References: DrakesForgeRuntime (public Format + hooks), DrakeModsLibs (UI Toolkit, permissions). No Libs change planned. If one is needed (e.g. a viewport widget), add it to Libs per the suite rules and rebuild all consumers.

## Build order

1. **Skeleton + pack bridge**: hotkey, admin gate, `BenchPackStore`, write a recipe and watch it hot reload.
2. **Material swap on a placed vanilla piece/held item** (picker UI, live reload). Proves the bridge and the picker.
3. **Item editor**: viewer pose, Hold, Stats, Recipe.
4. **Prefab mode**: plot, relaxed placement, tagging, Done, Kit save/place, auto-cost.
5. **Bake kit to piece** (restart-needed at first).
6. **ForgeLive spike** to remove the restart.

## Risks to spike before committing

| Risk | Spike |
|---|---|
| Live registration of a new piece/item after load | Register one clone mid-game; place it; save/reload world; check hammer list |
| Relaxed placement without ghost/ZDO issues | Patch placement status + overlap; place 200 pieces; reload world |
| Suppressing structural collapse for tagged members | Keep tagged pieces from `WearNTear` support failure |
| Terrain level + restore under the plot | Level with a terrain op; test undo |
| Baked collision for large merged piece | `Mesh` mode cost and ghost behaviour |
| Material swap re-apply on kit pieces | ZDO tag + local re-apply when zone loads |
| Item viewer pose with held item | Reuse ApronTuner camera/pose in a non-apron context |

## Out of scope (from the plan)

No full app recreation, no studio of 1000 options, no file browser, nothing bundled in Runtime, no zip/C# export, no rigged-mesh or `.glb` import. Those stay in the app.
