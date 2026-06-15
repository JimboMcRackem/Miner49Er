# Phase 6a — Dual-Grid Terrain Renderer + Water/Floor Art

**Status:** Design approved 2026-06-15. Ready for implementation planning.

**Goal:** Replace Godot's terrain-solver autotiling (which leaves seams) with a
deterministic **dual-grid corner renderer**, add a distinct **water** terrain so
ponds/rivers read as water instead of lava, and regenerate a **calmer floor**.

This is a **`game/`-only adapter + art-asset change** — `Miner49er.Core` is untouched,
so the 312-test suite stays green without modification.

---

## 1. Context

Terrain rendering currently uses a Godot `TileMapLayer` with `SetCellsTerrainConnect`
(`game/TerrainMap.cs`). The preceding fix (commit on this branch) made terrain *appear*
at all — but Godot's corner-terrain solver, fed cell-based paints, cannot consistently
assign vertex terrains at boundaries and leaves many boundary cells empty (~146/784 in a
stress map). Those are solid-filled by a fallback, producing **hard seams** at edges.

The tile art is authored by PixelLab as **2-terrain Wang pairs** (16 corner combos each):
`floor↔wall`, `lava↔wall`, `pit↔floor`. Two gaps:

- **Water has no art.** `ShallowWater`/`DeepWater` map onto the lava terrain, so ponds
  and rivers render as orange lava.
- **Floor is "too florid"** — busy center decoration and bricky walls.

The grid is **32×32 px/cell** (`MatchClient.TileSize = 32`); camera zoom `1.5×`. These
are unchanged by this work.

## 2. Locked decisions

1. **Dual-grid corner rendering.** A display layer offset by half a tile; each display
   tile sits over a world vertex and reads the 4 surrounding cells as its 4 corners,
   looked up against the tileset's corner-bit table. No Godot terrain solver.
2. **Five terrains:** wall, floor, lava, **water (new)**, pit.
3. **Generate art via PixelLab:** new `water↔floor` (ponds) and `water↔wall` (rivers /
   flooded rock) pairs, and a regenerated calmer `floor↔wall` pair.
4. **Renderer ships first, against current art** — proves the seams are gone before any
   PixelLab credits are spent. Water shows as lava until its art lands; dropping the new
   art in requires no renderer code change.
5. **Grid math, tile size, and camera zoom are unchanged.**

## 3. Renderer — `TerrainMap` rewrite (dual-grid)

`TerrainMap` stops using `SetCellsTerrainConnect`. It keeps a single `TileMapLayer`
(the *display* layer) and drives it by direct lookup.

**Init:**
- Load `combined_terrain.tres` for its atlas source + per-tile corner peering bits only.
  (The resource's `terrain_set`/`mode`/center-terrain data is now unused but harmless.)
- Build `Dictionary<(int tl,int tr,int bl,int br), Vector2I>` mapping a 4-corner terrain
  signature to its atlas coord, by reading every tile's `TopLeftCorner`/`TopRightCorner`/
  `BottomLeftCorner`/`BottomRightCorner` peering bits. Also record a per-terrain **solid**
  tile (all four corners equal) for fallback.
- Add the display `TileMapLayer` at local position `(-16, -16)` (half a cell), `ZIndex`
  unchanged (−10). For a `W×H` world the display grid is `(W+1)×(H+1)`.

**Cell mapping:** display cell `(i,j)` (for `i∈[0,W]`, `j∈[0,H]`) reads world cells:
`TL=(i-1,j-1)`, `TR=(i,j-1)`, `BL=(i-1,j)`, `BR=(i,j)`. Out-of-bounds cells resolve to
**wall** (the map border is `ImpermeableRock`, which maps to wall anyway).

**Paint (Begin):** compute every display cell's signature and set its tile.

**Update (per tick):** a world cell change at `(x,y)` affects exactly the four display
cells touching that vertex: `(x,y)`, `(x+1,y)`, `(x,y+1)`, `(x+1,y+1)`. Recompute only
those.

**Lookup + fallback (per display cell):**
1. Exact signature in the table → use it. (Common boundaries are authored pairs, so this
   is the usual path — and never blank.)
2. Otherwise (an unauthored pair, or a 3–4 terrain junction) → use the **solid tile of
   the majority** corner terrain; ties broken by fixed priority `wall > lava > water >
   pit > floor`. These junctions are rare and render as a clean solid block, never empty.

**`TileType → terrain` mapping** (single source of truth in `TerrainMap`):

| TileType | Terrain |
|---|---|
| Rock, GoldRock, ImpermeableRock | wall |
| Floor, Cracked, Crumbling, Plank | floor |
| Lava | lava |
| ShallowWater, DeepWater | **water** |
| Pit | pit |
| LavaVent | (none — `WorldRenderer` draws it) |

## 4. Converter — stable terrain ids + water

`tools/pixellab_tileset_converter.gd` assigns terrain ids by first-seen order, which
shifts when pairs are added. Change it to assign ids from a **fixed canonical registry**
so the tileset and `TileToTerrain` never drift:

```
wall = 0, floor = 1, lava = 2, water = 3, pit = 4
```

The existing wall-synonym normalization stays (merges "cave wall" variants). Water pair
names (lower) normalize to the `water` terrain. The converter is then re-run over five
pairs (three existing + two water) to regenerate `combined_terrain.tres`.

## 5. Art generation (PixelLab)

Generate three top-down tileset pairs, author-reviewed before acceptance:

| Pair | Lower → Upper | Purpose |
|---|---|---|
| `floor_wall` (regenerate) | calmer cave floor → cave wall | replace florid floor |
| `water_floor` | water/pond → cave floor | ponds on floor |
| `water_wall` | water/river → cave wall | rivers / flooded rock |

Each yields a `<name>_metadata.json` + `<name>_image.png` in `assets/tiles/`, consumed by
the converter. `water↔wall` may be deferred to a follow-up if ponds-only is enough at
review time; the renderer handles its absence via fallback with no code change.

## 6. Testing & verification

`Miner49er.Core` is untouched. Verification:

1. **Core tests stay green** — `dotnet test src/Miner49er.Core.Tests/...` → 312 passing.
2. **Coverage probe** — a headless GDScript probe paints a mixed map through the same
   dual-grid lookup and asserts **0 blank display cells** and that authored boundaries
   resolve to non-solid edge tiles (not fallback). Run via PowerShell.
3. **Headless boot exits 0** — `& godot --headless --quit-after 2`.
4. **Manual visual checklist:** floor/wall/lava/pit boundaries render with crisp corner
   pieces and no seams; ponds and rivers read as water (after art); flood/blast tile
   changes update terrain immediately; fog still reads correctly over the layer.

Rendering is Godot-typed and not unit-testable in C#; the safety net is "Core tests
untouched + headless coverage probe + manual review."

## 7. Plan shape

- **A — Dual-grid renderer** against current art. Rewrites `TerrainMap`; kills the seams
  immediately. Independent and verifiable now.
- **B — Art + converter.** Fixed-id converter registry; PixelLab `water_floor`,
  `water_wall`, calmer `floor_wall`; regenerate `.tres`. Drops into A with no code change.

Each sub-plan leaves the game runnable.

## 8. Out of scope (deferred)

- Flood "seep" cross-fade (not currently implemented; a separate enhancement).
- `pit↔wall` and `lava↔floor` transition art (rare; fallback solid-fills them).
- Animated water/lava frames, multiple floor variants.
- Miners, objects, effects (unchanged from their current renderers).
- Any `Miner49er.Core` change.
