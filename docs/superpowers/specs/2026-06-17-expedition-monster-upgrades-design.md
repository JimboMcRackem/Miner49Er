# Expedition Monster Upgrades Design

## Overview

Three interrelated improvements to the Expedition monster system:
1. Code-drawn direction-aware monster sprites (replacing geometric placeholders)
2. Monster–hazard interactions (mold slows terrain-bound monsters; deep water blocks ghosts)
3. Lantern item: a new pickup that kills and repels ghosts via an AOE light radius

---

## Section 1: Monster Sprites (code-drawn, direction-aware)

**Scope:** `game/WorldRenderer.cs` only. No Core changes. Uses Godot CanvasItem draw API exclusively (no textures generated, no PixelLab).

`MonsterSnapshot.Facing` (0=N, 1=E, 2=S, 3=W) drives eye/horn placement.

### Slime
- **Body**: filled circle, radius `ts * 0.34f`, color `SlimeColor` (`5fbf4f`)
- **Outline**: unfilled circle (same center, same radius, width 1.5f), darker green (`3a8f2a`)
- **Eyes**: two white filled circles (`radius ts * 0.07f`) with dark pupils (`radius ts * 0.04f`), offset `ts * 0.12f` from center toward facing, spaced `ts * 0.10f` apart perpendicular to facing

### Ghost
- **Body shape**: `DrawColoredPolygon` — teardrop. Wide oval at top (6 points along upper arc), three small downward waves at bottom. Overall height `ts * 0.72f`, width `ts * 0.60f`. Alpha 0.6f, color `GhostColor` (`dfe8ff`).
- **Eyes**: two dark hollow ovals (DrawArc pairs) shifted `ts * 0.10f` toward facing, `ts * 0.09f` apart perpendicular.
- Facing does NOT rotate the teardrop shape (ghost always trails downward); only eyes shift.

### Goat
- **Body**: filled oval via `DrawColoredPolygon` (ellipse approximation, 12 points), brown `b08050`, `ts * 0.58f` wide × `ts * 0.46f` tall
- **Head**: smaller filled circle `ts * 0.20f` radius, same color, offset `ts * 0.22f` toward facing from body center
- **Horns**: two filled triangles (`DrawColoredPolygon`, 3 points each), dark brown `6a4828`, placed on left/right sides of head, pointing away from body

Helper: a static `FacingOffset(int facing, float scale)` returns `Vector2` (N=up, E=right, S=down, W=left).

---

## Section 2: Monster–Hazard Interactions

### 2a: Mold slows slimes and goats

**Scope:** `src/Miner49er.Core/Sim/Monster.cs`, `src/Miner49er.Core/Sim/Simulation.cs`

`Monster` gets two new fields:
```csharp
public double SlowTimer { get; set; }       // seconds of slow remaining
public double SlowMultiplier { get; set; } = 1.0; // >1 = slower; reset to 1.0 when expired
```

In `Simulation.Tick()`, after each monster step: if the monster is terrain-bound (`Kind != Ghost`) and the destination tile has a mold patch (`_molds.Any(mo => mo.Pos == monster.Pos)`), apply:
```csharp
monster.SlowTimer = Config.MoldSlowSeconds;
monster.SlowMultiplier = Config.MoldSlowFactor;
```

In the monster move timer reset (after a step executes), multiply the base cadence by `SlowMultiplier`:
```csharp
double cadence = mo.Kind switch { ... } * mo.SlowMultiplier;
mo.MoveTimer = cadence;
```

Tick down `SlowTimer` each sim tick; when it reaches 0, reset `SlowMultiplier` to 1.0.

### 2b: Deep water blocks ghosts

**Scope:** `src/Miner49er.Core/Sim/Simulation.cs`

`CanMonsterEnter` currently returns `true` for any in-bounds tile for ghosts. Add:
```csharp
if (mo.Kind == MonsterKind.Ghost)
    return Grid.InBounds(p) && Grid.Get(p) != TileType.DeepWater;
```

---

## Section 3: Lantern Item

### 3a: Core — new ItemKind + SimConfig

- Add `Lantern` to `ItemKind` enum (`src/Miner49er.Core/Sim/ItemKind.cs`)
- Add to `SimConfig`: `public int LanternRadius { get; set; } = 3;` (Chebyshev distance)

### 3b: Core — Simulation logic

**Held lantern**: miner holds `ItemKind.Lantern` in `Miner.HeldItem`; the AOE is centered on the miner's position while alive.

**Placed lantern**: sits in `_items` list with `ItemPlacement.Floor`. Miner walks over it → picked up (standard item pickup). Miner presses `InputBindings.UseItem` (Space / Y) while holding lantern → `sim.TryUseItem` drops it at current tile (same as SlowMold drop pattern).

**AOE helper** (private, called each tick — uses existing `GridPos.ChebyshevTo`):
```csharp
bool InLanternLight(GridPos pos)
{
    foreach (var m in _miners)
        if (m.Alive && m.HeldItem == (int)ItemKind.Lantern)
            if (pos.ChebyshevTo(m.Pos) <= Config.LanternRadius) return true;
    foreach (var it in _items)
        if (it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Floor)
            if (pos.ChebyshevTo(it.Pos) <= Config.LanternRadius) return true;
    return false;
}
```

**Ghost kill**: after all monster moves each tick, for every living ghost: if `InLanternLight(ghost.Pos)` → kill ghost (`MonsterKilled` event).

**Ghost repel** (in `GhostDir`): after computing the toward-target direction `d`, if `InLanternLight(mo.Pos + d.ToOffset())` → return `null` (ghost skips turn rather than entering light). Ghost does not attempt alternate directions — it halts at the boundary.

**No new snapshot fields.** Placed lanterns already serialized as `ItemSnapshot`; held lantern already in `MinerSnapshot.Held`.

### 3c: Godot — rendering

In `WorldRenderer._Draw()`:

**AOE glow**: before drawing monsters, iterate lit tiles. For each position in the grid that is `InLanternLight` (approximated client-side from `_client.Monsters` + `_client.Items`), draw a dim yellow-gold overlay `new Color(1f, 0.9f, 0.3f, 0.18f)`.

Client-side `IsInLanternLight(GridPos pos)` mirrors the Core helper using `_client.Miners` (checking `MinerSnapshot.Held == (int)ItemKind.Lantern`) and `_client.Items` (checking `ItemKind.Lantern` + `ItemPlacement.Floor`). Only draws overlays on fog-visible tiles.

**Lantern item glyph** (when on floor or in toolbox): a warm-yellow filled circle (`ts * 0.22f`) with a small dark ring around it.

**Lantern held indicator**: when local miner holds a lantern, no extra rendering needed — the AOE glow on surrounding tiles makes it obvious.

---

## Section 4: Map seeding — lantern in item pool

`MapGenerator` / item placement: add `ItemKind.Lantern` to the standard item pool with 1–2 lanterns per map (same seeded placement as other items). Exact count: 1 lantern guaranteed; second lantern added if map area > 1500 tiles.

---

## Architecture summary

| Layer | Files changed |
|---|---|
| Core model | `Monster.cs` (+SlowTimer/SlowMultiplier), `ItemKind.cs` (+Lantern), `SimConfig.cs` (+LanternRadius) |
| Core sim | `Simulation.cs` (mold-slow monsters, deep water ghosts, lantern AOE kill/repel, lantern use/drop) |
| Map | `MapGenerator.cs` or item pool config (seed lanterns) |
| Godot render | `WorldRenderer.cs` (all sprite drawing + lantern glow + lantern glyph) |

No new snapshot fields. No codec changes. Tests needed: mold-slows-goat, mold-slows-slime, ghost-blocked-by-deep-water, ghost-killed-by-lantern-AOE, ghost-repelled-at-boundary, lantern-drop-pickup.
