# Phase 1: Core Single-Machine Loop — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A single-player, single-machine playable loop where a miner walks a procedurally generated mine, digs rock with a pickaxe, plants timed explosives that blast rock and kill anyone caught in the blast, all under fog of war — so that "mining and blasting is fun" before any networking exists.

**Architecture:** All game rules live in a pure-C# class library `Miner49er.Core` with **no Godot dependency**, unit-tested with xUnit. The Godot 4 (C#) project is a thin adapter: it reads input, calls into the simulation, advances it by delta time, and renders the world/player/fog with simple `_Draw()` colored rectangles (no art assets needed). This keeps the hard logic deterministic and testable, and the engine layer disposable.

**Tech Stack:** .NET 8, C#, xUnit (logic tests), Godot 4.4 (C#/.NET) for rendering and input.

---

## Prerequisites

- **.NET 8 SDK** installed (`dotnet --version` → 8.x). Required from Task 1.
- **Godot 4.4 (.NET/Mono build)** installed. Required from Task 9 onward only.
- Repo already initialized at `D:\Projects\Miner49er` with the design doc committed.

## File Structure

```
Miner49er/
  Miner49er.sln                              # solution (created Task 1)
  project.godot                              # Godot project (created Task 9)
  src/
    Miner49er.Core/                          # pure C# game logic (no Godot)
      Miner49er.Core.csproj
      Grid/Direction.cs
      Grid/GridPos.cs
      Grid/TileType.cs
      Grid/TileGrid.cs
      Map/MapConfig.cs
      Map/GeneratedMap.cs
      Map/MapGenerator.cs
      Sim/SimConfig.cs
      Sim/Miner.cs
      Sim/Charge.cs
      Sim/SimEvent.cs
      Sim/Simulation.cs
      Fog/Visibility.cs
      Fog/FogState.cs
    Miner49er.Core.Tests/                    # xUnit tests for the above
      Miner49er.Core.Tests.csproj
      DirectionTests.cs
      TileGridTests.cs
      MapGeneratorTests.cs
      SimulationMovementTests.cs
      SimulationMiningTests.cs
      SimulationExplosiveTests.cs
      VisibilityTests.cs
  Miner49er.csproj                           # Godot game project (created Task 9)
  game/                                       # Godot C# scripts + scenes
    Main.cs / Main.tscn
    WorldRenderer.cs
    FogRenderer.cs
    InputBindings.cs
```

**Responsibilities:**
- `Grid/*` — data: coordinates, tile types, the 2D tile array. No behavior beyond queries.
- `Map/*` — procedural generation: pure function `(MapConfig) → GeneratedMap`.
- `Sim/*` — the rules engine: miners, activities (mining/planting), charges, detonation, deaths, events. Advanced by `Tick(dt)`.
- `Fog/*` — visibility computation and explored-memory tracking.
- `game/*` — Godot nodes that render `Core` state and translate input into `Simulation` calls.

---

## Task 1: Solution scaffolding + first green test

**Files:**
- Create: `src/Miner49er.Core/Miner49er.Core.csproj`
- Create: `src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- Create: `Miner49er.sln`
- Create (temporary smoke test): `src/Miner49er.Core/Grid/Direction.cs` (stub), `src/Miner49er.Core.Tests/DirectionTests.cs`

- [ ] **Step 1: Create the library, test project, and solution**

Run from repo root:
```bash
dotnet new classlib -o src/Miner49er.Core -f net8.0
dotnet new xunit    -o src/Miner49er.Core.Tests -f net8.0
dotnet new sln -n Miner49er
dotnet sln Miner49er.sln add src/Miner49er.Core/Miner49er.Core.csproj
dotnet sln Miner49er.sln add src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
dotnet add src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj reference src/Miner49er.Core/Miner49er.Core.csproj
```
Then delete the template files `dotnet new` created:
```bash
rm src/Miner49er.Core/Class1.cs
rm src/Miner49er.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Enable nullable + implicit usings in Core**

Replace `src/Miner49er.Core/Miner49er.Core.csproj` contents with:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Miner49er.Core</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write a trivial failing test**

Create `src/Miner49er.Core.Tests/DirectionTests.cs`:
```csharp
using Miner49er.Core;
using Xunit;

public class DirectionTests
{
    [Fact]
    public void North_offset_points_up()
    {
        Assert.Equal(new GridPos(0, -1), Direction.North.ToOffset());
    }
}
```

- [ ] **Step 4: Run the test to verify it fails to compile**

Run: `dotnet test`
Expected: FAIL — `Direction` / `GridPos` do not exist yet.

- [ ] **Step 5: Commit the scaffolding**

```bash
git add Miner49er.sln src/Miner49er.Core src/Miner49er.Core.Tests
git commit -m "chore: scaffold Core library and xUnit test project"
```

---

## Task 2: GridPos and Direction

**Files:**
- Create: `src/Miner49er.Core/Grid/GridPos.cs`
- Create: `src/Miner49er.Core/Grid/Direction.cs`
- Test: `src/Miner49er.Core.Tests/DirectionTests.cs` (extend)

Convention: grid origin top-left, **Y increases downward** (matches screen/tile coordinates). North = up = `(0,-1)`.

- [ ] **Step 1: Write failing tests**

Replace `src/Miner49er.Core.Tests/DirectionTests.cs` with:
```csharp
using Miner49er.Core;
using Xunit;

public class DirectionTests
{
    [Theory]
    [InlineData(Direction.North, 0, -1)]
    [InlineData(Direction.East, 1, 0)]
    [InlineData(Direction.South, 0, 1)]
    [InlineData(Direction.West, -1, 0)]
    public void ToOffset_returns_expected(Direction d, int x, int y)
    {
        Assert.Equal(new GridPos(x, y), d.ToOffset());
    }

    [Fact]
    public void Add_and_operator_plus_agree()
    {
        var a = new GridPos(3, 4);
        Assert.Equal(new GridPos(4, 4), a + Direction.East.ToOffset());
        Assert.Equal(new GridPos(4, 4), a.Add(Direction.East.ToOffset()));
    }

    [Fact]
    public void Distances_are_correct()
    {
        var a = new GridPos(0, 0);
        var b = new GridPos(3, 2);
        Assert.Equal(5, a.ManhattanTo(b));
        Assert.Equal(3, a.ChebyshevTo(b));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement GridPos**

Create `src/Miner49er.Core/Grid/GridPos.cs`:
```csharp
namespace Miner49er.Core;

public readonly record struct GridPos(int X, int Y)
{
    public GridPos Add(GridPos o) => new(X + o.X, Y + o.Y);
    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public int ManhattanTo(GridPos o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y);
    public int ChebyshevTo(GridPos o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));
}
```

- [ ] **Step 4: Implement Direction**

Create `src/Miner49er.Core/Grid/Direction.cs`:
```csharp
namespace Miner49er.Core;

public enum Direction { North, East, South, West }

public static class DirectionExtensions
{
    public static GridPos ToOffset(this Direction d) => d switch
    {
        Direction.North => new GridPos(0, -1),
        Direction.East  => new GridPos(1, 0),
        Direction.South => new GridPos(0, 1),
        Direction.West  => new GridPos(-1, 0),
        _ => new GridPos(0, 0),
    };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test`
Expected: PASS (all DirectionTests green).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Grid/GridPos.cs src/Miner49er.Core/Grid/Direction.cs src/Miner49er.Core.Tests/DirectionTests.cs
git commit -m "feat(core): add GridPos and Direction"
```

---

## Task 3: TileType and TileGrid

**Files:**
- Create: `src/Miner49er.Core/Grid/TileType.cs`
- Create: `src/Miner49er.Core/Grid/TileGrid.cs`
- Test: `src/Miner49er.Core.Tests/TileGridTests.cs`

Phase 1 tile set: `Floor`, `Rock`, `GoldRock` (rock containing gold), `ImpermeableRock`. (Water is Phase 4.)

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/TileGridTests.cs`:
```csharp
using Miner49er.Core;
using Xunit;

public class TileGridTests
{
    [Fact]
    public void New_grid_is_filled_with_given_type()
    {
        var grid = new TileGrid(3, 2, TileType.Rock);
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(TileType.Rock, grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Set_then_Get_roundtrips()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(1, 1), TileType.Floor);
        Assert.Equal(TileType.Floor, grid.Get(new GridPos(1, 1)));
    }

    [Fact]
    public void InBounds_rejects_outside_positions()
    {
        var grid = new TileGrid(3, 3);
        Assert.True(grid.InBounds(new GridPos(0, 0)));
        Assert.True(grid.InBounds(new GridPos(2, 2)));
        Assert.False(grid.InBounds(new GridPos(-1, 0)));
        Assert.False(grid.InBounds(new GridPos(3, 0)));
    }

    [Fact]
    public void IsWalkable_only_true_for_floor_in_bounds()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(1, 1), TileType.Floor);
        Assert.True(grid.IsWalkable(new GridPos(1, 1)));
        Assert.False(grid.IsWalkable(new GridPos(0, 0)));   // rock
        Assert.False(grid.IsWalkable(new GridPos(-1, 0)));  // out of bounds
    }

    [Theory]
    [InlineData(TileType.Rock, true, true)]
    [InlineData(TileType.GoldRock, true, true)]
    [InlineData(TileType.Floor, false, false)]
    [InlineData(TileType.ImpermeableRock, false, false)]
    public void Tile_capability_flags(TileType t, bool minable, bool blastable)
    {
        Assert.Equal(minable, t.IsMinable());
        Assert.Equal(blastable, t.IsBlastable());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — `TileType` / `TileGrid` not defined.

- [ ] **Step 3: Implement TileType**

Create `src/Miner49er.Core/Grid/TileType.cs`:
```csharp
namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock }

public static class TileTypeExtensions
{
    public static bool IsWalkable(this TileType t) => t == TileType.Floor;
    public static bool IsMinable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
    public static bool IsBlastable(this TileType t) => t is TileType.Rock or TileType.GoldRock;
}
```

- [ ] **Step 4: Implement TileGrid**

Create `src/Miner49er.Core/Grid/TileGrid.cs`:
```csharp
namespace Miner49er.Core;

public sealed class TileGrid
{
    public int Width { get; }
    public int Height { get; }
    private readonly TileType[] _tiles;

    public TileGrid(int width, int height, TileType fill = TileType.Rock)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Grid dimensions must be positive.");
        Width = width;
        Height = height;
        _tiles = new TileType[width * height];
        Array.Fill(_tiles, fill);
    }

    public bool InBounds(GridPos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public TileType Get(GridPos p)
    {
        if (!InBounds(p)) throw new ArgumentOutOfRangeException(nameof(p));
        return _tiles[p.Y * Width + p.X];
    }

    public void Set(GridPos p, TileType t)
    {
        if (!InBounds(p)) throw new ArgumentOutOfRangeException(nameof(p));
        _tiles[p.Y * Width + p.X] = t;
    }

    public bool IsWalkable(GridPos p) => InBounds(p) && Get(p).IsWalkable();

    public IEnumerable<GridPos> Positions()
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new GridPos(x, y);
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core/Grid/TileGrid.cs src/Miner49er.Core.Tests/TileGridTests.cs
git commit -m "feat(core): add TileType and TileGrid"
```

---

## Task 4: MapGenerator — config, output, and dimensions

**Files:**
- Create: `src/Miner49er.Core/Map/MapConfig.cs`
- Create: `src/Miner49er.Core/Map/GeneratedMap.cs`
- Create: `src/Miner49er.Core/Map/MapGenerator.cs`
- Test: `src/Miner49er.Core.Tests/MapGeneratorTests.cs`

This task builds the generator incrementally across Steps. Final algorithm:
1. Compute dimensions scaled by player count.
2. Random fill interior with Floor/Rock by `InitialFloorChance`; border ring = `ImpermeableRock`.
3. Cellular-automata smoothing (`SmoothingSteps` passes).
4. Keep the **largest connected floor region**; convert all other floor to Rock (guarantees connectivity).
5. Place spawns inside that region, pairwise ≥ `MinSpawnDistance` (relaxing if needed).
6. Pick `Center` = region floor cell nearest the geometric center.
7. Convert some rock cells adjacent to floor into `GoldRock` (`GoldVeinCount`).

- [ ] **Step 1: Write failing tests (invariants)**

Create `src/Miner49er.Core.Tests/MapGeneratorTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorTests
{
    private static MapConfig Config(int seed, int players) => new()
    {
        Seed = seed,
        PlayerCount = players,
        BaseWidth = 24,
        BaseHeight = 24,
        SizePerPlayer = 6,
        InitialFloorChance = 0.45f,
        SmoothingSteps = 4,
        MinSpawnDistance = 6,
        GoldVeinCount = 8,
    };

    [Fact]
    public void Dimensions_scale_with_player_count()
    {
        var two = MapGenerator.Generate(Config(1, 2)).Grid;
        var eight = MapGenerator.Generate(Config(1, 8)).Grid;
        Assert.True(eight.Width > two.Width);
        Assert.True(eight.Height > two.Height);
    }

    [Fact]
    public void Border_is_impermeable()
    {
        var grid = MapGenerator.Generate(Config(7, 4)).Grid;
        for (int x = 0; x < grid.Width; x++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 0)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, grid.Height - 1)));
        }
        for (int y = 0; y < grid.Height; y++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(0, y)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(grid.Width - 1, y)));
        }
    }

    [Fact]
    public void Same_seed_produces_identical_maps()
    {
        var a = MapGenerator.Generate(Config(42, 4)).Grid;
        var b = MapGenerator.Generate(Config(42, 4)).Grid;
        Assert.True(a.Positions().All(p => a.Get(p) == b.Get(p)));
    }

    [Fact]
    public void Spawns_count_matches_players_and_are_walkable()
    {
        var map = MapGenerator.Generate(Config(3, 6));
        Assert.Equal(6, map.Spawns.Count);
        Assert.All(map.Spawns, s => Assert.True(map.Grid.IsWalkable(s)));
    }

    [Fact]
    public void Every_spawn_can_reach_the_center()
    {
        var map = MapGenerator.Generate(Config(9, 4));
        var reachable = FloodFillFloor(map.Grid, map.Center);
        Assert.All(map.Spawns, s => Assert.Contains(s, reachable));
    }

    [Fact]
    public void Gold_rock_is_present()
    {
        var grid = MapGenerator.Generate(Config(5, 4)).Grid;
        Assert.Contains(grid.Positions(), p => grid.Get(p) == TileType.GoldRock);
    }

    private static HashSet<GridPos> FloodFillFloor(TileGrid grid, GridPos start)
    {
        var seen = new HashSet<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(start);
        seen.Add(start);
        Direction[] dirs = { Direction.North, Direction.East, Direction.South, Direction.West };
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var d in dirs)
            {
                var n = p + d.ToOffset();
                if (grid.IsWalkable(n) && seen.Add(n)) stack.Push(n);
            }
        }
        return seen;
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — `MapConfig` / `GeneratedMap` / `MapGenerator` not defined.

- [ ] **Step 3: Implement MapConfig and GeneratedMap**

Create `src/Miner49er.Core/Map/MapConfig.cs`:
```csharp
namespace Miner49er.Core;

public sealed class MapConfig
{
    public int Seed { get; set; }
    public int PlayerCount { get; set; } = 1;
    public int BaseWidth { get; set; } = 24;
    public int BaseHeight { get; set; } = 24;
    public int SizePerPlayer { get; set; } = 6;
    public float InitialFloorChance { get; set; } = 0.45f;
    public int SmoothingSteps { get; set; } = 4;
    public int MinSpawnDistance { get; set; } = 6;
    public int GoldVeinCount { get; set; } = 8;
}
```

Create `src/Miner49er.Core/Map/GeneratedMap.cs`:
```csharp
namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
}
```

- [ ] **Step 4: Implement MapGenerator**

Create `src/Miner49er.Core/Map/MapGenerator.cs`:
```csharp
namespace Miner49er.Core;

public static class MapGenerator
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    public static GeneratedMap Generate(MapConfig config)
    {
        var rng = new Random(config.Seed);
        int width = config.BaseWidth + config.SizePerPlayer * (config.PlayerCount - 1);
        int height = config.BaseHeight + config.SizePerPlayer * (config.PlayerCount - 1);

        var grid = new TileGrid(width, height, TileType.Rock);
        RandomFill(grid, rng, config.InitialFloorChance);
        for (int i = 0; i < config.SmoothingSteps; i++) Smooth(grid);

        KeepLargestRegion(grid);
        var spawns = PlaceSpawns(grid, rng, config.PlayerCount, config.MinSpawnDistance);
        var center = NearestFloorToCenter(grid);
        PlaceGold(grid, rng, config.GoldVeinCount);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center };
    }

    private static bool IsBorder(TileGrid g, GridPos p) =>
        p.X == 0 || p.Y == 0 || p.X == g.Width - 1 || p.Y == g.Height - 1;

    private static void RandomFill(TileGrid g, Random rng, float floorChance)
    {
        foreach (var p in g.Positions())
        {
            if (IsBorder(g, p)) { g.Set(p, TileType.ImpermeableRock); continue; }
            g.Set(p, rng.NextDouble() < floorChance ? TileType.Floor : TileType.Rock);
        }
    }

    private static void Smooth(TileGrid g)
    {
        var next = new TileType[g.Width * g.Height];
        foreach (var p in g.Positions())
        {
            if (IsBorder(g, p)) { next[p.Y * g.Width + p.X] = TileType.ImpermeableRock; continue; }
            int rockNeighbors = CountRockNeighbors(g, p);
            // Standard cave CA: become rock if surrounded, floor if open.
            TileType result = rockNeighbors > 4 ? TileType.Rock
                            : rockNeighbors < 4 ? TileType.Floor
                            : g.Get(p);
            next[p.Y * g.Width + p.X] = result;
        }
        foreach (var p in g.Positions()) g.Set(p, next[p.Y * g.Width + p.X]);
    }

    private static int CountRockNeighbors(TileGrid g, GridPos p)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new GridPos(p.X + dx, p.Y + dy);
                // Treat out-of-bounds as rock (walls press inward).
                if (!g.InBounds(n) || g.Get(n) != TileType.Floor) count++;
            }
        return count;
    }

    private static void KeepLargestRegion(TileGrid g)
    {
        var visited = new HashSet<GridPos>();
        List<GridPos> largest = new();
        foreach (var p in g.Positions())
        {
            if (g.Get(p) != TileType.Floor || visited.Contains(p)) continue;
            var region = Flood(g, p, visited);
            if (region.Count > largest.Count) largest = region;
        }
        var keep = new HashSet<GridPos>(largest);
        foreach (var p in g.Positions())
            if (g.Get(p) == TileType.Floor && !keep.Contains(p))
                g.Set(p, TileType.Rock);
    }

    private static List<GridPos> Flood(TileGrid g, GridPos start, HashSet<GridPos> visited)
    {
        var region = new List<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(start); visited.Add(start);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            region.Add(p);
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n) == TileType.Floor && visited.Add(n))
                    stack.Push(n);
            }
        }
        return region;
    }

    private static List<GridPos> PlaceSpawns(TileGrid g, Random rng, int count, int minDistance)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
        Shuffle(floors, rng);
        var spawns = new List<GridPos>();
        int distance = minDistance;
        // Relax the distance until we can place all requested spawns.
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

    private static GridPos NearestFloorToCenter(TileGrid g)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        return g.Positions()
            .Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .First();
    }

    private static void PlaceGold(TileGrid g, Random rng, int veins)
    {
        var candidates = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasFloorNeighbor(g, p))
            .ToList();
        Shuffle(candidates, rng);
        foreach (var p in candidates.Take(veins)) g.Set(p, TileType.GoldRock);
    }

    private static bool HasFloorNeighbor(TileGrid g, GridPos p)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (g.InBounds(n) && g.Get(n) == TileType.Floor) return true;
        }
        return false;
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test`
Expected: PASS. If `Spawns_count_matches_players` flakes for a seed, the relax loop in `PlaceSpawns` guarantees placement down to distance 0; only an empty region could fail (not possible after `KeepLargestRegion` on a 0.45 fill).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Map src/Miner49er.Core.Tests/MapGeneratorTests.cs
git commit -m "feat(core): add seeded cellular-automata MapGenerator"
```

---

## Task 5: Simulation skeleton + movement

**Files:**
- Create: `src/Miner49er.Core/Sim/SimConfig.cs`
- Create: `src/Miner49er.Core/Sim/Miner.cs`
- Create: `src/Miner49er.Core/Sim/SimEvent.cs`
- Create: `src/Miner49er.Core/Sim/Charge.cs`
- Create: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/SimulationMovementTests.cs`

- [ ] **Step 1: Write failing movement tests**

Create `src/Miner49er.Core.Tests/SimulationMovementTests.cs`:
```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMovementTests
{
    // 3x3 all floor (no border concept needed for unit tests).
    private static TileGrid OpenGrid() => new(3, 3, TileType.Floor);

    [Fact]
    public void Move_into_floor_updates_position_and_facing()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.Equal(new GridPos(2, 1), m.Pos);
        Assert.Equal(Direction.East, m.Facing);
    }

    [Fact]
    public void Move_into_rock_is_blocked_but_still_sets_facing()
    {
        var grid = OpenGrid();
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.False(moved);
        Assert.Equal(new GridPos(1, 1), m.Pos);
        Assert.Equal(Direction.East, m.Facing);
    }

    [Fact]
    public void Move_emits_MinerMoved_event()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.North);
        var events = sim.DrainEvents();

        var moved = Assert.IsType<MinerMoved>(Assert.Single(events));
        Assert.Equal(new GridPos(1, 0), moved.To);
    }

    [Fact]
    public void DrainEvents_clears_the_buffer()
    {
        var sim = new Simulation(OpenGrid(), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.North);
        sim.DrainEvents();
        Assert.Empty(sim.DrainEvents());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — Simulation types not defined.

- [ ] **Step 3: Implement SimConfig**

Create `src/Miner49er.Core/Sim/SimConfig.cs`:
```csharp
namespace Miner49er.Core;

public sealed class SimConfig
{
    public double PickaxeSeconds { get; set; } = 6.0;
    public double PlantSeconds { get; set; } = 1.0;
    public double FuseSeconds { get; set; } = 3.0;
    public int BlastRockRadius { get; set; } = 1;   // Manhattan radius of rock destruction
    public int BlastKillRadius { get; set; } = 1;   // Chebyshev radius that kills miners
    public int MaxLiveChargesPerMiner { get; set; } = 3;
}
```

- [ ] **Step 4: Implement Miner**

Create `src/Miner49er.Core/Sim/Miner.cs`:
```csharp
namespace Miner49er.Core;

public enum ActivityKind { None, Mining, Planting }

public sealed class Miner
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public bool Alive { get; internal set; } = true;
    public int GoldCollected { get; internal set; }

    public ActivityKind Activity { get; internal set; } = ActivityKind.None;
    public GridPos ActivityTarget { get; internal set; }
    public double ActivitySecondsRemaining { get; internal set; }

    internal Miner(int id, GridPos pos) { Id = id; Pos = pos; }
}
```

- [ ] **Step 5: Implement SimEvent and Charge**

Create `src/Miner49er.Core/Sim/SimEvent.cs`:
```csharp
namespace Miner49er.Core;

public abstract record SimEvent;
public sealed record MinerMoved(int MinerId, GridPos From, GridPos To) : SimEvent;
public sealed record ActivityStarted(int MinerId, ActivityKind Kind, GridPos Target) : SimEvent;
public sealed record RockMined(int MinerId, GridPos Pos, bool WasGold) : SimEvent;
public sealed record ChargePlanted(int MinerId, GridPos WallPos) : SimEvent;
public sealed record Explosion(GridPos WallPos, IReadOnlyList<GridPos> DestroyedRock) : SimEvent;
public sealed record MinerKilled(int MinerId) : SimEvent;
```

Create `src/Miner49er.Core/Sim/Charge.cs`:
```csharp
namespace Miner49er.Core;

public sealed class Charge
{
    public int OwnerId { get; }
    public GridPos WallPos { get; }
    public double FuseRemaining { get; internal set; }

    internal Charge(int ownerId, GridPos wallPos, double fuse)
    {
        OwnerId = ownerId;
        WallPos = wallPos;
        FuseRemaining = fuse;
    }
}
```

- [ ] **Step 6: Implement Simulation (movement + event plumbing only)**

Create `src/Miner49er.Core/Sim/Simulation.cs`:
```csharp
namespace Miner49er.Core;

public sealed class Simulation
{
    public TileGrid Grid { get; }
    public SimConfig Config { get; }

    private readonly Dictionary<int, Miner> _miners = new();
    private readonly List<Charge> _charges = new();
    private readonly List<SimEvent> _events = new();

    public IReadOnlyCollection<Miner> Miners => _miners.Values;
    public IReadOnlyList<Charge> Charges => _charges;

    public Simulation(TileGrid grid, SimConfig config)
    {
        Grid = grid;
        Config = config;
    }

    public Miner AddMiner(int id, GridPos pos)
    {
        var m = new Miner(id, pos);
        _miners[id] = m;
        return m;
    }

    public Miner GetMiner(int id) => _miners[id];

    public IReadOnlyList<SimEvent> DrainEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public bool TryMove(int id, Direction dir)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        m.Facing = dir;
        CancelActivity(m);

        var target = m.Pos + dir.ToOffset();
        if (!Grid.IsWalkable(target)) return false;

        var from = m.Pos;
        m.Pos = target;
        _events.Add(new MinerMoved(id, from, target));
        return true;
    }

    private void CancelActivity(Miner m)
    {
        m.Activity = ActivityKind.None;
        m.ActivitySecondsRemaining = 0;
    }

    // Mining, planting, and Tick are added in later tasks.
}
```

- [ ] **Step 7: Run to verify pass**

Run: `dotnet test`
Expected: PASS (all SimulationMovementTests green).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Sim src/Miner49er.Core.Tests/SimulationMovementTests.cs
git commit -m "feat(core): add Simulation with grid movement and events"
```

---

## Task 6: Mining (pickaxe) with timed activity

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/SimulationMiningTests.cs`

- [ ] **Step 1: Write failing mining tests**

Create `src/Miner49er.Core.Tests/SimulationMiningTests.cs`:
```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMiningTests
{
    private static (Simulation sim, Miner m) Setup(TileType ahead)
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), ahead); // tile east of (1,1)
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 6.0 });
        var m = sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);       // face east (blocked if rock — fine)
        sim.DrainEvents();
        return (sim, m);
    }

    [Fact]
    public void Start_mining_rock_begins_activity()
    {
        var (sim, m) = Setup(TileType.Rock);
        bool ok = sim.TryStartMining(1);
        Assert.True(ok);
        Assert.Equal(ActivityKind.Mining, m.Activity);
        Assert.Equal(new GridPos(2, 1), m.ActivityTarget);
        Assert.Equal(6.0, m.ActivitySecondsRemaining);
    }

    [Fact]
    public void Cannot_mine_floor()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // faces floor at (2,1)
        Assert.False(sim.TryStartMining(1));
    }

    [Fact]
    public void Mining_completes_after_full_duration_and_clears_rock()
    {
        var (sim, m) = Setup(TileType.Rock);
        sim.TryStartMining(1);

        sim.Tick(3.0);
        Assert.Equal(ActivityKind.Mining, m.Activity); // not done yet
        sim.Tick(3.0);

        Assert.Equal(ActivityKind.None, m.Activity);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Mining_goldrock_awards_gold_and_emits_event()
    {
        var (sim, m) = Setup(TileType.GoldRock);
        sim.TryStartMining(1);
        sim.DrainEvents();

        sim.Tick(6.0);

        Assert.Equal(1, m.GoldCollected);
        var mined = sim.DrainEvents().OfType<RockMined>().Single();
        Assert.True(mined.WasGold);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Moving_cancels_mining()
    {
        var (sim, m) = Setup(TileType.Rock);
        sim.TryStartMining(1);
        sim.TryMove(1, Direction.West); // walkable, cancels
        Assert.Equal(ActivityKind.None, m.Activity);
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1))); // unchanged
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — `TryStartMining` / `Tick` not defined.

- [ ] **Step 3: Add mining + Tick to Simulation**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the comment line
`// Mining, planting, and Tick are added in later tasks.` with:
```csharp
    public bool TryStartMining(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return false;

        m.Activity = ActivityKind.Mining;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PickaxeSeconds;
        _events.Add(new ActivityStarted(id, ActivityKind.Mining, target));
        return true;
    }

    public void Tick(double dt)
    {
        AdvanceActivities(dt);
        // Charge fuses are advanced in a later task.
    }

    private void AdvanceActivities(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.Activity == ActivityKind.None) continue;

            m.ActivitySecondsRemaining -= dt;
            if (m.ActivitySecondsRemaining > 0) continue;

            CompleteActivity(m);
        }
    }

    private void CompleteActivity(Miner m)
    {
        var kind = m.Activity;
        var target = m.ActivityTarget;
        m.Activity = ActivityKind.None;
        m.ActivitySecondsRemaining = 0;

        if (kind == ActivityKind.Mining)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
            bool wasGold = Grid.Get(target) == TileType.GoldRock;
            Grid.Set(target, TileType.Floor);
            if (wasGold) m.GoldCollected++;
            _events.Add(new RockMined(m.Id, target, wasGold));
        }
        // Planting completion handled in a later task.
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMiningTests.cs
git commit -m "feat(core): add timed pickaxe mining with gold rewards"
```

---

## Task 7: Explosives — plant, fuse, blast, death

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`

- [ ] **Step 1: Write failing explosive tests**

Create `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`:
```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationExplosiveTests
{
    private static Simulation FacingRockEast(out Miner m, SimConfig? cfg = null)
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        // wall of rock at column 2 so (2,2) is rock, miner at (1,2) faces east into it
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(2, y), TileType.Rock);
        var sim = new Simulation(grid, cfg ?? new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // blocked by rock, faces east
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Plant_starts_planting_then_creates_charge_with_fuse()
    {
        var sim = FacingRockEast(out var m, new SimConfig { PlantSeconds = 1.0, FuseSeconds = 3.0 });

        Assert.True(sim.TryStartPlanting(1));
        Assert.Equal(ActivityKind.Planting, m.Activity);

        sim.Tick(1.0); // finish planting
        Assert.Equal(ActivityKind.None, m.Activity);
        var charge = Assert.Single(sim.Charges);
        Assert.Equal(new GridPos(2, 2), charge.WallPos);
        Assert.Equal(3.0, charge.FuseRemaining);
    }

    [Fact]
    public void Cannot_plant_on_impermeable_rock()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.ImpermeableRock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // blocked by impermeable; now facing east toward (2,2)
        Assert.False(sim.TryStartPlanting(1));
        Assert.Empty(sim.Charges);
    }

    [Fact]
    public void Cannot_plant_on_floor()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East); // moves onto floor (2,2); now facing east toward floor (3,2)
        Assert.False(sim.TryStartPlanting(1));
        Assert.Empty(sim.Charges);
    }

    [Fact]
    public void Charge_cap_blocks_extra_plants()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        for (int x = 0; x < 7; x++) grid.Set(new GridPos(x, 0), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { MaxLiveChargesPerMiner = 2, PlantSeconds = 0.0 });
        var m = sim.AddMiner(1, new GridPos(1, 1));

        // Plant on (1,0)
        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.0);
        // Move to (3,1), plant on (3,0)
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.0);
        // Move to (5,1), try to plant on (5,0) -> should fail (cap 2)
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.North);
        Assert.False(sim.TryStartPlanting(1));
        Assert.Equal(2, sim.Charges.Count);
    }

    [Fact]
    public void Detonation_destroys_adjacent_rock_and_emits_explosion()
    {
        var sim = FacingRockEast(out var m, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastRockRadius = 1 });
        sim.TryStartPlanting(1);
        sim.Tick(0.0);        // create charge at (2,2)
        sim.DrainEvents();

        sim.Tick(3.0);        // fuse expires

        Assert.Empty(sim.Charges);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2))); // charged wall gone
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1))); // manhattan-1 rock gone
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 3)));
        var boom = sim.DrainEvents().OfType<Explosion>().Single();
        Assert.Equal(new GridPos(2, 2), boom.WallPos);
    }

    [Fact]
    public void Impermeable_rock_survives_blast()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Rock);
        grid.Set(new GridPos(2, 1), TileType.ImpermeableRock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 1.0, BlastRockRadius = 1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.TryStartPlanting(1); sim.Tick(0.0);

        sim.Tick(1.0);

        Assert.Equal(TileType.ImpermeableRock, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Miner_in_kill_radius_dies_but_bystander_survives()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastKillRadius = 1 });
        var planter = sim.AddMiner(1, new GridPos(2, 1)); // adjacent to (3,1) -> dies
        var bystander = sim.AddMiner(2, new GridPos(5, 1)); // far -> survives
        sim.TryMove(1, Direction.East);
        sim.TryStartPlanting(1); sim.Tick(0.0);

        sim.Tick(3.0);

        Assert.False(planter.Alive);
        Assert.True(bystander.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MinerKilled k && k.MinerId == 1);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — `TryStartPlanting` not defined and charge logic missing.

- [ ] **Step 3: Implement planting completion in CompleteActivity**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the line
`// Planting completion handled in a later task.` with:
```csharp
        else if (kind == ActivityKind.Planting)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return;
            if (LiveChargeCount(m.Id) >= Config.MaxLiveChargesPerMiner) return;
            if (_charges.Any(c => c.WallPos == target)) return;
            _charges.Add(new Charge(m.Id, target, Config.FuseSeconds));
            _events.Add(new ChargePlanted(m.Id, target));
        }
```

- [ ] **Step 4: Add TryStartPlanting and charge helpers**

In `src/Miner49er.Core/Sim/Simulation.cs`, add these methods inside the class
(e.g. directly after `TryStartMining`):
```csharp
    public bool TryStartPlanting(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;

        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsBlastable()) return false;
        if (LiveChargeCount(id) >= Config.MaxLiveChargesPerMiner) return false;
        if (_charges.Any(c => c.WallPos == target)) return false;

        m.Activity = ActivityKind.Planting;
        m.ActivityTarget = target;
        m.ActivitySecondsRemaining = Config.PlantSeconds;
        _events.Add(new ActivityStarted(id, ActivityKind.Planting, target));
        return true;
    }

    private int LiveChargeCount(int ownerId) => _charges.Count(c => c.OwnerId == ownerId);
```

- [ ] **Step 5: Advance charge fuses and detonate in Tick**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the line
`// Charge fuses are advanced in a later task.` with:
```csharp
        AdvanceCharges(dt);
```
Then add these methods inside the class:
```csharp
    private void AdvanceCharges(double dt)
    {
        // Snapshot because Detonate mutates the list.
        foreach (var charge in _charges.ToList())
        {
            charge.FuseRemaining -= dt;
            if (charge.FuseRemaining <= 0)
                Detonate(charge);
        }
    }

    private void Detonate(Charge charge)
    {
        _charges.Remove(charge);

        var destroyed = new List<GridPos>();
        int r = Config.BlastRockRadius;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPos(charge.WallPos.X + dx, charge.WallPos.Y + dy);
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p) || !Grid.Get(p).IsBlastable()) continue;
                bool wasGold = Grid.Get(p) == TileType.GoldRock;
                Grid.Set(p, TileType.Floor);
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                }
                destroyed.Add(p);
            }

        foreach (var m in _miners.Values)
        {
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius)
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerKilled(m.Id));
            }
        }

        _events.Add(new Explosion(charge.WallPos, destroyed));
    }
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test`
Expected: PASS (all explosive tests green).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationExplosiveTests.cs
git commit -m "feat(core): add explosives with fuse, blast, and proximity death"
```

---

## Task 8: Fog of war — visibility + explored memory

**Files:**
- Create: `src/Miner49er.Core/Fog/Visibility.cs`
- Create: `src/Miner49er.Core/Fog/FogState.cs`
- Test: `src/Miner49er.Core.Tests/VisibilityTests.cs`

Phase 1 uses simple radial visibility (no line-of-sight occlusion; shadowcasting is a later enhancement noted in the spec).

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/VisibilityTests.cs`:
```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class VisibilityTests
{
    [Fact]
    public void Visible_set_is_a_radius_disc_clipped_to_bounds()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 2);

        Assert.Contains(new GridPos(5, 5), visible);
        Assert.Contains(new GridPos(7, 5), visible);     // distance 2 on axis
        Assert.DoesNotContain(new GridPos(8, 5), visible); // distance 3
        Assert.DoesNotContain(new GridPos(7, 7), visible); // euclidean > 2
    }

    [Fact]
    public void Visible_set_clips_at_grid_edges()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(0, 0), radius: 3);
        Assert.All(visible, p => Assert.True(grid.InBounds(p)));
    }

    [Fact]
    public void FogState_accumulates_explored_across_updates()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var fog = new FogState();

        fog.Update(Visibility.Compute(grid, new GridPos(2, 2), 1));
        Assert.True(fog.IsExplored(new GridPos(2, 2)));
        Assert.True(fog.IsVisible(new GridPos(2, 2)));

        fog.Update(Visibility.Compute(grid, new GridPos(6, 6), 1));
        Assert.True(fog.IsExplored(new GridPos(2, 2)));   // remembered
        Assert.False(fog.IsVisible(new GridPos(2, 2)));   // no longer in view
        Assert.True(fog.IsVisible(new GridPos(6, 6)));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test`
Expected: FAIL — `Visibility` / `FogState` not defined.

- [ ] **Step 3: Implement Visibility**

Create `src/Miner49er.Core/Fog/Visibility.cs`:
```csharp
namespace Miner49er.Core;

public static class Visibility
{
    public static HashSet<GridPos> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var set = new HashSet<GridPos>();
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                var p = new GridPos(origin.X + dx, origin.Y + dy);
                if (grid.InBounds(p)) set.Add(p);
            }
        return set;
    }
}
```

- [ ] **Step 4: Implement FogState**

Create `src/Miner49er.Core/Fog/FogState.cs`:
```csharp
namespace Miner49er.Core;

public sealed class FogState
{
    private readonly HashSet<GridPos> _explored = new();
    public HashSet<GridPos> Visible { get; private set; } = new();
    public IReadOnlySet<GridPos> Explored => _explored;

    public void Update(HashSet<GridPos> visible)
    {
        Visible = visible;
        foreach (var p in visible) _explored.Add(p);
    }

    public bool IsVisible(GridPos p) => Visible.Contains(p);
    public bool IsExplored(GridPos p) => _explored.Contains(p);
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test`
Expected: PASS. Run the full suite to confirm nothing regressed: `dotnet test` should show all tests across all files green.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Fog src/Miner49er.Core.Tests/VisibilityTests.cs
git commit -m "feat(core): add radial visibility and explored-memory fog"
```

---

## Task 9: Godot project setup wired to Core

> Godot 4.4 (.NET) required from here on. These tasks are verified by **running the editor** rather than xUnit, since they exercise rendering/input.

**Files:**
- Create: `project.godot`
- Create: `Miner49er.csproj`
- Create: `icon.svg` (placeholder; any small SVG)
- Modify: `Miner49er.sln` (add the Godot game project)
- Create: `game/.gitkeep` (placeholder so the folder exists)

- [ ] **Step 1: Create the Godot project file**

Create `project.godot`:
```ini
config_version=5

[application]
config/name="Miner49er"
run/main_scene="res://game/Main.tscn"
config/features=PackedStringArray("4.4", "C#", "Forward Plus")

[dotnet]
project/assembly_name="Miner49er"

[display]
window/size/viewport_width=1280
window/size/viewport_height=720
```

- [ ] **Step 2: Create the Godot game csproj referencing Core**

Create `Miner49er.csproj`:
```xml
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
    <RootNamespace>Miner49er</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="src/Miner49er.Core/Miner49er.Core.csproj" />
  </ItemGroup>
</Project>
```
> If your installed Godot reports a different .NET SDK version (Project menu → about, or the editor console on first build), change `Godot.NET.Sdk/4.4.0` to match.

- [ ] **Step 3: Add the game project to the solution**

Run from repo root:
```bash
dotnet sln Miner49er.sln add Miner49er.csproj
```

- [ ] **Step 4: Create a placeholder icon and game folder**

Create `icon.svg`:
```svg
<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" fill="#8a5a2b"/></svg>
```
Create empty file `game/.gitkeep` (so the directory is tracked before scripts exist).

- [ ] **Step 5: Open the project in Godot to generate build artifacts**

Open the Godot editor, "Import" the project at `D:\Projects\Miner49er`, let it open. It will create `.godot/` and build the C# solution. Confirm the editor opens with **no build errors** in the Output panel (the `Main.tscn` missing-scene warning is expected — we create it next).

- [ ] **Step 6: Commit**

```bash
git add project.godot Miner49er.csproj icon.svg Miner49er.sln game/.gitkeep
git commit -m "chore: add Godot 4 project referencing Core"
```

---

## Task 10: Input bindings + Main bootstrap (walking miner)

**Files:**
- Create: `game/InputBindings.cs`
- Create: `game/Main.cs`
- Create: `game/Main.tscn`

`Main` owns the simulation, the local miner, the camera, and renders the player as a simple shape. World/fog rendering come in Tasks 11–12.

- [ ] **Step 1: Create InputBindings (code-registered actions, no project.godot editing)**

Create `game/InputBindings.cs`:
```csharp
using Godot;

namespace Miner49er;

/// <summary>
/// Registers default keyboard + gamepad actions at startup. Phase 5 replaces
/// this with a persisted, user-editable rebinding system; until then this keeps
/// bindings in code so they always exist.
/// </summary>
public static class InputBindings
{
    public const string MoveUp = "move_up";
    public const string MoveDown = "move_down";
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string Pickaxe = "pickaxe";
    public const string Plant = "plant_explosive";
    public const string Listen = "listen";       // defined now, used in Phase 3
    public const string UseItem = "use_item";     // defined now, used in Phase 4
    public const string Restart = "restart";

    public static void EnsureDefaults()
    {
        Bind(MoveUp, Key.W, JoyButton.DpadUp);
        Bind(MoveDown, Key.S, JoyButton.DpadDown);
        Bind(MoveLeft, Key.A, JoyButton.DpadLeft);
        Bind(MoveRight, Key.D, JoyButton.DpadRight);
        Bind(Pickaxe, Key.J, JoyButton.X);
        Bind(Plant, Key.K, JoyButton.A);
        Bind(Listen, Key.L, JoyButton.B);
        Bind(UseItem, Key.Space, JoyButton.Y);
        Bind(Restart, Key.R, JoyButton.Start);
    }

    private static void Bind(string action, Key key, JoyButton button)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = button });
    }
}
```

- [ ] **Step 2: Create Main.cs (sim + movement + camera)**

Create `game/Main.cs`:
```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

public partial class Main : Node2D
{
    public const int TileSize = 32;
    public const float MoveTime = 0.12f; // seconds per tile step
    public const int VisionRadius = 5;
    private const int LocalId = 1;

    private Simulation _sim = null!;
    private Miner _local = null!;
    private readonly FogState _fog = new();

    private Node2D _playerVisual = null!;
    private Camera2D _camera = null!;

    private bool _isMoving;
    private float _moveT;
    private Vector2 _moveFrom;
    private Vector2 _moveTo;

    public override void _Ready()
    {
        InputBindings.EnsureDefaults();
        StartNewGame(seed: 12345);
    }

    private void StartNewGame(int seed)
    {
        var map = MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = 1 });
        _sim = new Simulation(map.Grid, new SimConfig());
        _local = _sim.AddMiner(LocalId, map.Spawns[0]);
        _isMoving = false;

        // Persistent nodes are created once; restart only resets sim + position.
        if (_playerVisual == null)
        {
            _playerVisual = BuildPlayerVisual();
            AddChild(_playerVisual);
            _camera = new Camera2D { Zoom = new Vector2(1.5f, 1.5f) };
            _playerVisual.AddChild(_camera);
            _camera.MakeCurrent();
        }
        _playerVisual.Position = ToPixelCenter(_local.Pos);

        UpdateFog();
    }

    private static Node2D BuildPlayerVisual()
    {
        var root = new Node2D { Name = "PlayerVisual" };
        var body = new Polygon2D
        {
            Color = new Color("e8c34a"),
            Polygon = new[]
            {
                new Vector2(-10, -10), new Vector2(10, -10),
                new Vector2(10, 10), new Vector2(-10, 10),
            },
        };
        root.AddChild(body);
        return root;
    }

    private static Vector2 ToPixelCenter(GridPos p) =>
        new(p.X * TileSize + TileSize / 2f, p.Y * TileSize + TileSize / 2f);

    public override void _PhysicsProcess(double delta)
    {
        HandleActions();
        HandleMovement(delta);
        _sim.Tick(delta);
        _sim.DrainEvents(); // events consumed in later tasks (SFX/FX)
    }

    private void HandleActions()
    {
        if (Input.IsActionJustPressed(InputBindings.Pickaxe)) _sim.TryStartMining(LocalId);
        if (Input.IsActionJustPressed(InputBindings.Plant)) _sim.TryStartPlanting(LocalId);
        if (Input.IsActionJustPressed(InputBindings.Restart)) StartNewGame(12345);
    }

    private void HandleMovement(double delta)
    {
        if (!_isMoving && _local.Alive)
        {
            Direction? dir = ReadDirection();
            if (dir.HasValue)
            {
                var before = _local.Pos;
                if (_sim.TryMove(LocalId, dir.Value))
                {
                    _moveFrom = ToPixelCenter(before);
                    _moveTo = ToPixelCenter(_local.Pos);
                    _moveT = 0f;
                    _isMoving = true;
                    UpdateFog();
                }
            }
        }

        if (_isMoving)
        {
            _moveT += (float)delta / MoveTime;
            if (_moveT >= 1f) { _moveT = 1f; _isMoving = false; }
            _playerVisual.Position = _moveFrom.Lerp(_moveTo, _moveT);
        }
    }

    private static Direction? ReadDirection()
    {
        if (Input.IsActionPressed(InputBindings.MoveUp)) return Direction.North;
        if (Input.IsActionPressed(InputBindings.MoveDown)) return Direction.South;
        if (Input.IsActionPressed(InputBindings.MoveLeft)) return Direction.West;
        if (Input.IsActionPressed(InputBindings.MoveRight)) return Direction.East;
        return null;
    }

    private void UpdateFog()
    {
        _fog.Update(Visibility.Compute(_sim.Grid, _local.Pos, VisionRadius));
    }

    public Simulation Sim => _sim;
    public Miner Local => _local;
    public FogState Fog => _fog;
}
```

- [ ] **Step 3: Create the Main scene**

Create `game/Main.tscn`:
```ini
[gd_scene load_steps=2 format=3 uid="uid://miner49ermain"]

[ext_resource type="Script" path="res://game/Main.cs" id="1"]

[node name="Main" type="Node2D"]
script = ExtResource("1")
```

- [ ] **Step 4: Run and verify in the editor**

Run the project (F5). Expected:
- A yellow square (the miner) appears centered.
- W/A/S/D (or D-pad) moves it one tile at a time with a smooth slide.
- Movement stops at rock (you can't see rock yet — that's Task 11 — but the miner will refuse to enter some tiles).
- Pressing R restarts.

If movement feels too fast/slow, tune `MoveTime`. Camera should follow the miner.

- [ ] **Step 5: Commit**

```bash
git add game/InputBindings.cs game/Main.cs game/Main.tscn
git commit -m "feat(game): bootstrap simulation with walking miner and camera"
```

---

## Task 11: World rendering (tiles, charges, activity progress)

**Files:**
- Create: `game/WorldRenderer.cs`
- Modify: `game/Main.cs` (instantiate the renderer; feed it events for FX)

- [ ] **Step 1: Create WorldRenderer**

Create `game/WorldRenderer.cs`:
```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Draws the tile grid, charges, and explosion flashes with simple
/// colored rectangles. Placeholder art for Phase 1.</summary>
public partial class WorldRenderer : Node2D
{
    private Main _main = null!;
    private readonly System.Collections.Generic.List<(GridPos pos, float life)> _flashes = new();

    private static readonly Color FloorColor = new("2b2b33");
    private static readonly Color RockColor = new("5a4a3a");
    private static readonly Color GoldColor = new("c9a227");
    private static readonly Color ImpermeableColor = new("20242b");
    private static readonly Color ChargeColor = new("ff5530");
    private static readonly Color FlashColor = new("ffd27f");

    public void Init(Main main) => _main = main;

    public void AddExplosionFlash(GridPos pos) => _flashes.Add((pos, 0.4f));

    public override void _Process(double delta)
    {
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            var f = _flashes[i];
            f.life -= (float)delta;
            if (f.life <= 0) _flashes.RemoveAt(i);
            else _flashes[i] = f;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_main == null) return;
        var grid = _main.Sim.Grid;
        int ts = Main.TileSize;

        foreach (var p in grid.Positions())
        {
            var color = grid.Get(p) switch
            {
                TileType.Floor => FloorColor,
                TileType.Rock => RockColor,
                TileType.GoldRock => GoldColor,
                TileType.ImpermeableRock => ImpermeableColor,
                _ => FloorColor,
            };
            DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
        }

        foreach (var c in _main.Sim.Charges)
        {
            var center = new Vector2(c.WallPos.X * ts + ts / 2f, c.WallPos.Y * ts + ts / 2f);
            DrawCircle(center, ts * 0.25f, ChargeColor);
        }

        foreach (var (pos, life) in _flashes)
        {
            var col = FlashColor with { A = Mathf.Clamp(life / 0.4f, 0f, 1f) };
            DrawRect(new Rect2(pos.X * ts, pos.Y * ts, ts, ts), col);
        }
    }
}
```

- [ ] **Step 2: Wire WorldRenderer into Main and consume events**

In `game/Main.cs`, add a field near the other node fields:
```csharp
    private WorldRenderer _world = null!;
```
In `StartNewGame`, after creating the simulation and before building the player visual, add:
```csharp
        if (_world == null)
        {
            _world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -10 };
            AddChild(_world);
            _world.Init(this);
        }
```
Replace the line `_sim.DrainEvents(); // events consumed in later tasks (SFX/FX)` in `_PhysicsProcess` with:
```csharp
        ConsumeEvents();
```
Add this method to `Main`:
```csharp
    private void ConsumeEvents()
    {
        foreach (var e in _sim.DrainEvents())
        {
            switch (e)
            {
                case Explosion ex:
                    _world.AddExplosionFlash(ex.WallPos);
                    foreach (var d in ex.DestroyedRock) _world.AddExplosionFlash(d);
                    UpdateFog(); // blasting reveals new space
                    break;
                case RockMined:
                    UpdateFog(); // digging reveals new space
                    break;
                case MinerKilled k when k.MinerId == LocalId:
                    // Phase 1: dying just restarts after the flash settles.
                    break;
            }
        }
    }
```

- [ ] **Step 3: Run and verify**

Run (F5). Expected:
- The mine renders: dark floor, brown rock, gold-colored gold veins, near-black impermeable border.
- The miner walks only on floor; rock blocks it.
- Face a rock and press J (pickaxe): after ~6s the rock turns to floor (gold veins give a brief gold count later; for now the tile clears).
- Face a rock and press K: after ~1s a red charge dot appears; ~3s later it flashes and clears a plus-shaped area. If you stand adjacent when it blows, you'll stop being able to move (dead) until you press R.

- [ ] **Step 4: Commit**

```bash
git add game/WorldRenderer.cs game/Main.cs
git commit -m "feat(game): render tiles, charges, and explosion flashes"
```

---

## Task 12: Fog-of-war rendering

**Files:**
- Create: `game/FogRenderer.cs`
- Modify: `game/Main.cs` (instantiate fog renderer on top)

- [ ] **Step 1: Create FogRenderer**

Create `game/FogRenderer.cs`:
```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness: unexplored = opaque black, explored-but-not-visible
/// = dim, currently visible = clear.</summary>
public partial class FogRenderer : Node2D
{
    private Main _main = null!;
    private static readonly Color Unexplored = new(0, 0, 0, 1f);
    private static readonly Color Dim = new(0, 0, 0, 0.6f);

    public void Init(Main main) => _main = main;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_main == null) return;
        var grid = _main.Sim.Grid;
        var fog = _main.Fog;
        int ts = Main.TileSize;

        foreach (var p in grid.Positions())
        {
            if (fog.IsVisible(p)) continue; // clear
            var color = fog.IsExplored(p) ? Dim : Unexplored;
            DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
        }
    }
}
```

- [ ] **Step 2: Wire FogRenderer into Main**

In `game/Main.cs`, add a field:
```csharp
    private FogRenderer _fogRenderer = null!;
```
In `StartNewGame`, after the player visual is added (so fog draws above the world but the player draws above fog — see ZIndex), add:
```csharp
        if (_fogRenderer == null)
        {
            _fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
            AddChild(_fogRenderer);
            _fogRenderer.Init(this);
        }
```
Ensure the player draws above fog: in `BuildPlayerVisual`, set the root's ZIndex by changing its declaration to:
```csharp
        var root = new Node2D { Name = "PlayerVisual", ZIndex = 10 };
```

- [ ] **Step 3: Run and verify**

Run (F5). Expected:
- Only a ~5-tile radius around the miner is fully lit.
- Tiles you've already walked past stay dimly visible (remembered) but darker.
- Unexplored areas are solid black.
- Mining/blasting opens new lit area as you reveal it; the camera-followed miner stays the bright center.

- [ ] **Step 4: Commit**

```bash
git add game/FogRenderer.cs game/Main.cs
git commit -m "feat(game): add fog-of-war overlay with explored memory"
```

---

## Task 13: HUD (gold + activity progress) and round-restart polish

**Files:**
- Create: `game/Hud.cs`
- Modify: `game/Main.cs`

- [ ] **Step 1: Create the HUD**

Create `game/Hud.cs`:
```csharp
using Godot;

namespace Miner49er;

public partial class Hud : CanvasLayer
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = new Label
        {
            Position = new Vector2(16, 12),
            Theme = null,
        };
        _label.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_label);
    }

    public void SetText(string text) => _label.Text = text;
}
```

- [ ] **Step 2: Wire HUD into Main**

In `game/Main.cs`, add a field:
```csharp
    private Hud _hud = null!;
```
In `StartNewGame`, after fog renderer setup, add:
```csharp
        if (_hud == null)
        {
            _hud = new Hud { Name = "Hud" };
            AddChild(_hud);
        }
```
At the end of `_PhysicsProcess`, add a HUD refresh:
```csharp
        _hud.SetText(BuildHudText());
```
Add this method to `Main`:
```csharp
    private string BuildHudText()
    {
        string status = _local.Alive
            ? _local.Activity switch
            {
                ActivityKind.Mining => $"Mining… {_local.ActivitySecondsRemaining:0.0}s",
                ActivityKind.Planting => $"Planting… {_local.ActivitySecondsRemaining:0.0}s",
                _ => "Ready",
            }
            : "Dead — press R to restart";
        return $"Gold: {_local.GoldCollected}    {status}";
    }
```

- [ ] **Step 3: Run and verify the full Phase 1 loop**

Run (F5). Verify end-to-end:
- HUD shows `Gold: 0   Ready`.
- Mine a gold vein → `Gold` increments by 1 when the gold tile clears (via pickaxe or blast).
- While mining/planting, the HUD counts down.
- Stand next to a charge when it detonates → HUD shows `Dead — press R to restart`; R starts a fresh map and resets gold.
- Fog, camera, movement, blasting all behave together.

- [ ] **Step 4: Commit**

```bash
git add game/Hud.cs game/Main.cs
git commit -m "feat(game): add HUD for gold and activity, finalize Phase 1 loop"
```

---

## Final verification

- [ ] Run `dotnet test` from repo root → **all Core tests pass**.
- [ ] Run the Godot project (F5) and confirm the full loop from Task 13 Step 3.
- [ ] Confirm `git status` is clean and all 13 tasks are committed.

---

## Spec coverage check (Phase 1 scope only)

| Spec Phase 1 item | Covered by |
|---|---|
| Tile grid | Task 3 |
| Grid movement + facing | Task 5, Task 10 |
| Pickaxe (~6s, tunable, interruptible) | Task 6 |
| Plant/blast with proximity death | Task 7 |
| Charge cap | Task 7 |
| Procedural map gen (seeded, scaled, connected, gold) | Task 4 |
| Placeholder art | Tasks 11–13 (colored rects) |
| Fog of war (visible/explored/unexplored) | Task 8 (logic), Task 12 (render) |
| Rebindable input foundation (keyboard + gamepad) | Task 10 (`InputBindings`; full UI is Phase 5) |

**Deferred to later phases (intentionally not in this plan):** networking and multiple players (Phase 2), listen mechanic and audio (Phase 3), water/cave-ins/time-pressure/items/secondary-goal modes (Phase 4), rebinding UI, settings persistence, custom-sprite editor, visibility culling, NAT/relay (Phase 5).

## Notes for the implementer

- **Determinism:** `Simulation` advances only via `Tick(double dt)` plus explicit input methods — no wall-clock reads — so it stays testable and (later) network-replayable.
- **No Godot types in Core:** keep `src/Miner49er.Core` free of any `using Godot;`. If you ever need a vector there, use `GridPos`. This boundary is what keeps the logic unit-testable and the engine swappable.
- **Tuning lives in `SimConfig`/`MapConfig`:** pickaxe time, fuse, blast radii, charge cap, map size, gold count. These become the host-side settings surface in later phases.
