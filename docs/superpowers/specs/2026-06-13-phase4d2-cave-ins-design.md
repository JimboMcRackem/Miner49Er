# Phase 4d-2 — Cave-Ins (design)

Date: 2026-06-13

## Summary

The cave-in is a **weakened-floor** hazard: patches of "cracked" floor placed at
map generation. Unlike the bottomless pit (which kills on first touch) or the
flood (an ambient clock-paced threat), a cracked tile *remembers being stepped
on*. You may cross a fresh crack safely, but **lingering on it or loading it a
second time makes the floor give way and you fall**. Cracks weaken globally —
the shortcut you wear down becomes a trap for rivals, and for you on the way
back. Explosives shake cracks loose: a blast collapses every crack in its disc.
A carried **water-plank** bridges a crack, the same counterplay it gives pits.

This is the second of the Phase 4d hazard family, after the bottomless pit
([4d-1](2026-06-13-phase4d-bottomless-pit-design.md)).

## Decisions (locked during brainstorm)

- **State model = global progressive.** A crack tile has three shared states:
  `Cracked` → `Crumbling` → collapsed hole. State is per-tile and visible to all
  players, not per-miner.
- **"Walking over 1 is ok"** — entering a `Cracked` tile is safe; stepping off it
  promotes it to `Crumbling`.
- **"staying on the crack triggers the fall"** — dwelling on a `Cracked` tile for
  ≥ `CrackDwellSeconds` collapses it under you.
- **"going over again triggers the fall"** — entering a `Crumbling` tile (the
  second loading, by anyone) collapses it immediately.
- **Explosives break cracks in a radius.** A detonation collapses every
  `Cracked`/`Crumbling` tile inside the blast disc.
- **Planks bridge cracks**, exactly as they bridge pits and water.
- **Lobby toggle**, off by default (its own checkbox, mirroring Pits/Flooding).

### Micro-decisions (approved)

1. **Collapse reuses `TileType.Pit`** — the collapsed hole is the existing pit
   tile (already lethal, bridgeable, near-black, fully rendered). No third hole
   type.
2. **Death cause:** a crack giving way under you = new `DeathCause.Crushed`
   ("caught in a cave-in"). Walking into a *leftover* hole later reuses the
   existing `DeathCause.Fell` ("fell into a pit"), because by then the tile is
   literally a `Pit`.
3. **Blast collapse radius = the existing `BlastRockRadius` disc** (the same
   radius that pulverizes rock), with a tunable knob if it wants widening later.
4. **`CrackDwellSeconds ≈ 0.75s`** — the "staying too long" window, comfortably
   longer than the ~0.12s move cadence so a normal walk-through is always safe.

## 1. Tile & state model (Core)

- Two new tile types: `Cracked`, `Crumbling`. Collapse converts a crack to the
  existing `TileType.Pit`.
- `TileTypeExtensions` for `Cracked` and `Crumbling`:
  - `IsWalkable` → **true** (safe ground for spawns/fog/reachability; they are
    floor you can stand on). Cracks are placed *after* the spawn/center/gold/item
    passes, so nothing important ever spawns on one regardless.
  - `IsEnterable` → **true**.
  - `IsLethal` → **false** (a crack is not instantly fatal; death is event-driven
    on the second loading or via dwell).
  - `BlocksSight` → **false** (open floor; transparent once fog reveals it).
  - `MoveCostMultiplier` → 1.0. Not minable, not blastable, not water.
  - `IsBridgeable` → **true** (a held plank caps a crack into a safe `Plank`).
    `IsBridgeable` becomes `IsWater() || t is Pit or Cracked or Crumbling`.
- New `DeathCause.Crushed`.
- New `SimEvent`s:
  - `CrackWeakened(GridPos Pos)` — a `Cracked` tile promoted to `Crumbling`.
  - `CrackCollapsed(GridPos Pos)` — a crack collapsed to `Pit`.
  - `MinerCrushed(int MinerId)` — a miner killed by a collapse (mirrors
    `MinerFell`/`MinerDrowned`).

## 2. Collapse mechanics (Core)

State is held entirely in the tile grid (`Cracked`/`Crumbling`/`Pit`) plus a
per-miner dwell accumulator. No separate crack registry is needed.

**On a successful `TryMove` from `from` to `target`:**

1. **Leaving a crack:** if `from` was `Cracked`, promote it to `Crumbling`
   (`Grid.Set(from, Crumbling)` + `CrackWeakened(from)`). The miner survived the
   crossing but wore the floor down. (If `from` was already `Crumbling`, it stays
   `Crumbling`.)
2. **Entering a crack:**
   - If `target` is `Crumbling`: collapse now — `Grid.Set(target, Pit)`,
     `CrackCollapsed(target)`, then `CollapseKill(miner)` (see below). The miner
     went over again.
   - If `target` is `Cracked`: safe entry; the miner is now standing on a fresh
     crack. Its dwell timer (per-miner) begins accruing on subsequent ticks.

Order note: the existing `TryMove` already checks `IsLethal()` on the target and
calls `KillByTile`. Crack tiles are **not** lethal, so they fall through that
check; the crack handling above is added alongside it. A collapse that turns the
target into `Pit` happens *after* the move, so the miner is positioned on the new
hole when killed.

**Dwell (per tick, in `Tick`):** for each living miner, if it is standing on a
`Cracked` (or `Crumbling`) tile and did not move this tick, accumulate
`CrackDwell += dt`; reset to 0 on any successful move. When
`CrackDwell ≥ Config.CrackDwellSeconds`, the floor gives way: `Grid.Set(pos,
Pit)`, `CrackCollapsed(pos)`, `CollapseKill(miner)`.

**`CollapseKill(Miner m)`:** sets `Alive = false`, clears activity,
`DeathCause = Crushed`, emits `MinerCrushed(m.Id)`. (Distinct from `KillByTile`,
which assigns `Fell`/`Drowned` for stepping onto an already-lethal pit/deep-water
tile. Crushed is reserved for the moment of collapse.)

New `SimConfig` knobs: `CrackDwellSeconds = 0.75`.

## 3. Blast interaction (Core)

In `Detonate`, the existing disc loop walks every tile within
`BlastRockRadius + charge.BlastBonus` (Manhattan). Extend it: if a tile in the
disc is `Cracked` or `Crumbling`, set it to `Pit` and emit `CrackCollapsed(p)`
(regardless of which crack state it was — the blast shakes it straight down).

Death-cause ordering inside `Detonate`:
1. Collapse cracks in the disc (tiles become `Pit`).
2. Kill miners within `BlastKillRadius + charge.BlastBonus` as `Exploded` (the
   existing pass).
3. Any *still-living* miner now standing on a freshly-collapsed `Pit` (its crack
   dropped out from under it, but it was outside the kill radius) dies via
   `CollapseKill` → `Crushed`.

This makes blasting near a crack field a deliberate area-denial / offensive tool.

## 4. Map generation (Core)

- New `MapConfig.CaveIns = false` gates the whole `PlaceCracks` pass.
- Knobs (tunable; cracks are "areas," so they skew larger than the mostly
  single-tile pits):
  - `int CrackSiteCount` — number of crack sites, light per-player scaling, in the
    same ballpark as pit sites.
  - `int CrackPatchMax = 8` — cap on a grown patch's tile count.
  - `double CrackPatchGrowChance = 0.7` — bias toward multi-tile blobs (higher
    than the pit cluster chance, since these are deliberately *areas*).
- `PlaceCracks` carves `Cracked` on **Floor** tiles after the cavern, water, and
  the spawn/center/gold/item passes are finalized. Each site starts as one Floor
  tile and flood-grows a small blob (≤ `CrackPatchMax`) over adjacent Floor.
- **Reachability:** cracks are `IsWalkable`/traversable at gen time, so the
  initial map stays one fully-connected component — no structural exclusion is
  needed (unlike pits, which are carved before the region recompute). Mid-match
  collapses can locally reshape the map, but only where players actively trigger
  them, so any severing is self-inflicted (comparable to the flood cutting a
  miner off). Crack placement filters to Floor-in-region, so spawns, center,
  gold, and visible items are never on a crack.
- Threading: `MapConfig.For(...)` must honor the `CaveIns` flag on **both** host
  and client (see §5). `MapConfig.For` gains a `bool caveIns = false` parameter
  alongside the existing `bool pits = false`.

## 5. Netcode / sync

- **Zero per-tile sync for the initial cracks.** Host and client both regenerate
  the identical map from `(seed, MapConfig)`, exactly like pits and 4a water.
- The new toggle plumbing: thread a `bool CaveIns` through the `BeginMatch` RPC
  (mirroring `pits`) into both the host sim map and the client render map →
  `NetworkManager.MatchCaveIns`.
- **Runtime transitions** (`Cracked → Crumbling`, crack `→ Pit`) ride the
  existing `TileChange(NewType = …)` path: `MatchHost` maps `CrackWeakened` →
  `TileChange(pos, Crumbling)` and `CrackCollapsed` → `TileChange(pos, Pit)`;
  `MatchClient` already applies `t.NewType`. No new sync channel.
- `DeathCause.Crushed` rides the existing `MinerSnapshot.Cause` byte — no
  snapshot schema change (just a new enum value; the codec already writes the
  cause as a byte).
- Plank-over-crack already syncs through the existing `PlankPlaced` /
  `TileChange(NewType = Plank)` path — no change.

## 6. Godot — render / audio / UI

- `WorldRenderer`: draw `Cracked` as floor with thin fracture lines; `Crumbling`
  as heavier fractures / loose rubble (clearly "this one's been used"); collapsed
  cracks render as the existing `Pit` near-black hole. All fog-gated.
- `MatchAudio`: a low rumble/crack SFX on `CrackCollapsed` near the local miner,
  and select it for `Crushed` deaths (mirrors splash-on-drown / wail-on-fall
  cause selection). New `SfxLibrary.CaveIn` — procedural placeholder, drop-in by
  filename later. Optional softer creak on `CrackWeakened` (YAGNI for v1 unless
  cheap).
- `DeathFeed`: add the `Crushed` case — self-banner ("CAVED IN!") plus a kill-feed
  line for rivals ("{name} was caught in a cave-in"), alongside
  Drowned/Exploded/Left/Fell.
- `Lobby`: host-only **"Cave-ins" CheckBox**, off by default, mirroring the
  Pits/Flooding checkboxes; its value threads into `BeginMatch`.

## 7. Tests (Core)

- Tile predicates for `Cracked`/`Crumbling`: walkable, enterable, transparent,
  bridgeable, not lethal / minable / blastable; `MoveCost` 1.0.
- Crossing a `Cracked` tile (enter, then move off) → tile becomes `Crumbling`,
  miner alive, `CrackWeakened` emitted.
- Dwelling on a `Cracked` tile ≥ `CrackDwellSeconds` → tile becomes `Pit`, miner
  dead, `DeathCause.Crushed`, `CrackCollapsed` + `MinerCrushed` emitted.
- Entering a `Crumbling` tile → immediate collapse to `Pit`, miner dead,
  `Crushed`.
- Plank on a `Cracked`/`Crumbling` tile → becomes `Plank`, `PlankPlaced` emitted,
  subsequently safe to stand on.
- Blast disc over cracks → each crack tile becomes `Pit`, `CrackCollapsed`
  emitted; a miner on a collapsed crack inside `BlastKillRadius` dies `Exploded`,
  one outside it dies `Crushed`.
- `CollapseKill` assigns `Crushed`; stepping onto the resulting `Pit` later
  assigns `Fell` (cause routing).
- Map-gen: determinism (same seed + config → identical cracks); cracks only on
  Floor; spawns / center / gold / visible items never on a crack; the initial map
  is connected; `CaveIns = false` ⇒ zero crack tiles.

## Out of scope

- Pit dynamism, lava, and other 4d hazards — separate cycles.
- A dedicated "cave-in hole" tile distinct from `Pit` (reusing `Pit` is the
  approved choice).
- Smoothing/animation of the collapse beyond the existing fog-gated tile redraw.
- Cracks regenerating or healing — once collapsed, a hole is permanent (until
  bridged by a plank).
