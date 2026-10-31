# Mine Carts & Tracks — Pre-Brainstorm Reconnaissance

**Status: NOT an approved spec.** This is codebase reconnaissance for a future
brainstorming session. Mine carts are a *gameplay* feature touching the deterministic
`Miner49er.Core` sim and netcode, so it is gated behind the normal brainstorm → spec →
plan → approval cycle (HARD-GATE). No implementation should start from this doc.

## What the user asked for
Visual mine-cart tracks with carts appearing occasionally. Carts are obstacles but need a
"physics change" so they can follow tracks. Possibly: attach lights to carts; carts can
squash slimes, skeletons, and zombies.

## Why this is not cosmetic (unlike pit props)
Carts move in the world, block tiles, and kill monsters — all of that is authoritative game
state that must be identical on every client. It lives in the sim + must be networked. Only
the *cart light* is presentation-layer.

## Integration seams (verified in code)

**1. Tracks**
- `src/Miner49er.Core/Grid/TileType.cs` is a flat enum + predicate extensions
  (`IsWalkable`, `IsEnterable`, `IsMinable`, `IsBlastable`, `BlocksSight`, …).
- A track tile is still ordinary walkable Floor, so a track should **not** be a new
  `TileType`. Model tracks as a deterministic **path graph produced by map generation**
  (`Map/MapGenerator.cs` → `Map/GeneratedMap.cs`), so host and all clients agree without
  networking the track layout. Carts need this graph in-sim to know where they can roll.

**2. Cart entity (new, networked)**
- Mirror the `Monster`/`Portal` entity pattern: `src/Miner49er.Core/Sim/Cart.cs`
  (`Id`, `Pos` (GridPos), `Direction`, alive/derailed flags — keep state **integer/tile-based,
  no floats**, for determinism).
- Snapshot: add `CartSnapshot` to `Net/Snapshots.cs` (record struct like
  `MonsterSnapshot`), add it to the `WorldSnapshot` list.
- Codec: `Net/SnapshotCodec.cs` encodes lists as `w.Write(count); foreach(...)` (see
  Monsters at lines 46–47) — add symmetric write/read.
- Populate in `Net/SnapshotFactory.cs`; advance carts each tick in `Sim/Simulation.cs`.

**3. "Physics change" = cart movement + occupancy**
- Carts should step tile-to-tile on a deterministic cadence (like miners' `MoveSeconds`
  discrete steps), following the track graph; reverse or stop at track ends / walls /
  hazards. A tile-stepping mover, NOT float physics — keeps the sim deterministic.
- Obstacle behaviour: miner/monster movement is gated by TileType passability **plus**
  per-entity occupancy (e.g. `Simulation.cs:1350` blocks a spawn tile already held by a
  living miner). A cart occupies a tile the same way → add cart-occupancy to the passability
  checks at **three sites**: miner move resolution, monster move, and
  `AI/BotPathfinder.Passable` (`BotPathfinder.cs:80`) so bots route around carts.

**4. Squash**
- Monster death is uniformly `mo.Alive = false` throughout `Simulation.cs`. A cart entering a
  tile occupied by `Slime` / `ZombieMiner` / `SkeletonHuman` / `SkeletonDino` sets that
  monster `Alive = false`. The Alive-diff already drives the monster-death particle burst
  (the `EmitMonsterDeath` path the parent just wired) — so squash visuals come for free.
- `MonsterKind` = `{ Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino, WaterSnake }`.
  Decide per-kind: Ghost (incorporeal), Goat, WaterSnake, Octopus likely excluded.

**5. Cart lights (the one low-risk / presentation piece)**
- `game/LightRenderer.AddSourceLights` already adds moving lights per entity via
  `_lights.AddLight(grid, pos, radius, Flicker.Multiplier(HeldSeed(id), …))`
  (held lantern, `LightRenderer.cs:86`). A lit cart is just one more moving source keyed by
  cart id — needs only the (already-networked) cart position. Cheap, cosmetic.

## Open design questions for the brainstorm (decide before any plan)
1. **Cart drive model:** autonomous rollers, player-pushable, or rideable? (biggest scope driver)
2. **Cart vs miner:** does a cart squash miners too, shove them, or just stop? Death cause?
3. **Track source:** map-generated deterministic graph (recommended) vs hand-authored.
4. **Cart-vs-cart / cart-vs-wall / cart-vs-hazard (water, pit, lava):** stop, reverse, derail, or fall in?
5. **Which monster kinds are squashable**, and does a cart survive the squash or get blocked?
6. **Spawn frequency** and whether carts are per-floor map features or roaming.
7. **Lights:** all carts lit, or only some? (independent, can be a follow-up.)

## Rough scope estimate
New Core files (`Cart.cs`, cart mover), edits to `Simulation.cs`, `Snapshots.cs`,
`SnapshotCodec.cs`, `SnapshotFactory.cs`, `MapGenerator.cs`/`GeneratedMap.cs`,
`BotPathfinder.cs`; Godot-side `game/` renderer for tracks+carts and the optional light.
Codec wire-symmetry + determinism + cross-mode isolation = the usual heist-sized networked
feature, needing its own multi-task plan and tests (the squash/occupancy/track-follow logic
is pure Core and should be TDD'd).
