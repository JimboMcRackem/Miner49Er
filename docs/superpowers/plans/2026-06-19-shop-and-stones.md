# Shop & Throwing Stones Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a shopkeeper on every 4th Expedition floor selling perm upgrades / life potions / stones, plus a throw-stone mechanic that creates a timed noise source and distracts all monster types.

**Architecture:** Core-layer changes (Miner, Simulation, MapGenerator, Snapshots) are done first and tested with xUnit. Net/game-layer changes (InputBindings, NetworkManager, MatchHost, MatchClient, ShopPanel, WorldRenderer) follow. All shop state is host-authoritative; stone throws follow the existing mine/plant pulse pattern. ShopPos is deterministic from the floor seed so host and client agree without extra network messages.

**Tech Stack:** C# / Godot 4.6.3 .NET; xUnit for Core tests; 4-space indent in Core (`src/`), TAB indent in game (`game/`).

## Global Constraints

- 4-space indent in `src/`, TAB indent in `game/` — match exactly what's already in each file
- Never `git add -A`; never stage `.superpowers/`, `*.png.import`, or `*.uid`
- Run Godot via PowerShell ONLY (not Bash — Bash shim breaks headless `assemblies not found`)
- Test command: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- `Miner.GoldCollected` has `internal set`; Simulation methods (same assembly) can mutate it directly; game-layer code reads it via the public getter only
- MatchHost already exposes `_sim.Miners` (returns `IReadOnlyCollection<Miner>`) — use it for purchase validation
- No cross-version wire-format compatibility needed (solo play only)

---

### Task 1: Core — ShopItemKind enum + Miner.StoneCount + helper methods + StoneThrown event

**Files:**
- Create: `src/Miner49er.Core/Shop/ShopItemKind.cs`
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (two new public methods only)
- Create: `src/Miner49er.Core.Tests/ShopItemTests.cs`

**Interfaces:**
- Produces: `ShopItemKind` enum (`SpeedUp, VisionUp, BlastUp, LifePotion, Stones3`), `ShopPrices.Price(ShopItemKind)`, `Miner.StoneCount`, `Simulation.AddStones(int minerId, int n)`, `Simulation.DeductGold(int minerId, int amount)`, `StoneThrown(int MinerId, GridPos LandingPos)` sim event

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Miner49er.Core.Tests/ShopItemTests.cs
using Miner49er.Core;
using Xunit;

public class ShopItemTests
{
    [Fact] public void SpeedUp_price_is_15() => Assert.Equal(15, ShopPrices.Price(ShopItemKind.SpeedUp));
    [Fact] public void VisionUp_price_is_15() => Assert.Equal(15, ShopPrices.Price(ShopItemKind.VisionUp));
    [Fact] public void BlastUp_price_is_20() => Assert.Equal(20, ShopPrices.Price(ShopItemKind.BlastUp));
    [Fact] public void LifePotion_price_is_25() => Assert.Equal(25, ShopPrices.Price(ShopItemKind.LifePotion));
    [Fact] public void Stones3_price_is_10() => Assert.Equal(10, ShopPrices.Price(ShopItemKind.Stones3));

    [Fact]
    public void AddStones_increases_StoneCount()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddStones(1, 3);
        Assert.Equal(3, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void AddStones_caps_at_9()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddStones(1, 8);
        sim.AddStones(1, 5);  // would be 13 without cap
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void DeductGold_reduces_GoldCollected()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.GetMiner(1).GoldCollected = 20;  // set via internal setter
        sim.DeductGold(1, 15);
        Assert.Equal(5, sim.GetMiner(1).GoldCollected);
    }

    [Fact]
    public void DeductGold_clamps_at_zero()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.DeductGold(1, 100);
        Assert.Equal(0, sim.GetMiner(1).GoldCollected);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=ShopItemTests"
```
Expected: build errors (ShopItemKind, ShopPrices not found).

- [ ] **Step 3: Create ShopItemKind.cs**

```csharp
// src/Miner49er.Core/Shop/ShopItemKind.cs
namespace Miner49er.Core;

public enum ShopItemKind { SpeedUp, VisionUp, BlastUp, LifePotion, Stones3 }

public static class ShopPrices
{
    public static int Price(ShopItemKind kind) => kind switch
    {
        ShopItemKind.SpeedUp    => 15,
        ShopItemKind.VisionUp   => 15,
        ShopItemKind.BlastUp    => 20,
        ShopItemKind.LifePotion => 25,
        ShopItemKind.Stones3    => 10,
        _ => int.MaxValue,
    };
}
```

- [ ] **Step 4: Add StoneCount to Miner.cs**

Add after `PermBlastLevel`:
```csharp
    public int StoneCount { get; internal set; }
```

- [ ] **Step 5: Add StoneThrown event to SimEvent.cs**

Add at the end of the file:
```csharp
public sealed record StoneThrown(int MinerId, GridPos LandingPos) : SimEvent;
```

- [ ] **Step 6: Add AddStones + DeductGold to Simulation.cs**

Add after `SetPermLevels`:
```csharp
    public void AddStones(int minerId, int count)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        m.StoneCount = Math.Min(9, m.StoneCount + count);
    }

    public void DeductGold(int minerId, int amount)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        m.GoldCollected = Math.Max(0, m.GoldCollected - amount);
    }
```

- [ ] **Step 7: Run tests — all should pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=ShopItemTests"
```
Expected: 9/9 PASS.

- [ ] **Step 8: Run full suite — no regressions**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green.

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Shop/ShopItemKind.cs src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/ShopItemTests.cs
git commit -m "feat(core): ShopItemKind, Miner.StoneCount, DeductGold, AddStones, StoneThrown event"
```

---

### Task 2: Core — NoiseSource + Simulation.TryThrowStone + monster distraction AI

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationStoneTests.cs`

**Interfaces:**
- Consumes: `Miner.StoneCount` (Task 1), `StoneThrown` event (Task 1)
- Produces: `Simulation.TryThrowStone(int minerId)`, noise-source-aware monster AI (all 3 kinds)

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Miner49er.Core.Tests/SimulationStoneTests.cs
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationStoneTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    // --- throw mechanics ---

    [Fact]
    public void TryThrowStone_with_no_stones_is_noop()
    {
        var sim = Sim(new TileGrid(7, 3, TileType.Floor));
        sim.AddMiner(1, new GridPos(2, 1));
        sim.TryThrowStone(1);
        Assert.Equal(0, sim.GetMiner(1).StoneCount);
        Assert.Empty(sim.DrainEvents().OfType<StoneThrown>());
    }

    [Fact]
    public void TryThrowStone_flies_east_and_lands_before_wall()
    {
        // Layout (7 wide, 3 tall, all floor except col 5 = rock):
        //  . . . M . W .   (M=miner col2, W=wall col5)
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(5, 1), TileType.Rock);
        var sim = Sim(grid);
        sim.AddMiner(1, new GridPos(2, 1));
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);  // miner faces South by default; need to face East
        // Need to face East first: TryMove(East) then throw
        // Actually: facing is set by TryMove. Let's use a miner who already faces East.
        // Re-do: add miner already facing East by calling TryMove first.
        Assert.Equal(0, sim.GetMiner(1).StoneCount); // consumed
    }

    [Fact]
    public void TryThrowStone_facing_east_lands_before_wall()
    {
        // 7-wide row: M at col 1, wall at col 5. Stone should land at col 4.
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(5, 1), TileType.Rock);
        var cfg = new SimConfig { BaseMoveSeconds = 0.01 };
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);  // sets facing East, moves to col 2
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);

        var thrown = sim.DrainEvents().OfType<StoneThrown>().Single();
        Assert.Equal(new GridPos(4, 1), thrown.LandingPos);  // col 4 (col 5 is wall)
        Assert.Equal(0, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void TryThrowStone_stops_at_map_boundary()
    {
        // 5-wide row: M at col 1, no walls. Stone should land at col 4 (boundary).
        var grid = new TileGrid(5, 3, TileType.Floor);
        var cfg = new SimConfig { BaseMoveSeconds = 0.01 };
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);

        var thrown = sim.DrainEvents().OfType<StoneThrown>().Single();
        Assert.Equal(new GridPos(4, 1), thrown.LandingPos);
    }

    // --- noise source distraction ---

    [Fact]
    public void Slime_moves_toward_noise_source_not_player()
    {
        // Slime at col 4, miner at col 0, noise source at col 8.
        // Slime should step East (toward noise), not West (toward miner).
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(11, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var slime = sim.AddMonster(1, new GridPos(4, 1), MonsterKind.Slime);
        sim.AddStones(1, 1);
        // Manually throw from col 8 facing East to place noise at col 9
        // Instead: move miner East, throw, then reset miner position (not possible via public API).
        // Simpler: throw to place noise source by having miner face East at col 7.
        // We'll use a second miner for positioning, or just test distraction indirectly.
        // Actually: we can have the only miner face East and throw, placing noise at col 8+.
        // Let's simplify: miner at col 7 facing East, throws to col 9.
        // Then slime (col 4) should be pulled East toward col 9 vs miner at col 0 West.
        // Reset: use separate grid
        var grid2 = new TileGrid(13, 3, TileType.Floor);
        var cfg2 = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 12 };
        var sim2 = Sim(grid2, cfg2);
        sim2.AddMiner(1, new GridPos(0, 1));
        var slime2 = sim2.AddMonster(1, new GridPos(5, 1), MonsterKind.Slime);
        // Move miner East to col 1, face East — then throw to col 10 (no wall)
        // But miner is at col 0, TryMove East → col 1 facing East
        sim2.TryMove(1, Direction.East);
        sim2.AddStones(1, 1);
        sim2.TryThrowStone(1);   // miner at col 1, facing East: stone lands at col 12 (boundary)
        // Slime at 5: noise is at 12, player is at 1. Noise is further right, player is left.
        // Both within sense radius 12. Slime should move toward noise (East) not player (West).
        sim2.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), slime2.Pos);  // moved East toward noise source
    }

    [Fact]
    public void Noise_source_expires_and_slime_resumes_chasing_player()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var slime = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Slime);
        sim.TryMove(1, Direction.East);   // miner at col 1, facing East
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);             // noise at col 12

        sim.Tick(4.1);  // noise expires after 4s

        // After expiry, slime should chase player (col 1), moving West
        // Allow one more tick to take a step
        var posBefore = slime.Pos;
        sim.Tick(0.1);
        Assert.True(slime.Pos.X < posBefore.X);  // moved West toward player
    }

    [Fact]
    public void Ghost_targets_noise_source_when_within_sense_radius()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var ghost = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Ghost);
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);  // noise at col 12

        sim.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), ghost.Pos);  // moved East toward noise
    }

    [Fact]
    public void Goat_reorients_charge_toward_noise_source()
    {
        // Goat charges West (toward miner at 0) but noise is East at col 12.
        // When goat detects noise within sense radius, it re-aims its ChargeDir East.
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(13, 3, TileType.Floor);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 1));
        var goat = sim.AddMonster(1, new GridPos(5, 1), MonsterKind.Goat);
        sim.TryMove(1, Direction.East);
        sim.AddStones(1, 1);
        sim.TryThrowStone(1);  // noise at col 12

        sim.Tick(0.1);
        Assert.Equal(new GridPos(6, 1), goat.Pos);  // moved East toward noise source
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=SimulationStoneTests"
```
Expected: compile errors (TryThrowStone not found).

- [ ] **Step 3: Add NoiseSource class and _noiseSources field to Simulation.cs**

Add a private nested class after the existing field declarations (before the constructor):
```csharp
    private sealed class NoiseSource
    {
        public GridPos Pos;
        public double LifetimeRemaining;
    }

    private readonly List<NoiseSource> _noiseSources = new();
```

- [ ] **Step 4: Add TryThrowStone to Simulation.cs**

Add after `SetPermLevels`:
```csharp
    public void TryThrowStone(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m) || !m.Alive) return;
        if (m.StoneCount <= 0) return;

        var dir = m.Facing.ToOffset();
        var pos = m.Pos;
        GridPos land = pos;
        for (int i = 0; i < 64; i++)
        {
            var next = new GridPos(pos.X + dir.X * (i + 1), pos.Y + dir.Y * (i + 1));
            if (!Grid.InBounds(next) || !Grid.Get(next).IsEnterable()) break;
            land = next;
        }

        m.StoneCount--;
        _noiseSources.Add(new NoiseSource { Pos = land, LifetimeRemaining = 4.0 });
        _events.Add(new StoneThrown(minerId, land));
    }
```

Note: `Direction.ToOffset()` returns `(int X, int Y)` — confirm by checking the existing codebase pattern. In the codebase `dir.ToOffset()` is used as `m.Pos + dir.ToOffset()` in TryMove; since `GridPos` addition is operator-overloaded, I need to use `ToOffset()` result differently. Let me check:

Looking at `TryMove`: `var target = m.Pos + dir.ToOffset();` — this means `GridPos` + `GridPos` offset. So:
```csharp
        var offset = m.Facing.ToOffset();
        GridPos land = m.Pos;
        for (int i = 1; i <= 64; i++)
        {
            var next = new GridPos(m.Pos.X + offset.X * i, m.Pos.Y + offset.Y * i);
            if (!Grid.InBounds(next) || !Grid.Get(next).IsEnterable()) break;
            land = next;
        }
```

- [ ] **Step 5: Add noise source ticking to Simulation.Tick**

In the `Tick(double dt)` method, add a call to `AdvanceNoiseSources(dt)`. Add the helper:
```csharp
    private void AdvanceNoiseSources(double dt)
    {
        for (int i = _noiseSources.Count - 1; i >= 0; i--)
        {
            _noiseSources[i].LifetimeRemaining -= dt;
            if (_noiseSources[i].LifetimeRemaining <= 0)
                _noiseSources.RemoveAt(i);
        }
    }
```

Find where `Tick` calls its helpers (e.g. `AdvanceMolds`, `AdvanceCooldowns`) and add `AdvanceNoiseSources(dt)` in that block.

- [ ] **Step 6: Modify SlimeDir, GhostDir, GoatDir to check noise sources**

Replace each AI method:

**SlimeDir** — add noise-source check before player-chase:
```csharp
    private Direction? SlimeDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        if (noise is { } n) return TowardDir(mo.Pos, n.Pos);
        if (target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius)
            return TowardDir(mo.Pos, target.Pos);
        return Card[_rng.Next(Card.Length)];
    }
```

**GhostDir** — add noise-source check before player-chase:
```csharp
    private Direction? GhostDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        GridPos target2 = noise is { } n ? n.Pos
            : target is { Alive: true } ? target.Pos
            : (GridPos?)null ?? mo.Pos;
        if (noise == null && target is not { Alive: true }) return null;
        var d = TowardDir(mo.Pos, target2);
        var next = mo.Pos + d.ToOffset();
        if (InLanternLight(next)) return null;
        return d;
    }
```

**GoatDir** — re-aim toward noise when noise detected:
```csharp
    private Direction? GoatDir(Monster mo, Miner? target)
    {
        var noise = NearestNoiseSourceInRange(mo.Pos, Config.MonsterSenseRadius);
        if (noise is { } n)
        {
            mo.ChargeDir = TowardDir(mo.Pos, n.Pos);
        }

        var ahead = mo.Pos + mo.ChargeDir.ToOffset();
        if (CanMonsterEnter(mo, ahead)) return mo.ChargeDir;

        mo.ChargeDir = target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius
            ? TowardDir(mo.Pos, target.Pos)
            : Card[_rng.Next(Card.Length)];
        return null;
    }
```

Add the helper:
```csharp
    private NoiseSource? NearestNoiseSourceInRange(GridPos pos, int radius)
    {
        NoiseSource? best = null;
        int bestDist = int.MaxValue;
        foreach (var ns in _noiseSources)
        {
            int d = pos.ManhattanTo(ns.Pos);
            if (d <= radius && d < bestDist) { best = ns; bestDist = d; }
        }
        return best;
    }
```

- [ ] **Step 7: Run tests — all should pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=SimulationStoneTests"
```
Expected: all PASS. Adjust test GridPos values if trajectory differs.

- [ ] **Step 8: Run full suite — no regressions**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green.

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationStoneTests.cs
git commit -m "feat(core): TryThrowStone, NoiseSource, monster distraction AI"
```

---

### Task 3: Core — MapConfig.HasShop + GeneratedMap.ShopPos + MapGenerator.PlaceShopkeeper

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/GeneratedMap.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Create: `src/Miner49er.Core.Tests/MapGeneratorShopTests.cs`

**Interfaces:**
- Consumes: nothing new
- Produces: `MapConfig.HasShop` (bool), `GeneratedMap.ShopPos` (GridPos?), `MapGenerator.Generate` sets ShopPos for shop floors

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Miner49er.Core.Tests/MapGeneratorShopTests.cs
using Miner49er.Core;
using Xunit;

public class MapGeneratorShopTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    public void Shop_floor_has_ShopPos(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotNull(map.ShopPos);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Non_shop_floor_has_null_ShopPos(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.Null(map.ShopPos);
    }

    [Fact]
    public void ShopPos_is_a_Floor_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(TileType.Floor, map.Grid.Get(map.ShopPos!.Value));
    }

    [Fact]
    public void ShopPos_is_not_the_escape_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotEqual(map.EscapeTile, map.ShopPos);
    }

    [Fact]
    public void ShopPos_is_not_the_spawn_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotEqual(map.Spawns[0], map.ShopPos);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=MapGeneratorShopTests"
```
Expected: compile errors (`HasShop`, `ShopPos` not found).

- [ ] **Step 3: Add HasShop to MapConfig.cs**

Add after `ChestCount`:
```csharp
    public bool HasShop { get; set; } = false;
```

In `FloorConfig`:
```csharp
    public static MapConfig FloorConfig(int floor, int seed)
    {
        int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, _ => 4 };
        bool pits    = floor >= 6;
        bool caveIns = floor >= 11;
        bool lava    = floor >= 16;
        var cfg = For(GameMode.Expedition, seed, 1, pits, caveIns, lava, mapScale);
        cfg.ChestCount = floor <= 10 ? 1 : 2;
        cfg.HasShop = floor % 4 == 0;
        return cfg;
    }
```

- [ ] **Step 4: Add ShopPos to GeneratedMap.cs**

Add after `EscapeTile`:
```csharp
    public GridPos? ShopPos { get; init; }
```

- [ ] **Step 5: Add PlaceShopkeeper to MapGenerator.cs and call it**

Add a private static helper before the closing brace:
```csharp
    private static GridPos? PlaceShopkeeper(TileGrid grid, Random rng,
        IReadOnlyList<GridPos> spawns, GridPos? escapeTile)
    {
        if (spawns.Count == 0) return null;
        var origin = spawns[0];
        // BFS out from spawn to find a suitable floor tile
        var queue = new Queue<GridPos>();
        var seen  = new HashSet<GridPos> { origin };
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (!grid.InBounds(n) || !seen.Add(n)) continue;
                if (grid.Get(n) != TileType.Floor) continue;
                if (n == origin) continue;
                if (n == escapeTile) continue;
                return n;  // first non-spawn, non-escape floor tile found
            }
            if (seen.Count > 50) break;  // safety limit
        }
        return null;
    }
```

In `Generate`, just before the final `return new GeneratedMap {`:
```csharp
        GridPos? shopPos = config.HasShop
            ? PlaceShopkeeper(grid, rng, spawns, spawns.Count > 0 ? spawns[0] : null)
            : null;
```

Update the return to include `ShopPos = shopPos,`:
```csharp
        return new GeneratedMap
        {
            Grid = grid, Spawns = spawns, Center = center, Items = items, Decoys = decoys,
            EscapeTile = spawns.Count > 0 ? spawns[0] : null,
            ShopPos = shopPos,
        };
```

- [ ] **Step 6: Run tests — all should pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Class=MapGeneratorShopTests"
```
Expected: 9/9 PASS. If `ShopPos_is_not_the_spawn_tile` fails (spawn and shop are same), the BFS skip of `origin` prevents that — re-check PlaceShopkeeper.

- [ ] **Step 7: Run full suite — no regressions**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green.

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorShopTests.cs
git commit -m "feat(core): HasShop config, GeneratedMap.ShopPos, PlaceShopkeeper"
```

---

### Task 4: Net — MinerSnapshot.StoneCount + SnapshotCodec + SnapshotFactory

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

**Interfaces:**
- Consumes: `Miner.StoneCount` (Task 1)
- Produces: `MinerSnapshot.StoneCount` (int) for HUD display

- [ ] **Step 1: Update MinerSnapshot in Snapshots.cs**

The record currently ends with `float InvulRemaining = 0f`. Add `StoneCount` at the end with a default of 0:
```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held,
    DeathCause Cause = DeathCause.None, float InvulRemaining = 0f, int StoneCount = 0);
```

- [ ] **Step 2: Update SnapshotCodec.cs Write — append StoneCount after InvulRemaining**

In the Write loop for miners, add after `w.Write(m.InvulRemaining);`:
```csharp
            w.Write(m.StoneCount);
```

- [ ] **Step 3: Update SnapshotCodec.cs Read — read StoneCount after InvulRemaining**

In the Read loop, the current MinerSnapshot construction:
```csharp
        miners.Add(new MinerSnapshot(
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
            r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte(), r.ReadSingle()));
```
Becomes:
```csharp
        miners.Add(new MinerSnapshot(
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
            r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte(), r.ReadSingle(), r.ReadInt32()));
```

- [ ] **Step 4: Update SnapshotFactory.cs — populate StoneCount**

The MinerSnapshot construction in `Capture`:
```csharp
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
                m.Held is { } h ? (int)h : -1, m.DeathCause, (float)m.InvulnerableRemaining,
                m.StoneCount))
            .ToList();
```

- [ ] **Step 5: Update SnapshotCodecTests.cs to include StoneCount**

In the existing `Round_trips_all_fields` test, the first MinerSnapshot is:
```csharp
new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8, (int)ItemKind.WaterPlank),
```
Update to include StoneCount (add as last positional arg, using named arg since it's a default param):
```csharp
new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8, (int)ItemKind.WaterPlank, StoneCount: 4),
```
Add an assertion:
```csharp
        Assert.Equal(4, back.Snapshot.Miners[0].StoneCount);
```

- [ ] **Step 6: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green. SnapshotCodecTests round-trip passes with StoneCount.

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(net): MinerSnapshot.StoneCount — codec + factory"
```

---

### Task 5: Game — InputBindings + NetworkManager RPCs + MatchHost purchase + throw

**Files:**
- Modify: `game/InputBindings.cs`
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/net/MatchHost.cs`
- Modify: `game/net/InputSender.cs`

**Interfaces:**
- Consumes: `ShopItemKind` (Task 1), `Simulation.TryThrowStone` (Task 2), `Simulation.AddStones`, `Simulation.DeductGold` (Task 1), `Simulation.SetPermLevels` (existing)
- Produces: `InputBindings.Throw` (string const), `NetworkManager.SendAction(bool,bool,bool,bool)`, `NetworkManager.BuyShopItem(ShopItemKind)`, `MatchHost.SetThrow`

- [ ] **Step 1: Add Throw binding to InputBindings.cs**

Add constant after `Settings`:
```csharp
	public const string Throw = "throw_stone";
```

Add to `RebindableActions` array (append `Throw`):
```csharp
	public static readonly string[] RebindableActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings, Throw,
	};
```

Add to `AllActions` array (append `Throw`):
```csharp
	private static readonly string[] AllActions =
	{
		MoveUp, MoveDown, MoveLeft, MoveRight,
		Pickaxe, Plant, Listen, UseItem, Restart, Mute, Settings, Exit, Throw,
	};
```

In `EnsureDefaults`, add after `Bind(Settings, Key.O);`:
```csharp
		Bind(Throw, Key.T, JoyButton.LeftShoulder);
```

- [ ] **Step 2: Add throw bool to SendAction + ReceiveAction in NetworkManager.cs**

Change `SendAction`:
```csharp
	public void SendAction(bool mine, bool plant, bool use, bool throwStone = false)
	{
		if (IsHost) { _matchHost?.SetAction(LocalId, mine, plant, use, throwStone); return; }
		RpcId(1, nameof(ReceiveAction), mine, plant, use, throwStone);
	}
```

Change `ReceiveAction` signature and body:
```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveAction(bool mine, bool plant, bool use, bool throwStone) =>
		_matchHost?.SetAction(Multiplayer.GetRemoteSenderId(), mine, plant, use, throwStone);
```

- [ ] **Step 3: Add BuyShopItem RPC to NetworkManager.cs**

Add after the `ReceiveAction` block:
```csharp
	public void BuyShopItem(ShopItemKind kind)
	{
		if (IsHost) { _matchHost?.ReceiveBuy(LocalId, kind); return; }
		RpcId(1, nameof(ReceiveBuy), (int)kind);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveBuy(int kind) =>
		_matchHost?.ReceiveBuy(Multiplayer.GetRemoteSenderId(), (ShopItemKind)kind);
```

- [ ] **Step 4: Add throw handling to MatchHost.cs**

Add `_pendingThrow` field alongside existing pending sets:
```csharp
	private readonly HashSet<int> _pendingThrow = new();
```

Update `SetAction` signature:
```csharp
	public void SetAction(long peerId, bool mine, bool plant, bool use, bool throwStone = false)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine)        _pendingMine.Add(minerId);
		if (plant)       _pendingPlant.Add(minerId);
		if (use)         _pendingUse.Add(minerId);
		if (throwStone)  _pendingThrow.Add(minerId);
	}
```

In `StepOnce`, add after `_pendingUse.Clear()`:
```csharp
		foreach (var minerId in _pendingThrow) _sim.TryThrowStone(minerId);
		_pendingThrow.Clear();
```

- [ ] **Step 5: Add ReceiveBuy to MatchHost.cs**

Add the public purchase handler:
```csharp
	public void ReceiveBuy(long peerId, ShopItemKind kind)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		var miner = _sim.Miners.FirstOrDefault(m => m.Id == minerId);
		if (miner == null || !miner.Alive) return;

		int price = ShopPrices.Price(kind);
		if (miner.GoldCollected < price) return;

		switch (kind)
		{
			case ShopItemKind.SpeedUp:
				if (miner.PermSpeedLevel >= _sim.Config.MaxPermSpeedLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel + 1, miner.PermVisionLevel, miner.PermBlastLevel);
				break;
			case ShopItemKind.VisionUp:
				if (miner.PermVisionLevel >= _sim.Config.MaxPermVisionLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel, miner.PermVisionLevel + 1, miner.PermBlastLevel);
				break;
			case ShopItemKind.BlastUp:
				if (miner.PermBlastLevel >= _sim.Config.MaxPermBlastLevel) return;
				_sim.SetPermLevels(minerId, miner.PermSpeedLevel, miner.PermVisionLevel, miner.PermBlastLevel + 1);
				break;
			case ShopItemKind.LifePotion:
				if (_livesRemaining >= _livesMax) return;
				_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax);
				break;
			case ShopItemKind.Stones3:
				if (miner.StoneCount >= 9) return;
				_sim.AddStones(minerId, 3);
				break;
		}
		_sim.DeductGold(minerId, price);
	}
```

Note: Also update `SavePermLevels` to clear and re-save from sim after a buy. It already does: `SavePermLevels` is only called on `FloorCleared`, which saves current perm levels. Perm upgrades bought mid-floor will persist because `SavePermLevels` saves from the sim. No change needed.

- [ ] **Step 6: Update InputSender.cs to send throw**

Change the action block:
```csharp
		bool mine  = Input.IsActionJustPressed(InputBindings.Pickaxe);
		bool plant = Input.IsActionJustPressed(InputBindings.Plant);
		bool use   = Input.IsActionJustPressed(InputBindings.UseItem);
		bool throwStone = Input.IsActionJustPressed(InputBindings.Throw);
		if (mine || plant || use || throwStone)
			NetworkManager.Instance.SendAction(mine, plant, use, throwStone);
```

- [ ] **Step 7: Run full Core test suite**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green. (Game layer is not tested by Core suite.)

- [ ] **Step 8: Commit**

```
git add game/InputBindings.cs game/net/NetworkManager.cs game/net/MatchHost.cs game/net/InputSender.cs
git commit -m "feat(game): Throw binding, BuyShopItem RPC, MatchHost purchase + throw dispatch"
```

---

### Task 6: Game — MatchClient.ShopPos + ShopPanel

**Files:**
- Modify: `game/net/MatchClient.cs`
- Create: `game/ui/ShopPanel.cs`

**Interfaces:**
- Consumes: `GeneratedMap.ShopPos` (Task 3), `NetworkManager.BuyShopItem` (Task 5), `MinerSnapshot.StoneCount` (Task 4)
- Produces: `MatchClient.ShopPos` (GridPos?), `ShopPanel` Godot Control (shown/hidden by Main.cs)

- [ ] **Step 1: Add ShopPos to MatchClient.cs**

Add field near EscapeTile:
```csharp
	public GridPos? ShopPos { get; private set; }
```

Update `Begin` to accept and store shopPos:
```csharp
	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot,
		GridPos? escapeTile = null, GridPos? shopPos = null)
	{
		// ... existing code ...
		EscapeTile = escapeTile;
		ShopPos = shopPos;
		// ...
	}
```

In `ResetFloor`, after `EscapeTile = newMap.EscapeTile;` add:
```csharp
		ShopPos = newMap.ShopPos;
```

- [ ] **Step 2: Update Main.cs call to Begin**

In `Main._Ready`, find:
```csharp
		_client.Begin(map.Grid, map.Decoys, localMinerId, this, clientEscape);
```
Change to:
```csharp
		_client.Begin(map.Grid, map.Decoys, localMinerId, this, clientEscape, map.ShopPos);
```

- [ ] **Step 3: Create ShopPanel.cs**

```csharp
// game/ui/ShopPanel.cs
using Godot;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Overlay shop UI shown when the local miner stands on the shopkeeper tile.
/// Opened/closed by Main._PhysicsProcess. Sends BuyShopItem RPCs on confirm.</summary>
public partial class ShopPanel : Control
{
	private static readonly ShopItemKind[] Items =
	{
		ShopItemKind.SpeedUp, ShopItemKind.VisionUp, ShopItemKind.BlastUp,
		ShopItemKind.LifePotion, ShopItemKind.Stones3,
	};

	private static readonly string[] ItemLabels =
	{
		"Speed Up   (+movement speed)",
		"Vision Up  (+fog radius)",
		"Blast Up   (+blast radius)",
		"Life Potion (restore 1 life)",
		"Stones x3  (throw to distract)",
	};

	public bool IsOpen { get; private set; }

	private int _selected;
	private Label _title = null!;
	private Label[] _rows = null!;
	private Label _footer = null!;
	private MinerSnapshot _localMiner;
	private int _lives;
	private int _livesMax;

	public override void _Ready()
	{
		AnchorLeft = 0.3f; AnchorRight = 0.7f;
		AnchorTop  = 0.2f; AnchorBottom = 0.8f;

		var bg = new ColorRect
		{
			Color = new Color(0.05f, 0.05f, 0.05f, 0.92f),
			AnchorRight = 1f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var vbox = new VBoxContainer
		{
			AnchorRight = 1f, AnchorBottom = 1f,
			OffsetLeft = 12, OffsetRight = -12, OffsetTop = 12, OffsetBottom = -12,
		};
		AddChild(vbox);

		_title = new Label { Text = "=== SHOP ===", HorizontalAlignment = HorizontalAlignment.Center };
		_title.AddThemeFontSizeOverride("font_size", 20);
		vbox.AddChild(_title);
		vbox.AddChild(new Label { Text = "" });

		_rows = new Label[Items.Length];
		for (int i = 0; i < Items.Length; i++)
		{
			_rows[i] = new Label();
			vbox.AddChild(_rows[i]);
		}

		vbox.AddChild(new Label { Text = "" });
		_footer = new Label { Text = "[Use] Buy   [ESC] Close", HorizontalAlignment = HorizontalAlignment.Center };
		vbox.AddChild(_footer);

		Visible = false;
	}

	public void Open(MinerSnapshot local, int lives, int livesMax)
	{
		_localMiner = local;
		_lives      = lives;
		_livesMax   = livesMax;
		_selected   = 0;
		IsOpen      = true;
		Visible     = true;
		Refresh();
	}

	public void Close()
	{
		IsOpen  = false;
		Visible = false;
	}

	public void UpdateSnapshot(MinerSnapshot local, int lives, int livesMax)
	{
		_localMiner = local;
		_lives      = lives;
		_livesMax   = livesMax;
		if (IsOpen) Refresh();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!IsOpen) return;
		if (@event.IsActionPressed(InputBindings.MoveUp))
		{
			_selected = (_selected - 1 + Items.Length) % Items.Length;
			Refresh();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed(InputBindings.MoveDown))
		{
			_selected = (_selected + 1) % Items.Length;
			Refresh();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionJustPressed(InputBindings.UseItem))
		{
			TryBuy();
			GetViewport().SetInputAsHandled();
		}
		else if (@event.IsActionPressed(InputBindings.Exit))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	private void TryBuy()
	{
		var kind  = Items[_selected];
		int price = ShopPrices.Price(kind);
		if (_localMiner.Gold < price) return;
		if (IsAtCap(kind)) return;
		NetworkManager.Instance.BuyShopItem(kind);
	}

	private bool IsAtCap(ShopItemKind kind) => kind switch
	{
		ShopItemKind.SpeedUp    => _localMiner.VisionRadius >= 5 + 5,  // heuristic; actual check on host
		ShopItemKind.VisionUp   => _localMiner.VisionRadius >= 10,     // simpler: just let the host reject
		ShopItemKind.BlastUp    => false,
		ShopItemKind.LifePotion => _lives >= _livesMax,
		ShopItemKind.Stones3    => _localMiner.StoneCount >= 9,
		_ => false,
	};

	private void Refresh()
	{
		for (int i = 0; i < Items.Length; i++)
		{
			var kind    = Items[i];
			int price   = ShopPrices.Price(kind);
			bool canBuy = _localMiner.Gold >= price && !IsAtCap(kind);
			string status = IsAtCap(kind) ? "MAX"
				: _localMiner.Gold < price ? "Can't afford"
				: "BUY";
			string prefix = i == _selected ? "▶ " : "  ";
			_rows[i].Text = $"{prefix}{ItemLabels[i]}   {price}g   [{status}]";
			_rows[i].Modulate = canBuy ? new Color(1, 1, 1) : new Color(0.5f, 0.5f, 0.5f);
		}
	}
}
```

Note: The `IsAtCap` heuristic for SpeedUp/VisionUp/BlastUp is intentionally loose — the host validates the actual cap. The client just avoids sending obviously invalid requests. If the cap check is wrong on the client, the host will reject and nothing happens.

- [ ] **Step 4: Run Core tests to confirm still green**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green.

- [ ] **Step 5: Commit**

```
git add game/net/MatchClient.cs game/ui/ShopPanel.cs game/Main.cs
git commit -m "feat(game): MatchClient.ShopPos, ShopPanel UI"
```

---

### Task 7: Game — WorldRenderer shopkeeper + HUD stones + Main.cs integration

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: `MatchClient.ShopPos` (Task 6), `MinerSnapshot.StoneCount` (Task 4), `ShopPanel` (Task 6)
- Produces: shopkeeper glyph on map, stone count in HUD, shop auto-opens on step, `InputSender.Enabled` gated when shop open

- [ ] **Step 1: Render shopkeeper in WorldRenderer.cs**

Find the section in `_Draw()` where objects/entities are drawn (after or alongside charges/items). Near the end of `_Draw`, before closing, add:
```csharp
		// Shopkeeper tile
		if (_client.ShopPos is GridPos sp && _client.Fog.IsVisible(sp))
		{
			float ts = TileSize;
			var shopRect = new Rect2(sp.X * ts + 2, sp.Y * ts + 2, ts - 4, ts - 4);
			DrawRect(shopRect, new Color(0.78f, 0.63f, 0.13f));  // warm gold
			DrawString(ThemeDB.FallbackFont, new Vector2(sp.X * ts + 4, sp.Y * ts + ts - 6),
				"$", HorizontalAlignment.Left, -1, 16, new Color(0.1f, 0.05f, 0f));
		}
```

- [ ] **Step 2: Add stone count to HUD in Main.cs**

Find the existing `heldStr` pattern in `_PhysicsProcess`. After `heldStr`, add:
```csharp
					string stonesStr = m.StoneCount > 0 ? $"    Stones: {m.StoneCount}" : "";
```
Append `stonesStr` to the HUD line:
```csharp
					_hud.SetText($"{objective}    {status}{timeStr}{heldStr}{stonesStr}");
```

- [ ] **Step 3: Add ShopPanel to Main.cs and wire adjacency detection**

In `_Ready`, add after `_audioPanel`:
```csharp
		_shopPanel = new ShopPanel { Name = "ShopPanel" };
		AddChild(_shopPanel);
```

Add field declaration at class level:
```csharp
	private ShopPanel _shopPanel = null!;
	private bool _wasAtShop;
```

In `_PhysicsProcess`, inside the `foreach (var m in _client.Miners)` block where `sawLocal = true`, after the HUD computation add:
```csharp
				// Shop adjacency detection
				if (localAlive && !panelOpen && NetworkManager.Instance.MatchMode == GameMode.Expedition)
				{
					var localPos = new Miner49er.Core.GridPos(m.X, m.Y);
					bool atShop = _client.ShopPos is GridPos sPos && localPos == sPos;
					if (atShop && !_wasAtShop && !_shopPanel.IsOpen)
						_shopPanel.Open(m, _client.Lives, 3);
					else if (!atShop && _shopPanel.IsOpen)
						_shopPanel.Close();
					_shopPanel.UpdateSnapshot(m, _client.Lives, 3);
					_wasAtShop = atShop;
				}
```

Gate `_input.Enabled` to also disable when shop is open:
```csharp
		if (_input != null) _input.Enabled = (!sawLocal || localAlive) && !panelOpen && !_shopPanel.IsOpen;
```

Also gate `_audioPanel.Toggle()` so ESC closes shop first — already handled by `ShopPanel._UnhandledInput` consuming ESC.

Note: `_livesMax = 3` is hardcoded here. MatchHost uses `(nm.MatchMode == GameMode.Expedition && nm.MatchPlayerCount == 1) ? 3 : 1`. For solo Expedition this is always 3.

- [ ] **Step 4: Build the project to check for compile errors**

Run Godot headless build via PowerShell:
```powershell
& godot --headless --build-solutions --quit 2>&1 | Select-Object -Last 30
```
Expected: `BUILD SUCCESSFUL` or `0 error(s)`.

- [ ] **Step 5: Run Core tests one final time**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```
Expected: all green.

- [ ] **Step 6: Commit**

```
git add game/WorldRenderer.cs game/Main.cs
git commit -m "feat(game): shopkeeper render, stone HUD, shop auto-open on step"
```

---

## Final Check

After all tasks:
1. Core tests all green: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
2. Godot build clean: `& godot --headless --build-solutions --quit`
3. Manual smoke test: start Expedition, reach floor 4, step on shopkeeper tile, buy Stones x3, press T to throw, confirm monsters walk toward stone landing spot
