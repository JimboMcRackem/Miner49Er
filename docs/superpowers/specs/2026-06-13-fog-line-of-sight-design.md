# Fog Line-of-Sight & Smooth Flood — Design

**Date:** 2026-06-13
**Status:** Approved (brainstorm)
**Branch:** `fog-los-overhaul`

## Goal

Replace the current occlusion-free fog with true line-of-sight (rock blocks
vision), present it as a soft round lantern light with hard wall shadows, and
soften the flood so rising water eases in instead of popping ring-by-ring.

## Motivation

Today `Visibility.Compute` returns a plain Euclidean disc — walls do not block
sight, so a miner sees terrain and incidental item positions straight through
solid rock. `FogRenderer` paints flat black/dim squares, giving a blocky circle.
The flood flips a whole edge-distance ring of tiles to water in a single tick,
which reads as a hard pop. Three targeted changes fix all three.

## Locked decisions (from brainstorm)

- **Listen is unchanged.** Its through-rock shimmer reveals **items/decoys only**
  (a sonar ping for loot), never terrain. It is a separate path in
  `WorldRenderer`, drawn regardless of fog, and LOS does not touch it. With LOS,
  Listen simply becomes more valuable because normal vision no longer leaks item
  locations through walls.
- **Flood smoothing is cosmetic only.** The authoritative flooded tile set still
  advances ring-by-ring in Core (deterministic, synced, drowning unchanged). Only
  the renderer softens the appearance.
- **Gameplay visibility stays crisp.** A tile is either in the line-of-sight set
  or not — that binary set drives entity rendering, item reveal, and
  explored-memory. The softness is a purely cosmetic layer on top, so multiplayer
  stays deterministic and the play area stays readable.
- **Soft edge, not global dimming.** Only the outer rim of the lit region feathers
  into fog; the bulk of the lit area is fully clear and readable.

## Architecture

Three focused changes, each in one file:

1. **Core — `src/Miner49er.Core/Fog/Visibility.cs`:** recursive shadowcasting,
   replacing the disc. Same signature, engine-free, unit-tested.
2. **Renderer — `game/FogRenderer.cs`:** radial-gradient darkness overlay carved by
   the crisp LOS set (soft round light + wall shadows).
3. **Renderer — `game/WorldRenderer.cs`:** per-tile displayed-color crossfade with a
   deterministic per-position stagger (smooth, seeping flood).

No net/snapshot/codec changes: visibility is already derived client-side from the
synced grid + local miner position in `MatchClient.UpdateFog()`.

---

## Section 1 — Core: line-of-sight via recursive shadowcasting

**File:** `src/Miner49er.Core/Fog/Visibility.cs` (rewrite `Compute`; signature
unchanged).

### Sight-blocker predicate

Add to `TileTypeExtensions` (`src/Miner49er.Core/Grid/TileType.cs`):

```csharp
/// <summary>Blocks line-of-sight (the rock family). Floor, water, and planks are transparent.</summary>
public static bool BlocksSight(this TileType t) =>
    t is TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock;
```

Floor, ShallowWater, DeepWater, and Plank are transparent — you can see across
lakes and planks; only the rock family casts shadows.

### Algorithm

Classic recursive shadowcasting over 8 octants:

- The origin tile is always visible.
- Each octant scans row-by-row outward to `radius`, tracking a `(startSlope,
  endSlope)` shadow cone and recursing when a blocker splits the cone.
- A **transparent** tile within `dx*dx + dy*dy <= radius*radius` is added to the
  visible set (this keeps the round outer edge).
- A **blocking** tile is **itself added** to the visible set (you see the rock
  face in front of you) but narrows/ends the cone, so everything behind it in that
  cone is skipped — the carved shadow.
- Integer/rational slope math only → deterministic and identical on host and every
  client.

### Signature & integration

```csharp
public static HashSet<GridPos> Compute(TileGrid grid, GridPos origin, int radius)
```

Unchanged. `MatchClient.UpdateFog()` keeps calling it exactly as today.
`FogState`, explored-memory accumulation, and the `LongerVision` radius buff are
untouched — you now only "explore" tiles you have actually seen.

### Tests (xUnit, `tests/.../VisibilityTests.cs` or extend existing)

- Origin tile is always visible.
- Fully open area within radius equals the round disc (no regression where nothing
  blocks).
- A single rock pillar casts a shadow: the tile directly behind it (from origin)
  is **not** visible; the tiles flanking it are.
- A 1-thick rock wall blocks sight: a miner in a tunnel cannot see the floor on the
  far side.
- A blocking tile is itself visible (you see the rock face).
- Nothing beyond `radius` is ever visible.
- Symmetry on an open map: `B ∈ vis(A)` ⟺ `A ∈ vis(B)`.
- `BlocksSight` predicate: rock family blocks; Floor/ShallowWater/DeepWater/Plank
  do not.

---

## Section 2 — Renderer: soft round light + wall shadows

**File:** `game/FogRenderer.cs` (rewrite `_Draw`; keep `Init`/`_Process`).

Authoritative input stays the crisp sets from Core: `fog.IsVisible(p)`,
`fog.IsExplored(p)`, plus the local miner position and vision radius (available via
`_client`). Three darkness bands per tile:

- **Not explored** → opaque black (`0,0,0,1`), as today.
- **Explored but not currently visible** → flat dim (`0,0,0,0.6`), as today.
- **Currently visible** → a **radial falloff** instead of fully clear. With
  `t = distance(p, minerPos) / radius`, alpha is `0` through roughly the inner
  `0.7·radius`, then eases via a smoothstep up to a capped veil (≈ `0.35`) at the
  edge. Tiles **not** in the visible set get no light — so the falloff is
  automatically carved by the wall shadows (a tile one step behind rock is simply
  "not visible," reads full dark, and the gradient never bleeds into it).

Implementation is the same per-tile `DrawRect` loop as today, with a computed alpha
instead of a binary clear/skip. No shader, no occluder polygons, no 2D lights.

The round edge still lands on tile boundaries; the radial alpha softens it so it
reads as a lantern glow rather than a pixelated circle — the goal of "soft round
light." The bulk of the lit area stays clear and readable; only the outer rim
feathers.

No unit tests (Godot adapter); verified by `build 0/0` and play-test.

---

## Section 3 — Renderer: smooth flood (cosmetic crossfade + seep)

**File:** `game/WorldRenderer.cs` (the tile loop in `_Draw`, plus a small per-tile
state map and easing in `_Process`).

Sim and net are unchanged — the authoritative grid still flips
`Floor → ShallowWater → DeepWater` ring-by-ring via the usual `TileChange`. The
renderer stops snapping to the new color.

### Mechanism — per-tile displayed-color easing

- Add `Dictionary<GridPos, Color> _displayed`.
- On first encounter of a tile, **snap** `_displayed[p]` to its current target
  color (so water that already exists when you join a match does not fade in — no
  "flood storm" on connect).
- Each `_Process(delta)`: for every tile, ease `_displayed[p]` toward the grid's
  target color at a fixed rate (≈ 0.4–0.5 s to converge). `_Draw` paints
  `_displayed[p]` instead of the raw tile color.

One rule handles **both** transitions: `Floor → ShallowWater` eases the blue
creeping in, and the later `ShallowWater → DeepWater` eases the darkening. No hard
pops. Because easing runs every frame regardless of fog, anything that floods while
unseen is already settled by the time LOS reveals it — you never catch a stale
mid-animation.

### Seep stagger (the "creep" feel)

A ring flips all its tiles in the same tick, so a plain crossfade fades that whole
band in uniformly. To make it read as water *seeping* rather than a clean
rectangle, stagger each tile's ease-start by a **deterministic per-position offset**
— a hash of `GridPos` mapped to ≈ `0–0.25 s`. The offset is deterministic (same
tile → same delay) so it is stable frame to frame and needs no per-tile RNG state.
Tiles within a ring then begin easing at slightly different moments, so the band
seeps in unevenly.

No unit tests (Godot adapter); verified by `build 0/0` and play-test.

---

## Section 4 — Integration, scope & process

### Integration surface (deliberately tiny)

- Visibility is already computed **client-side** in `MatchClient.UpdateFog()` from
  the synced grid + local miner position. LOS changes only *how* that set is
  computed → **zero net/snapshot/codec changes**, no new sync, no cross-wire
  determinism risk (every client derives its own LOS from the grid it already
  holds).
- `FogState`, explored-memory, and vision radius (incl. the `LongerVision` buff)
  are unchanged.
- **Listen** stays exactly as-is.

### Scope guard (YAGNI)

No 2D lights/shaders, no occluder polygons, no Core flood changes, no new items or
abilities. Exactly three changes: shadowcasting in `Visibility.cs`, soft overlay in
`FogRenderer.cs`, crossfade + seep in `WorldRenderer.cs` (plus the small
`BlocksSight` predicate in `TileType.cs`).

### Process

Own feature branch (`fog-los-overhaul`). `build 0/0` + Core tests green →
play-test (LOS feel, soft-light readability, flood seep) → merge on user approval.
