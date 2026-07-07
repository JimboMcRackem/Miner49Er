# Floors to 50, Flooded Levels & Water Snake — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend Expedition to floor 50, add flooded-cave map variants from floor 10, and add a new WaterSnake mob that moves fast in water and slow on land.

**Architecture:** Three layered additions — floor extension is pure config changes; flooded levels are a post-processing pass in `MapGenerator` that runs after item placement; the water snake is a new `MonsterKind` with dynamic per-tile cadence and a deep-water immunity exception in `Simulation`. PixelLab generates the sprite assets; `WorldRenderer` loads and animates them.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4, PixelLab MCP

## Global Constraints

- Baseline: 576 tests pass. No regressions allowed.
- Test command: `dotnet test src/Miner49er.Core.Tests/`
- All new public API in `Miner49er.Core` namespace.
- Sprite assets path convention: `assets/monsters/{folder}/{n,e,s,w}.png` (static) and `assets/monsters/{folder}/walk/{north,east,south,west}_{0-8}.png` (9-frame walk animation).
- `Direction` enum order: North=0, East=1, South=2, West=3. All `[dir]` arrays use this ordering.
- `MonsterCadence` = seconds per one-tile step; lower = faster.

---

## Task 1: Extend FloorConfig to floor 50 + MonsterRoster depth bonuses + lobby UI

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (FloorConfig method)
- Modify: `src/Miner49er.Core/Map/MonsterRoster.cs` (CountFor + FloorMax)
- Modify: `game/ui/Lobby.cs` (SpinBox MaxValue)
- Modify: `src/Miner49er.Core.Tests/MapConfigFloorTests.cs`

**Interfaces:**
- Produces: `MapConfig.FloorConfig(int floor, int seed, int playerCount=1)` handles floors 1–50
- Produces: `MonsterRoster.FloorMax = 12`, bonuses at floors 8, 14, 20, 28

- [ ] **Step 1: Write failing tests**

Add to `src/Miner49er.Core.Tests/MapConfigFloorTests.cs`:

```csharp
[Theory]
[InlineData(21)] [InlineData(25)] [InlineData(30)]
public void Floors_21_to_30_are_scale5_all_hazards(int floor)
{
    var cfg = MapConfig.FloorConfig(floor, 42);
    Assert.Equal(96, cfg.BaseWidth);
    Assert.Equal(96, cfg.BaseHeight);
    Assert.True(cfg.Pits);
    Assert.True(cfg.CaveIns);
    Assert.True(cfg.Lava);
}

[Theory]
[InlineData(31)] [InlineData(35)] [InlineData(40)]
public void Floors_31_to_40_are_scale6_all_hazards(int floor)
{
    var cfg = MapConfig.FloorConfig(floor, 42);
    Assert.Equal(112, cfg.BaseWidth);
    Assert.True(cfg.Pits);
    Assert.True(cfg.CaveIns);
    Assert.True(cfg.Lava);
}

[Theory]
[InlineData(41)] [InlineData(45)] [InlineData(50)]
public void Floors_41_to_50_are_scale7_all_hazards(int floor)
{
    var cfg = MapConfig.FloorConfig(floor, 42);
    Assert.Equal(128, cfg.BaseWidth);
    Assert.True(cfg.Pits);
    Assert.True(cfg.CaveIns);
    Assert.True(cfg.Lava);
}

[Fact]
public void MonsterRoster_has_extra_bonuses_at_floors_20_and_28()
{
    // CountFor base (32x32 → 3) + bonus at 8 + bonus at 14 + bonus at 20 + bonus at 28 = 7, capped at 12
    int count20 = MonsterRoster.CountFor(32, 32, 20);
    int count28 = MonsterRoster.CountFor(32, 32, 28);
    int count14 = MonsterRoster.CountFor(32, 32, 14);
    Assert.True(count20 > count14);
    Assert.True(count28 > count20);
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~MapConfigFloorTests"
```

Expected: 3–4 failures on the new tests.

- [ ] **Step 3: Update MapConfig.FloorConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, replace the `FloorConfig` method:

```csharp
public static MapConfig FloorConfig(int floor, int seed, int playerCount = 1)
{
    int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, <= 20 => 4, <= 30 => 5, <= 40 => 6, _ => 7 };
    bool pits    = floor >= 6;
    bool caveIns = floor >= 11;
    bool lava    = floor >= 16;
    var cfg = For(GameMode.Expedition, seed, playerCount, pits, caveIns, lava, mapScale);
    cfg.ChestCount = floor <= 10 ? 1 : 2;
    cfg.HasShop = floor % 4 == 0;
    cfg.DetonatorCount = floor >= 3 ? 1 : 0;
    return cfg;
}
```

- [ ] **Step 4: Update MonsterRoster**

In `src/Miner49er.Core/Map/MonsterRoster.cs`, replace the file contents:

```csharp
using System;

namespace Miner49er.Core;

public static class MonsterRoster
{
    public const int Min      = 3;
    public const int Max      = 8;
    public const int FloorMax = 12;

    public static int CountFor(int width, int height)
    {
        int area = width * height;
        int extra = Math.Max(0, (area - 24 * 24) / 384);
        return Math.Clamp(Min + extra, Min, Max);
    }

    public static int CountFor(int width, int height, int floor)
    {
        int bonus = (floor >= 8  ? 1 : 0)
                  + (floor >= 14 ? 1 : 0)
                  + (floor >= 20 ? 1 : 0)
                  + (floor >= 28 ? 1 : 0);
        return Math.Clamp(CountFor(width, height) + bonus, Min, FloorMax);
    }
}
```

- [ ] **Step 5: Update Lobby SpinBox**

In `game/ui/Lobby.cs`, find the line:

```csharp
_startFloorPicker = new SpinBox { MinValue = 1, MaxValue = 20, Step = 1, Value = savedStartFloor };
```

Change to:

```csharp
_startFloorPicker = new SpinBox { MinValue = 1, MaxValue = 50, Step = 1, Value = savedStartFloor };
```

- [ ] **Step 6: Run tests**

```
dotnet test src/Miner49er.Core.Tests/
```

Expected: all 576+ tests pass (new tests now green).

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MonsterRoster.cs game/ui/Lobby.cs src/Miner49er.Core.Tests/MapConfigFloorTests.cs
git commit -m "feat(expedition): extend floor range to 50 with larger maps and deeper monster bonuses"
```

---

## Task 2: Flooded cave map generation

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (new fields + FloodedCave trigger in FloorConfig)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (FloodCavePass + call in Generate)
- Modify: `src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs`

**Interfaces:**
- Consumes: `MapConfig.FloodedCave`, `MapConfig.FloodedCaveDryRatio`
- Produces: `MapGenerator.Generate` runs `FloodCavePass` when `config.FloodedCave == true`, leaving ≤25% of non-protected floor tiles dry

- [ ] **Step 1: Write failing tests**

Add to `src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs`:

```csharp
[Fact]
public void FloodedCave_leaves_at_most_25_percent_dry_floor()
{
    var cfg = new MapConfig { Seed = 42, PlayerCount = 1, FloodedCave = true, FloodedCaveDryRatio = 0.20f };
    var map = MapGenerator.Generate(cfg);
    var grid = map.Grid;

    int totalTraversable = grid.Positions().Count(p =>
        grid.Get(p) is TileType.Floor or TileType.ShallowWater or TileType.DeepWater);
    int dryFloor = grid.Positions().Count(p => grid.Get(p) == TileType.Floor);

    Assert.True(totalTraversable == 0 || (double)dryFloor / totalTraversable <= 0.25,
        $"Expected ≤25% dry floor, got {dryFloor}/{totalTraversable}");
}

[Fact]
public void FloodedCave_keeps_spawns_on_dry_floor()
{
    var cfg = new MapConfig { Seed = 99, PlayerCount = 2, FloodedCave = true, FloodedCaveDryRatio = 0.20f };
    var map = MapGenerator.Generate(cfg);
    foreach (var s in map.Spawns)
        Assert.Equal(TileType.Floor, map.Grid.Get(s));
}

[Fact]
public void FloodedCave_keeps_item_positions_on_dry_floor()
{
    var cfg = new MapConfig { Seed = 7, PlayerCount = 1, FloodedCave = true, FloodedCaveDryRatio = 0.20f,
                              BaseItemCount = 4, VisibleItemCount = 2 };
    var map = MapGenerator.Generate(cfg);
    foreach (var it in map.Items)
        Assert.Equal(TileType.Floor, map.Grid.Get(it.Pos));
}

[Theory]
[InlineData(10, 42)] [InlineData(15, 7)] [InlineData(30, 99)]
public void FloorConfig_floor_10_plus_can_produce_flooded_cave(int floor, int seed)
{
    // Run 20 seeds per floor; at least one should be flooded (1/5 probability)
    bool anyFlooded = Enumerable.Range(seed, 20)
        .Any(s => MapConfig.FloorConfig(floor, s).FloodedCave);
    Assert.True(anyFlooded, $"No flooded floor found in 20 seeds for floor {floor}");
}

[Theory]
[InlineData(1)] [InlineData(5)] [InlineData(9)]
public void FloorConfig_floors_below_10_are_never_flooded(int floor)
{
    for (int s = 0; s < 50; s++)
        Assert.False(MapConfig.FloorConfig(floor, s).FloodedCave);
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~MapGeneratorWaterTests"
```

Expected: 5 new failures (FloodedCave field doesn't exist yet).

- [ ] **Step 3: Add MapConfig fields and FloodedCave trigger**

In `src/Miner49er.Core/Map/MapConfig.cs`, add these two properties after the existing `Flooding` property (around line 27):

```csharp
// Flooded cave — most floor tiles converted to water after normal map gen.
public bool  FloodedCave         { get; set; } = false;
public float FloodedCaveDryRatio { get; set; } = 0.20f;
```

Also update `FloorConfig` (already modified in Task 1) — add the `FloodedCave` line at the end before `return cfg;`:

```csharp
cfg.FloodedCave = floor >= 10 && (uint)HashCode.Combine(seed, floor) % 5 == 0;
return cfg;
```

- [ ] **Step 4: Add FloodCavePass to MapGenerator**

In `src/Miner49er.Core/Map/MapGenerator.cs`, add the following private static method (place it near `PlaceWater`):

```csharp
/// <summary>Converts ~80% of non-protected floor tiles to shallow water, then re-promotes
/// fully-surrounded shallow tiles to deep water. Runs after all item/spawn placement so
/// protected positions are guaranteed to stay dry.</summary>
private static void FloodCavePass(TileGrid g, Random rng, MapConfig cfg,
    IReadOnlyList<GridPos> spawns, IReadOnlyList<Item> items, GridPos? shopPos)
{
    var protect = new HashSet<GridPos>(spawns);
    foreach (var it in items) protect.Add(it.Pos);
    if (shopPos is { } sp) protect.Add(sp);

    var candidates = g.Positions()
        .Where(p => g.Get(p) == TileType.Floor && !protect.Contains(p))
        .ToList();
    Shuffle(candidates, rng);

    int keepCount = (int)Math.Ceiling(candidates.Count * cfg.FloodedCaveDryRatio);
    for (int i = keepCount; i < candidates.Count; i++)
        g.Set(candidates[i], TileType.ShallowWater);

    // Re-run deep-water promotion on the newly flooded tiles.
    PromoteDeep(g, rng, cfg);
}
```

- [ ] **Step 5: Call FloodCavePass in Generate**

In `src/Miner49er.Core/Map/MapGenerator.cs`, the `Generate` method currently ends with:

```csharp
        GridPos? shopPos = config.HasShop
            ? PlaceShopkeeper(grid, spawns, spawns.Count > 0 ? spawns[0] : (GridPos?)null, rng)
            : null;
        return new GeneratedMap
```

Add the FloodCavePass call between those two lines:

```csharp
        GridPos? shopPos = config.HasShop
            ? PlaceShopkeeper(grid, spawns, spawns.Count > 0 ? spawns[0] : (GridPos?)null, rng)
            : null;
        if (config.FloodedCave)
            FloodCavePass(grid, rng, config, spawns, items, shopPos);
        return new GeneratedMap
```

- [ ] **Step 6: Run tests**

```
dotnet test src/Miner49er.Core.Tests/
```

Expected: all 576+ tests pass.

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs
git commit -m "feat(map): add FloodedCave generation pass and wire into FloorConfig from floor 10"
```

---

## Task 3: IsTraversable extension + MonsterSpawner spawn tile fix

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs` (add `IsTraversable` extension)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (use extension instead of private static method)
- Modify: `src/Miner49er.Core/Map/MonsterSpawner.cs` (filter change)

**Interfaces:**
- Produces: `TileTypeExtensions.IsTraversable(this TileType t)` — true for Floor or ShallowWater
- Produces: `MonsterSpawner.Place` considers ShallowWater as valid spawn tile

- [ ] **Step 1: Add IsTraversable to TileTypeExtensions**

In `src/Miner49er.Core/Grid/TileType.cs`, add after `IsWater`:

```csharp
/// <summary>Tile counts as traversable open space for region detection and monster spawning.</summary>
public static bool IsTraversable(this TileType t) => t is TileType.Floor or TileType.ShallowWater;
```

- [ ] **Step 2: Update MapGenerator to use the extension**

In `src/Miner49er.Core/Map/MapGenerator.cs`, find:

```csharp
    private static bool IsTraversable(TileType t) => t is TileType.Floor or TileType.ShallowWater;
```

Delete that line. The extension method is now used wherever `IsTraversable(...)` was called — verify the call sites still compile (they call `IsTraversable(g.Get(p))` which now resolves to the extension method via `g.Get(p).IsTraversable()`; you may need to change the call sites).

Find all occurrences of `IsTraversable(g.Get(p))` in `MapGenerator.cs` and change them to `g.Get(p).IsTraversable()`.

- [ ] **Step 3: Update MonsterSpawner spawn tile filter**

In `src/Miner49er.Core/Map/MonsterSpawner.cs`, find:

```csharp
        var floors = grid.Positions()
            .Where(p => grid.Get(p) == TileType.Floor && p != start)
```

Change to:

```csharp
        var floors = grid.Positions()
            .Where(p => grid.Get(p).IsTraversable() && p != start)
```

- [ ] **Step 4: Run tests**

```
dotnet test src/Miner49er.Core.Tests/
```

Expected: all tests pass (no regressions; monster spawning on ShallowWater is harmless for land monsters).

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core/Map/MonsterSpawner.cs
git commit -m "refactor(tiles): promote IsTraversable to TileTypeExtensions; MonsterSpawner spawns on water tiles"
```

---

## Task 4: Water snake core data (enum, SimConfig, DeathCause)

**Files:**
- Modify: `src/Miner49er.Core/Sim/Monster.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/DeathCause.cs`

**Interfaces:**
- Produces: `MonsterKind.WaterSnake`
- Produces: `SimConfig.MonsterWaterSnakeWaterMoveSeconds = 0.35`, `SimConfig.MonsterWaterSnakeLandMoveSeconds = 0.70`
- Produces: `DeathCause.Bitten`

This task has no new tests — enum additions are covered by behaviour tests in Task 5. Just confirm it compiles.

- [ ] **Step 1: Add WaterSnake to MonsterKind**

In `src/Miner49er.Core/Sim/Monster.cs`, change:

```csharp
public enum MonsterKind { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino }
```

to:

```csharp
public enum MonsterKind { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino, WaterSnake }
```

- [ ] **Step 2: Add cadence config values**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after `MonsterSkeletonDinoMoveSeconds`:

```csharp
public double MonsterWaterSnakeWaterMoveSeconds { get; set; } = 0.35;  // full speed in water
public double MonsterWaterSnakeLandMoveSeconds  { get; set; } = 0.70;  // half speed on land
```

- [ ] **Step 3: Add Bitten death cause**

In `src/Miner49er.Core/Sim/DeathCause.cs`, change:

```csharp
public enum DeathCause { None, Drowned, Exploded, Left, Fell, Crushed, Burned, Slimed, Terrified, Headbutted, Mauled, Boned }
```

to:

```csharp
public enum DeathCause { None, Drowned, Exploded, Left, Fell, Crushed, Burned, Slimed, Terrified, Headbutted, Mauled, Boned, Bitten }
```

- [ ] **Step 4: Verify build**

```
dotnet build src/Miner49er.Core/
```

Expected: build succeeds. (The `Simulation.cs` switch on `MonsterKind` will now produce a warning about unhandled enum value — that's expected, fixed in Task 5.)

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Sim/Monster.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/DeathCause.cs
git commit -m "feat(monsters): add WaterSnake MonsterKind, cadence config, and Bitten death cause"
```

---

## Task 5: Water snake simulation behaviour

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

**Interfaces:**
- Consumes: `MonsterKind.WaterSnake`, `SimConfig.MonsterWaterSnakeWaterMoveSeconds`, `SimConfig.MonsterWaterSnakeLandMoveSeconds`, `DeathCause.Bitten`, `TileTypeExtensions.IsWater()`
- Produces: WaterSnake moves at water cadence on ShallowWater/DeepWater, land cadence on Floor; immune to DeepWater lethality; killed by Lava/Pit; mauls miners with `DeathCause.Bitten`

- [ ] **Step 1: Write failing tests**

Add to `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`:

```csharp
[Fact]
public void WaterSnake_moves_fast_in_shallow_water()
{
    var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.3, MonsterSenseRadius = 10 };
    var grid = new TileGrid(9, 3, TileType.ShallowWater);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(8, 1));
    var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

    sim.Tick(0.3);

    Assert.Equal(new GridPos(3, 1), snake.Pos);  // moved one step in 0.3s
}

[Fact]
public void WaterSnake_moves_slow_on_floor()
{
    var cfg = new SimConfig
    {
        MonsterWaterSnakeLandMoveSeconds  = 0.7,
        MonsterWaterSnakeWaterMoveSeconds = 0.35,
        MonsterSenseRadius = 10
    };
    var grid = new TileGrid(9, 3, TileType.Floor);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(8, 1));
    var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

    sim.Tick(0.35);  // less than land cadence — should NOT have moved yet

    Assert.Equal(new GridPos(2, 1), snake.Pos);

    sim.Tick(0.35);  // total 0.70s — now it should have moved

    Assert.Equal(new GridPos(3, 1), snake.Pos);
}

[Fact]
public void WaterSnake_survives_deep_water()
{
    var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.1, MonsterSenseRadius = 0 };
    var grid = new TileGrid(5, 3, TileType.DeepWater);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(0, 0));
    var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

    sim.Tick(0.1);
    sim.Tick(0.1);

    Assert.True(snake.Alive);  // deep water must not kill the snake
}

[Fact]
public void WaterSnake_dies_on_lava()
{
    var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.1, MonsterSenseRadius = 10 };
    var grid = new TileGrid(5, 3, TileType.Floor);
    grid.Set(new GridPos(3, 1), TileType.Lava);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(4, 1));
    var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

    sim.Tick(0.1);  // snake steps onto lava

    Assert.False(snake.Alive);
}

[Fact]
public void WaterSnake_kills_miner_with_Bitten_cause()
{
    var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.1, MonsterSenseRadius = 10 };
    var grid = new TileGrid(5, 3, TileType.ShallowWater);
    var sim = new Simulation(grid, cfg);
    var miner = sim.AddMiner(1, new GridPos(3, 1));
    sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

    sim.Tick(0.1);

    Assert.False(miner.Alive);
    Assert.Equal(DeathCause.Bitten, miner.DeathCause);
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~SimulationMonsterTests"
```

Expected: 5 new failures.

- [ ] **Step 3: Add MonsterCadenceFor and update MonsterCadence**

In `src/Miner49er.Core/Sim/Simulation.cs`, find the `MonsterCadence` method:

```csharp
    private double MonsterCadence(MonsterKind kind) => kind switch
    {
        MonsterKind.Slime          => Config.MonsterSlimeMoveSeconds,
        MonsterKind.Ghost          => Config.MonsterGhostMoveSeconds,
        MonsterKind.Goat           => Config.MonsterGoatMoveSeconds,
        MonsterKind.ZombieMiner    => Config.MonsterZombieMoveSeconds,
        MonsterKind.SkeletonHuman  => Config.MonsterSkeletonMoveSeconds,
        MonsterKind.SkeletonDino   => Config.MonsterSkeletonDinoMoveSeconds,
        _ => Config.MonsterSlimeMoveSeconds,
    };
```

Add the fallback for WaterSnake (land cadence as the static default), and add the new `MonsterCadenceFor` method directly below:

```csharp
    private double MonsterCadence(MonsterKind kind) => kind switch
    {
        MonsterKind.Slime          => Config.MonsterSlimeMoveSeconds,
        MonsterKind.Ghost          => Config.MonsterGhostMoveSeconds,
        MonsterKind.Goat           => Config.MonsterGoatMoveSeconds,
        MonsterKind.ZombieMiner    => Config.MonsterZombieMoveSeconds,
        MonsterKind.SkeletonHuman  => Config.MonsterSkeletonMoveSeconds,
        MonsterKind.SkeletonDino   => Config.MonsterSkeletonDinoMoveSeconds,
        MonsterKind.WaterSnake     => Config.MonsterWaterSnakeLandMoveSeconds,
        _ => Config.MonsterSlimeMoveSeconds,
    };

    private double MonsterCadenceFor(Monster mo)
    {
        if (mo.Kind != MonsterKind.WaterSnake) return MonsterCadence(mo.Kind);
        return Grid.Get(mo.Pos).IsWater()
            ? Config.MonsterWaterSnakeWaterMoveSeconds
            : Config.MonsterWaterSnakeLandMoveSeconds;
    }
```

- [ ] **Step 4: Update AddMonster to use MonsterCadenceFor**

In `Simulation.cs`, find `AddMonster`:

```csharp
    public Monster AddMonster(int id, GridPos pos, MonsterKind kind)
    {
        var mo = new Monster(id, pos, kind) { MoveCooldownRemaining = MonsterCadence(kind) };
```

Change to:

```csharp
    public Monster AddMonster(int id, GridPos pos, MonsterKind kind)
    {
        var mo = new Monster(id, pos, kind);
        mo.MoveCooldownRemaining = MonsterCadenceFor(mo);
```

- [ ] **Step 5: Update the tick cooldown reset to use MonsterCadenceFor**

In `Simulation.cs`, find (around line 510):

```csharp
            mo.MoveCooldownRemaining += MonsterCadence(mo.Kind) * mo.SlowMultiplier;
```

Change to:

```csharp
            mo.MoveCooldownRemaining += MonsterCadenceFor(mo) * mo.SlowMultiplier;
```

- [ ] **Step 6: Add WaterSnake to StepMonster direction switch**

In `Simulation.cs`, find `StepMonster`'s direction switch:

```csharp
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime                                     => SlimeDir(mo, target),
            MonsterKind.Ghost                                     => GhostDir(mo, target),
            MonsterKind.Goat                                      => GoatDir(mo, target),
            MonsterKind.ZombieMiner                               => ZombieDir(mo, target),
            MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino => ZombieDir(mo, target),
            _ => null,
        };
```

Add the WaterSnake case:

```csharp
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime                                     => SlimeDir(mo, target),
            MonsterKind.Ghost                                     => GhostDir(mo, target),
            MonsterKind.Goat                                      => GoatDir(mo, target),
            MonsterKind.ZombieMiner                               => ZombieDir(mo, target),
            MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino => ZombieDir(mo, target),
            MonsterKind.WaterSnake                                => ZombieDir(mo, target),
            _ => null,
        };
```

- [ ] **Step 7: Fix lethality in StepMonster**

In `Simulation.cs`, in the `StepMonster` method, find:

```csharp
        if (mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal())
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
            return;
        }
```

Change to:

```csharp
        bool immuneToTile = mo.Kind == MonsterKind.Ghost
                         || (mo.Kind == MonsterKind.WaterSnake && Grid.Get(mo.Pos) == TileType.DeepWater);
        if (!immuneToTile && Grid.Get(mo.Pos).IsLethal())
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
            return;
        }
```

- [ ] **Step 8: Fix lethality in KillOccupantsOnLethalTiles**

In `Simulation.cs`, find `KillOccupantsOnLethalTiles`:

```csharp
        foreach (var mo in _monsters)
        {
            if (mo.Alive && mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal())
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
```

Change to:

```csharp
        foreach (var mo in _monsters)
        {
            if (!mo.Alive) continue;
            bool immuneToTile = mo.Kind == MonsterKind.Ghost
                             || (mo.Kind == MonsterKind.WaterSnake && Grid.Get(mo.Pos) == TileType.DeepWater);
            if (!immuneToTile && Grid.Get(mo.Pos).IsLethal())
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
```

- [ ] **Step 9: Add Bitten to MaulMiner**

In `Simulation.cs`, find `MaulMiner`'s kind switch:

```csharp
        m.DeathCause = kind switch
        {
            MonsterKind.Slime          => DeathCause.Slimed,
            MonsterKind.Ghost          => DeathCause.Terrified,
            MonsterKind.Goat           => DeathCause.Headbutted,
            MonsterKind.ZombieMiner    => DeathCause.Mauled,
            MonsterKind.SkeletonHuman  => DeathCause.Boned,
            MonsterKind.SkeletonDino   => DeathCause.Boned,
            _ => DeathCause.Mauled,
        };
```

Add the WaterSnake case:

```csharp
        m.DeathCause = kind switch
        {
            MonsterKind.Slime          => DeathCause.Slimed,
            MonsterKind.Ghost          => DeathCause.Terrified,
            MonsterKind.Goat           => DeathCause.Headbutted,
            MonsterKind.ZombieMiner    => DeathCause.Mauled,
            MonsterKind.SkeletonHuman  => DeathCause.Boned,
            MonsterKind.SkeletonDino   => DeathCause.Boned,
            MonsterKind.WaterSnake     => DeathCause.Bitten,
            _ => DeathCause.Mauled,
        };
```

- [ ] **Step 10: Run tests**

```
dotnet test src/Miner49er.Core.Tests/
```

Expected: all tests pass.

- [ ] **Step 11: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(monsters): implement WaterSnake simulation — dynamic cadence, deep-water immunity, Bitten kill"
```

---

## Task 6: MonsterSpawner KindsForFloor — add WaterSnake from floor 5

**Files:**
- Modify: `src/Miner49er.Core/Map/MonsterSpawner.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs` (or create `MonsterSpawnerTests.cs`)

**Interfaces:**
- Produces: `KindsForFloor(floor)` returns pool including `WaterSnake` for floor ≥ 5

- [ ] **Step 1: Write failing test**

Add to `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`:

```csharp
[Theory]
[InlineData(5)] [InlineData(7)] [InlineData(12)] [InlineData(30)]
public void KindsForFloor_includes_WaterSnake_from_floor_5(int floor)
{
    var grid = new TileGrid(20, 20, TileType.Floor);
    var start = new GridPos(1, 1);
    var spawns = MonsterSpawner.Place(grid, start, 7, floor);
    Assert.Contains(spawns, s => s.Kind == MonsterKind.WaterSnake);
}

[Theory]
[InlineData(1)] [InlineData(3)] [InlineData(4)]
public void KindsForFloor_excludes_WaterSnake_below_floor_5(int floor)
{
    var grid = new TileGrid(20, 20, TileType.Floor);
    var start = new GridPos(1, 1);
    var spawns = MonsterSpawner.Place(grid, start, 7, floor);
    Assert.DoesNotContain(spawns, s => s.Kind == MonsterKind.WaterSnake);
}
```

- [ ] **Step 2: Run to verify they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~KindsForFloor"
```

Expected: 2 failures.

- [ ] **Step 3: Update KindsForFloor**

In `src/Miner49er.Core/Map/MonsterSpawner.cs`, replace `KindsForFloor`:

```csharp
    private static MonsterKind[] KindsForFloor(int floor)
    {
        if (floor >= 12)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner,
                           MonsterKind.WaterSnake, MonsterKind.SkeletonHuman, MonsterKind.SkeletonDino };
        if (floor >= 8)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner,
                           MonsterKind.WaterSnake, MonsterKind.SkeletonHuman };
        if (floor >= 5)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner,
                           MonsterKind.WaterSnake };
        return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner };
    }
```

- [ ] **Step 4: Run tests**

```
dotnet test src/Miner49er.Core.Tests/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Map/MonsterSpawner.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(monsters): add WaterSnake to spawn pool from floor 5"
```

---

## Task 7: Generate water snake sprites via PixelLab

**Files:**
- Create: `assets/monsters/water_snake/n.png`, `e.png`, `s.png`, `w.png`
- Create: `assets/monsters/water_snake/walk/north_0.png` … `west_8.png` (36 files)

**Interfaces:**
- Produces: 4 static directional sprites + 9-frame walk animation × 4 directions

The WorldRenderer falls back to a coloured circle if sprites are missing, so the game is playable before this task — but it must be completed before Task 8 can render correctly.

- [ ] **Step 1: Check PixelLab balance**

Use the `mcp__pixellab__get_balance` tool to confirm sufficient credits before generating.

- [ ] **Step 2: Generate the snake character (4 directional statics)**

Use `mcp__pixellab__create_character` to create a top-down pixel-art water snake with these properties:
- Style: top-down view, pixel art, ~32×32 canvas
- Appearance: blue-green serpentine body with visible scale banding; head slightly enlarged and distinct to show facing direction; fits the dark atmospheric palette of the other monsters
- Generate one static frame per direction: north, east, south, west

Save the four output images to:
- `assets/monsters/water_snake/n.png`
- `assets/monsters/water_snake/e.png`
- `assets/monsters/water_snake/s.png`
- `assets/monsters/water_snake/w.png`

- [ ] **Step 3: Generate walk animations (9 frames × 4 directions)**

Use `mcp__pixellab__animate_character` on the base character to produce a 9-frame wriggling/slithering walk cycle for each direction. The animation should emphasise a sinuous body wave, matching the 150 ms/frame playback speed used for slime and ghost.

Save outputs to `assets/monsters/water_snake/walk/`:
- `north_0.png` … `north_8.png`
- `east_0.png` … `east_8.png`
- `south_0.png` … `south_8.png`
- `west_0.png` … `west_8.png`

- [ ] **Step 4: Verify files exist**

```
ls assets/monsters/water_snake/
ls assets/monsters/water_snake/walk/
```

Expected: 4 static PNGs + 36 walk PNGs.

- [ ] **Step 5: Commit**

```
git add assets/monsters/water_snake/
git commit -m "assets: add water snake pixel-art sprites and walk animations (PixelLab)"
```

---

## Task 8: WorldRenderer — load and render water snake

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: `assets/monsters/water_snake/{n,e,s,w}.png` and `walk/{dir}_{0-8}.png` from Task 7
- Consumes: `MonsterKind.WaterSnake` from Task 4
- Produces: water snake drawn at `ts * 1.3f` scale with 9-frame walk animation at 150 ms/frame; falls back to teal circle if sprites are missing

- [ ] **Step 1: Add texture fields**

In `game/WorldRenderer.cs`, find the skeleton texture declarations:

```csharp
	private Texture2D?[]  _skeletonHumanTex     = new Texture2D?[4];
	private Texture2D?[]  _skeletonDinoTex      = new Texture2D?[4];
	private Texture2D?[,] _skeletonHumanWalkTex = new Texture2D?[4, 4];
	private Texture2D?[,] _skeletonDinoWalkTex  = new Texture2D?[4, 4];
```

Add after them:

```csharp
	private Texture2D?[]  _waterSnakeTex     = new Texture2D?[4];
	private Texture2D?[,] _waterSnakeWalkTex = new Texture2D?[4, 9]; // [dir, frame 0-8]
```

- [ ] **Step 2: Load textures in Init**

In `Init`, find the block that loads skeleton textures:

```csharp
		BuildSkeletonWalkTextures(_skeletonDinoWalkTex,  "skeleton_dino");
```

Add after it:

```csharp
		LoadMonsterTex(_waterSnakeTex, "water_snake");
		BuildWaterSnakeWalkTextures();
```

- [ ] **Step 3: Add BuildWaterSnakeWalkTextures**

In `WorldRenderer.cs`, add the method alongside `BuildSlimeWalkTextures`:

```csharp
	private void BuildWaterSnakeWalkTextures()
	{
		string[] dirName = { "north", "east", "south", "west" };
		for (int d = 0; d < 4; d++)
			for (int f = 0; f <= 8; f++)
			{
				string path = $"res://assets/monsters/water_snake/walk/{dirName[d]}_{f}.png";
				if (ResourceLoader.Exists(path))
					_waterSnakeWalkTex[d, f] = GD.Load<Texture2D>(path);
			}
	}
```

- [ ] **Step 4: Add render case**

In `WorldRenderer.cs`, inside the monster-kind switch (after the `SkeletonDino` case, before the closing `}`):

```csharp
			case MonsterKind.WaterSnake:
			{
				int snakeFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
				var tex = _waterSnakeWalkTex[mo.Facing, snakeFrame] ?? _waterSnakeTex[mo.Facing];
				if (tex != null)
				{
					float ss = ts * 1.3f;
					DrawTextureRect(tex, new Rect2(c.X - ss / 2f, c.Y - ss / 2f, ss, ss), false);
				}
				else
				{
					DrawCircle(c, ts * 0.30f, new Color(0.25f, 0.70f, 0.55f));  // teal fallback
				}
				break;
			}
```

- [ ] **Step 5: Build Godot project**

```
dotnet build
```

Expected: 0 errors.

- [ ] **Step 6: Run the game and verify**

Start an Expedition at floor 5 or higher. Confirm:
- Water snake appears in the dungeon
- It animates while moving
- It is visually distinct from other monsters
- It moves noticeably faster when crossing shallow-water tiles vs floor tiles
- The miner death message says "Bitten" (visible in the death feed)

- [ ] **Step 7: Commit**

```
git add game/WorldRenderer.cs
git commit -m "feat(renderer): add water snake sprite loading and animated rendering"
```

---

## Self-Review Notes

**Spec coverage check:**
- ✅ Floor 50: Task 1 (FloorConfig bands 21-30/31-40/41-50, MonsterRoster bonuses, Lobby MaxValue)
- ✅ Flooded levels: Task 2 (FloodCavePass, FloodedCave trigger in FloorConfig) + Task 3 (IsTraversable spawn fix)
- ✅ Water snake behaviour: Tasks 4+5 (enum, config, cadence, lethality, death cause)
- ✅ Water snake roster: Task 6 (KindsForFloor from floor 5)
- ✅ Water snake art: Task 7 (PixelLab generation)
- ✅ Water snake rendering: Task 8 (WorldRenderer)

**Type consistency:**
- `MonsterCadenceFor(Monster mo)` defined in Task 5 Step 3; called in Steps 4 and 5 of same task ✅
- `IsTraversable()` extension defined in Task 3 Step 1; used in MonsterSpawner Step 3 ✅
- `FloodedCave` field defined in Task 2 Step 3; called in `Generate` Step 5 ✅
- `MonsterKind.WaterSnake` defined in Task 4; used in Tasks 5 and 6 ✅
- `DeathCause.Bitten` defined in Task 4; used in Task 5 Step 9 ✅
