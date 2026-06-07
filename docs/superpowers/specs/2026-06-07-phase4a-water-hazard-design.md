# Miner49er — Phase 4a: Water Hazard (static substrate) Design

**Date:** 2026-06-07
**Status:** Design approved; ready for implementation planning.
**Builds on:** `2026-06-06-miner49er-game-design.md` (§3 water, §8 map gen, §9.4 phase 4)
and the merged Phase 1–3 (core loop, multiplayer, listen + audio).

---

## 1. Goal & scope

Add **water** as the game's first environmental hazard, as **static map terrain**.
Two tile depths:

- **Shallow water** — walkable but **slow** (a channel you wade through).
- **Deep water** — **lethal**: stepping in **drowns** you (a one-hit death like a blast).

**Shape and depth are independent.** Water is generated as bodies of two
*shapes* — **pools** (blobs) and **rivers** (snaking lines) — and each body may be
shallow or deep depending on its width and the seed. Deep water is always
**ringed by shallow shore**, so a lethal tile is never adjacent to plain floor and
a drowning is always telegraphed.

This sub-phase builds the **water substrate** only: tiles, drowning, slowing, map
generation, and rendering. It is deliberately architected so the **flood mode**
(sub-phase 4b — deep water rising inward from the map edge on a timer) can drive
the same tiles later, but that mode is **not built here**.

### Out of scope (explicit)
- **Flood mode** + rising-water driver and **type-aware tile-change sync** → **4b**.
- **Water-plank** item (cross deep water) and the §3.5 status-effect system → **4c**.
- **Cave-ins** → **4d**.
- Currents, water physics, swimming, knockback — not planned.

## 2. Key architectural fact: no new netcode

Water is **seeded terrain**. Every client already regenerates an identical
`TileGrid` from the match seed (Phase 1/2), so water tiles are byte-identical
across peers with **zero new sync**. Drowning is just a miner's `Alive` flag
flipping `true → false`, which the per-tick snapshot already carries. Therefore
Phase 4a is a **Core rules + map-gen change plus a client render/audio change** —
no `NetworkManager`/`MatchHost` RPC changes.

## 3. Core: tile model & semantics (`Miner49er.Core`, unit-tested)

Extend the tile enum:

```
enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater }
```

New pure helpers on `TileTypeExtensions` (each unit-tested):

| Helper | Floor | Rock | GoldRock | ImpermeableRock | ShallowWater | DeepWater |
|---|---|---|---|---|---|---|
| `IsEnterable()` | yes | no | no | no | yes | yes |
| `IsLethal()` | no | no | no | no | no | yes |
| `MoveCostMultiplier()` | 1.0 | – | – | – | 2.0 | 1.0 |
| `IsWalkable()` (existing) | yes | no | no | no | yes | no |
| `IsMinable()` / `IsBlastable()` (existing) | no | yes | yes | no | no | no |

Notes:
- `IsWalkable` keeps its meaning "safe to occupy" = Floor + ShallowWater; existing
  callers that ask "is this a safe standable tile" stay correct. Movement uses the
  new `IsEnterable` (which additionally allows DeepWater, so you *can* step in to
  your death).
- `IsMinable`/`IsBlastable` are **unchanged**, so pickaxe and charges are **inert
  to water** with no code change (they only ever matched Rock/GoldRock).
- `MoveCostMultiplier` for DeepWater is moot (you die on entry); defined as 1.0.
- `ShallowSlowFactor = 2.0` is the source of the ShallowWater multiplier.

### Drowning in `Simulation.TryMove`
- Gate movement on `IsEnterable(target)` instead of `IsWalkable(target)`.
- On a successful move, emit `MinerMoved(id, from, target)` as today; then if
  `target.IsLethal()`: set `Alive = false`, cancel activity, and emit a new
  `MinerDrowned(int Id)` `SimEvent`.
- Deep water is thus **passable-but-deadly**: the move succeeds (visual steps onto
  the tile) and the miner immediately drowns.

## 4. Movement slowing (host-side; no §3.5 system)

"Channels slow you" lives where the move cadence already lives — `MatchHost`
(`MoveStepSeconds`). A miner's per-move cooldown becomes:

```
cooldown = MoveStepSeconds × TileType.MoveCostMultiplier(tileTheMinerIsStandingOn)
```

So while standing in shallow water every step is at half speed (factor 2.0).
Core owns the multiplier (pure, testable); the host only reads it. The full
status-effect / effective-speed system (§3.5) remains entirely in sub-phase 4c.

## 5. Map generation & fairness (`MapGenerator`, seeded & unit-tested)

New pass order:

```
RandomFill → Smooth → KeepLargestRegion → PlaceWater → PlaceSpawns → PlaceGold
```

`PlaceWater` (all driven by the seeded `Random`, so deterministic across clients):

1. **Carve water bodies** into the floor region in two shapes:
   - **Pools** — a few random blobs (random-walk / disc growth).
   - **Rivers** — snaking lines (drunkard's walk across the region).
   All carved tiles start as **ShallowWater** (only ever placed on what was Floor,
   never on Rock/ImpermeableRock, so the cavern walls stay intact).
2. **Assign depth by interior + chance:** a ShallowWater tile is promoted to
   **DeepWater** only if it is **interior** — all four orthogonal neighbours are
   water — with probability `DeepWaterChance`. Boundary water (touching
   floor/rock) always stays shallow. This makes thin rivers / small pools fully
   shallow and wide rivers / large pools grow deep cores, and **guarantees every
   deep tile is ringed by shallow shore** with no separate ring step.
3. **Preserve fairness / connectivity:** recompute the largest **traversable**
   region over `Floor + ShallowWater` (treating DeepWater as a wall). Restrict
   spawns and gold to that region so nothing is ever gated behind a forced
   drowning. (Mirrors the existing `KeepLargestRegion`, but over the traversable
   set; tiles outside it are left as terrain — they just won't host spawns/gold.)

`PlaceSpawns`: on Floor, **not orthogonally adjacent to any water**, keeping the
existing min-distance rule. `PlaceGold`: unchanged placement, but candidates are
restricted to the traversable region so every vein is reachable without entering
deep water.

New `MapConfig` knobs (sensible defaults, tuned in play-test):
`PoolCount`, `PoolSizeRange`, `RiverCount`, `RiverLengthRange`,
`DeepWaterChance`, `MinWaterSpawnDistance`.

## 6. Rendering & audio (`game/`)

- **`WorldRenderer`** draws the two new tiles distinctly — ShallowWater light
  blue, DeepWater dark blue — alongside the existing tile colors. `FogRenderer`
  is unchanged (the fog overlay composites the same way).
- **Drown SFX, still zero netcode:** `MatchAudio` already plays a death stinger on
  a miner's `Alive: true → false`. It additionally inspects that miner's tile in
  the synced grid: if `DeepWater`, play a **splash** instead. Add a `splash`
  procedural placeholder to `SfxLibrary` (and to the `assets/audio` manifest).
- **Optional nicety:** bias the Phase 3 ambient drip emitters toward water tiles
  when any exist (decoupled; skip if it complicates the change).

## 7. Sync & the flood-mode seam (for 4b)

No netcode changes in 4a (see §2). For 4b the seam is **documented, not built**:

- Water is mutated through ordinary `TileGrid.Set`, so a future host-side
  `FloodDriver` can convert tiles edge-inward on a timer to raise the water line.
- The one known extension 4b will need: the current tile-change sync **hardcodes
  `Floor`** on the client (`MatchClient.ApplyUpdate`). 4b will make a `TileChange`
  carry a target `TileType` so flooding can broadcast Shallow/Deep changes. 4a
  leaves this untouched (YAGNI) because static water needs no tile-change.

## 8. Components & boundaries

**Core (`src/Miner49er.Core`), unit-tested:**
- `TileType` + `TileTypeExtensions` — new tiles and the `IsEnterable` / `IsLethal`
  / `MoveCostMultiplier` helpers.
- `Simulation.TryMove` — `IsEnterable` gate + drown-on-entry + `MinerDrowned`.
- `SimEvent` — new `MinerDrowned(int Id)`.
- `MapGenerator` (+ `MapConfig`) — `PlaceWater`, depth assignment, traversable
  connectivity, water-aware spawn/gold placement.

**Godot (`game/`):**
- `MatchHost` — per-tile move cooldown via `MoveCostMultiplier`.
- `WorldRenderer` — render the two water tiles.
- `MatchAudio` + `SfxLibrary` — splash-on-drown derived from tile + `Alive`.

## 9. Testing & verification
- **Core unit tests:**
  - Tile semantics per type (`IsEnterable`, `IsLethal`, `MoveCostMultiplier`,
    `IsWalkable`, `IsMinable`, `IsBlastable`).
  - `TryMove`: onto Floor (ok, alive), ShallowWater (ok, alive), DeepWater (move
    succeeds, then `Alive == false` and a `MinerDrowned` event), Rock (blocked).
  - `MapGenerator` invariants on several seeds: every DeepWater tile is fully
    surrounded by water (shore guarantee); the `Floor + ShallowWater` network is
    connected; spawns are on Floor and not water-adjacent; every gold vein is
    reachable without entering DeepWater; same seed → identical water.
  - Existing 60 tests stay green.
- **Build + headless boot** clean as always.
- **Play-test (user):** drowning feel, shallow-water slow feel, map readability,
  splash audio — the agent verifies it compiles/boots and that the Core invariants
  hold; the player confirms it feels right.

## 10. Scope notes
- Water is static terrain; flooding/rising water is sub-phase 4b.
- Deep water is always shallow-ringed, so lethal tiles are always telegraphed; with
  the existing radius fog (no LOS occlusion) deep water inside vision is seen
  before it is stepped on.
- The §3.5 movement-speed/status-effect system stays independent future work (4c);
  shallow slowing here is a localized terrain cost, not that system.
