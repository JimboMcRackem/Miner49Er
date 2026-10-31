# Mine Carts & Tracks — Design

**Date:** 2026-07-18
**Status:** Approved (design), pending implementation plan
**Area:** Gameplay. Touches the deterministic `Miner49er.Core` sim + netcode (authoritative, networked) plus Godot presentation. NOT cosmetic (unlike pit props).

Pushable mine carts that roll along seeded rail lines: battering rams that squash monsters and shove miners, and a cargo platform for a lantern (a rolling ghost-sweeping light) or a charge (a rolling bomb). A heist-sized networked feature, built in phases; all authoritative logic is pure Core and TDD'd.

## Global constraints

- **Determinism:** all cart state is integer/tile-based (no floats) and resolved in `Simulation`; identical on every client. Reuse `_rng` only where a random choice is genuinely needed (spawn/seeding is map-gen, not per-tick).
- **Networking:** carts are a networked sim entity (snapshot + codec), mirroring `Monster`/`Portal`. The *track layout* is derived from the map seed and is NOT networked.
- **Cross-mode isolation:** carts spawn only in the standard exploration floor generator (see Spawn). Modes that zero base map features (Derby/Heist) get no carts unless they opt in later. No cross-mode leakage in the sim.
- **Reuse, don't reinvent:** ghost-kill uses the existing `InLanternLight`; the rolling bomb uses the existing charge fuse + `DetonateAt`; the cart light uses the existing per-entity light path in `LightRenderer`.
- Named constants for all tuning (spawn rate, cart count, cadence, radii).

---

## 1. Tracks (deterministic path graph)

Rails sit on ordinary walkable `Floor` — a track tile is **not** a new `TileType`. Map generation (`Map/MapGenerator.cs` → `Map/GeneratedMap.cs`) produces a deterministic **rail path-graph** for the floor: a set of track tiles plus, for each, which orthogonal neighbours it connects to (the directions a cart may roll). Because it derives from the map seed, host and all clients compute the identical graph with zero networking. Carts may move only along this graph; a track end (a track tile with no onward connection in the travel direction) stops a cart.

The sim needs the graph to resolve cart movement; expose it on the generated map / grid so `Simulation` can query "is there track from tile A in direction D."

## 2. Cart entity (networked)

New `Sim/Cart.cs`: `Id`, `Pos` (GridPos), `Direction` (last/facing travel dir), `Cargo` (enum: `None | Lantern | Charge`), `FuseRemaining` (double; >0 only for a launched Charge cart), and a `Derailed`/destroyed flag. All integer/tile-based except `FuseRemaining` (a countdown mirroring existing charge fuses).

- **Snapshot:** add `CartSnapshot` (record struct like `MonsterSnapshot`) carrying `Id, X, Y, Direction, Cargo, FuseRemaining`; add the list to `WorldSnapshot`.
- **Codec:** `Net/SnapshotCodec.cs` — symmetric `w.Write(count); foreach(...)` write/read (mirroring Monsters), wire-symmetry tested.
- **Factory:** populate in `Net/SnapshotFactory.cs`.
- **Tick:** carts are advanced by the push resolver in `Simulation` (they are inert otherwise — no autonomous motion).

## 3. Movement — momentum push (tracks only)

Carts have no autonomous motion. A miner **walking into a cart along the rail** (reusing normal movement input; no new key) shoves it. Resolution is a deterministic loop from the cart's tile in the push direction, one tile at a time, **until blocked**:

- **empty rail ahead** → cart advances one tile; continue.
- **track end / wall / non-track tile ahead** → cart stops. Done.
- **another cart ahead** → chain-push: the struck cart is pushed too (a train); resolve the lead cart first, then trailing carts follow into vacated tiles. The whole train halts when the lead cart is blocked.
- **hazard tile on the rail ahead** (`IsLethal()` — DeepWater/Pit/Lava/LavaVent, or a flooded/lava-crept rail) → the cart **derails and is destroyed** (removed); a Charge cart detonates at that tile (see §5).
- **monster ahead** that is squashable (§4) → the monster is **killed**; the cart rolls on through (monsters don't block a cart).
- **miner ahead** → the miner is **shoved** one tile further in the push direction. If the miner's destination is open, both advance and resolution continues. If the miner cannot be shoved (a wall/cart/non-track solid behind them) they are **crushed** (die, cause `Crushed`). If the miner is shoved onto a lethal tile they die to that hazard. Shoving works on rivals too — a competitive kill tactic.

The push only starts if the rail continues from the cart in the push direction; a miner walking into a cart from a side the track does not continue toward finds it immovable (the cart blocks like a wall, miner does not advance). The pushing miner moves one tile into the cart's original tile (standard Sokoban step); the cart may travel many tiles (momentum).

A stationary cart **occupies its tile** and blocks movement like a living miner does today. Add cart-occupancy to the three passability checks: miner move resolution, monster move (`CanMonsterEnter`), and `AI/BotPathfinder.Passable` (so bots route around carts).

## 4. Squash

Squashable kinds: **Slime, ZombieMiner, SkeletonHuman, SkeletonDino, Goat**. When a rolling cart enters a tile holding one, set `mo.Alive = false` and add `MonsterKilled` — the existing Alive-diff drives the death-particle burst, so squash visuals come free. The cart continues rolling. Excluded (unaffected, and they do not block the cart): **Ghost** (incorporeal — but see the lantern sweep, §5), **WaterSnake** (lives in deep water, off-rail), **Octopus** boss.

## 5. Cargo — lantern & charge (attach/detach)

A cart has one cargo slot: `None`, `Lantern`, or `Charge`. **Attach:** a miner standing adjacent to a cart and holding a lantern or a charge presses the use/interact action → the item moves from the miner's hands onto the cart (`cart.Cargo = …`, miner `Held = None`). **Detach:** an empty-handed miner adjacent to a laden cart presses the same action → cargo returns to the miner's hands (for a Charge, only while its fuse is unlit — see below).

**Lantern cart (ghost sweeper).** A cart with `Cargo == Lantern` is added as a third source in `InLanternLight(pos)` (alongside held lanterns and loose lantern items): any ghost within `LanternRadius` (Chebyshev) of a lit cart dies, and ghosts halt at its AOE edge — identical to existing lantern behaviour. Parked, it is a stationary ghost-ward; momentum-launched, its AOE sweeps the whole rail segment, clearing ghosts along the way. It also emits the moving visual glow: one added source in `LightRenderer.AddSourceLights`, keyed by cart id. Detaching reclaims the lantern.

**Charge cart (rolling bomb).** Attach a charge (`Cargo == Charge`, fuse unlit). **Launching** the cart (the push that starts it rolling) **lights the fuse** (`FuseRemaining = ` the existing charge fuse duration). The cart rolls per §3; it **detonates**:
- when `FuseRemaining` reaches 0 (decremented each tick) at whatever tile it currently occupies, OR
- immediately when it stops (wall/track-end/train-blocked), derails into a hazard, or squashes — whichever happens first.

Detonation calls the existing `DetonateAt` (same blast as a planted/thrown charge) at the cart's tile and destroys the cart. Detaching a charge is allowed only before launch (unlit fuse); once launched it is committed.

## 6. Presentation

- **Rails:** drawn client-side along the derived track graph (PixelLab rail sprite tiled along connections, or a procedural rail). Static per floor.
- **Carts:** cart sprite drawn at the cart's position with smooth interpolation between tile steps (like miners/monsters). A laden cart shows its cargo — a lantern glow (via the added `LightRenderer` source) or a charge sprite with the existing fuse-spark FX.
- All positions/cargo come from the snapshot, so clients match.

## 7. Spawn

Standard exploration floor generator seeds a rail line on **~35% of floors** (`CartTrackFloorChance`), carrying **1–2 carts** (`CartsPerTrack`) at seeded positions on the line, cargo `None`. Deterministic from the map seed. Competitive modes that zero base features do not get carts in this version (documented opt-in follow-up).

---

## Files (by phase)

**Phase 1 — Core sim (TDD):**
- Create `src/Miner49er.Core/Sim/Cart.cs`.
- `Map/MapGenerator.cs` / `Map/GeneratedMap.cs` — rail path-graph generation + accessor.
- `Sim/Simulation.cs` — push resolver (roll/stop/train/derail/squash/shove-crush), cart occupancy in miner/monster passability, `InLanternLight` cart source, fuse tick + detonation, cart spawn from the map graph.
- `Net/Snapshots.cs` — `CartSnapshot` + `WorldSnapshot` list.
- `Net/SnapshotCodec.cs` — symmetric write/read.
- `Net/SnapshotFactory.cs` — populate carts.
- `AI/BotPathfinder.cs` — cart occupancy in `Passable`.
- Tests: `src/Miner49er.Core.Tests/` — movement resolution, squash, shove/crush, chain-push, derail, lantern AOE (incl. moving), detonation, codec wire-symmetry, cross-mode isolation.

**Phase 2 — Presentation:** `game/` rail + cart renderer, smooth interpolation, cart light in `LightRenderer`.

**Phase 3 — Cargo:** attach/detach input in the client + `Simulation` action; cargo/fuse in snapshot/codec (folded from Phase 1 if simpler); charge launch/detonate; lantern sweep visuals.

**Phase 4 — Bots:** verify bot routing around carts; (optional, deferred) bots using carts.

## Testing

- All authoritative logic is pure Core and unit-tested (Phase 1 tests above). Existing suite stays green.
- Codec wire-symmetry round-trips `CartSnapshot`. Cross-mode isolation: no carts leak into modes that don't seed them.
- Presentation verified by running the game.

## Tuning knobs

`CartTrackFloorChance` (~0.35), `CartsPerTrack` (1–2), cart fuse duration (reuse existing charge fuse), `LanternRadius` (existing), squashable-kind set, roll cadence / interpolation speed.

## Open follow-ups (out of scope for v1)

- Carts in competitive modes (shove-a-rival tactic) behind a mode toggle.
- Bots actively using carts (loading/launching).
- Multiple cargo or other cargo kinds.
