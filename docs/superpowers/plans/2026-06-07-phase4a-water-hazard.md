# Phase 4a — Water Hazard (static substrate) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add water as static map terrain — shallow water (walkable but slow) and deep water (lethal drown-on-entry) — generated as pools and rivers of independent depth, with deep water always ringed by shallow shore.

**Architecture:** Pure-C# `Miner49er.Core` owns the tile model, drown rule, and seeded map generation (all unit-tested). The Godot adapter reads Core: `MatchHost` applies a per-tile move cooldown, `WorldRenderer` draws the new tiles, and `MatchAudio` plays a splash on drowning. No netcode changes — water is seeded terrain (identical on every client) and drowning is an `Alive` flip already carried by the per-tick snapshot.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, .NET 8, xUnit. Build/test/run via PowerShell shims on Windows; verification is headless.

**Reference spec:** `docs/superpowers/specs/2026-06-07-phase4a-water-hazard-design.md`

**Branch:** `phase4a-water-hazard` (already created).

---

## File structure

**Core (`src/Miner49er.Core`):**
- `Grid/TileType.cs` — add `ShallowWater`, `DeepWater`; add `IsEnterable`, `IsLethal`, `MoveCostMultiplier`; extend `IsWalkable`.
- `Sim/SimEvent.cs` — add `MinerDrowned`.
- `Sim/Simulation.cs` — `TryMove` enters on `IsEnterable`, drowns on `IsLethal`.
- `Map/MapConfig.cs` — water generation knobs.
- `Map/MapGenerator.cs` — `PlaceWater` + depth promotion + traversable-region-aware spawns/gold.

**Core tests (`src/Miner49er.Core.Tests`):**
- `TileTypeWaterTests.cs` (new), additions to `SimulationMovementTests.cs`, `MapGeneratorWaterTests.cs` (new).

**Godot (`game/`):**
- `net/MatchHost.cs` — per-tile move cooldown.
- `WorldRenderer.cs` — water tile colors.
- `audio/SfxLibrary.cs` — `Splash` placeholder.
- `net/MatchAudio.cs` — splash-on-drown (tile-aware death sound).
- `assets/audio/README.md` — manifest row for `splash`.

**Task dependency:** Task 1 is the foundation. Tasks 2 and 3 depend only on Task 1 and are independent of each other (different files) — they may run in parallel. Tasks 4, 5, 6 depend on Task 1's Core types and should run after the Core tasks are merged.

---

## Task 1: Tile types & semantics (Core, TDD)

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Test: `src/Miner49er.Core.Tests/TileTypeWaterTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/TileTypeWaterTests.cs`:
```csharp
using Miner49er.Core;
using Xunit;

public class TileTypeWaterTests
{
    [Theory]
    [InlineData(TileType.Floor, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.GoldRock, false)]
    [InlineData(TileType.ImpermeableRock, false)]
    public void IsEnterable_allows_floor_and_water_only(TileType t, bool expected)
        => Assert.Equal(expected, t.IsEnterable());

    [Theory]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.ShallowWater, false)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    public void IsLethal_is_deep_water_only(TileType t, bool expected)
        => Assert.Equal(expected, t.IsLethal());

    [Theory]
    [InlineData(TileType.Floor, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, false)]
    [InlineData(TileType.Rock, false)]
    public void IsWalkable_is_floor_and_shallow(TileType t, bool expected)
        => Assert.Equal(expected, t.IsWalkable());

    [Fact]
    public void ShallowWater_costs_double_to_move_through()
    {
        Assert.Equal(1.0, TileType.Floor.MoveCostMultiplier());
        Assert.Equal(2.0, TileType.ShallowWater.MoveCostMultiplier());
        Assert.Equal(2.0, TileTypeExtensions.ShallowSlowFactor);
    }

    [Theory]
    [InlineData(TileType.ShallowWater, false)]
    [InlineData(TileType.DeepWater, false)]
    public void Water_is_inert_to_tools(TileType t, bool expected)
    {
        Assert.Equal(expected, t.IsMinable());
        Assert.Equal(expected, t.IsBlastable());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: FAIL to compile — `ShallowWater`/`DeepWater` and `IsEnterable`/`IsLethal`/`MoveCostMultiplier`/`ShallowSlowFactor` do not exist.

- [ ] **Step 3: Implement the tile types and helpers**

Replace the entire contents of `src/Miner49er.Core/Grid/TileType.cs` with:
```csharp
namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater }

public static class TileTypeExtensions
{
    /// <summary>Multiplier applied to a miner's move cadence while on shallow water.</summary>
    public const double ShallowSlowFactor = 2.0;

    /// <summary>Safe to stand on (used for spawns, fog, drip placement, reachability).</summary>
    public static bool IsWalkable(this TileType t) => t is TileType.Floor or TileType.ShallowWater;

    /// <summary>A miner may move onto this tile. Deep water is enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater;

    /// <summary>Entering this tile kills the miner (drowning).</summary>
    public static bool IsLethal(this TileType t) => t == TileType.DeepWater;

    /// <summary>Move-cadence multiplier for the tile a miner is standing on.</summary>
    public static double MoveCostMultiplier(this TileType t) =>
        t == TileType.ShallowWater ? ShallowSlowFactor : 1.0;

    public static bool IsMinable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
    public static bool IsBlastable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all `TileTypeWaterTests` green and the existing 60 tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core.Tests/TileTypeWaterTests.cs
git commit -m "feat(core): add shallow/deep water tile types and semantics"
```

---

## Task 2: Drown-on-entry in Simulation (Core, TDD)

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs:46-61` (`TryMove`)
- Test: `src/Miner49er.Core.Tests/SimulationMovementTests.cs` (append)

- [ ] **Step 1: Write the failing tests**

Append these tests to `src/Miner49er.Core.Tests/SimulationMovementTests.cs` (inside the class, before the closing brace):
```csharp
    [Fact]
    public void Move_into_shallow_water_succeeds_and_miner_lives()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.ShallowWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.Equal(new GridPos(2, 1), m.Pos);
        Assert.True(m.Alive);
    }

    [Fact]
    public void Move_into_deep_water_drowns_the_miner()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                       // the step happens
        Assert.Equal(new GridPos(2, 1), m.Pos);   // onto the deep tile
        Assert.False(m.Alive);                    // then drowns
        Assert.Equal(ActivityKind.None, m.Activity);
    }

    [Fact]
    public void Drowning_emits_MinerMoved_then_MinerDrowned()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(1, 0), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.North);
        var events = sim.DrainEvents();

        Assert.Equal(2, events.Count);
        Assert.IsType<MinerMoved>(events[0]);
        var drowned = Assert.IsType<MinerDrowned>(events[1]);
        Assert.Equal(1, drowned.MinerId);
    }

    [Fact]
    public void Dead_miner_cannot_move()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // drowns
        sim.DrainEvents();

        bool moved = sim.TryMove(1, Direction.West);

        Assert.False(moved);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: FAIL to compile — `MinerDrowned` does not exist; the deep-water test fails because `TryMove` still blocks non-walkable tiles.

- [ ] **Step 3a: Add the MinerDrowned event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add this line after the `MinerKilled` record:
```csharp
public sealed record MinerDrowned(int MinerId) : SimEvent;
```

- [ ] **Step 3b: Update TryMove**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the `TryMove` method (currently lines 46-61) with:
```csharp
    public bool TryMove(int id, Direction dir)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        m.Facing = dir;
        CancelActivity(m);

        var target = m.Pos + dir.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsEnterable()) return false;

        var from = m.Pos;
        m.Pos = target;
        _events.Add(new MinerMoved(id, from, target));

        if (Grid.Get(target).IsLethal())
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            _events.Add(new MinerDrowned(id));
        }
        return true;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — new drown tests green; existing movement tests (floor move, rock blocked, MinerMoved) still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMovementTests.cs
git commit -m "feat(core): drown miners that step into deep water"
```

---

## Task 3: Map generation water pass (Core, TDD)

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Test: `src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs` (create)

> Generation rule: carve pools (Manhattan discs) and rivers (drunken walks) as
> ShallowWater over Floor only; then promote *interior* shallow tiles (all four
> orthogonal neighbours are water) to DeepWater with `DeepWaterChance`. Interior
> promotion guarantees every deep tile is surrounded by water, so deep water is
> always shallow-ringed. Spawns and gold are restricted to the largest
> Floor+ShallowWater connected region (deep water = wall) so nothing is gated
> behind a forced drowning.
>
> Spawn placement uses "not orthogonally adjacent to any water" (the spec's
> fairness rule); we implement that directly rather than a separate
> `MinWaterSpawnDistance` knob (YAGNI).

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorWaterTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed) => new() { Seed = seed, PlayerCount = 4 };

    private static bool IsWater(TileType t) => t is TileType.ShallowWater or TileType.DeepWater;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Water_is_generated(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed)).Grid;
        Assert.Contains(grid.Positions(), p => grid.Get(p) == TileType.ShallowWater);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Deep_water_is_always_ringed_by_water(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed)).Grid;
        foreach (var p in grid.Positions())
        {
            if (grid.Get(p) != TileType.DeepWater) continue;
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                Assert.True(grid.InBounds(n) && IsWater(grid.Get(n)),
                    $"deep tile {p} has non-water neighbour {n}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Spawns_are_floor_and_not_water_adjacent(int seed)
    {
        var map = MapGenerator.Generate(Config(seed));
        foreach (var s in map.Spawns)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(s));
            foreach (var d in Card)
            {
                var n = s + d.ToOffset();
                if (map.Grid.InBounds(n))
                    Assert.False(IsWater(map.Grid.Get(n)), $"spawn {s} touches water at {n}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void All_spawns_and_gold_are_reachable_without_deep_water(int seed)
    {
        var map = MapGenerator.Generate(Config(seed));
        // Flood over Floor + ShallowWater only (deep water is a wall).
        var reachable = new HashSet<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(map.Spawns[0]); reachable.Add(map.Spawns[0]);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (map.Grid.InBounds(n) && map.Grid.Get(n).IsWalkable() && reachable.Add(n))
                    stack.Push(n);
            }
        }
        Assert.All(map.Spawns, s => Assert.Contains(s, reachable));
        foreach (var p in map.Grid.Positions())
            if (map.Grid.Get(p) == TileType.GoldRock)
                Assert.Contains(Card.Select(d => p + d.ToOffset()), n => reachable.Contains(n));
    }

    [Fact]
    public void Same_seed_produces_identical_water()
    {
        var a = MapGenerator.Generate(Config(99)).Grid;
        var b = MapGenerator.Generate(Config(99)).Grid;
        Assert.True(a.Positions().All(p => a.Get(p) == b.Get(p)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: FAIL — `Water_is_generated` fails (no water placed yet), and the reachability/spawn tests may fail because water is not yet considered.

- [ ] **Step 3a: Add water knobs to MapConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, add these properties before the closing brace:
```csharp
    // Water generation (Phase 4a).
    public int PoolCount { get; set; } = 3;
    public int PoolRadiusMin { get; set; } = 2;
    public int PoolRadiusMax { get; set; } = 4;
    public int RiverCount { get; set; } = 2;
    public int RiverLengthMin { get; set; } = 12;
    public int RiverLengthMax { get; set; } = 30;
    public float DeepWaterChance { get; set; } = 0.6f;
```

- [ ] **Step 3b: Wire the water pass into Generate and update spawn/gold/center**

In `src/Miner49er.Core/Map/MapGenerator.cs`, replace the `Generate` method body (lines 8-24) with:
```csharp
    public static GeneratedMap Generate(MapConfig config)
    {
        var rng = new Random(config.Seed);
        int width = config.BaseWidth + config.SizePerPlayer * (config.PlayerCount - 1);
        int height = config.BaseHeight + config.SizePerPlayer * (config.PlayerCount - 1);

        var grid = new TileGrid(width, height, TileType.Rock);
        RandomFill(grid, rng, config.InitialFloorChance);
        for (int i = 0; i < config.SmoothingSteps; i++) Smooth(grid);

        KeepLargestRegion(grid);
        PlaceWater(grid, rng, config);
        var region = LargestTraversableRegion(grid);
        var spawns = PlaceSpawns(grid, rng, config.PlayerCount, config.MinSpawnDistance, region);
        var center = NearestFloorToCenter(grid, region);
        PlaceGold(grid, rng, config.GoldVeinCount, region);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center };
    }
```

- [ ] **Step 3c: Add the water generation helpers**

In `src/Miner49er.Core/Map/MapGenerator.cs`, add these methods (anywhere inside the class, e.g. after `KeepLargestRegion`):
```csharp
    private static bool IsWater(TileType t) => t is TileType.ShallowWater or TileType.DeepWater;
    private static bool IsTraversable(TileType t) => t is TileType.Floor or TileType.ShallowWater;

    private static void PlaceWater(TileGrid g, Random rng, MapConfig cfg)
    {
        for (int i = 0; i < cfg.PoolCount; i++) CarvePool(g, rng, cfg);
        for (int i = 0; i < cfg.RiverCount; i++) CarveRiver(g, rng, cfg);
        PromoteDeep(g, rng, cfg);
    }

    private static GridPos? RandomFloor(TileGrid g, Random rng)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
        return floors.Count == 0 ? null : floors[rng.Next(floors.Count)];
    }

    private static void CarvePool(TileGrid g, Random rng, MapConfig cfg)
    {
        var c = RandomFloor(g, rng);
        if (c is null) return;
        int r = rng.Next(cfg.PoolRadiusMin, cfg.PoolRadiusMax + 1);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;
                var p = new GridPos(c.Value.X + dx, c.Value.Y + dy);
                if (g.InBounds(p) && g.Get(p) == TileType.Floor)
                    g.Set(p, TileType.ShallowWater);
            }
    }

    private static void CarveRiver(TileGrid g, Random rng, MapConfig cfg)
    {
        var start = RandomFloor(g, rng);
        if (start is null) return;
        var pos = start.Value;
        int len = rng.Next(cfg.RiverLengthMin, cfg.RiverLengthMax + 1);
        var dir = Card[rng.Next(Card.Length)];
        for (int i = 0; i < len; i++)
        {
            if (g.InBounds(pos) && g.Get(pos) == TileType.Floor)
                g.Set(pos, TileType.ShallowWater);
            if (rng.NextDouble() < 0.3) dir = Card[rng.Next(Card.Length)];
            var next = pos + dir.ToOffset();
            if (!g.InBounds(next) || g.Get(next) == TileType.ImpermeableRock)
                dir = Card[rng.Next(Card.Length)];
            else
                pos = next;
        }
    }

    private static void PromoteDeep(TileGrid g, Random rng, MapConfig cfg)
    {
        // Decide on the pre-promotion grid so order is irrelevant: an interior
        // shallow tile (all 4 neighbours water) may become deep. Boundary water
        // stays shallow, guaranteeing every deep tile is ringed by water.
        var interior = new List<GridPos>();
        foreach (var p in g.Positions())
        {
            if (g.Get(p) != TileType.ShallowWater) continue;
            bool allWater = true;
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (!g.InBounds(n) || !IsWater(g.Get(n))) { allWater = false; break; }
            }
            if (allWater) interior.Add(p);
        }
        foreach (var p in interior)
            if (rng.NextDouble() < cfg.DeepWaterChance)
                g.Set(p, TileType.DeepWater);
    }

    private static HashSet<GridPos> LargestTraversableRegion(TileGrid g)
    {
        var visited = new HashSet<GridPos>();
        HashSet<GridPos> largest = new();
        foreach (var p in g.Positions())
        {
            if (!IsTraversable(g.Get(p)) || visited.Contains(p)) continue;
            var region = new HashSet<GridPos>();
            var stack = new Stack<GridPos>();
            stack.Push(p); visited.Add(p); region.Add(p);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var d in Card)
                {
                    var n = cur + d.ToOffset();
                    if (g.InBounds(n) && IsTraversable(g.Get(n)) && visited.Add(n))
                    {
                        region.Add(n);
                        stack.Push(n);
                    }
                }
            }
            if (region.Count > largest.Count) largest = region;
        }
        return largest;
    }

    private static bool IsWaterAdjacent(TileGrid g, GridPos p)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (g.InBounds(n) && IsWater(g.Get(n))) return true;
        }
        return false;
    }
```

- [ ] **Step 3d: Make spawn/gold/center placement region-aware**

In `src/Miner49er.Core/Map/MapGenerator.cs`, replace `PlaceSpawns`, `NearestFloorToCenter`, and `PlaceGold` with these signatures/bodies:
```csharp
    private static List<GridPos> PlaceSpawns(TileGrid g, Random rng, int count, int minDistance, HashSet<GridPos> region)
    {
        var floors = region.Where(p => g.Get(p) == TileType.Floor && !IsWaterAdjacent(g, p)).ToList();
        if (floors.Count < count) // fallback: relax the water-adjacency rule if too few
            floors = region.Where(p => g.Get(p) == TileType.Floor).ToList();
        Shuffle(floors, rng);
        var spawns = new List<GridPos>();
        int distance = minDistance;
        while (spawns.Count < count && distance >= 0)
        {
            spawns.Clear();
            foreach (var p in floors)
            {
                if (spawns.All(s => s.ManhattanTo(p) >= distance))
                    spawns.Add(p);
                if (spawns.Count == count) break;
            }
            if (spawns.Count < count) distance--;
        }
        return spawns;
    }

    private static GridPos NearestFloorToCenter(TileGrid g, HashSet<GridPos> region)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        return region.Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .First();
    }

    private static void PlaceGold(TileGrid g, Random rng, int veins, HashSet<GridPos> region)
    {
        var candidates = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(candidates, rng);
        foreach (var p in candidates.Take(veins)) g.Set(p, TileType.GoldRock);
    }

    private static bool HasRegionNeighbor(TileGrid g, GridPos p, HashSet<GridPos> region)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (region.Contains(n)) return true;
        }
        return false;
    }
```

> Delete the now-unused old `PlaceSpawns`, `NearestFloorToCenter`, `PlaceGold`, and the old `HasFloorNeighbor` helper if it is no longer referenced. (The old `Flood` helper used by `KeepLargestRegion` stays.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — new `MapGeneratorWaterTests` green; existing `MapGeneratorTests` (border, determinism, spawn-reaches-center, gold present) and `MapDeterminismTests` still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorWaterTests.cs
git commit -m "feat(core): generate water pools/rivers with shored deep water"
```

---

## Task 4: Host-side shallow slowing (Godot)

**Files:**
- Modify: `game/net/MatchHost.cs:69-73` (the move loop in `StepOnce`)

No unit test (the Core `MoveCostMultiplier` is already tested); verify via build + headless boot.

- [ ] **Step 1: Apply the per-tile move cooldown**

In `game/net/MatchHost.cs`, replace the pending-direction loop in `StepOnce` (currently lines 69-73):
```csharp
			foreach (var (minerId, dir) in _pendingDir)
			{
				if (dir < 0 || _moveCooldown[minerId] > 0) continue;
				if (_sim.TryMove(minerId, (Direction)dir)) _moveCooldown[minerId] = MoveStepSeconds;
			}
```
with:
```csharp
			foreach (var (minerId, dir) in _pendingDir)
			{
				if (dir < 0 || _moveCooldown[minerId] > 0) continue;
				if (_sim.TryMove(minerId, (Direction)dir))
				{
					var tile = _sim.Grid.Get(_sim.GetMiner(minerId).Pos);
					_moveCooldown[minerId] = MoveStepSeconds * (float)tile.MoveCostMultiplier();
				}
			}
```

- [ ] **Step 2: Build**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Headless boot**

Run: `godot --headless --quit-after 60`
Expected: exit 0, no errors.

- [ ] **Step 4: Commit**

```bash
git add game/net/MatchHost.cs
git commit -m "feat(host): slow miners moving through shallow water"
```

---

## Task 5: Render water tiles (Godot)

**Files:**
- Modify: `game/WorldRenderer.cs:13-18` (colors) and `:44-51` (tile switch)

- [ ] **Step 1: Add the water colors**

In `game/WorldRenderer.cs`, add two color fields alongside the existing ones (after `ImpermeableColor`):
```csharp
	private static readonly Color ShallowWaterColor = new("2f6f8f");
	private static readonly Color DeepWaterColor = new("16384f");
```

- [ ] **Step 2: Draw the new tiles**

In `game/WorldRenderer.cs`, update the tile color switch in `_Draw` to include the water cases:
```csharp
			var color = grid.Get(p) switch
			{
				TileType.Floor => FloorColor,
				TileType.Rock => RockColor,
				TileType.GoldRock => GoldColor,
				TileType.ImpermeableRock => ImpermeableColor,
				TileType.ShallowWater => ShallowWaterColor,
				TileType.DeepWater => DeepWaterColor,
				_ => FloorColor,
			};
```

- [ ] **Step 3: Build + headless boot**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.
Run: `godot --headless --quit-after 60`
Expected: exit 0, no errors.

- [ ] **Step 4: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(render): draw shallow and deep water tiles"
```

---

## Task 6: Splash-on-drown audio (Godot)

**Files:**
- Modify: `game/audio/SfxLibrary.cs` (add `Splash`)
- Modify: `game/net/MatchAudio.cs:72-75` (tile-aware death sound)
- Modify: `assets/audio/README.md` (manifest row)

- [ ] **Step 1: Add a Splash placeholder to SfxLibrary**

In `game/audio/SfxLibrary.cs`, add this accessor alongside the others (after `Drip`):
```csharp
	public static AudioStream Splash => Get("splash", () => Noise(0.25f, 700f, decay: true));
```

- [ ] **Step 2: Play splash when a miner dies in deep water**

In `game/net/MatchAudio.cs`, replace the death block in `_Process` (currently lines 72-75):
```csharp
				bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
				if (prevAlive && !m.Alive)
					OneShot(SfxLibrary.Death, WorldOf(m.X, m.Y));
				_prevAlive[m.Id] = m.Alive;
```
with:
```csharp
				bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
				if (prevAlive && !m.Alive)
				{
					var tile = new GridPos(m.X, m.Y);
					bool drowned = _client.Grid.InBounds(tile)
						&& _client.Grid.Get(tile) == TileType.DeepWater;
					OneShot(drowned ? SfxLibrary.Splash : SfxLibrary.Death, WorldOf(m.X, m.Y));
				}
				_prevAlive[m.Id] = m.Alive;
```

- [ ] **Step 3: Add the manifest row**

In `assets/audio/README.md`, add this row to the table (after the `drip` row):
```
| splash         | splash.ogg/.wav      | a miner drowns in deep water    |
```

- [ ] **Step 4: Build + headless boot**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.
Run: `godot --headless --quit-after 60`
Expected: exit 0, no errors.

- [ ] **Step 5: Commit**

```bash
git add game/audio/SfxLibrary.cs game/net/MatchAudio.cs assets/audio/README.md
git commit -m "feat(audio): play a splash when a miner drowns"
```

---

## Final verification

- [ ] **Full Core suite:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` → all green (60 prior + new water/drown/map tests).
- [ ] **Build:** `dotnet build Miner49er.csproj` → 0 errors.
- [ ] **Headless boot:** `godot --headless --quit-after 180` → exit 0, no errors.
- [ ] **Manual play-test (user, two instances):** confirm — water appears as shallow (light) and deep (dark) tiles; wading through shallow water is visibly slower; stepping into deep water kills you with a splash; deep water is always fringed by shallow so a drowning is telegraphed; spawns and gold are never stranded behind deep water. (Audio/feel is the user's to verify.)
- [ ] **Update memory:** refresh the Phase 4 status note (4a water substrate done on `phase4a-water-hazard`; flood mode remains 4b).

---

## Notes carried forward (not built here)
- **Flood mode** (rising water from the edges) and **type-aware tile-change sync** → sub-phase 4b. The water tiles are mutated via `TileGrid.Set`, so a host `FloodDriver` can drive them later; 4b will extend `TileChange` to carry a `TileType` (today `MatchClient.ApplyUpdate` hardcodes `Floor`).
- **Water-plank item** (cross deep water) and the §3.5 status-effect system → 4c.
- **Cave-ins** → 4d.
