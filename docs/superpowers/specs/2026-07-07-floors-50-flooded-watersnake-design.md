# Floors to 50, Flooded Levels & Water Snake — Design Spec

**Date:** 2026-07-07
**Status:** Approved

---

## 1. Overview

Three related additions to the Expedition mode:

1. **Extend floor range to 50** — three new difficulty bands beyond the current cap of 20, with larger maps and all hazards active.
2. **Flooded levels** — a recurring map variant from floor 10 onward where ~80 % of dry floor is converted to shallow/deep water, leaving only small islands of dry land.
3. **Water snake mob** — a new `MonsterKind` that wriggles at full speed in water but half speed on land; introduced from floor 5; immune to deep-water death.

---

## 2. Floor extension (1–50)

### 2.1 Difficulty bands

| Floors | Map scale | Grid size | Active hazards |
|--------|-----------|-----------|----------------|
| 1–5    | 1         | 32×32     | —              |
| 6–10   | 2         | 48×48     | Pits           |
| 11–15  | 3         | 64×64     | Pits + Cave-ins |
| 16–20  | 4         | 80×80     | Pits + Cave-ins + Lava |
| 21–30  | 5         | 96×96     | All hazards + flooded variant |
| 31–40  | 6         | 112×112   | All hazards + flooded variant |
| 41–50  | 7         | 128×128   | All hazards + flooded variant |

### 2.2 `MapConfig.FloorConfig` changes

`mapScale` switch gains three new arms:

```csharp
int mapScale = floor switch {
    <= 5  => 1, <= 10 => 2, <= 15 => 3, <= 20 => 4,
    <= 30 => 5, <= 40 => 6, _     => 7
};
```

`FloodedCave` is computed deterministically from seed + floor (see §3.2).

### 2.3 Monster difficulty

`MonsterRoster` gains two extra floor-difficulty bonuses — at floors 20 and 28 — so monster counts continue to climb in the deep floors. `FloorMax` bumps from 10 to 12 to accommodate.

```csharp
int bonus = (floor >= 8 ? 1 : 0) + (floor >= 14 ? 1 : 0)
          + (floor >= 20 ? 1 : 0) + (floor >= 28 ? 1 : 0);
return Math.Clamp(CountFor(width, height) + bonus, Min, FloorMax);
```

### 2.4 Lobby UI

`_startFloorPicker` SpinBox: `MaxValue` changes from `20` to `50`.

---

## 3. Flooded levels

### 3.1 Concept

A flooded level has very little dry land: only ~20 % of floor tiles remain walkable as dry ground. The rest becomes shallow water (or deep water where the interior is fully surrounded). This dramatically favours water snakes while making lanterns and mold patches (which need floor tiles) less effective.

### 3.2 Trigger condition

Computed deterministically so host and all clients regenerate identical maps:

```csharp
cfg.FloodedCave = floor >= 10 && ((uint)(seed * 37 + floor * 13) % 5 == 0);
```

This gives roughly 1-in-5 probability starting at floor 10, with no band restriction — any floor from 10 to 50 can be flooded.

### 3.3 New `MapConfig` fields

```csharp
public bool  FloodedCave          { get; set; } = false;
public float FloodedCaveDryRatio  { get; set; } = 0.20f; // fraction of floor tiles to keep dry
```

### 3.4 Generator pipeline

The flood pass runs **after** spawn placement, item placement, shop placement, and the escape-tile assignment. This guarantees those features always land on dry ground.

**`FloodCavePass(TileGrid grid, Random rng, MapConfig cfg, GeneratedMap partial)`:**

1. Build a protected set: spawns + item positions + shop + escape tile.
2. Collect all `Floor` tiles not in the protected set → `candidates`.
3. Shuffle `candidates` with the seeded RNG.
4. Keep the first `ceil(candidates.Count × FloodedCaveDryRatio)` tiles as dry; convert the rest to `ShallowWater`.
5. Re-run the existing deep-water interior detection pass (the same logic already in `PlaceWater`) so fully-surrounded water cells become `DeepWater`.

The pass is a pure data transformation; no new tile types are introduced.

### 3.5 MonsterSpawner spawn tiles

`MonsterSpawner.Place` currently filters for `TileType.Floor` tiles only. On flooded maps this would leave almost no valid snake spawn positions. Change the filter to `IsTraversable` (Floor or ShallowWater), which is already defined in the codebase. This is safe for all monster kinds — land monsters can start on shallow water and walk onto floor; snakes stay in water.

---

## 4. Water snake

### 4.1 Core properties

| Property | Value |
|----------|-------|
| `MonsterKind` | `WaterSnake` |
| Speed in water (ShallowWater, DeepWater) | 0.35 s/tile |
| Speed on land (all other tile types) | 0.70 s/tile |
| Can enter | All `IsEnterable()` tiles (same as non-ghost monsters) |
| Immune to | Deep-water lethality only |
| Killed by | Lava, LavaVent, Pit — same as other land monsters |
| Death cause inflicted on miner | `DeathCause.Bitten` |
| First available floor | 5 |

### 4.2 `SimConfig` additions

```csharp
public double MonsterWaterSnakeWaterMoveSeconds { get; set; } = 0.35;
public double MonsterWaterSnakeLandMoveSeconds  { get; set; } = 0.70;
```

### 4.3 Dynamic cadence

`MonsterCadence(MonsterKind)` is currently a fixed lookup. Water snakes need a per-move tile check. Add an overload:

```csharp
private double MonsterCadenceFor(Monster mo, GridPos dest)
{
    if (mo.Kind != MonsterKind.WaterSnake) return MonsterCadence(mo.Kind);
    var tile = Grid.Get(dest);
    return tile.IsWater() ? Config.MonsterWaterSnakeWaterMoveSeconds
                          : Config.MonsterWaterSnakeLandMoveSeconds;
}
```

`StepMonster` calls `MonsterCadenceFor(mo, nextPos)` when resetting `MoveCooldownRemaining` after a move.

### 4.4 Lethality exception

The existing lethality check in `Simulation` (`StepMonster` and the end-of-tick sweep):

```csharp
// before:
if (mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal()) ...

// after:
bool immune = mo.Kind == MonsterKind.Ghost                                     // ghost: immune to all lethal tiles
           || (mo.Kind == MonsterKind.WaterSnake && Grid.Get(mo.Pos) == TileType.DeepWater); // snake: immune to deep water only
if (!immune && Grid.Get(mo.Pos).IsLethal()) ...
```

Ghosts remain fully immune to all lethal tiles (unchanged). Snakes are immune only to deep water; lava, lava vents, and pits still kill them.

### 4.5 `DeathCause`

Add `Bitten` to the `DeathCause` enum. `MaulMiner` switch:

```csharp
MonsterKind.WaterSnake => DeathCause.Bitten,
```

### 4.6 Monster roster

`MonsterSpawner.KindsForFloor` updated:

| Floor range | Kind pool |
|-------------|-----------|
| < 5         | Slime, Ghost, Goat, ZombieMiner |
| 5–7         | + WaterSnake |
| 8–11        | + SkeletonHuman |
| 12+         | + SkeletonDino |

---

## 5. Art — water snake sprites (PixelLab)

### 5.1 Required assets

Following the ghost/slime/goat pattern exactly:

- **Static directional sprites** (4 files): `assets/monsters/water_snake/{n,e,s,w}.png`
- **Walk animation** (36 files): `assets/monsters/water_snake/walk/{north,east,south,west}_{0-8}.png`

### 5.2 Style brief

Top-down pixel art (~32 px canvas), matching the existing monster palette. Blue-green serpentine body with visible scales or banding; head slightly enlarged to show facing direction. Should read clearly against both floor tiles and shallow-water tiles.

### 5.3 `WorldRenderer` additions

Mirrors the slime/ghost pattern:

```csharp
private Texture2D?[]  _waterSnakeTex     = new Texture2D?[4];
private Texture2D?[,] _waterSnakeWalkTex = new Texture2D?[4, 9]; // [dir, frame]
```

- `LoadMonsterTex(_waterSnakeTex, "water_snake")` in `Init`.
- `BuildWaterSnakeWalkTextures()` loads walk frames (same path pattern as goat/slime).
- New case `MonsterKind.WaterSnake` in the monster draw switch, rendered at `ts * 1.3f` scale, 9-frame walk animation at 150 ms/frame.

---

## 6. Tests

| Test file | New cases |
|-----------|-----------|
| `MapConfigFloorTests.cs` | FloorConfig for floors 21, 30, 40, 50 — correct scale, hazards, flooded probability |
| `MapGeneratorWaterTests.cs` | FloodCavePass leaves ≤ 25 % dry tiles; protected positions stay Floor |
| `SimulationMonsterTests.cs` | WaterSnake cadence is fast on ShallowWater/DeepWater; slow on Floor; immune to DeepWater death; killed by Lava |

---

## 7. Out of scope

- New hazard types for floors 21–50 (reuses existing pits, cave-ins, lava at higher density).
- Flooded levels in non-Expedition game modes.
- Water snake variants (e.g. boss-scale sea serpent).
- AI bot pathfinding updates for flooded maps (bots will still function; water-nav optimisation is a future pass).
