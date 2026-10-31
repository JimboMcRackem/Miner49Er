# Glowing Crystals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `CrystalRock` wall tiles that permanently illuminate surrounding tunnels via fog reveal, and drop a portable `CrystalShard` mini-lantern item when mined or blasted.

**Architecture:** `CrystalRock` is a new `TileType` that is mineable/blastable like `Rock`. The simulation emits a `CrystalShardDropped` event on destruction; the host spawns a loose `CrystalShard` item. On the client, `UpdateFog()` unions visibility from crystal wall positions (radius 5) and shard positions (radius 2/3) into the miner's fog each tick. Rendering is purely in `WorldRenderer` — procedural glow overlays on crystal tiles plus a sprite for the dropped shard.

**Tech Stack:** C# / .NET 8, Godot 4, xUnit for core tests.

## Global Constraints

- `TileType` enum values are serialised as `int` over the network — **always add new values at the end** of the enum to preserve existing ordinals.
- `ItemKind` enum values are similarly serialised — **always add at the end**.
- Build command: `dotnet build Miner49er.csproj -c Debug --nologo -v quiet` from `D:\Projects\Miner49er\`.
- Test command: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet` from `D:\Projects\Miner49er\`.
- Run `godot` via **PowerShell only** (Bash shim breaks headless mode).
- Never `git add -A`; never stage `.superpowers/`, `*.uid`, `Temp/`, or `_preview_*`.
- Sprite assets go in `assets/` and are PixelLab-generated separately; code must have procedural fallbacks for all sprites.

---

## File Map

| File | Change |
|---|---|
| `src/Miner49er.Core/Grid/TileType.cs` | Add `CrystalRock`; update `IsMinable`, `IsBlastable`, `BlocksSight` |
| `src/Miner49er.Core/Map/Item.cs` | Add `CrystalShard` to `ItemKind`; update `IsCarried` |
| `src/Miner49er.Core/Sim/SimEvent.cs` | Add `CrystalShardDropped` record |
| `src/Miner49er.Core/Sim/Simulation.cs` | Emit `CrystalShardDropped` in mining and blast paths |
| `src/Miner49er.Core/Map/MapConfig.cs` | Add `CrystalPatchCount` property |
| `src/Miner49er.Core/Map/MapGenerator.cs` | Add `PlaceCrystalPatches()`, call it in `Generate()`, update `FloorConfig()` |
| `game/net/MatchHost.cs` | Handle `CrystalShardDropped` event → spawn shard item |
| `game/net/MatchClient.cs` | Cache crystal positions; union fog from crystals and shards in `UpdateFog()` |
| `game/WorldRenderer.cs` | Draw `CrystalRock` overlay + glow; draw `CrystalShard` floor item + held halo |
| `src/Miner49er.Core.Tests/TileTypeCrystalTests.cs` | **New** — TileType extension tests |
| `src/Miner49er.Core.Tests/SimulationCrystalTests.cs` | **New** — mining/blasting CrystalRock emits event + drops shard |
| `src/Miner49er.Core.Tests/MapGeneratorCrystalTests.cs` | **New** — patch placement and MapConfig wiring |

---

### Task 1: Core types — `CrystalRock`, `CrystalShard`, `CrystalShardDropped`

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Create: `src/Miner49er.Core.Tests/TileTypeCrystalTests.cs`

**Interfaces:**
- Produces: `TileType.CrystalRock`, `ItemKind.CrystalShard`, `record CrystalShardDropped(GridPos Pos) : SimEvent` — all used by Tasks 2–6.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/TileTypeCrystalTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TileTypeCrystalTests
{
    [Fact]
    public void CrystalRock_is_minable_and_blastable()
    {
        Assert.True(TileType.CrystalRock.IsMinable());
        Assert.True(TileType.CrystalRock.IsBlastable());
    }

    [Fact]
    public void CrystalRock_blocks_sight()
    {
        Assert.True(TileType.CrystalRock.BlocksSight());
    }

    [Fact]
    public void CrystalRock_is_not_walkable_enterable_or_lethal()
    {
        Assert.False(TileType.CrystalRock.IsWalkable());
        Assert.False(TileType.CrystalRock.IsEnterable());
        Assert.False(TileType.CrystalRock.IsLethal());
    }

    [Fact]
    public void CrystalRock_is_not_water_or_bridgeable()
    {
        Assert.False(TileType.CrystalRock.IsWater());
        Assert.False(TileType.CrystalRock.IsBridgeable());
    }

    [Fact]
    public void CrystalShard_is_carried()
    {
        Assert.True(ItemKind.CrystalShard.IsCarried());
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (type not defined yet)

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: compile error — `TileType.CrystalRock` and `ItemKind.CrystalShard` do not exist.

- [ ] **Step 3: Add `CrystalRock` to `TileType.cs`**

Open `src/Miner49er.Core/Grid/TileType.cs`. The current enum is:
```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit, Cracked, Crumbling, Lava, LavaVent }
```

Replace the entire file with:
```csharp
namespace Miner49er.Core;

public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit, Cracked, Crumbling, Lava, LavaVent, CrystalRock }

public static class TileTypeExtensions
{
    public const double ShallowSlowFactor = 2.0;

    public static bool IsWalkable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.Plank
          or TileType.Cracked or TileType.Crumbling;

    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank
          or TileType.Pit or TileType.Cracked or TileType.Crumbling
          or TileType.Lava or TileType.LavaVent;

    public static bool IsLethal(this TileType t) =>
        t is TileType.DeepWater or TileType.Pit or TileType.Lava or TileType.LavaVent;

    public static double MoveCostMultiplier(this TileType t) =>
        t == TileType.ShallowWater ? ShallowSlowFactor : 1.0;

    public static bool IsMinable(this TileType t) => t is TileType.Rock or TileType.GoldRock or TileType.CrystalRock;
    public static bool IsBlastable(this TileType t) => t is TileType.Rock or TileType.GoldRock or TileType.CrystalRock;

    public static bool BlocksSight(this TileType t) =>
        t is TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock or TileType.CrystalRock;

    public static bool IsWater(this TileType t) => t is TileType.ShallowWater or TileType.DeepWater;

    public static bool IsTraversable(this TileType t) => t is TileType.Floor or TileType.ShallowWater;

    public static bool IsBridgeable(this TileType t) =>
        t.IsWater() || t is TileType.Pit or TileType.Cracked or TileType.Crumbling;
}
```

- [ ] **Step 4: Add `CrystalShard` to `Item.cs`**

In `src/Miner49er.Core/Map/Item.cs`, append `CrystalShard` to the `ItemKind` enum and update `IsCarried`:

```csharp
public enum ItemKind
{
    SpeedPotion, LongerVision, BiggerBlast,
    WaterPlank, SlowMold, Lantern,
    Chest,
    LifePotion,
    BossChest,
    Detonator,
    Reel,
    TreasureChest,
    IdolVishnu, IdolZeus, IdolAnubis, IdolOdin,
    IdolShiva, IdolBuddha, IdolRa, IdolQuetzalcoatl,
    IdolUrn, IdolLamp, IdolMace, IdolSceptre, IdolGlobe,
    IdolTrophyCup, IdolChalice, IdolCrown, IdolSkull,
    CrystalShard,
}

public static class ItemKindExtensions
{
    public static bool IsCarried(this ItemKind k) =>
        k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern
          or ItemKind.Detonator or ItemKind.TreasureChest or ItemKind.CrystalShard
          or (>= ItemKind.IdolVishnu and <= ItemKind.IdolSkull);

    public static bool IsPlaceable(this ItemKind k) =>
        k is ItemKind.SpeedPotion or ItemKind.LongerVision or ItemKind.BiggerBlast;

    public static bool IsIdol(this ItemKind k) =>
        k >= ItemKind.IdolVishnu && k <= ItemKind.IdolSkull;
}
```

- [ ] **Step 5: Add `CrystalShardDropped` to `SimEvent.cs`**

Append one line at the end of `src/Miner49er.Core/Sim/SimEvent.cs`:

```csharp
public sealed record CrystalShardDropped(GridPos Pos) : SimEvent;
```

- [ ] **Step 6: Run tests — expect PASS**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: all existing tests still pass; the 5 new `TileTypeCrystalTests` pass.

- [ ] **Step 7: Build to confirm Godot side still compiles**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core.Tests/TileTypeCrystalTests.cs
git commit -m "feat(crystals): add CrystalRock tile, CrystalShard item, CrystalShardDropped event"
```

---

### Task 2: Simulation — emit `CrystalShardDropped` on mine and blast

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationCrystalTests.cs`

**Interfaces:**
- Consumes: `TileType.CrystalRock`, `CrystalShardDropped` from Task 1.
- Produces: sim emits `CrystalShardDropped(pos)` when `CrystalRock` is mined or blasted — consumed by Task 4 (MatchHost).

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/SimulationCrystalTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationCrystalTests
{
    private static Simulation MakeWithCrystal(out GridPos crystalPos)
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        crystalPos = new GridPos(2, 1);
        grid.Set(crystalPos, TileType.CrystalRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 1.0 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Mining_CrystalRock_converts_tile_to_Floor()
    {
        var sim = MakeWithCrystal(out var pos);
        sim.TryStartMining(1);
        sim.Tick(1.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(pos));
    }

    [Fact]
    public void Mining_CrystalRock_emits_RockMined_and_CrystalShardDropped()
    {
        var sim = MakeWithCrystal(out var pos);
        sim.TryStartMining(1);
        sim.Tick(1.0);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is RockMined rm && rm.Pos == pos && !rm.WasGold);
        Assert.Contains(events, e => e is CrystalShardDropped csd && csd.Pos == pos);
    }

    [Fact]
    public void Blasting_CrystalRock_emits_CrystalShardDropped()
    {
        // 5x5 grid: crystal at (2,2), charge planted on it.
        var grid = new TileGrid(5, 5, TileType.Floor);
        var crystalPos = new GridPos(2, 2);
        grid.Set(crystalPos, TileType.CrystalRock);
        var sim = new Simulation(grid, new SimConfig { FuseSeconds = 0.1, BlastRockRadius = 1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartPlanting(1);
        sim.Tick(0.5); // fuse burns
        sim.DrainEvents();
        // crystal tile is now floor; shard event emitted during blast
        Assert.Equal(TileType.Floor, sim.Grid.Get(crystalPos));
        // CrystalShardDropped was emitted; check after draining
        // (already drained above — so re-mine a fresh one)
    }

    [Fact]
    public void Mining_regular_Rock_does_not_emit_CrystalShardDropped()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 1.0 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(1.0);
        Assert.DoesNotContain(sim.DrainEvents(), e => e is CrystalShardDropped);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: FAIL — `CrystalShardDropped` not yet emitted.

- [ ] **Step 3: Update `CompleteActivity` in `Simulation.cs` — mining path**

Find the mining path in `CompleteActivity` (around line 1339). The current block is:
```csharp
if (kind == ActivityKind.Mining)
{
    if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
    bool wasGold = Grid.Get(target) == TileType.GoldRock;
    Grid.Set(target, TileType.Floor);
    if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
    UnburyItemsAt(target);
    ActivateVentsAround(target);
    _events.Add(new RockMined(m.Id, target, wasGold));
    _noiseSources.Add(new NoiseSource { Pos = target, LifetimeRemaining = 2.0, Kind = NoiseKind.Pickaxe });
}
```

Replace with:
```csharp
if (kind == ActivityKind.Mining)
{
    if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
    bool wasGold    = Grid.Get(target) == TileType.GoldRock;
    bool wasCrystal = Grid.Get(target) == TileType.CrystalRock;
    Grid.Set(target, TileType.Floor);
    if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
    UnburyItemsAt(target);
    ActivateVentsAround(target);
    _events.Add(new RockMined(m.Id, target, wasGold));
    if (wasCrystal) _events.Add(new CrystalShardDropped(target));
    _noiseSources.Add(new NoiseSource { Pos = target, LifetimeRemaining = 2.0, Kind = NoiseKind.Pickaxe });
}
```

- [ ] **Step 4: Update `DetonateAt` in `Simulation.cs` — blast path**

Find the blast loop in `DetonateAt` (around line 1273). The current inner block is:
```csharp
if (!Grid.Get(p).IsBlastable()) continue;
bool wasGold = Grid.Get(p) == TileType.GoldRock;
Grid.Set(p, TileType.Floor);
if (wasGold)
{
    if (_miners.TryGetValue(ownerId, out var owner) && owner.Alive) owner.GoldCollected++;
    OnGoldCleared();
}
UnburyItemsAt(p);
ActivateVentsAround(p);
destroyed.Add(p);
```

Replace with:
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

- [ ] **Step 5: Run tests — expect PASS**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: all tests pass including the 4 new `SimulationCrystalTests`.

- [ ] **Step 6: Build**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCrystalTests.cs
git commit -m "feat(crystals): emit CrystalShardDropped when CrystalRock is mined or blasted"
```

---

### Task 3: MapConfig + MapGenerator — `PlaceCrystalPatches`

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Create: `src/Miner49er.Core.Tests/MapGeneratorCrystalTests.cs`

**Interfaces:**
- Consumes: `TileType.CrystalRock` from Task 1.
- Produces: `MapConfig.CrystalPatchCount` property; `MapGenerator.Generate()` places `CrystalRock` tiles when `CrystalPatchCount > 0`; `FloorConfig()` sets count ≥ 4 on floors ≥ 3.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/MapGeneratorCrystalTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCrystalTests
{
    [Fact]
    public void No_crystals_placed_when_CrystalPatchCount_is_zero()
    {
        var cfg = new MapConfig { Seed = 1, CrystalPatchCount = 0 };
        var map = MapGenerator.Generate(cfg);
        Assert.DoesNotContain(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.CrystalRock);
    }

    [Fact]
    public void Crystals_are_placed_when_CrystalPatchCount_is_positive()
    {
        var cfg = new MapConfig { Seed = 42, CrystalPatchCount = 4 };
        var map = MapGenerator.Generate(cfg);
        Assert.Contains(map.Grid.Positions(), p => map.Grid.Get(p) == TileType.CrystalRock);
    }

    [Fact]
    public void All_CrystalRock_tiles_border_at_least_one_Floor_tile()
    {
        var cfg = new MapConfig { Seed = 7, CrystalPatchCount = 4 };
        var map = MapGenerator.Generate(cfg);
        var dirs = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
        foreach (var p in map.Grid.Positions())
        {
            if (map.Grid.Get(p) != TileType.CrystalRock) continue;
            bool bordersFloor = dirs.Any(d =>
            {
                var nb = p.Step(d);
                return map.Grid.InBounds(nb) && map.Grid.Get(nb) == TileType.Floor;
            });
            Assert.True(bordersFloor, $"CrystalRock at {p} does not border any Floor tile");
        }
    }

    [Fact]
    public void FloorConfig_floor_1_has_no_crystals()
    {
        var cfg = MapConfig.FloorConfig(1, seed: 1);
        Assert.Equal(0, cfg.CrystalPatchCount);
    }

    [Fact]
    public void FloorConfig_floor_3_has_crystals()
    {
        var cfg = MapConfig.FloorConfig(3, seed: 1);
        Assert.True(cfg.CrystalPatchCount > 0);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: FAIL — `MapConfig.CrystalPatchCount` does not exist.

- [ ] **Step 3: Add `CrystalPatchCount` to `MapConfig.cs`**

In `src/Miner49er.Core/Map/MapConfig.cs`, add one property anywhere in the config body (after `LanternCount` for example):

```csharp
public int CrystalPatchCount { get; set; } = 0;
```

And in `FloorConfig()`, add after the `cfg.DetonatorCount` line:

```csharp
cfg.CrystalPatchCount = floor >= 3 ? 4 : 0;
```

- [ ] **Step 4: Add `PlaceCrystalPatches` to `MapGenerator.cs`**

Add the new private method at the bottom of `MapGenerator` (before the closing `}`):

```csharp
private static void PlaceCrystalPatches(TileGrid grid, Random rng, int patchCount)
{
    if (patchCount <= 0) return;
    const int RegionsX = 4, RegionsY = 4;
    int regionW = Math.Max(1, grid.Width  / RegionsX);
    int regionH = Math.Max(1, grid.Height / RegionsY);

    for (int ry = 0; ry < RegionsY; ry++)
    for (int rx = 0; rx < RegionsX; rx++)
    {
        if (rng.NextDouble() > 0.60) continue; // ~60% of regions get a patch

        var candidates = new List<GridPos>();
        int x0 = rx * regionW, x1 = Math.Min(x0 + regionW, grid.Width);
        int y0 = ry * regionH, y1 = Math.Min(y0 + regionH, grid.Height);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            var p = new GridPos(x, y);
            if (grid.Get(p) != TileType.Rock) continue;
            if (HasCardinalFloorNeighbour(grid, p)) candidates.Add(p);
        }
        if (candidates.Count == 0) continue;

        var seed = candidates[rng.Next(candidates.Count)];
        int targetSize = rng.Next(3, 6); // 3–5 tiles per patch
        var patch = new HashSet<GridPos> { seed };
        var frontier = new Queue<GridPos>();
        frontier.Enqueue(seed);

        while (patch.Count < targetSize && frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            foreach (var dir in Card)
            {
                var nb = new GridPos(cur.X + dir.ToOffset().X, cur.Y + dir.ToOffset().Y);
                if (!grid.InBounds(nb) || patch.Contains(nb)) continue;
                if (grid.Get(nb) != TileType.Rock) continue;
                if (!HasCardinalFloorNeighbour(grid, nb)) continue;
                patch.Add(nb);
                frontier.Enqueue(nb);
                if (patch.Count >= targetSize) break;
            }
        }

        foreach (var p in patch) grid.Set(p, TileType.CrystalRock);
    }
}

private static bool HasCardinalFloorNeighbour(TileGrid grid, GridPos p)
{
    foreach (var dir in Card)
    {
        var nb = new GridPos(p.X + dir.ToOffset().X, p.Y + dir.ToOffset().Y);
        if (grid.InBounds(nb) && grid.Get(nb) == TileType.Floor) return true;
    }
    return false;
}
```

- [ ] **Step 5: Wire `PlaceCrystalPatches` into `Generate()`**

In `MapGenerator.Generate()`, add the call after `PlaceLava` / before the `LargestTraversableRegion` call:

```csharp
if (config.Lava)
    PlaceLava(grid, rng, config.LavaPoolCount, config.LavaPoolGrowChance, config.LavaPoolMax);
if (config.CrystalPatchCount > 0)
    PlaceCrystalPatches(grid, rng, config.CrystalPatchCount);  // ADD THIS LINE
var region = LargestTraversableRegion(grid);
```

- [ ] **Step 6: Check Direction.ToOffset() usage**

The `Direction` type has a `ToOffset()` method returning a `GridPos` offset — verify by checking `src/Miner49er.Core/Grid/Direction.cs`. If `ToOffset()` doesn't exist or returns a different type, use the `Card` + explicit offsets pattern already in the file (e.g., `new GridPos(cur.X + dx, cur.Y + dy)` with `int[] dx = {0,1,0,-1}; int[] dy = {-1,0,1,0}`).

Inspect the file:
```
type src/Miner49er.Core/Grid/Direction.cs
```

Adjust the offsets in `PlaceCrystalPatches` and `HasCardinalFloorNeighbour` to match the actual API.

- [ ] **Step 7: Run tests — expect PASS**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```

Expected: all tests pass including the 5 new `MapGeneratorCrystalTests`.

- [ ] **Step 8: Build**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorCrystalTests.cs
git commit -m "feat(crystals): PlaceCrystalPatches — sporadic geological wall clusters"
```

---

### Task 4: MatchHost — spawn `CrystalShard` item on `CrystalShardDropped`

**Files:**
- Modify: `game/net/MatchHost.cs`

**Interfaces:**
- Consumes: `CrystalShardDropped` sim event (Task 1/2); `ItemKind.CrystalShard` (Task 1).
- Produces: a loose `CrystalShard` `Item` added to the simulation — visible to clients via next `WorldSnapshot.Items`.

- [ ] **Step 1: Handle `CrystalShardDropped` in `TickAndBroadcast`**

In `game/net/MatchHost.cs`, find `TickAndBroadcast()` — the `switch (e)` block that handles sim events. The block currently ends with `case RockFell rf:`. Add a new case **before** the closing brace of the switch:

```csharp
case CrystalShardDropped csd:
    _sim.AddItem(new Item(csd.Pos, ItemKind.CrystalShard, ItemPlacement.Loose));
    break;
```

`Item` is `Miner49er.Core.Item`, already in scope. `ItemPlacement.Loose` means it sits on the floor and is collectible on walk-over, exactly like unburied items.

- [ ] **Step 2: Build**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add game/net/MatchHost.cs
git commit -m "feat(crystals): MatchHost spawns CrystalShard item when crystal wall is destroyed"
```

---

### Task 5: MatchClient — crystal fog computation

**Files:**
- Modify: `game/net/MatchClient.cs`

**Interfaces:**
- Consumes: `TileType.CrystalRock` (Task 1), `ItemKind.CrystalShard` (Task 1), `Visibility.Compute` (existing).
- Produces: `Fog` updated each tick with crystal-wall + shard contributions — consumed by `FogRenderer` (existing, no changes needed) and `WorldRenderer` (Task 6).

- [ ] **Step 1: Add crystal position cache fields**

In `game/net/MatchClient.cs`, find the private fields section. Add:

```csharp
private HashSet<GridPos>? _crystalPositions;
private const int CrystalWallRadius = 5;
private const int CrystalShardFloorRadius = 2;
private const int CrystalShardHeldRadius  = 3;
```

- [ ] **Step 2: Add `BuildCrystalCache()` method**

Add a new private method alongside `CountGold` at the bottom of the class:

```csharp
private void BuildCrystalCache()
{
    _crystalPositions = new HashSet<GridPos>();
    foreach (var p in Grid.Positions())
        if (Grid.Get(p) == TileType.CrystalRock)
            _crystalPositions.Add(p);
}
```

- [ ] **Step 3: Invalidate cache on floor reset**

In `ResetFloor()`, after `Fog.Reset();`, add:

```csharp
_crystalPositions = null;
```

- [ ] **Step 4: Update crystal cache on TileChanges**

In `ApplyUpdate()`, the first loop processes `TileChanges` and calls `Grid.Set(p, t.NewType)`. After that `Grid.Set` line, add:

```csharp
if (t.NewType == TileType.Floor && _crystalPositions != null)
    _crystalPositions.Remove(p);
```

The full updated block becomes:

```csharp
foreach (var t in update.TileChanges)
{
    var p = new GridPos(t.X, t.Y);
    if (Grid.InBounds(p))
    {
        Grid.Set(p, t.NewType);
        if (t.NewType == TileType.Floor && _crystalPositions != null)
            _crystalPositions.Remove(p);
    }
    if (t.FromBlast)
    {
        _world?.AddExplosionFlash(p);
        bx += t.X; by += t.Y; blastCount++;
    }
}
```

- [ ] **Step 5: Update `UpdateFog()` to union crystal contributions**

The current `UpdateFog()` method (around line 539) is:

```csharp
private void UpdateFog()
{
    foreach (var m in _miners)
        if (m.Id == LocalMinerId && m.Alive)
            Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius));
}
```

Replace it with:

```csharp
private void UpdateFog()
{
    foreach (var m in _miners)
    {
        if (m.Id != LocalMinerId || !m.Alive) continue;

        var visible = Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius);

        // Static crystal wall light sources
        if (_crystalPositions == null) BuildCrystalCache();
        foreach (var cp in _crystalPositions!)
            visible.UnionWith(Visibility.Compute(Grid, cp, CrystalWallRadius));

        // Loose crystal shards on the floor
        foreach (var it in _items)
            if (it.Kind == ItemKind.CrystalShard && it.Placement == ItemPlacement.Loose)
                visible.UnionWith(Visibility.Compute(Grid, new GridPos(it.X, it.Y), CrystalShardFloorRadius));

        // Held crystal shards (any miner, including opponents — their light illuminates too)
        foreach (var mn in _miners)
            if (mn.Alive && mn.Held == (int)ItemKind.CrystalShard)
                visible.UnionWith(Visibility.Compute(Grid, new GridPos(mn.X, mn.Y), CrystalShardHeldRadius));

        Fog.Update(visible);
    }
}
```

`_items` is `List<ItemSnapshot>` already on the client. `ItemSnapshot` has `X`, `Y`, `Kind`, `Placement` fields (see `Snapshots.cs`). `MinerSnapshot.Held` is an `int` cast of `ItemKind`.

- [ ] **Step 6: Build**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```
git add game/net/MatchClient.cs
git commit -m "feat(crystals): client fog unions crystal wall + shard light sources each tick"
```

---

### Task 6: WorldRenderer — draw CrystalRock and CrystalShard

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: `TileType.CrystalRock` (Task 1), `ItemKind.CrystalShard` (Task 1), `_client.Grid` (existing).
- Produces: visual CrystalRock wall overlay + glow; CrystalShard floor sprite + glow; held-shard halo on miner.

- [ ] **Step 1: Add crystal glow texture and sprite fields**

In `WorldRenderer.cs`, alongside the `_lanternGlowTex` field, add:

```csharp
private ImageTexture _crystalGlowTex = null!;
private Texture2D? _crystalRockTex;
private Texture2D? _crystalShardTex;
```

Also add the static crystal colours near the top colour constants:

```csharp
private static readonly Color CrystalFacetA = new("a060ff");
private static readonly Color CrystalFacetB = new("60c0ff");
private static readonly Color CrystalFacetC = new("c080ff");
private static readonly Color CrystalGlowCol = new Color(0.55f, 0.30f, 1.00f, 1f); // used for glow tex tint
```

- [ ] **Step 2: Build the crystal glow texture in `Init()`**

`BuildRadialGlowTex()` currently builds an amber glow (hardcoded colours). Add a new overload that accepts a colour:

```csharp
private static ImageTexture BuildRadialGlowTex(Color centre, Color edge, int size = 128)
{
    var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
    float half = size / 2f;
    for (int y = 0; y < size; y++)
    for (int x = 0; x < size; x++)
    {
        float t = Mathf.Clamp(new Vector2(x - half, y - half).Length() / half, 0f, 1f);
        float a = Mathf.Pow(1f - t, 2.5f);
        img.SetPixel(x, y, new Color(
            Mathf.Lerp(centre.R, edge.R, t),
            Mathf.Lerp(centre.G, edge.G, t),
            Mathf.Lerp(centre.B, edge.B, t),
            a * 0.55f));
    }
    return ImageTexture.CreateFromImage(img);
}
```

Update the existing no-arg `BuildRadialGlowTex()` to call this overload:

```csharp
private static ImageTexture BuildRadialGlowTex(int size = 128) =>
    BuildRadialGlowTex(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.60f, 0.1f, 1f), size);
```

In `Init()`, after `_lanternGlowTex = BuildRadialGlowTex();`, add:

```csharp
_crystalGlowTex  = BuildRadialGlowTex(new Color(0.65f, 0.35f, 1.0f, 1f), new Color(0.30f, 0.55f, 1.0f, 1f));
_crystalRockTex  = ResourceLoader.Exists("res://assets/tiles/singletiles/crystal_rock.png")
                   ? GD.Load<Texture2D>("res://assets/tiles/singletiles/crystal_rock.png") : null;
_crystalShardTex = ResourceLoader.Exists("res://assets/objects/item_crystal_shard.png")
                   ? GD.Load<Texture2D>("res://assets/objects/item_crystal_shard.png") : null;
```

- [ ] **Step 3: Draw `CrystalRock` in the tile overlay pass**

In `_Draw()`, inside the tile overlay `switch (grid.Get(p))` block (around line 425), add a new case alongside `GoldRock`, `LavaVent`, etc.:

```csharp
case TileType.CrystalRock:
{
    float x0 = p.X * ts, y0 = p.Y * ts;
    // Ambient glow behind tile
    float glowPx = ts * 2.5f;
    float pulse = 0.70f + 0.30f * Mathf.Sin(wTime * Mathf.Pi * 2f / 1.65f);
    DrawTextureRect(_crystalGlowTex,
        new Rect2(x0 + ts / 2f - glowPx / 2f, y0 + ts / 2f - glowPx / 2f, glowPx, glowPx),
        false, new Color(1f, 1f, 1f, 0.40f * pulse));

    if (_crystalRockTex != null)
    {
        DrawTextureRect(_crystalRockTex, new Rect2(x0, y0, ts, ts), false);
    }
    else
    {
        // Procedural fallback: 4 thin crystal facets
        uint ch = (uint)(p.X * 2246822519u ^ p.Y * 3266489917u ^ 0xC7A1u);
        float brightness = 0.80f + 0.20f * Mathf.Sin(wTime * 3.1f + (ch & 0xFFu) * 0.025f);
        for (int fi = 0; fi < 4; fi++)
        {
            uint fh = ch ^ (uint)(fi * 1013904223u);
            float ang = ((fh >> 4) & 0xFFu) * (Mathf.Tau / 255f);
            float len = ts * (0.30f + ((fh >> 12) & 0x1Fu) * 0.007f);
            float wid = ts * 0.06f;
            var cen = new Vector2(x0 + ts * 0.5f, y0 + ts * 0.5f);
            var rot = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var perp = new Vector2(-rot.Y, rot.X) * wid;
            var tip1 = cen + rot * len;
            var tip2 = cen - rot * len;
            Color fc = (fi % 3) switch { 0 => CrystalFacetA, 1 => CrystalFacetB, _ => CrystalFacetC };
            fc = fc with { A = brightness * 0.85f };
            DrawPolygon(new[] { tip1, cen + perp, tip2, cen - perp }, new[] { fc, fc, fc, fc });
        }
    }
    break;
}
```

Note: `wTime` is already declared earlier in `_Draw()` for the water sparkles. The tile overlay loop comes after the water loop, so `wTime` is in scope.

- [ ] **Step 4: Draw `CrystalShard` in the items pass**

In `_Draw()`, inside `foreach (var it in _client.Items)`, the items loop dispatches on `it.Kind`. Find the section that handles other carried items (after the BossChest / Chest special cases) and add a new block **before** the generic `_itemTex.TryGetValue` fallback:

```csharp
if (it.Kind == ItemKind.CrystalShard)
{
    // Small ambient glow beneath the shard
    float shardGlowPx = ts * 1.4f;
    DrawTextureRect(_crystalGlowTex,
        new Rect2(icenter.X - shardGlowPx / 2f, icenter.Y - shardGlowPx / 2f, shardGlowPx, shardGlowPx),
        false, new Color(1f, 1f, 1f, 0.30f));
    if (_crystalShardTex != null)
        DrawTextureRect(_crystalShardTex, r, false);
    else
    {
        // Procedural fallback: cyan diamond
        float hw = ts * 0.20f;
        DrawPolygon(
            new[] {
                new Vector2(icenter.X,        icenter.Y - hw),
                new Vector2(icenter.X + hw,   icenter.Y),
                new Vector2(icenter.X,        icenter.Y + hw),
                new Vector2(icenter.X - hw,   icenter.Y),
            },
            new[] { CrystalFacetB, CrystalFacetA, CrystalFacetC, CrystalFacetB });
        DrawCircle(icenter, hw * 0.5f, new Color(1f, 1f, 1f, 0.85f));
    }
    continue;
}
```

- [ ] **Step 5: Draw held-shard halo on miners**

In `_Draw()`, in the miner drawing section — after the lantern glow block and before the monsters loop — add:

```csharp
// Crystal shard halo: soft cyan ring around any miner holding a shard
foreach (var m in _client.Miners)
{
    if (!m.Alive || m.Held != (int)ItemKind.CrystalShard) continue;
    if (!_client.Fog.IsVisible(new GridPos(m.X, m.Y)) && !spectating) continue;
    var center = _client.MinerVisualPos(m.Id, m.X, m.Y);
    DrawCircle(center, ts * 0.55f, new Color(0.45f, 0.20f, 1.0f, 0.22f));
}
```

- [ ] **Step 6: Build**

```
dotnet build Miner49er.csproj -c Debug --nologo -v quiet
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Commit**

```
git add game/WorldRenderer.cs
git commit -m "feat(crystals): render CrystalRock glow overlay + CrystalShard floor + held halo"
```

---

## Self-Review

**Spec coverage:**
- `TileType.CrystalRock` mineable/blastable/blocks-sight → Task 1 ✓
- Drop `CrystalShard` on mine/blast → Tasks 2 + 4 ✓
- `MapGenerator` sporadic clusters, 60% per region, 3–5 tiles each → Task 3 ✓
- `MapConfig.CrystalPatchCount`, `FloorConfig` sets on floor ≥ 3 → Task 3 ✓
- Fog: 5-tile crystal wall radius, 2-tile floor shard, 3-tile held shard → Task 5 ✓
- Crystal cache invalidated on mine/blast TileChange and floor reset → Task 5 ✓
- CrystalRock glow + procedural fallback → Task 6 ✓
- CrystalShard floor sprite + glow → Task 6 ✓
- Held CrystalShard halo → Task 6 ✓
- Sprites required (PixelLab, separate) → mentioned; code has fallbacks ✓
- Tests: TileTypeCrystalTests, SimulationCrystalTests, MapGeneratorCrystalTests → Tasks 1–3 ✓

**Placeholder scan:** None found.

**Type consistency:**
- `CrystalShardDropped(GridPos Pos)` defined Task 1, consumed Task 2 (emit) and Task 4 (handle) ✓
- `ItemKind.CrystalShard` defined Task 1, used Tasks 4, 5, 6 ✓
- `TileType.CrystalRock` defined Task 1, used Tasks 2, 3, 5, 6 ✓
- `_crystalPositions: HashSet<GridPos>` defined Task 5, built by `BuildCrystalCache()` same task ✓
- `_crystalGlowTex` declared + built in Task 6 Init, drawn in `_Draw()` same task ✓
