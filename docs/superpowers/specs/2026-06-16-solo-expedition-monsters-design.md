# Solo Expedition Mode + Wandering Monsters — Design

**Status:** Approved (brainstorm) — 2026-06-16
**Author:** Jim + Claude

## Summary

A single-player `Expedition` game mode: the lone miner must clear **every gold
vein** on the map and then **escape at their start tile**, while **wandering
monsters** (slimes, ghosts, goats) hunt them. One touch from a monster is fatal.
Monsters are killed by blasts, and (except the ghost) by the map's own hazards —
pits, lava, deep water. The run is finite: a fixed light roster (3–5), no
respawns, the tension coming from out-maneuvering a known set while you mine.

Monsters are a new mobile entity living inside the deterministic `Simulation`
alongside miners, so the existing host/snapshot pipeline carries them unchanged
in shape, and they drop into co-op/multiplayer later for free.

## Goals

- A solo, objective-driven mode that reuses existing systems (gold, hazards,
  blasts, snapshot pipeline, farthest-point spawn dispersion).
- Three monster types with genuinely distinct movement behaviour.
- Fully deterministic simulation (reproducible monster paths from a seed) so the
  Core is headless-testable and multiplayer-ready.
- Clear win (all gold + escape) and loss (miner dies) conditions.

## Non-Goals

- Health/hearts for the miner — the binary alive/dead model is kept.
- Escalating spawns / waves — fixed roster only.
- Monsters in the existing PvP modes — Expedition only for now (the determinism
  keeps the door open).
- Pathfinding cleverness (A*) — greedy/line movement is sufficient and cheap.

## Mode: `Expedition`

Add `Expedition` to `enum GameMode`. Single-player, launched as a host with one
local peer through the existing `MatchHost` path.

**Win / loss — resolved in `RoundResolver.Resolve`:**

- **Win:** miner is alive **and** no `GoldRock` tiles remain on the grid **and**
  the miner stands on the **escape tile** (their start spawn).
- **Loss:** the universal last-man-standing rule already returns game-over when
  zero miners are alive, so any miner death (monster contact or existing hazard)
  ends the run as a loss (winner = -1).

**Escape tile:** the miner's start spawn position. Inert until the **last gold
vein is cleared**; at that moment an `EscapeOpened` event fires (drives a map
marker + audio sting) and standing on the tile thereafter wins.

The sim tracks **remaining `GoldRock` count** (decremented whenever a `GoldRock`
becomes `Floor` via mining or blast) rather than trusting a separate counter, so
"all gold cleared" is `remaining == 0`. The escape tile is passed into the
`Simulation` (like `Center` already is).

## Monsters

New entity and kind:

```
enum MonsterKind { Slime, Ghost, Goat }

sealed class Monster
{
    int Id;                       // stable, ascending; drives deterministic iteration order
    GridPos Pos;
    Direction Facing;
    MonsterKind Kind;
    bool Alive = true;
    Direction ChargeDir;          // Goat: current charge heading
    double MoveCooldownRemaining; // per-kind cadence gate, mirrors Miner
}
```

Monsters are held in `Simulation` (`List<Monster>`), advanced by a new
`AdvanceMonsters(dt)` step in `Tick`.

### AI behaviours

Each kind moves one tile when its `MoveCooldownRemaining` hits zero, then resets
the cooldown to its per-kind cadence (`SimConfig`).

- **Slime** — *terrain-bound.* If the miner is within `MonsterSenseRadius`
  (Manhattan), step greedily toward them (reduce Manhattan distance, cardinal
  only); otherwise wander one random cardinal step. Rock/edge blocks movement
  (move is skipped if blocked). Will step onto an **open lethal floor tile**
  (pit/lava/deep water) while chasing → can be lured to its death; dies there.
- **Ghost** — *wall-phasing.* Always steps toward the miner (greedy cardinal,
  ignoring rock and edges within bounds — moves through walls). **Floats:** never
  dies on a lethal tile. Only a **blast** banishes it. The "you can't wall
  yourself in" threat.
- **Goat** — *fast charger.* Moves in `ChargeDir` each cadence. On hitting a wall
  (rock/edge ahead), it **re-aims**: toward the miner if within sense radius
  (pick the cardinal that most reduces Manhattan distance), else a random
  cardinal. Terrain-bound; dies to all hazards + blasts like the slime.

`Facing` is set to the direction moved (for sprite orientation). Cadences
(illustrative, tuned in `SimConfig`): goat fastest, slime slowest, ghost between.

### Contact and death

- **Monster kills miner (`DeathCause.Mauled`, event `MinerMauled`):** checked in
  two places — inside `AdvanceMonsters` after a monster steps (monster moves onto
  the miner), and inside `Miner.TryMove` after the miner steps (miner moves onto
  a monster). Either way the live miner on a shared tile with a live monster dies.
- **Blast kills monster:** in `Detonate`, any live monster within the blast kill
  radius (same radius used for miners) is killed (`MonsterKilled`).
- **Hazard kills monster:** a non-ghost monster that ends its step on a lethal
  floor tile dies (`MonsterKilled`). Ghosts are immune (float).

### Spawning

A fixed roster of `MonsterCount` = **3–5**, scaling lightly with map size
(`base + perGrowth`). Deterministic type mix (round-robin over
`{Slime, Ghost, Goat}` by spawn index). Placed on floor tiles **far from the
start tile**, reusing `SpawnPlacement.SelectFarthest` against the start position
(seed the candidate list with the start tile already "chosen" so picks maximise
distance from it). No respawns.

## Determinism

The sim is currently RNG-free. Add a **seeded `System.Random` to `Simulation`**,
seed threaded from the match seed via `SimConfig` (or constructor). All monster
randomness (wander steps, goat re-aim) draws from this one stream, consumed in a
fixed order: monsters iterated by **ascending `Id`** each tick.

**Tick order becomes:**
effects → molds → cooldowns → cracks → lava → **monsters** → activities →
pickups → charges → flood.

Monsters move before activities/charges so a charge detonating this tick can
still catch a monster. The miner-contact check runs in both `AdvanceMonsters`
(monster→miner) and `TryMove` (miner→monster).

## Networking & rendering

- **Core/Net:**
  - `MonsterSnapshot(int Id, int X, int Y, int Facing, MonsterKind Kind, bool Alive)`.
  - `WorldSnapshot` gains `IReadOnlyList<MonsterSnapshot> Monsters` and a
    `bool EscapeOpen` flag.
  - `SnapshotFactory.Capture` populates monsters + escape flag;
    `SnapshotCodec` read/write extended (append-only to keep the format simple).
  - New `SimEvent`s: `MonsterMoved(int Id, GridPos From, GridPos To)`,
    `MonsterKilled(int Id)`, `MinerMauled(int MinerId)`, `EscapeOpened()`.
- **Game:**
  - `WorldRenderer` renders each live monster as a sprite per kind (4-direction
    facing, position-interpolated between ticks like miners).
  - An **exit marker** drawn on the start tile once `EscapeOpen`.
  - `Hud` shows **gold remaining** and, once escape opens, an **"Escape!"**
    prompt.
  - `MainMenu` gains a solo-Expedition entry that starts a host with one local
    miner, `MatchMode = Expedition`, monsters seeded. Loss/win flow through the
    existing `BroadcastResult` path (winner peer = the player on win, -1 on loss).

## Testing

Core carries the heavy TDD coverage (pure, headless):

- Slime: wanders when far, steps toward miner when within sense radius, blocked
  by rock.
- Ghost: steps through rock toward miner; survives standing on a pit; dies to a
  blast.
- Goat: charges in a straight line until a wall, then re-aims (toward miner when
  sensed).
- Contact kills the miner from both directions (monster-onto-miner,
  miner-onto-monster) → `DeathCause.Mauled`.
- Hazard kills a slime/goat that steps onto lava/pit/deep water; ghost unaffected.
- Blast kills monsters within radius.
- Escape opens **only** when the last `GoldRock` is cleared (`EscapeOpened` fires
  once); not before.
- `Expedition` win fires only when alive **and** all gold cleared **and** on the
  exit tile; never earlier.
- Determinism: same seed → identical monster positions after N ticks.

Rendering, the exit marker, HUD, and the solo launch flow are verified by
play-test.

## Phasing

One spec, three implementation plans executed in order:

1. **Phase 1 — Core (TDD):** `Monster` + `MonsterKind`, the three AIs, seeded
   RNG in `Simulation`, contact/hazard/blast kills, remaining-gold tracking +
   escape opening, `Expedition` mode + `RoundResolver` win, monster spawning.
   Fully headless-testable; no rendering.
2. **Phase 2 — Plumbing & play:** monster + escape snapshot/event/codec
   plumbing, `WorldRenderer` monster rendering (placeholder art), exit marker,
   solo launch flow, HUD gold-remaining + escape prompt. First playable.
3. **Phase 3 — Art:** PixelLab sprites for slime/ghost/goat (4-dir, miner-style),
   wired into `WorldRenderer`.

## Open knobs (defaults chosen; adjustable after play-test)

- Mode name: **`Expedition`**.
- `MonsterCount` base/scaling, per-kind cadences, `MonsterSenseRadius` — all in
  `SimConfig`, tuned after the first play-test.
