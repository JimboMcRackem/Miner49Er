# Phase 6 — Art Pass Design

**Status:** Design approved 2026-06-14. Ready for implementation planning.

**Goal:** Replace the procedural placeholder graphics (colored `DrawRect`/`DrawCircle`
shapes) with real authored art: an autotiled terrain `TileSet`, textured miners, and
textured objects/items. Effects and UI stay procedural for now.

This is a **`game/`-only adapter change** — `Miner49er.Core` is untouched, so the
existing test suite (312) stays green without modification.

---

## 1. Context

Today every in-world visual is drawn in immediate mode:

- `game/WorldRenderer.cs` (`_Draw`) — tiles (full-cell `DrawRect`), planted charges
  (`DrawCircle`), items/pickups (circles, optional toolbox box), mold patches
  (tinted rects), the Listen shimmer (glow circles), and explosion flashes.
- `game/net/MatchClient.cs` (`_Draw`) — miners as 20×20 squares tinted from
  `PlayerColors.Palette` (8 colors), positioned at smoothed pixel coordinates.
- `game/FogRenderer.cs` — a black-alpha "lantern" darkness overlay (kept as-is).

The grid is **32×32 px per cell** (`MatchClient.TileSize = 32`); tiles are drawn from
each cell's top-left, entities centered. The camera is zoomed `1.5×`.

`MinerSnapshot` already carries a `Facing` field (`Direction` enum: North/East/South/West
= 0/1/2/3) that the current square ignores.

## 2. Locked decisions (this design)

1. **Terrain renders via autotiling** — true edges and corners, using Godot's
   `TileMapLayer` + `TileSet` with the **"Corners and Sides"** terrain peering mode.
   (Not a flat drop-in; not a hybrid that leaves walls alone.)
2. **Scope: terrain + miners + objects/items.** Effects (explosion flash, Listen
   shimmer) and UI (compass) are **deferred** — they read fine procedurally and stay
   as they are.
3. **Preserve the seep** — the smooth tile-change cross-fade (most visible as flooding
   creeping in) survives the `TileMap` migration via a thin transition overlay.
4. **Miners: 4 facings, tinted.** One white/grayscale sprite per facing (N/E/S/W),
   recolored in code per the 8-color palette. Idle only — mining/planting reuse idle.
5. **Pixel art authored at 2× (64px), camera zoom unchanged.** Nearest-filter import;
   the 1.5× camera and 32-unit grid stay exactly as today, so the on-screen view and
   all positioning math are unchanged.

## 3. Architecture — split the overloaded `WorldRenderer`

`WorldRenderer` currently has six responsibilities. The migration splits it by concern.
Each unit has one job, communicates through the existing `MatchClient` read surface
(`Grid`, `Fog`, `Charges`, `Items`, `Molds`, `Listening`, `Decoys`), and can be
understood independently.

| Node | Responsibility | ZIndex |
|---|---|---|
| **`TerrainMap`** (new; wraps `TileMapLayer` + `TileSet`) | paint all terrain from the grid; apply per-tick tile changes via terrain-connect so neighbor corners auto-fix | −10 |
| **`TileTransitionOverlay`** (new `Node2D`) | the seep cross-fade — reuses the current fade + `SeepDelay` + out-of-sight-freeze logic | −9 |
| **`FogRenderer`** (unchanged) | black-alpha lantern darkness overlay | −5 |
| **`ObjectRenderer`** (new `Node2D`; the non-tile half of today's `WorldRenderer`) | charges, items, toolbox, mold patches — plus the **deferred** Listen shimmer + explosion flash, still procedural | −4 |
| **`MatchClient._Draw`** (unchanged location) | miners, now textured | 5 (set in `Main`) |

`WorldRenderer.cs` is effectively retired: its tile drawing moves to `TerrainMap`, its
fade bookkeeping to `TileTransitionOverlay`, and its object/effect drawing to
`ObjectRenderer`. `MatchClient.Begin` constructs the new nodes in place of the old
`WorldRenderer`, wired to the same `AddExplosionFlash` / explosion hooks.

`MatchClient.TileSize = 32` and every grid-to-pixel calculation stay unchanged.

## 4. Terrain — `TileSet` and autotiling

- **`TileSet` tile size = 64px** (matches the authored art). **`TileMapLayer.Scale =
  0.5`**, so each cell occupies 32 world units — preserving all 32-unit positioning
  used by miners, items, charges, and fog. Textures import with **Nearest** filtering.
- Terrain set uses **"Corners and Sides"** peering (the mode that produces true corner
  pieces).
- **Autotiled terrains** (corner+side aware; authored as a terrain sheet — the full
  47-tile "blob" set, or a reduced 16-tile set as a starting point that can be
  upgraded): **Rock, ImpermeableRock (bedrock), Lava, Water (shallow + deep share one
  shoreline terrain), Pit (rim against floor).**
- **Single tiles** (one 64px image, no edge logic): **Floor, GoldRock** (gold-flecked,
  reads as rock so it sits inside a rock body without seams), **Plank, Cracked,
  Crumbling, LavaVent.**

Runtime updates: on `Begin`, paint the whole grid. On each `TickUpdate`, update the
changed cells; autotiled terrains use the terrain-connect API so neighbor corners
re-resolve automatically. The `TileType → terrain/atlas` mapping is a single lookup
table in `TerrainMap`.

### Color intent for the artist (current placeholder hex)

| Tile | Hex | Tile | Hex |
|---|---|---|---|
| Floor | `2b2b33` | Pit | `070709` |
| Rock | `5a4a3a` | Cracked | `3a342b` |
| GoldRock | `c9a227` | Crumbling | `4a3b28` |
| ImpermeableRock | `20242b` | Lava | `d2521a` |
| ShallowWater | `2f6f8f` | LavaVent | `ff7a2a` |
| DeepWater | `16384f` | Plank | `b5803a` |

## 5. Transition overlay — preserving the seep

The `TileMap` shows the **new** tile immediately. `TileTransitionOverlay` draws the
**old** tile's texture on top of the changed cell at alpha **1 → 0** over ~0.45s
(`FadeRate = 6`), gated by the existing deterministic per-tile `SeepDelay` (0–0.25s
stagger). Net result is today's cross-fade: the new tile is revealed as the old fades.

The current out-of-sight freeze carries over: a tile that changes while unseen holds its
old texture and only plays the fade once it enters line of sight, so an unseen flood
seeps in the moment it's revealed rather than settling invisibly behind the fog.

The overlay tracks `(GridPos → outgoing tile type, life, delay)` and clears entries when
their fade completes. Explosion flashes are **not** handled here (they remain in
`ObjectRenderer`, deferred/procedural).

## 6. Miners

- **4 facing textures** (`miner_n/e/s/w`), authored white/grayscale, drawn in
  `MatchClient._Draw` with `DrawTexture`'s modulate set to `PlayerColors.At(id-1)`.
- `MinerSnapshot.Facing` (0=N,1=E,2=S,3=W) selects the texture.
- Drawn centered at the smoothed `_visualPos`, scaled to occupy roughly a cell.
- Dead miners are not drawn (unchanged — spectating shows the world only).
- Mining/planting poses reuse idle (no extra art this pass).

## 7. Objects

Drawn centered in `ObjectRenderer`, replacing the current circles/rects, all gated by
`Fog.IsVisible` exactly as today:

- **Charge** (planted explosive) — 1 texture (was a red `ff5530` circle).
- **Pickups** — 5 textures: `item_speed` (`4ad06a`), `item_vision` (`4ad0d0`),
  `item_blast` (`e08a2f`), `item_plank` (`c8a060`), `item_mold` (`8fae4f`).
- **Toolbox container** — 1 texture drawn behind an item when
  `Placement == ItemPlacement.Toolbox` (was the box outline). Buried items stay hidden
  except under Listen (deferred shimmer path, unchanged).
- **Mold patch** — 1 texture, keeping the decay-alpha fade over its last second.

Buried-item / decoy **Listen shimmer** and **explosion flash** stay procedural in
`ObjectRenderer` (deferred from this pass).

## 8. Art manifest (deliverable)

All **PNG, RGBA, authored at 2× = 64px, Nearest-filter import** unless noted.

| Group | Files | Count |
|---|---|---|
| Autotile terrains | `rock`, `bedrock`, `lava`, `water`, `pit` — each a terrain sheet (47-tile blob, or 16-tile reduced to start) | 5 sheets |
| Single terrain tiles | `floor`, `goldrock`, `plank`, `cracked`, `crumbling`, `lavavent` | 6 |
| Miners | `miner_n`, `miner_e`, `miner_s`, `miner_w` (white; tinted in code) | 4 |
| Objects | `charge`, `item_speed`, `item_vision`, `item_blast`, `item_plank`, `item_mold`, `toolbox`, `mold_patch` | 8 |

Proposed location: `assets/tiles/`, `assets/miners/`, `assets/objects/` (mirroring the
existing `assets/` convention; final paths fixed in the plan). A Godot `TileSet`
resource (`.tres`) wires the terrain sheets into terrains.

## 9. Testing & verification

This pass touches only `game/` (Godot adapter, TAB-indented); no `Miner49er.Core`
change. Verification:

1. **Existing tests stay green** — `dotnet test src/Miner49er.Core.Tests/...`
   reports the current 312 passing, unchanged.
2. **Headless boot exits 0** — `& godot --headless --quit-after 2` (run via PowerShell
   only), confirming the `TileSet`/nodes load without crashing.
3. **Manual visual checklist** (needs a display): terrain corners render correctly
   where rock/bedrock/lava/water/pit meet floor; flooding still seeps in (cross-fade
   intact); a blast's tile changes still fade; miners face their movement direction and
   tint per palette; charges/items/toolbox/mold render and respect fog; fog still reads
   correctly over the `TileMap`.

This is an intentional deviation from the usual TDD-heavy plan: rendering is
Godot-typed and not unit-testable, so the safety net is "Core tests untouched +
headless smoke + manual review."

## 10. Plan shape

One spec, but the implementation plan is expected to split into:

- **6a — Terrain:** `TileSet` resource, `TerrainMap`, `TileTransitionOverlay`, retiring
  `WorldRenderer`'s tile half. The heavy, independent piece.
- **6b — Entities:** textured miners in `MatchClient`, `ObjectRenderer` for
  charges/items/toolbox/molds.

Each sub-plan leaves the game runnable.

## 11. Out of scope (deferred)

- Explosion flash and Listen shimmer art (stay procedural).
- Compass / HUD art and the rest of the UI.
- Miner mining/planting poses and walk animation.
- Multiple random floor variants, animated lava/water frames.
- Any `Miner49er.Core` change.
