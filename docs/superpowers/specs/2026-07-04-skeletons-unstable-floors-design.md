# Skeletons & Unstable Floors Design Spec

## Overview

Two related features that deepen the world-physics layer of Miner 49er:

1. **Skeleton Creatures** — dormant bone piles that rise when disturbed by noise; two variants with different floor-damage behaviour.
2. **Unstable Floors** — pickaxe a floor tile in LMS/Derby to crack it, feeding into the existing collapse pipeline.

---

## Feature 1: Skeleton Creatures

### Modes

Skeletons appear in **Expedition** (floors 8+) and **ReachCenter** (treated as floor 10).  
LMS, Derby, and TreasureHunt are unaffected.

### Two Kinds

| Kind | Speed | Floor damage | Wakes on |
|---|---|---|---|
| `SkeletonHuman` | 0.7 s/tile | None | Noise within range |
| `SkeletonDino` | 1.2 s/tile | Heavy (see below) | Noise within range |

### Dormancy

Skeletons start **dormant** — placed as bone piles during map generation, not moving.  
`Monster` gains `bool Dormant` (default `true` for skeleton kinds, `false` for all existing kinds).  
`MonsterSnapshot` gains `bool Dormant = false`.

Dormant skeletons are rendered as scattered bones on the floor. Awake skeletons use standing skeleton sprites.

### Noise System Extension

The private `NoiseSource` class gains a `NoiseKind` field:

```csharp
private enum NoiseKind { Stone, Explosion, Pickaxe }
private sealed class NoiseSource { public GridPos Pos; public double LifetimeRemaining; public NoiseKind Kind; }
```

Two new call-sites create noise sources:
- `DetonateAt(...)`: `NoiseKind.Explosion`, lifetime 8.0 s, at `wallPos`
- `CompleteActivity` (mining path): `NoiseKind.Pickaxe`, lifetime 2.0 s, at mined tile

Stone throws already create noise — extended to set `Kind = NoiseKind.Stone`.

### Wake Check

Each tick in `AdvanceMonsters`, dormant skeleton skips movement and instead checks `SkeletonWakeCheck`:

```csharp
private bool SkeletonWakeCheck(GridPos pos)
{
    foreach (var ns in _noiseSources)
    {
        int d = pos.ManhattanTo(ns.Pos);
        if (ns.Kind == NoiseKind.Explosion && d <= Config.SkeletonExplosionWakeRadius) return true;
        if (ns.Kind == NoiseKind.Pickaxe   && d <= Config.SkeletonPickaxeWakeRadius)   return true;
        if (ns.Kind == NoiseKind.Stone      && d <= Config.SkeletonStoneWakeRadius)     return true;
    }
    return false;
}
```

**Default radii (SimConfig):**
- `SkeletonExplosionWakeRadius = 12`
- `SkeletonPickaxeWakeRadius = 3`
- `SkeletonStoneWakeRadius = 8`

On wake: `mo.Dormant = false`, emit `SkeletonAroused(mo.Id)`.

### Awake Movement

Both skeleton kinds use the ZombieMiner movement strategy: always move toward the nearest living miner (no sense radius cap), terrain-bound (cannot enter Rock, ImpermeableRock, DeepWater, Pit).

New `SimConfig` fields:
- `MonsterSkeletonMoveSeconds = 0.7`
- `MonsterSkeletonDinoMoveSeconds = 1.2`

`MonsterCadence` switch extended for both kinds.

### SkeletonDino Floor Damage

Applied in `StepMonster` **after** the move completes, iff `mo.Kind == MonsterKind.SkeletonDino`:

1. **Tile just left (`from`):** if it was `TileType.Floor` → `Grid.Set(from, TileType.Cracked)`, emit `CrackWeakened(from)`.
2. **Tile just entered (`next` = current `mo.Pos`):** if it was `TileType.Cracked` or `TileType.Crumbling`:
   - `Grid.Set(mo.Pos, TileType.Pit)`, emit `CrackCollapsed(mo.Pos)`
   - Mark dino dead: `mo.Alive = false`, emit `MonsterKilled(mo.Id)`
   - Kill any miner on that tile: call `CollapseKill(miner)` for each occupant

`CanMonsterEnter` for SkeletonDino: same as other terrain-bound monsters — `IsEnterable()` — which already includes `Cracked` and `Crumbling` (the collapse is handled post-move, not as an entry block).

### Spawning

`MonsterSpawner.Place` gains an optional `int floor = 0` parameter. The `Kinds` rotation becomes floor-gated:

```
floor < 8:  { Slime, Ghost, Goat, ZombieMiner }
floor 8–11: { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman }
floor 12+:  { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino }
```

Round-robin index cycles over the active set for that floor.

`MonsterRoster.CountFor` already accounts for floor difficulty; no changes needed there.

Call-site in `Main.cs` (or `RoundResolver.cs`) passes the current floor number when calling `MonsterSpawner.Place`.

ReachCenter passes `floor: 10` (gives SkeletonHuman, no Dino).

### New SimEvents

```csharp
public sealed record SkeletonAroused(int MonsterId) : SimEvent;
```

(`CrackWeakened`, `CrackCollapsed`, `MonsterKilled` already exist and are reused for dino damage.)

### SnapshotCodec

`MonsterSnapshot` gains `bool Dormant = false`. `SnapshotFactory.BuildMonsterSnapshot` sets it from `mo.Dormant`. Codec (encode/decode) updated accordingly.

---

## Feature 2: Unstable Floors (LMS / Derby)

### Gate

`SimConfig.UnstableFloorEnabled` (default `false`).  
`Main.cs` sets `UnstableFloorEnabled = true` for `GameMode.LastManStanding` and `GameMode.DemolitionDerby`.

### New ActivityKind

```csharp
public enum ActivityKind { None, Mining, Planting, PlantingDetonator, FloorCracking }
```

Keeps render-side animation distinct from rock-mining.

### TryStartActivity change

In `TryStartActivity` (the pickaxe path), after the existing `IsMinable()` check, add:

```csharp
if (Config.UnstableFloorEnabled && tile == TileType.Floor)
{
    m.Activity = ActivityKind.FloorCracking;
    m.ActivityTarget = target;
    m.ActivitySecondsRemaining = Config.PickaxeSeconds;
    _events.Add(new ActivityStarted(id, ActivityKind.FloorCracking, target));
    return true;
}
```

Target must be in-bounds and adjacent (already guaranteed by the existing guard).

### CompleteActivity change

New branch in `CompleteActivity`:

```csharp
else if (kind == ActivityKind.FloorCracking)
{
    if (Grid.InBounds(target) && Grid.Get(target) == TileType.Floor)
    {
        Grid.Set(target, TileType.Cracked);
        // TileChange(Floor → Cracked) is picked up by the existing tile-diff logic in Main.cs
        _events.Add(new CrackWeakened(target));  // reuse existing event; client renders crack
    }
}
```

No gold award, no item unburying. The cracked tile feeds into the existing `AdvanceCracks` dwell timer (0.75 s to fall if occupied).

### PvP gameplay note

Cracking a tile an enemy is standing on gives them ~0.75 s to move off before they fall. This is intentional — it's a skill shot, not a guaranteed kill.

### No new events

`CrackWeakened` (already exists) carries the tile position. Clients already handle `TileType.Cracked` rendering.

---

## Implementation Order

These features are independent and can be planned/executed separately:

1. **Unstable floors** — smaller, self-contained, builds no dependencies
2. **Skeleton creatures** — larger; depends on noise system extension (which also improves existing monster reactions to explosions/pickaxe)

---

## Files Touched

**Unstable floors:**
- `src/Miner49er.Core/Sim/Miner.cs` — add `FloorCracking` to `ActivityKind`
- `src/Miner49er.Core/Sim/SimConfig.cs` — add `UnstableFloorEnabled`
- `src/Miner49er.Core/Sim/Simulation.cs` — `TryStartActivity` + `CompleteActivity`
- `game/Main.cs` — set `UnstableFloorEnabled` for LMS/Derby

**Skeleton creatures:**
- `src/Miner49er.Core/Sim/Monster.cs` — add `SkeletonHuman`, `SkeletonDino` to `MonsterKind`; add `bool Dormant`
- `src/Miner49er.Core/Sim/SimConfig.cs` — add skeleton cadence + wake radius fields
- `src/Miner49er.Core/Sim/SimEvent.cs` — add `SkeletonAroused`
- `src/Miner49er.Core/Sim/Simulation.cs` — noise kinds, wake logic, dino floor damage, new cadence cases
- `src/Miner49er.Core/Map/MonsterSpawner.cs` — floor-gated kind rotation
- `src/Miner49er.Core/Net/Snapshots.cs` — `MonsterSnapshot` + `Dormant` field
- `src/Miner49er.Core/Net/SnapshotCodec.cs` — encode/decode `Dormant`
- `src/Miner49er.Core/Net/SnapshotFactory.cs` — set `Dormant` in snapshot
- `game/WorldRenderer.cs` — render dormant/awake skeleton states
- `game/Main.cs` — pass floor number to `MonsterSpawner.Place`
- Art: two skeleton sprite sheets (PixelLab) — SkeletonHuman (walk 4-dir) + SkeletonDino (walk 4-dir)
