# Phase 4d-1 — Bottomless Pit (design)

Date: 2026-06-13

## Summary

The bottomless pit is a new static, lethal terrain hazard — "deep water's lethal
twin." It is a hole in the floor placed at map generation (like the 4a water
pools), but with a **hard edge**: unlike deep water it has **no shallow-ring
telegraph**, so the only warning is the fog. Step onto a pit and you fall to your
death (`DeathCause.Fell`). It is opt-in via a host **lobby toggle**, off by
default. A carried **water-plank** can bridge a pit, giving the hazard its single
piece of counterplay.

This is the first of the Phase 4d hazard family; **cave-ins** are a later,
separate cycle.

## Decisions (locked during brainstorm)

- **Static terrain**, placed at map-gen like water pools — never grows or moves.
- **Lobby toggle**, off by default (mirrors the Flooding checkbox), *not* always-on
  terrain. A fog-only instant-death tile is harsher than shallow-ringed water, so
  it stays opt-in.
- **Water-plank bridges pits.** The plank's faced-tile check generalizes from
  "water" to "water or pit," capping a faced pit into a safe `Plank`.
- **Shape:** mostly single-tile holes, with an occasional small 2–5 tile cluster.

## 1. Tile & death model (Core)

- New `TileType.Pit`.
- `TileTypeExtensions`:
  - `IsEnterable` → add `Pit` (a miner *can* step onto it — and dies).
  - `IsLethal` → becomes `t is TileType.DeepWater or TileType.Pit`.
  - `IsWalkable` → unchanged (Floor/Shallow/Plank only). Spawns, fog, drip
    placement, and reachability never treat a pit as safe ground.
  - `BlocksSight` → unchanged (rock family only). A pit is an open hole, so it is
    **transparent** — once fog reveals it, you can see across it. The danger is
    moving blind into unrevealed fog.
  - Not minable, not blastable, not water. `MoveCostMultiplier` 1.0 (moot — entry
    is fatal).
- New `DeathCause.Fell`.
- New `SimEvent MinerFell(int MinerId)` (mirrors `MinerDrowned`).
- New shared helper on `Simulation`, e.g. `KillByTile(Miner m)`: sets
  `Alive = false`, clears activity, and picks cause + event from the tile the
  miner is on — `DeepWater` → `Drowned`/`MinerDrowned`, `Pit` → `Fell`/`MinerFell`.
  Both `TryMove`'s lethal block and `DrownOccupants` route through it (today they
  hardcode `Drowned`/`MinerDrowned`).
- Pits are static, so they never appear *under* a standing miner. The only death
  path is "moved onto a pit" in `TryMove`; `DrownOccupants` still exists for the
  rising-flood case and now assigns the correct cause via the helper.

## 2. Map generation (Core)

- New `MapConfig` knobs:
  - `bool Pits = false` — gates the whole `PlacePits` pass.
  - `int PitSiteCount` — number of pit sites, with light per-player scaling
    (tunable; start in the same ballpark as gold/items).
  - `double PitClusterChance = 0.3` — chance a site grows beyond one tile.
  - `int PitClusterMax = 5` — cap on a grown cluster's tile count.
- New `PlacePits` pass, carved on **Floor** tiles after the cavern + water are
  finalized and the largest region is known. Each site starts as one Floor tile;
  with `PitClusterChance` it flood-grows into a small 2–5 tile blob over adjacent
  Floor.
- **Reachability is structural, not guarded per-site.** After carving pits,
  recompute the largest traversable region — `Pit` is automatically excluded
  because it is neither Floor nor Shallow. All later placement passes (spawns,
  center, gold, items, decoys) run against that post-pit region. Consequences:
  - Pits that fragment the map merely shrink the playable area; the chosen region
    is always one fully-connected component, so spawns can always reach center —
    exactly how deep-water holes behave today.
  - Pits never land on a spawn tile or a visible item, because those passes filter
    to Floor-in-region and run *after* `PlacePits`.
- Threading: `MapConfig.For(...)` / map construction must honor the `Pits` flag on
  **both** host and client (see §3). Exact pass ordering relative to existing
  passes is finalized against the real `MapGenerator` during planning; the design
  constraint is "carve pits on finalized Floor, then recompute region, then place
  everything else."

## 3. Netcode / sync

- **Zero per-tile sync for pits.** Host and client both regenerate the identical
  map from `(seed, MapConfig)` — exactly like 4a water (which added no netcode).
- The **only** new plumbing: the `Pits` toggle must reach **both** sides'
  `MapConfig`. Thread a `bool Pits` through the `BeginMatch` RPC (mirroring how
  `mode` and `flooding` thread today) into both the host sim map and the client
  render map. Note this is slightly more than the flood toggle, which only needed
  to reach the host `Simulation` at runtime; pits are map-gen, so the flag must
  reach map generation on both peers.
- `DeathCause.Fell` rides the existing `MinerSnapshot.Cause` field — no snapshot
  schema change.
- Plank-over-pit already syncs through the existing `PlankPlaced` /
  `TileChange(NewType = Plank)` path — no change.

## 4. Godot — render / audio / UI

- `WorldRenderer`: draw `Pit` as a near-black hole, visually distinct from deep
  water's dark blue (e.g. black fill with a faint rim). Fog-gated like every tile.
- `MatchAudio`: falling-scream SFX when a death's `DeathCause` is `Fell` (mirrors
  the splash-on-drown selection). New `SfxLibrary` entry — procedural placeholder,
  drop-in by filename later.
- `DeathFeed`: add the `Fell` case ("fell into a pit" self-banner + a kill-feed
  line for rivals) alongside Drowned/Exploded/Left.
- `Lobby`: host-only **"Pits" CheckBox**, off by default, mirroring the Flooding
  checkbox; its value threads into `BeginMatch`.
- `TryPlacePlank`: generalize the faced-tile guard from `IsWater()` to "water or
  pit" so a held plank caps a faced pit into a `Plank`.

## 5. Tests (Core)

- Tile predicates for `Pit`: enterable, lethal, not walkable / minable / blastable,
  transparent (does not block sight).
- `TryMove` onto a pit → miner dead, `DeathCause.Fell`, `MinerFell` emitted.
- `KillByTile` assigns the correct cause for a deep-water tile vs a pit tile.
- Plank bridges a faced pit → tile becomes `Plank`, `PlankPlaced` emitted, and
  subsequently moving onto it is safe (no death).
- Map-gen: determinism (same seed + config → identical pits); pits only on Floor;
  spawns / center / gold / visible items never on a pit; the post-pit region is
  connected (a spawn can reach center); `Pits = false` ⇒ zero pit tiles.

## Out of scope

- Cave-ins (separate later cycle).
- Any pit dynamism (spreading, opening from collapse) — pits are static.
- Flood filling a pit: `AdvanceFlood` only converts Floor/water, so a pit walls off
  the flood like rock and stays a pit. No special-casing.
