# Scree / Rockfall Hazard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three tiers of unstable rock (ScreeRock, UnstableRock, VolatileRock) that trigger rockslide collapses when mined or blasted, visible only in Listen mode with amber/red colour coding.

**Architecture:** New tile types append to `TileType` enum (safe for network serialisation). The `Simulation` handles collapse probability and tile-fill logic. `MatchHost` forwards the new `ScreeCollapsed` event via a new `WorldSnapshot` field that all clients decode and use for audio/visual feedback. `WorldRenderer` adds shimmer overlays in Listen mode.

**Tech Stack:** C# / .NET 8, Godot 4, xUnit

## Global Constraints

- `TileType` enum values must be appended at the END (current last: `CrystalRock = 12`); never insert or reorder
- Run tests with: `dotnet test src/Miner49er.Core.Tests/ -v q`
- Run game via PowerShell only (`godot`), never via Bash
- Never stage `.superpowers/`, `*.uid`, `Temp/`, or `_preview_*` files; never `git add -A`
- No new dependencies; game project is Godot-only, core project is engine-free

---

## Task 1: Tile Types, Extension Methods, and SimEvent

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Create: `src/Miner49er.Core.Tests/TileTypeScreeTests.cs`

**Interfaces:**
- Produces: `TileType.ScreeRock` (13), `TileType.UnstableRock` (14), `TileType.VolatileRock` (15)
- Produces: `TileTypeExtensions.IsScree(this TileType t) => bool`
- Produces: `TileTypeExtensions.ScreeCollapseRadius(this TileType t) => int`
- Produces: `TileTypeExtensions.ScreeTriggerChance(this TileType t) => double`
- Produces: `SimEvent ScreeCollapsed(GridPos Pos, int Radius)`

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/TileTypeScreeTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TileTypeScreeTests
{
    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_minable(TileType t) => Assert.True(t.IsMinable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_blastable(TileType t) => Assert.True(t.IsBlastable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_block_sight(TileType t) => Assert.True(t.BlocksSight());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_not_walkable(TileType t) => Assert.False(t.IsWalkable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_not_enterable(TileType t) => Assert.False(t.IsEnterable());

    [Fact]
    public void IsScree_returns_true_for_all_scree_types()
    {
        Assert.True(TileType.ScreeRock.IsScree());
        Assert.True(TileType.UnstableRock.IsScree());
        Assert.True(TileType.VolatileRock.IsScree());
    }

    [Fact]
    public void IsScree_returns_false_for_non_scree()
    {
        Assert.False(TileType.Rock.IsScree());
        Assert.False(TileType.Floor.IsScree());
        Assert.False(TileType.CrystalRock.IsScree());
    }

    [Fact]
    public void ScreeRock_has_radius_1() => Assert.Equal(1, TileType.ScreeRock.ScreeCollapseRadius());

    [Fact]
    public void UnstableRock_has_radius_1() => Assert.Equal(1, TileType.UnstableRock.ScreeCollapseRadius());

    [Fact]
    public void VolatileRock_has_radius_2() => Assert.Equal(2, TileType.VolatileRock.ScreeCollapseRadius());

    [Fact]
    public void ScreeRock_trigger_chance_is_50_percent() =>
        Assert.Equal(0.5, TileType.ScreeRock.ScreeTriggerChance());

    [Fact]
    public void UnstableRock_trigger_chance_is_100_percent() =>
        Assert.Equal(1.0, TileType.UnstableRock.ScreeTriggerChance());

    [Fact]
    public void VolatileRock_trigger_chance_is_100_percent() =>
        Assert.Equal(1.0, TileType.VolatileRock.ScreeTriggerChance());
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "TileTypeScreeTests" -v q
```

Expected: all tests FAIL (enum values don't exist yet)

- [ ] **Step 3: Add enum values to TileType**

In `src/Miner49er.Core/Grid/TileType.cs`, replace the enum line:

```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit, Cracked, Crumbling, Lava, LavaVent, CrystalRock, ScreeRock, UnstableRock, VolatileRock }
```

- [ ] **Step 4: Extend IsMinable, IsBlastable, BlocksSight**

In `TileTypeExtensions`, update the three methods to include all three scree types:

```csharp
public static bool IsMinable(this TileType t) =>
    t is TileType.Rock or TileType.GoldRock or TileType.CrystalRock
      or TileType.ScreeRock or TileType.UnstableRock or TileType.VolatileRock;

public static bool IsBlastable(this TileType t) =>
    t is TileType.Rock or TileType.GoldRock or TileType.CrystalRock
      or TileType.ScreeRock or TileType.UnstableRock or TileType.VolatileRock;

public static bool BlocksSight(this TileType t) =>
    t is TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock or TileType.CrystalRock
      or TileType.ScreeRock or TileType.UnstableRock or TileType.VolatileRock;
```

- [ ] **Step 5: Add IsScree, ScreeCollapseRadius, ScreeTriggerChance extension methods**

Append to `TileTypeExtensions` in `TileType.cs`:

```csharp
public static bool IsScree(this TileType t) =>
    t is TileType.ScreeRock or TileType.UnstableRock or TileType.VolatileRock;

public static int ScreeCollapseRadius(this TileType t) =>
    t == TileType.VolatileRock ? 2 : 1;

public static double ScreeTriggerChance(this TileType t) =>
    t == TileType.ScreeRock ? 0.5 : 1.0;
```

- [ ] **Step 6: Add ScreeCollapsed SimEvent**

In `src/Miner49er.Core/Sim/SimEvent.cs`, append after the last line:

```csharp
public sealed record ScreeCollapsed(GridPos Pos, int Radius) : SimEvent;
```

- [ ] **Step 7: Run tests to confirm they pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "TileTypeScreeTests" -v q
```

Expected: all 14 tests PASS

- [ ] **Step 8: Run full suite to confirm no regressions**

```
dotnet test src/Miner49er.Core.Tests/ -v q
```

Expected: all existing tests still PASS

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core.Tests/TileTypeScreeTests.cs
git commit -m "feat(scree): add ScreeRock/UnstableRock/VolatileRock tile types and ScreeCollapsed event"
```

---

## Task 2: Simulation Collapse Logic

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationScreeTests.cs`

**Interfaces:**
- Consumes: `TileType.IsScree()`, `TileType.ScreeCollapseRadius()`, `TileType.ScreeTriggerChance()` (Task 1)
- Consumes: `SimEvent.ScreeCollapsed(Pos, Radius)`, `SimEvent.RockFell(Pos)`, `SimEvent.MinerCrushed(MinerId)` (existing + Task 1)

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/SimulationScreeTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationScreeTests
{
    // Helper: 5x5 grid, scree tile at (2,2), miner at (1,2) facing East
    private static Simulation SetupScree(TileType screeType, int seed = 0)
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), screeType);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1, Seed = seed });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Mining_ScreeRock_converts_tile_to_Floor()
    {
        // seed=1 gives NextDouble()>=0.5, so collapse does NOT trigger; tile still becomes Floor
        // Use a seed where ScreeRock does NOT trigger to test the tile-set-to-Floor path cleanly
        // seed=0: _rng draws first for spawn offset. We fix the result by checking tile type only.
        var sim = SetupScree(TileType.ScreeRock, seed: 99);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Regardless of whether collapse fires, the mined tile is always Floor
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2)));
    }

    [Fact]
    public void Mining_UnstableRock_converts_tile_to_Floor()
    {
        var sim = SetupScree(TileType.UnstableRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2)));
    }

    [Fact]
    public void Mining_UnstableRock_always_emits_ScreeCollapsed()
    {
        var sim = SetupScree(TileType.UnstableRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Pos == new GridPos(2, 2) && sc.Radius == 1);
    }

    [Fact]
    public void Mining_VolatileRock_emits_ScreeCollapsed_with_radius_2()
    {
        var sim = SetupScree(TileType.VolatileRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Radius == 2);
    }

    [Fact]
    public void Mining_UnstableRock_fills_adjacent_floor_tiles_with_Rock()
    {
        var sim = SetupScree(TileType.UnstableRock, seed: 0);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Chebyshev radius 1 around (2,2): (1,1),(2,1),(3,1),(1,2),(3,2),(1,3),(2,3),(3,3)
        // (2,2) itself is now Floor (mined). The adjacent floor tiles become Rock.
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(3, 2)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 3)));
    }

    [Fact]
    public void Mining_UnstableRock_emits_RockFell_for_each_filled_tile()
    {
        var sim = SetupScree(TileType.UnstableRock, seed: 0);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        var rockFells = events.OfType<RockFell>().ToList();
        Assert.True(rockFells.Count >= 1, "Expected at least one RockFell event");
    }

    [Fact]
    public void Miner_in_collapse_zone_is_crushed()
    {
        // Place a second miner inside the 3x3 zone
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2)); // will mine
        sim.AddMiner(2, new GridPos(3, 2)); // in zone
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var m2 = sim.GetMiner(2);
        Assert.False(m2.Alive);
        Assert.Equal(DeathCause.Crushed, m2.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerCrushed mc && mc.MinerId == 2);
    }

    [Fact]
    public void Miner_outside_collapse_zone_survives()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(2, 3)); // will mine
        sim.AddMiner(2, new GridPos(6, 3)); // far away, outside radius 1
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.True(sim.GetMiner(2).Alive);
    }

    [Fact]
    public void VolatileRock_fills_radius_2_zone()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.VolatileRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Chebyshev radius 2 around (3,3): tiles at (5,3) should be Rock
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(5, 3)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(3, 5)));
    }

    [Fact]
    public void Collapse_does_not_convert_non_Floor_tiles()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        grid.Set(new GridPos(3, 2), TileType.ImpermeableRock); // already rock, stays
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // ImpermeableRock should not be changed
        Assert.Equal(TileType.ImpermeableRock, sim.Grid.Get(new GridPos(3, 2)));
    }

    [Fact]
    public void Blasting_UnstableRock_triggers_collapse()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig
        {
            PlantSeconds = 0.1, FuseSeconds = 0.5,
            BlastRockRadius = 1, BlastKillRadius = 0,
            PickaxeSeconds = 0.1
        });
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartPlanting(1);
        sim.Tick(0.1);
        sim.DrainEvents();
        sim.Tick(0.5);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Pos == new GridPos(3, 3));
    }

    [Fact]
    public void ScreeRock_does_not_always_collapse()
    {
        // Run many seeds; verify at least some don't collapse (ScreeRock is probabilistic)
        int collapseCount = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            var sim = SetupScree(TileType.ScreeRock, seed);
            sim.TryStartMining(1);
            sim.Tick(0.1);
            var events = sim.DrainEvents();
            if (events.Any(e => e is ScreeCollapsed)) collapseCount++;
        }
        Assert.True(collapseCount > 0 && collapseCount < 20,
            $"Expected ~50% collapse rate, got {collapseCount}/20");
    }

    [Fact]
    public void Invulnerable_miner_in_zone_is_not_crushed()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2));
        var m2 = sim.AddMiner(2, new GridPos(3, 2));
        m2.InvulnerableRemaining = 5.0;
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.True(m2.Alive);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "SimulationScreeTests" -v q
```

Expected: all tests FAIL

- [ ] **Step 3: Implement TriggerScreeCollapse in Simulation.cs**

Add a private method to `Simulation.cs` (before `CollapseKill` around line 1198 is a good spot):

```csharp
private void TriggerScreeCollapse(GridPos pos, TileType screeType)
{
    if (_rng.NextDouble() >= screeType.ScreeTriggerChance()) return;

    int radius = screeType.ScreeCollapseRadius();
    for (int dy = -radius; dy <= radius; dy++)
    for (int dx = -radius; dx <= radius; dx++)
    {
        var p = new GridPos(pos.X + dx, pos.Y + dy);
        if (!Grid.InBounds(p) || Grid.Get(p) != TileType.Floor) continue;
        Grid.Set(p, TileType.Rock);
        _events.Add(new RockFell(p));
    }
    foreach (var m in _miners.Values)
    {
        if (!m.Alive || m.InvulnerableRemaining > 0) continue;
        if (Math.Max(Math.Abs(m.Pos.X - pos.X), Math.Abs(m.Pos.Y - pos.Y)) <= radius)
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            m.DeathCause = DeathCause.Crushed;
            _events.Add(new MinerCrushed(m.Id));
        }
    }
    _events.Add(new ScreeCollapsed(pos, radius));
}
```

- [ ] **Step 4: Hook TriggerScreeCollapse into mining (CompleteActivity)**

In `CompleteActivity`, find the mining section (around line 1341). It currently reads:

```csharp
if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
bool wasGold    = Grid.Get(target) == TileType.GoldRock;
bool wasCrystal = Grid.Get(target) == TileType.CrystalRock;
Grid.Set(target, TileType.Floor);
if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
UnburyItemsAt(target);
ActivateVentsAround(target);
_events.Add(new RockMined(m.Id, target, wasGold));
if (wasCrystal) _events.Add(new CrystalShardDropped(target));
```

Change to:

```csharp
if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
var targetTile  = Grid.Get(target);
bool wasGold    = targetTile == TileType.GoldRock;
bool wasCrystal = targetTile == TileType.CrystalRock;
Grid.Set(target, TileType.Floor);
if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
UnburyItemsAt(target);
ActivateVentsAround(target);
_events.Add(new RockMined(m.Id, target, wasGold));
if (wasCrystal) _events.Add(new CrystalShardDropped(target));
if (targetTile.IsScree()) TriggerScreeCollapse(target, targetTile);
```

- [ ] **Step 5: Hook TriggerScreeCollapse into blasting (DetonateAt)**

In `DetonateAt`, find the per-tile blast section (around line 1273). It currently reads:

```csharp
if (!Grid.Get(p).IsBlastable()) continue;
bool wasGold    = Grid.Get(p) == TileType.GoldRock;
bool wasCrystal = Grid.Get(p) == TileType.CrystalRock;
Grid.Set(p, TileType.Floor);
if (wasGold)
{
    if (_miners.TryGetValue(ownerId, out var owner) && owner.Alive) owner.GoldCollected++;
    OnGoldCleared();
}
UnburyItemsAt(p);
ActivateVentsAround(p);
if (wasCrystal) _events.Add(new CrystalShardDropped(p));
destroyed.Add(p);
```

Change to:

```csharp
var blastTile = Grid.Get(p);
if (!blastTile.IsBlastable()) continue;
bool wasGold    = blastTile == TileType.GoldRock;
bool wasCrystal = blastTile == TileType.CrystalRock;
Grid.Set(p, TileType.Floor);
if (wasGold)
{
    if (_miners.TryGetValue(ownerId, out var owner) && owner.Alive) owner.GoldCollected++;
    OnGoldCleared();
}
UnburyItemsAt(p);
ActivateVentsAround(p);
if (wasCrystal) _events.Add(new CrystalShardDropped(p));
if (blastTile.IsScree()) TriggerScreeCollapse(p, blastTile);
destroyed.Add(p);
```

- [ ] **Step 6: Run tests to confirm they pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "SimulationScreeTests" -v q
```

Expected: all 12 tests PASS

- [ ] **Step 7: Run full suite to confirm no regressions**

```
dotnet test src/Miner49er.Core.Tests/ -v q
```

Expected: all tests PASS

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationScreeTests.cs
git commit -m "feat(scree): simulation collapse logic — fill + crush on mine/blast"
```

---

## Task 3: Map Generation

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Create: `src/Miner49er.Core.Tests/MapGeneratorScreeTests.cs`
- Modify: `src/Miner49er.Core.Tests/MapConfigFloorTests.cs`

**Interfaces:**
- Consumes: `TileType.ScreeRock`, `TileType.UnstableRock`, `TileType.VolatileRock` (Task 1)
- Produces: `MapConfig.ScreePatchCount`, `MapConfig.UnstableRockCount`, `MapConfig.VolatileRockCount`
- Produces: `MapGenerator.PlaceScreePatches(grid, rng, screeCount, unstableCount, volatileCount)`

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/MapGeneratorScreeTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorScreeTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    [Fact]
    public void No_scree_placed_when_all_counts_are_zero()
    {
        var cfg = new MapConfig { Seed = 1, ScreePatchCount = 0, UnstableRockCount = 0, VolatileRockCount = 0 };
        var map = MapGenerator.Generate(cfg);
        Assert.DoesNotContain(map.Grid.Positions(), p =>
            map.Grid.Get(p) == TileType.ScreeRock ||
            map.Grid.Get(p) == TileType.UnstableRock ||
            map.Grid.Get(p) == TileType.VolatileRock);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void ScreePatchCount_positive_places_ScreeRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, ScreePatchCount = 3 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.ScreeRock);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(7)]
    public void UnstableRockCount_positive_places_UnstableRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, UnstableRockCount = 2 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.UnstableRock);
    }

    [Theory]
    [InlineData(42)]
    public void VolatileRockCount_positive_places_VolatileRock(int seed)
    {
        var cfg = new MapConfig { Seed = seed, VolatileRockCount = 1 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.VolatileRock);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(42)]
    public void All_scree_tiles_border_at_least_one_Floor_tile(int seed)
    {
        var cfg = new MapConfig { Seed = seed, ScreePatchCount = 3, UnstableRockCount = 2, VolatileRockCount = 1 };
        var map = MapGenerator.Generate(cfg);
        foreach (var p in map.Grid.Positions())
        {
            if (!map.Grid.Get(p).IsScree()) continue;
            bool bordersFloor = Card.Any(d =>
            {
                var off = d.ToOffset();
                var nb = new GridPos(p.X + off.X, p.Y + off.Y);
                return map.Grid.InBounds(nb) && map.Grid.Get(nb) == TileType.Floor;
            });
            Assert.True(bordersFloor, $"Scree tile at {p} does not border any Floor tile");
        }
    }

    [Fact]
    public void FloorConfig_floor_1_has_no_scree()
    {
        var cfg = MapConfig.FloorConfig(1, seed: 1);
        Assert.Equal(0, cfg.ScreePatchCount);
        Assert.Equal(0, cfg.UnstableRockCount);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_3_has_scree()
    {
        var cfg = MapConfig.FloorConfig(3, seed: 1);
        Assert.True(cfg.ScreePatchCount > 0);
        Assert.Equal(0, cfg.UnstableRockCount);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_8_has_unstable_rock()
    {
        var cfg = MapConfig.FloorConfig(8, seed: 1);
        Assert.True(cfg.UnstableRockCount > 0);
        Assert.Equal(0, cfg.VolatileRockCount);
    }

    [Fact]
    public void FloorConfig_floor_15_has_volatile_rock()
    {
        var cfg = MapConfig.FloorConfig(15, seed: 1);
        Assert.True(cfg.VolatileRockCount > 0);
    }

    [Fact]
    public void FloorConfig_floor_20_scree_count_never_exceeds_cap()
    {
        var cfg = MapConfig.FloorConfig(20, seed: 1);
        Assert.True(cfg.ScreePatchCount <= 3);
        Assert.True(cfg.UnstableRockCount <= 2);
        Assert.True(cfg.VolatileRockCount <= 1);
    }
}
```

Also add to `src/Miner49er.Core.Tests/MapConfigFloorTests.cs` (append before closing brace):

```csharp
[Fact]
public void FloorConfig_floor_2_has_no_scree()
{
    var cfg = MapConfig.FloorConfig(2, seed: 1);
    Assert.Equal(0, cfg.ScreePatchCount);
}

[Fact]
public void FloorConfig_scree_caps_at_3_on_late_floors()
{
    var cfg = MapConfig.FloorConfig(50, seed: 1);
    Assert.True(cfg.ScreePatchCount <= 3);
    Assert.True(cfg.UnstableRockCount <= 2);
    Assert.True(cfg.VolatileRockCount <= 1);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/ --filter "MapGeneratorScreeTests|MapConfigFloorTests" -v q
```

Expected: new tests FAIL

- [ ] **Step 3: Add MapConfig properties**

In `src/Miner49er.Core/Map/MapConfig.cs`, after `CrystalPatchCount`:

```csharp
public int ScreePatchCount    { get; set; } = 0; // amber: 50% trigger, radius 1
public int UnstableRockCount  { get; set; } = 0; // light red: 100% trigger, radius 1
public int VolatileRockCount  { get; set; } = 0; // bright red: 100% trigger, radius 2
```

- [ ] **Step 4: Update FloorConfig**

In `MapConfig.FloorConfig()`, after the `CrystalPatchCount` line:

```csharp
cfg.ScreePatchCount   = floor switch { >= 20 => 3, >= 8 => 2, >= 3 => 1, _ => 0 };
cfg.UnstableRockCount = floor switch { >= 20 => 2, >= 8 => 1, _ => 0 };
cfg.VolatileRockCount = floor >= 15 ? 1 : 0;
```

- [ ] **Step 5: Add PlaceScreePatches to MapGenerator**

In `src/Miner49er.Core/Map/MapGenerator.cs`, add after `PlaceCrystalPatches`:

```csharp
private static void PlaceScreePatches(TileGrid g, Random rng,
    int screeCount, int unstableCount, int volatileCount)
{
    if (screeCount + unstableCount + volatileCount == 0) return;

    const int RegionsX = 4, RegionsY = 4;
    int regionW = Math.Max(1, g.Width  / RegionsX);
    int regionH = Math.Max(1, g.Height / RegionsY);

    // Build pool of (type, remaining) to place
    var toPlace = new List<TileType>();
    for (int i = 0; i < screeCount;    i++) toPlace.Add(TileType.ScreeRock);
    for (int i = 0; i < unstableCount; i++) toPlace.Add(TileType.UnstableRock);
    for (int i = 0; i < volatileCount; i++) toPlace.Add(TileType.VolatileRock);
    Shuffle(toPlace, rng);

    int idx = 0;
    for (int ry = 0; ry < RegionsY && idx < toPlace.Count; ry++)
    for (int rx = 0; rx < RegionsX && idx < toPlace.Count; rx++)
    {
        if (rng.NextDouble() > 0.60) continue;

        var candidates = new List<GridPos>();
        int x0 = rx * regionW, x1 = Math.Min(x0 + regionW, g.Width);
        int y0 = ry * regionH, y1 = Math.Min(y0 + regionH, g.Height);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            var p = new GridPos(x, y);
            if (g.Get(p) == TileType.Rock && HasFloorNeighbour(g, p))
                candidates.Add(p);
        }
        if (candidates.Count == 0) continue;

        TileType tileType = toPlace[idx++];
        var seed = candidates[rng.Next(candidates.Count)];
        int targetSize = rng.Next(2, 5); // 2-4 tiles per patch (smaller than crystal)
        var patch = new HashSet<GridPos> { seed };
        var frontier = new Queue<GridPos>();
        frontier.Enqueue(seed);

        while (patch.Count < targetSize && frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            foreach (var d in Card)
            {
                var nb = cur + d.ToOffset();
                if (!g.InBounds(nb) || patch.Contains(nb)) continue;
                if (g.Get(nb) != TileType.Rock) continue;
                if (!HasFloorNeighbour(g, nb)) continue;
                patch.Add(nb);
                frontier.Enqueue(nb);
                if (patch.Count >= targetSize) break;
            }
        }

        foreach (var p in patch) g.Set(p, tileType);
    }
}
```

- [ ] **Step 6: Call PlaceScreePatches from Generate()**

In `MapGenerator.Generate()`, after the `PlaceCrystalPatches` call (around line 27):

```csharp
if (config.ScreePatchCount + config.UnstableRockCount + config.VolatileRockCount > 0)
    PlaceScreePatches(grid, rng, config.ScreePatchCount, config.UnstableRockCount, config.VolatileRockCount);
```

- [ ] **Step 7: Run tests to confirm they pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "MapGeneratorScreeTests|MapConfigFloorTests" -v q
```

Expected: all tests PASS

- [ ] **Step 8: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/ -v q
```

Expected: all tests PASS

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorScreeTests.cs src/Miner49er.Core.Tests/MapConfigFloorTests.cs
git commit -m "feat(scree): map generation — ScreePatchCount/UnstableRockCount/VolatileRockCount in FloorConfig"
```

---

## Task 4: Network Layer (Snapshots, Codec, MatchHost)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `game/net/MatchHost.cs`

**Interfaces:**
- Consumes: `SimEvent.ScreeCollapsed(Pos, Radius)` (Task 1)
- Produces: `ScreeCollapseSnapshot(int X, int Y, int Radius)` record struct
- Produces: `WorldSnapshot.ScreeCollapses: IReadOnlyList<ScreeCollapseSnapshot>?`
- Produces: codec encoding at END of the byte stream (append-only, backward-compatible read returns null if not present)

- [ ] **Step 1: Add ScreeCollapseSnapshot and WorldSnapshot field**

In `src/Miner49er.Core/Net/Snapshots.cs`, after `PendingFallSnapshot`:

```csharp
public readonly record struct ScreeCollapseSnapshot(int X, int Y, int Radius);
```

And add `ScreeCollapses` to `WorldSnapshot` (append as last optional param after `PendingFalls`):

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false,
    OctopusSnapshot? Octopus = null, int Lives = 3,
    IReadOnlyList<ReelChargeSnapshot>? ReelCharges = null,
    IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress = null,
    IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     = null,
    IReadOnlyList<TripChargeSnapshot>?       TripCharges      = null,
    IReadOnlyList<PendingFallSnapshot>?      PendingFalls     = null,
    IReadOnlyList<ScreeCollapseSnapshot>?    ScreeCollapses   = null);
```

- [ ] **Step 2: Add codec Write for ScreeCollapses**

In `SnapshotCodec.Write`, just before `w.Flush()`:

```csharp
w.Write(snap.ScreeCollapses?.Count ?? 0);
foreach (var sc in snap.ScreeCollapses ?? System.Array.Empty<ScreeCollapseSnapshot>())
    { w.Write(sc.X); w.Write(sc.Y); w.Write(sc.Radius); }
```

- [ ] **Step 3: Add codec Read for ScreeCollapses**

In `SnapshotCodec.Read`, just before the final `return new TickUpdate(...)`:

```csharp
int screeCount = r.ReadInt32();
List<ScreeCollapseSnapshot>? screeCollapses = screeCount > 0
    ? new List<ScreeCollapseSnapshot>(screeCount) : null;
for (int i = 0; i < screeCount; i++)
    screeCollapses!.Add(new ScreeCollapseSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
```

And update the final `return` to pass `ScreeCollapses`:

```csharp
return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
    monsters, secondsRemaining, escapeOpen, octopus, lives, reelCharges,
    treasureProgress, placedChests, tripCharges, PendingFalls: null,
    ScreeCollapses: screeCollapses), changes);
```

(Note: `PendingFalls` is not encoded in the codec — pass `null` explicitly so the position-based constructor call remains unambiguous when adding `ScreeCollapses`.)

- [ ] **Step 4: Handle ScreeCollapsed in MatchHost**

In `game/net/MatchHost.cs`, in the `TickAndBroadcast()` method:

At the top of the method, add a list for accumulated scree events:

```csharp
var changes = new List<TileChange>();
var screeCollapses = new List<ScreeCollapseSnapshot>();  // NEW
```

Add a case to the event switch:

```csharp
case ScreeCollapsed sc:
    screeCollapses.Add(new ScreeCollapseSnapshot(sc.Pos.X, sc.Pos.Y, sc.Radius));
    break;
```

After `SnapshotFactory.Capture`, augment the snapshot with scree events:

```csharp
var snapshot = SnapshotFactory.Capture(_sim, _tick, _livesRemaining);
if (screeCollapses.Count > 0)
    snapshot = snapshot with { ScreeCollapses = screeCollapses };
var update = new TickUpdate(snapshot, changes);
```

(Remove the old `var update = new TickUpdate(SnapshotFactory.Capture(...), changes);` line and replace with the three lines above.)

- [ ] **Step 5: Run existing SnapshotCodecTests to confirm no regressions**

```
dotnet test src/Miner49er.Core.Tests/ --filter "SnapshotCodecTests" -v q
```

Expected: PASS (existing test creates `TileChange` without scree fields; null ScreeCollapses round-trips cleanly as count=0)

- [ ] **Step 6: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/ -v q
```

Expected: all tests PASS

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs game/net/MatchHost.cs
git commit -m "feat(scree): network — ScreeCollapseSnapshot in codec + MatchHost event forwarding"
```

---

## Task 5: Client Presentation (MatchClient, WorldRenderer, Audio)

**Files:**
- Modify: `game/net/MatchClient.cs`
- Modify: `game/WorldRenderer.cs`
- Modify: `game/audio/SfxLibrary.cs`
- Modify: `game/net/MatchAudio.cs`

**Interfaces:**
- Consumes: `WorldSnapshot.ScreeCollapses` (Task 4)
- Produces: `MatchClient.ScreeCollapsed: event Action<Vector2, int>?` (worldPos, radius)
- Produces: `WorldRenderer.AddRockfallDust(GridPos center, int radius)`
- Produces: `SfxLibrary.Rockfall: AudioStream`

This task has no unit tests (Godot rendering code); verify manually by running the game and observing Listen shimmer on scree tiles and collapse sound/visual.

- [ ] **Step 1: Add ScreeCollapsed event to MatchClient**

In `game/net/MatchClient.cs`, after the `Exploded` event declaration:

```csharp
public event System.Action<Vector2, int>? ScreeCollapsed; // worldPos, radius
```

- [ ] **Step 2: Fire ScreeCollapsed in ApplyUpdate**

In `MatchClient.ApplyUpdate()`, after the explosion ring block (after the `if (blastCount > 0)` block), add:

```csharp
if (update.Snapshot.ScreeCollapses is { } scrCollapses && scrCollapses.Count > 0)
{
    foreach (var sc in scrCollapses)
    {
        var wpos = new Vector2(sc.X * TileSize + TileSize / 2f, sc.Y * TileSize + TileSize / 2f);
        _world?.AddRockfallDust(new GridPos(sc.X, sc.Y), sc.Radius);
        ScreeCollapsed?.Invoke(wpos, sc.Radius);
    }
}
```

- [ ] **Step 3: Add Listen shimmer colors to WorldRenderer**

In `game/WorldRenderer.cs`, after the `ShimmerColor` constant:

```csharp
private static readonly Color ScreeColor    = new(1.0f, 0.67f, 0.0f, 1f); // amber
private static readonly Color UnstableColor = new(1.0f, 0.27f, 0.07f, 1f); // light red
private static readonly Color VolatileColor = new(1.0f, 0.0f,  0.0f, 1f);  // bright red
```

- [ ] **Step 4: Add scree shimmer loop to WorldRenderer Listen section**

In `WorldRenderer._Draw()`, inside the Listen block (after the decoys loop, before the closing brace of the `if (_client.Listening...` block), add:

```csharp
// Scree tile shimmer — loops over grid in reveal radius
for (int dy = -ListenItemRevealRadius; dy <= ListenItemRevealRadius; dy++)
for (int dx = -ListenItemRevealRadius; dx <= ListenItemRevealRadius; dx++)
{
    int nx = lt.X + dx, ny = lt.Y + dy;
    var gp = new GridPos(nx, ny);
    if (!_client.Grid.InBounds(gp)) continue;
    Color screeCol;
    switch (_client.Grid.Get(gp))
    {
        case TileType.ScreeRock:    screeCol = ScreeColor;    break;
        case TileType.UnstableRock: screeCol = UnstableColor; break;
        case TileType.VolatileRock: screeCol = VolatileColor; break;
        default: continue;
    }
    int dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
    float fade = Mathf.Clamp(wavePos - dist + 1f, 0f, 1f);
    if (fade <= 0f) continue;
    DrawShimmer(nx, ny, screeCol with { A = baseA * fade }, ts);
}
```

- [ ] **Step 5: Add AddRockfallDust to WorldRenderer**

Add a field near `_flashes` and `_rings`:

```csharp
private readonly List<(GridPos center, int radius, float life)> _rockfallDusts = new();
```

Add the public method:

```csharp
public void AddRockfallDust(GridPos center, int radius) =>
    _rockfallDusts.Add((center, radius, 0.6f));
```

In `_Process`, age the dust list (same pattern as `_flashes`):

```csharp
for (int i = _rockfallDusts.Count - 1; i >= 0; i--)
{
    var d = _rockfallDusts[i];
    d.life -= (float)delta;
    if (d.life <= 0) _rockfallDusts.RemoveAt(i);
    else _rockfallDusts[i] = d;
}
if (_rockfallDusts.Count > 0) QueueRedraw();
```

In `_Draw`, draw the dust circles (after the ring drawing section):

```csharp
foreach (var (center, radius, life) in _rockfallDusts)
{
    float alpha = Mathf.Clamp(life / 0.6f, 0f, 1f) * 0.45f;
    var wc = new Vector2(center.X * ts + ts / 2f, center.Y * ts + ts / 2f);
    DrawCircle(wc, (radius * ts + ts * 0.5f), new Color(0.55f, 0.45f, 0.35f, alpha));
}
```

- [ ] **Step 6: Add Rockfall to SfxLibrary**

In `game/audio/SfxLibrary.cs`, after the `CaveIn` line:

```csharp
public static AudioStream Rockfall => Get("rockfall", () => Noise(0.55f, 90f, decay: true)); // rock debris crash
```

- [ ] **Step 7: Wire ScreeCollapsed audio in MatchAudio**

In `game/net/MatchAudio.cs`:

In `Begin(client)`, after the `_client.Exploded += OnExploded;` line:

```csharp
_client.ScreeCollapsed += OnScreeCollapsed;
```

In `_ExitTree()`, after the `_client.Exploded -= OnExploded;` line:

```csharp
if (_client != null) _client.ScreeCollapsed -= OnScreeCollapsed;
```

Add the handler method (near `OnExploded`):

```csharp
private void OnScreeCollapsed(Vector2 worldPos, int radius)
{
    OneShot(SfxLibrary.Rockfall, worldPos);
}
```

- [ ] **Step 8: Build game project to confirm no compile errors**

```
dotnet build game/
```

Expected: 0 errors, 0 warnings (or pre-existing warnings only)

- [ ] **Step 9: Run full core test suite**

```
dotnet test src/Miner49er.Core.Tests/ -v q
```

Expected: all tests PASS

- [ ] **Step 10: Commit**

```
git add game/net/MatchClient.cs game/WorldRenderer.cs game/audio/SfxLibrary.cs game/net/MatchAudio.cs
git commit -m "feat(scree): client presentation — Listen shimmer, rockfall dust, audio"
```

---

## Self-Review Checklist

- [x] **Spec coverage**: Tile types (§1 Task 1), collapse mechanic (§2 Task 2), Listen rendering (§4 Task 5), map generation (§3 Task 3), audio/visual (§6 Task 5), death cause messaging uses existing `MinerCrushed`/`DeathCause.Crushed` (§7, no new task needed)
- [x] **No placeholders**: All steps have exact code
- [x] **Type consistency**: `ScreeCollapseSnapshot` defined in Task 4 Step 1 and consumed in Task 4 Steps 2–3 and Task 5 Step 2. `ScreeCollapsed` event `Action<Vector2, int>` defined in Task 5 Step 1 and subscribed in Task 5 Step 7. `AddRockfallDust(GridPos, int)` defined in Task 5 Step 5 and called in Task 5 Step 2.
- [x] **Enum serialisation**: ScreeRock=13, UnstableRock=14, VolatileRock=15 appended after CrystalRock=12
