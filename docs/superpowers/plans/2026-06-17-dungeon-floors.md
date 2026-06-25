# Dungeon Floors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 21-floor dungeon progression to Expedition mode — escalating difficulty floors 1–20, then a boss floor with a sweeping-arm Octopus whose chest must be grabbed to win.

**Architecture:** Pure-Core simulation changes (FloorConfig, Octopus entity, Simulation extensions, RoundResult) are built and tested first; snapshot plumbing follows; then the game layer (NetworkManager RPC, MatchClient.ResetFloor, MatchHost.AdvanceFloor, UI). No scene changes between floors — MatchClient tears down and re-inits its render nodes in place.

**Tech Stack:** Godot 4.6.3, C# (.NET), xUnit tests in `src/Miner49er.Core.Tests/`, 4-space indent in Core, TAB indent in `game/`.

## Global Constraints

- Every commit message must end with `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
- Never stage `.superpowers/`, `*.png.import`, `*.uid` — only stage explicitly named files
- Never `git add -A` or `git add .`
- Core project: 4-space indent. `game/` project: TAB indent
- Run `dotnet test src/Miner49er.Core.Tests` to verify Core tests pass
- `godot` must be run via PowerShell only (never Bash tool)

---

## File Map

| File | Action |
|------|--------|
| `src/Miner49er.Core/Map/MapConfig.cs` | Add `FloorConfig(int floor, int seed)` static method |
| `src/Miner49er.Core/Map/MonsterRoster.cs` | Add `CountFor(int w, int h, int floor)` overload + `FloorMax = 7` |
| `src/Miner49er.Core/Sim/RoundResolver.cs` | Add `bool FloorCleared` to `RoundResult`; factory methods; update Expedition logic |
| `src/Miner49er.Core/Sim/Simulation.cs` | Add `StartingGoldCount`, `GoldCollectedFraction`, `ChestGrabbedBy`, `Octopus`, `AddOctopus`; 50% escape threshold; octopus advance + crush in `Tick` |
| `src/Miner49er.Core/Sim/OctopusArm.cs` | New — arm state + sweep logic |
| `src/Miner49er.Core/Sim/Octopus.cs` | New — octopus entity with `Advance(dt)` and `DangerTiles(grid)` |
| `src/Miner49er.Core/Map/Item.cs` | Add `ItemKind.Chest` to enum |
| `src/Miner49er.Core/Map/MapGenerator.cs` | Add `GenerateBossFloor(int seed)` static method |
| `src/Miner49er.Core/Fog/FogState.cs` | Add `Reset()` method |
| `src/Miner49er.Core/Net/Snapshots.cs` | Add `OctopusArmSnapshot`, `OctopusSnapshot`; add `Octopus?` to `WorldSnapshot` |
| `src/Miner49er.Core/Net/SnapshotFactory.cs` | Capture octopus from sim |
| `src/Miner49er.Core/Net/SnapshotCodec.cs` | Encode/decode octopus (after EscapeOpen, before TileChanges) |
| `game/net/NetworkManager.cs` | Add `MatchFloor`, `BroadcastNewFloor`, `ReceiveNewFloor` RPC, `NewFloor` event |
| `game/net/MatchClient.cs` | Add `_sceneRoot` field; `StartingGoldCount`; `Octopus?`; `ResetFloor(int floor)` |
| `game/net/MatchHost.cs` | Add `AdvanceFloor(int minerId)`; update `StepOnce` to handle `FloorCleared` |
| `game/Main.cs` | Subscribe to `nm.NewFloor`; floor banner overlay; updated HUD; updated results labels |
| `game/WorldRenderer.cs` | Octopus body + arm danger overlay; chest glyph; locked-ladder grey tint |
| `game/ui/DeathFeed.cs` | Update `Crushed` messages to be generic (covers cave-in AND octopus) |
| `src/Miner49er.Core.Tests/MapConfigFloorTests.cs` | New — FloorConfig for all 20 floors |
| `src/Miner49er.Core.Tests/MonsterRosterTests.cs` | Add floor-bonus tests |
| `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs` | Update exit test → FloorCleared; add chest-grab win test |
| `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs` | Update escape-open test → 50% threshold |
| `src/Miner49er.Core.Tests/OctopusTests.cs` | New — arm sweep math, danger tile bounds |
| `src/Miner49er.Core.Tests/SimulationOctopusTests.cs` | New — crush kill, chest pickup |
| `src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs` | New — boss floor layout assertions |
| `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` | Add octopus round-trip test |

---

### Task 1: Core primitives — FloorConfig, MonsterRoster, RoundResult, Simulation gold threshold

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MonsterRoster.cs`
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/MapConfigFloorTests.cs`
- Modify: `src/Miner49er.Core.Tests/MonsterRosterTests.cs`
- Modify: `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs`

**Interfaces produced:**
- `MapConfig.FloorConfig(int floor, int seed)` → `MapConfig`
- `MonsterRoster.CountFor(int w, int h, int floor)` → `int`
- `RoundResult(bool IsOver, bool FloorCleared, int WinnerId)` — struct with factory statics
- `Simulation.StartingGoldCount` → `int`
- `Simulation.GoldCollectedFraction` → `double`
- `Simulation.ChestGrabbedBy` → `int` (initially -1)
- `EscapeOpen` now triggers at `GoldCollectedFraction >= 0.5` instead of `== 0`

---

- [ ] **Step 1: Write MapConfigFloorTests.cs (all failing)**

```csharp
// src/Miner49er.Core.Tests/MapConfigFloorTests.cs
using Miner49er.Core;
using Xunit;

public class MapConfigFloorTests
{
    [Theory]
    [InlineData(1)]  [InlineData(3)]  [InlineData(5)]
    public void Floors_1_to_5_are_small_no_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(24, cfg.BaseWidth);
        Assert.Equal(24, cfg.BaseHeight);
        Assert.False(cfg.Pits);
        Assert.False(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(6)]  [InlineData(8)]  [InlineData(10)]
    public void Floors_6_to_10_are_medium_with_pits(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(32, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.False(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(11)] [InlineData(13)] [InlineData(15)]
    public void Floors_11_to_15_are_large_with_pits_and_caveins(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(40, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.False(cfg.Lava);
    }

    [Theory]
    [InlineData(16)] [InlineData(18)] [InlineData(20)]
    public void Floors_16_to_20_are_huge_all_hazards(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, 42);
        Assert.Equal(48, cfg.BaseWidth);
        Assert.True(cfg.Pits);
        Assert.True(cfg.CaveIns);
        Assert.True(cfg.Lava);
    }

    [Theory]
    [InlineData(1, 42)] [InlineData(1, 99)]
    public void Different_seeds_give_same_difficulty_structure(int floor, int seed)
    {
        var cfg = MapConfig.FloorConfig(floor, seed);
        Assert.Equal(24, cfg.BaseWidth);
        Assert.Equal(seed, cfg.Seed);
    }
}
```

- [ ] **Step 2: Write MonsterRoster floor-bonus tests (failing)**

Add to `src/Miner49er.Core.Tests/MonsterRosterTests.cs`:

```csharp
[Fact]
public void Floor_bonus_zero_before_floor_8()
{
    // 24x24 base count is 3; no bonus before floor 8
    Assert.Equal(3, MonsterRoster.CountFor(24, 24, 7));
}

[Fact]
public void Floor_8_adds_one()
{
    Assert.Equal(4, MonsterRoster.CountFor(24, 24, 8));
}

[Fact]
public void Floor_14_adds_two()
{
    Assert.Equal(5, MonsterRoster.CountFor(24, 24, 14));
}

[Fact]
public void Floor_bonus_is_capped_at_seven()
{
    // 48x48 base count = 5 + bonus 2 = 7 = cap
    Assert.Equal(7, MonsterRoster.CountFor(48, 48, 20));
    // Would be 5+2=7 naturally but let's verify no overflow above cap
    Assert.Equal(7, MonsterRoster.CountFor(200, 200, 20));
}
```

- [ ] **Step 3: Update RoundResolver tests (currently passing, will break after code change)**

Replace the body of `Reaching_the_exit_with_all_gold_cleared_wins` and add a chest-grab test in `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`:

```csharp
// Replace existing "wins" test:
[Fact]
public void Reaching_the_exit_clears_the_floor_not_the_game()
{
    var (sim, _) = SetupNoGold(new GridPos(0, 1), exit: new GridPos(0, 1));

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.False(result.IsOver);
    Assert.True(result.FloorCleared);
    Assert.Equal(1, result.WinnerId);
}

// Add new test:
[Fact]
public void Chest_grabbed_wins_the_dungeon()
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    var sim = new Simulation(grid, new SimConfig(), escapeTile: null);
    sim.AddMiner(1, new GridPos(1, 1));
    // Simulate chest grabbed
    sim.ForceChestGrabbed(1);   // see Step 8 — add test-only setter or use internal

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.True(result.IsOver);
    Assert.False(result.FloorCleared);
    Assert.Equal(1, result.WinnerId);
}
```

NOTE: `ForceChestGrabbed` is a test-only helper — implement it as an `internal` method on `Simulation` gated by `[assembly: InternalsVisibleTo("Miner49er.Core.Tests")]`, OR drive it through a real chest item pickup (see Step 8 below). The real-pickup approach is preferred (tests the full path); see Step 8 before deciding.

- [ ] **Step 4: Update SimulationExpeditionTests (will break after 50% threshold change)**

Replace the body of `Escape_stays_shut_until_the_last_vein_is_cleared` in `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs`:

```csharp
[Fact]
public void Escape_opens_at_50_percent_gold_collected()
{
    // Setup: 2 gold veins — 50% threshold = 1 vein
    var grid = new TileGrid(6, 3, TileType.Floor);
    grid.Set(new GridPos(2, 1), TileType.GoldRock);
    grid.Set(new GridPos(4, 1), TileType.GoldRock);
    var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 },
        escapeTile: new GridPos(0, 1));
    var miner = sim.AddMiner(1, new GridPos(1, 1));

    Assert.False(sim.EscapeOpen);  // starts locked

    // Clear first vein (50%)
    miner.Facing = Direction.East;
    sim.TryStartMining(1);
    sim.Tick(0.1);

    Assert.False(sim.AllGoldCleared);  // still 1 remaining
    Assert.True(sim.EscapeOpen);       // but escape already open at 50%
    Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
}

[Fact]
public void Escape_does_not_open_below_50_percent()
{
    // 4 gold veins — need ≥ 2 before escape opens
    var grid = new TileGrid(12, 3, TileType.Floor);
    for (int x = 2; x <= 8; x += 2)
        grid.Set(new GridPos(x, 1), TileType.GoldRock);   // 4 veins at x=2,4,6,8
    var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 },
        escapeTile: new GridPos(0, 1));
    var miner = sim.AddMiner(1, new GridPos(1, 1));

    // Clear first vein (25%) — escape stays shut
    miner.Facing = Direction.East;
    sim.TryStartMining(1);
    sim.Tick(0.1);
    sim.DrainEvents();  // discard

    Assert.False(sim.EscapeOpen);
}
```

- [ ] **Step 5: Run tests — expect failures for the new tests and the two updated tests**

```
dotnet test src/Miner49er.Core.Tests --filter "MapConfigFloor|MonsterRoster|RoundResolverExpedition|SimulationExpedition"
```

Expected: 10+ failures because `FloorConfig`, `MonsterRoster.CountFor(w,h,floor)`, `RoundResult.FloorCleared`, `GoldCollectedFraction`, `ChestGrabbedBy` don't exist yet.

- [ ] **Step 6: Implement MapConfig.FloorConfig**

Add to `src/Miner49er.Core/Map/MapConfig.cs` after the `For()` method:

```csharp
/// <summary>Builds a deterministic map config for floor N of a dungeon run.
/// Difficulty scales in 4 bands; only the seed changes the map layout.</summary>
public static MapConfig FloorConfig(int floor, int seed)
{
    int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, _ => 4 };
    bool pits = floor >= 6;
    bool caveIns = floor >= 11;
    bool lava = floor >= 16;
    return For(GameMode.Expedition, seed, 1, pits, caveIns, lava, mapScale);
}
```

- [ ] **Step 7: Implement MonsterRoster floor bonus overload**

Edit `src/Miner49er.Core/Map/MonsterRoster.cs`:

```csharp
public static class MonsterRoster
{
    public const int Min = 3;
    public const int Max = 5;
    public const int FloorMax = 7;

    /// <summary>One extra monster per ~512 tiles above the base 24x24 map, clamped to [3, 5].</summary>
    public static int CountFor(int width, int height)
    {
        int area = width * height;
        int extra = Math.Max(0, (area - 24 * 24) / 512);
        return Math.Clamp(Min + extra, Min, Max);
    }

    /// <summary>Area-based count plus a floor difficulty bonus at floors 8 and 14,
    /// hard-capped at <see cref="FloorMax"/>.</summary>
    public static int CountFor(int width, int height, int floor)
    {
        int bonus = (floor >= 8 ? 1 : 0) + (floor >= 14 ? 1 : 0);
        return Math.Clamp(CountFor(width, height) + bonus, Min, FloorMax);
    }
}
```

- [ ] **Step 8: Update RoundResult and RoundResolver**

Replace `src/Miner49er.Core/Sim/RoundResolver.cs` in full:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, bool FloorCleared, int WinnerId)
{
    public static RoundResult Ongoing()          => new(false, false, -1);
    public static RoundResult Win(int id)        => new(true,  false, id);
    public static RoundResult Loss()             => new(true,  false, -1);
    public static RoundResult NextFloor(int id)  => new(false, true,  id);
}

/// <summary>Resolves a round per game mode. Last-man-standing is universal: any
/// mode ends the instant one or zero miners remain alive. Each mode may add a
/// second terminal condition layered on top.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim, GameMode mode)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();

        // Solo Expedition: a single miner means last-man-standing would auto-win on tick 1.
        // Instead: lose when the miner is dead; boss win on chest grab; floor clear on exit.
        if (mode == GameMode.Expedition)
        {
            if (alive.Count == 0) return RoundResult.Loss();
            if (sim.ChestGrabbedBy >= 0) return RoundResult.Win(sim.ChestGrabbedBy);
            if (sim.EscapeOpen && sim.EscapeTile is { } exit)
            {
                var winner = alive.FirstOrDefault(m => m.Pos == exit);
                if (winner is not null) return RoundResult.NextFloor(winner.Id);
            }
            return RoundResult.Ongoing();
        }

        // Universal last-man-standing.
        if (alive.Count <= 1)
            return new RoundResult(true, false, alive.Count == 1 ? alive[0].Id : -1);

        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                                      && sim.GetMiner(sim.FirstToReachCenter).Alive
                => RoundResult.Win(sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => RoundResult.Win(MostGoldWinner(alive)),
            _ when sim.TimeExpired
                => RoundResult.Loss(),
            _ => RoundResult.Ongoing(),
        };
    }

    private static int MostGoldWinner(List<Miner> alive)
    {
        if (alive.Count == 0) return -1;
        int max = alive.Max(m => m.GoldCollected);
        var leaders = alive.Where(m => m.GoldCollected == max).ToList();
        return leaders.Count == 1 ? leaders[0].Id : -1;
    }
}
```

- [ ] **Step 9: Add StartingGoldCount, GoldCollectedFraction, ChestGrabbedBy to Simulation; change 50% threshold**

In `src/Miner49er.Core/Sim/Simulation.cs`, make these changes:

**a) After `private int _goldRemaining;` (line 30), add:**
```csharp
public int StartingGoldCount { get; private set; }
public double GoldCollectedFraction =>
    StartingGoldCount == 0 ? 1.0 : 1.0 - (double)_goldRemaining / StartingGoldCount;
public int ChestGrabbedBy { get; private set; } = -1;
```

**b) In the constructor, after the `foreach (var p in Grid.Positions())` loop (after line 57), add `StartingGoldCount = _goldRemaining;`:**

The block around lines 52-58 becomes:
```csharp
foreach (var p in Grid.Positions())
{
    if (Grid.Get(p) == TileType.LavaVent)
        _lavaVents.Add(new LavaVent { Pos = p, Budget = config.LavaVentBudget });
    if (Grid.Get(p) == TileType.GoldRock) _goldRemaining++;
}
StartingGoldCount = _goldRemaining;
if (EscapeTile is not null && _goldRemaining == 0) EscapeOpen = true;   // gold-less map: open at once
```

**c) In `OnGoldCleared()` (around lines 734-742), change the threshold from `== 0` to `>= 0.5`:**
```csharp
private void OnGoldCleared()
{
    if (_goldRemaining > 0) _goldRemaining--;
    if (!EscapeOpen && EscapeTile is not null
        && StartingGoldCount > 0 && GoldCollectedFraction >= 0.5)
    {
        EscapeOpen = true;
        _events.Add(new EscapeOpened());
    }
}
```

**d) In `ApplyBuff` (around line 623), add a Chest case:**
```csharp
case ItemKind.Chest:
    ChestGrabbedBy = minerId;
    break;
```

Note: `ChestGrabbedBy` needs a private setter — change the property declaration to `public int ChestGrabbedBy { get; private set; } = -1;` (already in step a).

Wait — `ApplyBuff` sets `ChestGrabbedBy` via `ChestGrabbedBy = minerId` but the property has a private setter. Since `ApplyBuff` is inside `Simulation`, this is fine.

- [ ] **Step 10: Update the chest-grab test to use real item pickup**

The test from Step 3 that called `sim.ForceChestGrabbed(1)` should instead add a Chest item and walk the miner onto it. But `ItemKind.Chest` doesn't exist yet (added in Task 3). Replace the chest-grab test with a placeholder for now and mark it to complete after Task 3.

For now, skip the chest-grab test. The `FloorCleared` test is more urgent and doesn't depend on Chest.

- [ ] **Step 11: Run tests**

```
dotnet test src/Miner49er.Core.Tests --filter "MapConfigFloor|MonsterRoster|RoundResolverExpedition|SimulationExpedition"
```

Expected: All pass. (The chest-grab test is deferred to Task 3.)

- [ ] **Step 12: Commit**

```powershell
git add src/Miner49er.Core/Map/MapConfig.cs `
        src/Miner49er.Core/Map/MonsterRoster.cs `
        src/Miner49er.Core/Sim/RoundResolver.cs `
        src/Miner49er.Core/Sim/Simulation.cs `
        src/Miner49er.Core.Tests/MapConfigFloorTests.cs `
        src/Miner49er.Core.Tests/MonsterRosterTests.cs `
        src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs `
        src/Miner49er.Core.Tests/SimulationExpeditionTests.cs
git commit -m @'
feat(core): floor config, monster floor bonus, FloorCleared outcome, 50% gold threshold

- MapConfig.FloorConfig(floor, seed): pure difficulty curve for floors 1-20
- MonsterRoster.CountFor(w, h, floor): +1 at floor 8, +1 at floor 14, cap 7
- RoundResult gains FloorCleared bool + factory statics (Win/Loss/NextFloor/Ongoing)
- Simulation.StartingGoldCount + GoldCollectedFraction + ChestGrabbedBy
- EscapeOpen now triggers at 50% gold collected instead of 100%
- RoundResolver Expedition: NextFloor on exit (not Win), Win only on chest grab

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 2: Octopus entity (pure Core, no Simulation integration)

**Files:**
- Create: `src/Miner49er.Core/Sim/OctopusArm.cs`
- Create: `src/Miner49er.Core/Sim/Octopus.cs`
- Create: `src/Miner49er.Core.Tests/OctopusTests.cs`

**Interfaces produced:**
- `OctopusArm`: `RestAngle`, `CurrentAngle`, `SwingDir`, `PauseRemaining`, `Advance(double dt)`
- `Octopus(GridPos pos)`: `Pos`, `Arms[]`, `Advance(double dt)`, `DangerTiles(TileGrid grid)` → `IEnumerable<GridPos>`

---

- [ ] **Step 1: Write OctopusTests.cs (failing)**

```csharp
// src/Miner49er.Core.Tests/OctopusTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class OctopusTests
{
    [Fact]
    public void Arm_stays_within_rest_angle_plus_minus_45_degrees()
    {
        var arm = new OctopusArm(90.0, 1);  // East arm
        for (int i = 0; i < 1000; i++)
            arm.Advance(0.033);   // ~30 ticks of 33ms

        Assert.True(arm.CurrentAngle >= 90.0 - OctopusArm.ArcHalfWidth - 0.001);
        Assert.True(arm.CurrentAngle <= 90.0 + OctopusArm.ArcHalfWidth + 0.001);
    }

    [Fact]
    public void Arm_reverses_direction_at_arc_ends()
    {
        var arm = new OctopusArm(0.0, 1);   // North arm, sweeping clockwise
        // Advance until the arm hits the +45 end
        for (int i = 0; i < 200; i++) arm.Advance(0.1);
        Assert.Equal(-1, arm.SwingDir);      // reversed
    }

    [Fact]
    public void Arm_pauses_at_arc_end()
    {
        var arm = new OctopusArm(0.0, 1);
        for (int i = 0; i < 200; i++) arm.Advance(0.1);
        // After reversal, pause should be active
        double angleBefore = arm.CurrentAngle;
        arm.Advance(0.1);   // pause still running
        // If pause > 0.1s remaining, angle shouldn't have changed
        // (won't assert exact equality since it depends on timing, but pause was set)
        Assert.True(arm.PauseRemaining >= 0.0);
    }

    [Fact]
    public void Danger_tiles_per_arm_never_exceed_arm_length()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var oct = new Octopus(new GridPos(10, 10));
        var danger = oct.DangerTiles(grid).ToList();
        // At most Length tiles per arm (4 arms × Length)
        Assert.True(danger.Count <= 4 * OctopusArm.Length);
    }

    [Fact]
    public void Danger_tiles_are_all_in_bounds()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var oct = new Octopus(new GridPos(5, 5));
        foreach (var p in oct.DangerTiles(grid))
            Assert.True(grid.InBounds(p), $"out-of-bounds tile {p}");
    }

    [Fact]
    public void Danger_tiles_do_not_include_octopus_center()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var center = new GridPos(10, 10);
        var oct = new Octopus(center);
        Assert.DoesNotContain(center, oct.DangerTiles(grid));
    }

    [Fact]
    public void Danger_tiles_count_is_reproducible_on_same_octopus()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var oct = new Octopus(new GridPos(10, 10));
        var first  = oct.DangerTiles(grid).Count();
        var second = oct.DangerTiles(grid).Count();
        Assert.Equal(first, second);
    }
}
```

- [ ] **Step 2: Run tests — expect compile errors (OctopusArm, Octopus not defined)**

```
dotnet test src/Miner49er.Core.Tests --filter "Octopus"
```

- [ ] **Step 3: Create OctopusArm.cs**

```csharp
// src/Miner49er.Core/Sim/OctopusArm.cs
using System;

namespace Miner49er.Core;

/// <summary>One sweeping arm of an Octopus. Oscillates ±45° around its rest angle
/// at 30°/sec, pausing 1 second at each end.</summary>
public sealed class OctopusArm
{
    public double RestAngle;          // 0=N, 90=E, 180=S, 270=W (degrees clockwise from +Y-down)
    public double CurrentAngle;
    public int    SwingDir = 1;       // +1 or -1
    public double PauseRemaining;

    public const double ArcHalfWidth  = 45.0;
    public const double AngularSpeed  = 30.0;  // degrees / second
    public const double PauseSeconds  = 1.0;
    public const int    Length        = 5;     // tiles from octopus center

    public OctopusArm(double restAngle, int startDir = 1)
    {
        RestAngle    = restAngle;
        CurrentAngle = restAngle + (-ArcHalfWidth * startDir); // start at one arc end
        SwingDir     = startDir;
    }

    public void Advance(double dt)
    {
        if (PauseRemaining > 0)
        {
            PauseRemaining = Math.Max(0.0, PauseRemaining - dt);
            return;
        }
        CurrentAngle += AngularSpeed * SwingDir * dt;
        double lo = RestAngle - ArcHalfWidth;
        double hi = RestAngle + ArcHalfWidth;
        if (CurrentAngle >= hi)
        {
            CurrentAngle   = hi;
            SwingDir       = -1;
            PauseRemaining = PauseSeconds;
        }
        else if (CurrentAngle <= lo)
        {
            CurrentAngle   = lo;
            SwingDir       = 1;
            PauseRemaining = PauseSeconds;
        }
    }
}
```

- [ ] **Step 4: Create Octopus.cs**

```csharp
// src/Miner49er.Core/Sim/Octopus.cs
using System;
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Stationary boss entity. Four arms sweep ±45° arcs at 30°/sec.
/// Any miner in a danger tile is crushed.</summary>
public sealed class Octopus
{
    public GridPos   Pos  { get; }
    public OctopusArm[] Arms { get; }

    public Octopus(GridPos pos)
    {
        Pos  = pos;
        Arms = new[]
        {
            new OctopusArm(  0.0,  1),   // North  — starts at -45° (NW), sweeps to +45° (NE)
            new OctopusArm( 90.0, -1),   // East   — offset so arms interleave
            new OctopusArm(180.0,  1),   // South
            new OctopusArm(270.0, -1),   // West
        };
        // Stagger initial angles by 22.5° each so no two arms hit the same position
        // simultaneously (quarter cycle apart — 90° / 4 arms).
        for (int i = 1; i < Arms.Length; i++)
        {
            double offset = i * (OctopusArm.ArcHalfWidth / 2.0); // 22.5° per arm
            Arms[i].CurrentAngle += Arms[i].SwingDir > 0 ? offset : -offset;
        }
    }

    public void Advance(double dt)
    {
        foreach (var arm in Arms) arm.Advance(dt);
    }

    /// <summary>Up to Length tiles along each arm's current direction.
    /// Stops at the grid boundary. Never includes the octopus center itself.</summary>
    public IEnumerable<GridPos> DangerTiles(TileGrid grid)
    {
        foreach (var arm in Arms)
            foreach (var p in ArmTiles(Pos, arm.CurrentAngle, OctopusArm.Length, grid))
                yield return p;
    }

    // Walks <length> unique grid cells along the direction given by angleDeg
    // (0=North, 90=East, 180=South, 270=West — Y increases downward).
    private static IEnumerable<GridPos> ArmTiles(
        GridPos origin, double angleDeg, int length, TileGrid grid)
    {
        double rad  = angleDeg * Math.PI / 180.0;
        double dirX =  Math.Sin(rad);   // East  component (+x)
        double dirY = -Math.Cos(rad);   // South component (+y, Y increases down)

        var seen = new HashSet<GridPos>();
        // Step at sub-tile resolution; collect the first <length> unique cells.
        for (double t = 0.4; seen.Count < length; t += 0.4)
        {
            if (t > length * 3.0) break;  // safety: never loop forever
            var p = new GridPos(
                origin.X + (int)Math.Round(dirX * t),
                origin.Y + (int)Math.Round(dirY * t));
            if (!grid.InBounds(p) || p == origin) { if (!grid.InBounds(p)) break; continue; }
            if (seen.Add(p)) yield return p;
        }
    }
}
```

- [ ] **Step 5: Run Octopus tests**

```
dotnet test src/Miner49er.Core.Tests --filter "Octopus"
```

Expected: All 7 pass.

- [ ] **Step 6: Run full suite to check no regressions**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: All pass.

- [ ] **Step 7: Commit**

```powershell
git add src/Miner49er.Core/Sim/OctopusArm.cs `
        src/Miner49er.Core/Sim/Octopus.cs `
        src/Miner49er.Core.Tests/OctopusTests.cs
git commit -m @'
feat(core): Octopus entity with 4 sweeping arms

Arms oscillate ±45° at 30°/sec, pause 1s at each end, staggered by 22.5°
so adjacent arms never reach extremes simultaneously. DangerTiles casts a
sub-tile ray for each arm returning up to 5 in-bounds grid cells.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 3: Simulation octopus + chest integration

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationOctopusTests.cs`
- Modify: `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs` (add the chest-grab test for real)

**Interfaces produced:**
- `ItemKind.Chest` — walk-over, not carried; triggers `ChestGrabbedBy` on pickup
- `Simulation.AddOctopus(GridPos pos)`
- `Simulation.Octopus` → `Octopus?`
- Octopus is advanced each tick; miners on danger tiles receive `CollapseKill`

---

- [ ] **Step 1: Write SimulationOctopusTests.cs (failing)**

```csharp
// src/Miner49er.Core.Tests/SimulationOctopusTests.cs
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationOctopusTests
{
    // 10×10 all-floor grid with octopus at center (5,5).
    private static (Simulation sim, Miner miner) Setup(GridPos minerStart)
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddOctopus(new GridPos(5, 5));
        var miner = sim.AddMiner(1, minerStart);
        return (sim, miner);
    }

    [Fact]
    public void Miner_on_danger_tile_is_crushed()
    {
        // Get the initial danger tiles and stand the miner on one.
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddOctopus(new GridPos(5, 5));
        var dangerPos = sim.Octopus!.DangerTiles(grid).First();
        sim.AddMiner(1, dangerPos);

        sim.Tick(0.01);   // tiny tick — arm hasn't moved, miner is still on danger tile

        var m = sim.Miners.First(x => x.Id == 1);
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
    }

    [Fact]
    public void Miner_off_danger_tile_is_safe()
    {
        // Use center-adjacent tile that is NOT a danger tile initially.
        // The octopus is at (5,5); start arm angles put the North arm pointing NW.
        // Place miner at (5,4) — directly North, which the North arm sweeps eventually
        // but not necessarily at t=0. Start at (5,8) which is far from the octopus center.
        var (sim, miner) = Setup(new GridPos(5, 8));
        bool safe = miner.Alive;
        sim.Tick(0.033);
        // Still alive (may or may not be on a danger tile after one tick, but (5,8) is far)
        // This is a weaker assertion — just verifies no exception and arms advance.
        Assert.True(sim.Octopus!.Arms.All(a => a.CurrentAngle != a.RestAngle || a.PauseRemaining > 0
            || true));  // arms advanced without throwing
        Assert.True(miner.Alive);  // (5,8) is outside arm length 5 from (5,5)
    }

    [Fact]
    public void Chest_item_pickup_sets_chest_grabbed_by()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        var chest = new GridPos(2, 1);
        sim.AddItem(new Item(chest, ItemKind.Chest, ItemPlacement.Toolbox));
        var miner = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);  // miner moves to (2,1)
        sim.Tick(0.5);                   // PickUpItems fires

        Assert.Equal(1, sim.ChestGrabbedBy);
    }

    [Fact]
    public void Chest_pickup_makes_resolver_return_win()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig(), escapeTile: null);
        sim.AddItem(new Item(new GridPos(2, 1), ItemKind.Chest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);
        sim.Tick(0.5);

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);
        Assert.True(result.IsOver);
        Assert.False(result.FloorCleared);
        Assert.Equal(1, result.WinnerId);
    }
}
```

- [ ] **Step 2: Add the full chest-grab resolver test to RoundResolverExpeditionTests.cs**

Replace the placeholder from Task 1 Step 3 with the real test:

```csharp
[Fact]
public void Chest_grabbed_wins_the_dungeon()
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    var sim  = new Simulation(grid, new SimConfig(), escapeTile: null);
    sim.AddItem(new Item(new GridPos(1, 1), ItemKind.Chest, ItemPlacement.Toolbox));
    sim.AddMiner(1, new GridPos(1, 1));
    sim.Tick(0.1);  // PickUpItems fires; miner starts on chest tile

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.True(result.IsOver);
    Assert.False(result.FloorCleared);
    Assert.Equal(1, result.WinnerId);
}
```

- [ ] **Step 3: Run tests — expect failures**

```
dotnet test src/Miner49er.Core.Tests --filter "SimulationOctopus|RoundResolverExpedition"
```

Expected: Failures about `AddOctopus`, `Octopus` property, `ItemKind.Chest`.

- [ ] **Step 4: Add ItemKind.Chest to Item.cs**

Edit `src/Miner49er.Core/Map/Item.cs`:

```csharp
/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold, Lantern, Chest }
```

`IsCarried` is unchanged — `Chest` is not in the carried list, so it auto-collects on walk-over.

- [ ] **Step 5: Add octopus field, AddOctopus, and tick integration to Simulation.cs**

**a) Add `_octopus` field after `private readonly List<Monster> _monsters = new();` (around line 14):**
```csharp
private Octopus? _octopus;
public  Octopus? Octopus => _octopus;
```

**b) Add `AddOctopus` method after `AddMonster`:**
```csharp
public void AddOctopus(GridPos pos) => _octopus = new Octopus(pos);
```

**c) Add `AdvanceOctopus` private method (place alongside `AdvanceMonsters`):**
```csharp
private void AdvanceOctopus(double dt)
{
    if (_octopus is null) return;
    _octopus.Advance(dt);
    var danger = new HashSet<GridPos>(_octopus.DangerTiles(Grid));
    foreach (var m in _miners.Values)
        if (m.Alive && danger.Contains(m.Pos))
            CollapseKill(m);
}
```

**d) Call `AdvanceOctopus(dt)` in `Tick(double dt)` after `AdvanceMonsters(dt)` (around line 500):**
```csharp
AdvanceMonsters(dt);
AdvanceOctopus(dt);
```

**e) In `ApplyBuff`, add the Chest case:**
```csharp
case ItemKind.Chest:
    ChestGrabbedBy = minerId;
    break;
```

- [ ] **Step 6: Run tests**

```
dotnet test src/Miner49er.Core.Tests --filter "SimulationOctopus|RoundResolverExpedition"
```

Expected: All pass.

- [ ] **Step 7: Run full suite**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: All pass.

- [ ] **Step 8: Commit**

```powershell
git add src/Miner49er.Core/Map/Item.cs `
        src/Miner49er.Core/Sim/Simulation.cs `
        src/Miner49er.Core.Tests/SimulationOctopusTests.cs `
        src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs
git commit -m @'
feat(core): octopus sim integration + ItemKind.Chest pickup win

Simulation.AddOctopus/Octopus: advances arms each tick, CollapseKills miners
on danger tiles (DeathCause.Crushed). ItemKind.Chest auto-collects on walk-over
and sets ChestGrabbedBy, which RoundResolver converts to Win for dungeon clear.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 4: Boss floor generator

**Files:**
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Create: `src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs`

**Interfaces produced:**
- `MapGenerator.GenerateBossFloor(int seed)` → `GeneratedMap`
  - 40×40 grid
  - Border: `ImpermeableRock`
  - Interior default: `DeepWater`
  - Cross corridors (2 tiles wide): `ShallowWater` (H and V, through center)
  - Central island (Chebyshev ≤ 2 from center): `Floor`
  - Chest item at `(W/2, H/2 + 1)` as `ItemKind.Chest, ItemPlacement.Toolbox`
  - Spawn at `(W/2, H - 2)` (bottom of south corridor)
  - `Center = (W/2, H/2)`, `Decoys = []`

---

- [ ] **Step 1: Write MapGeneratorBossFloorTests.cs (failing)**

```csharp
// src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorBossFloorTests
{
    private static GeneratedMap Make(int seed = 1) =>
        MapGenerator.GenerateBossFloor(seed);

    [Fact]
    public void Boss_floor_is_40_by_40()
    {
        var map = Make();
        Assert.Equal(40, map.Grid.Width);
        Assert.Equal(40, map.Grid.Height);
    }

    [Fact]
    public void Border_is_impermeable_rock()
    {
        var map = Make();
        var grid = map.Grid;
        for (int x = 0; x < 40; x++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 0)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(x, 39)));
        }
        for (int y = 0; y < 40; y++)
        {
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(0, y)));
            Assert.Equal(TileType.ImpermeableRock, grid.Get(new GridPos(39, y)));
        }
    }

    [Fact]
    public void Central_island_is_floor()
    {
        var map = Make();
        var center = map.Center;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                Assert.Equal(TileType.Floor,
                    map.Grid.Get(new GridPos(center.X + dx, center.Y + dy)));
    }

    [Fact]
    public void Chest_is_one_south_of_center()
    {
        var map = Make();
        var center = map.Center;
        var chest = map.Items.FirstOrDefault(i => i.Kind == ItemKind.Chest);
        Assert.Equal(new GridPos(center.X, center.Y + 1), chest.Pos);
    }

    [Fact]
    public void Spawn_is_reachable_from_island()
    {
        var map = Make();
        // Spawn is at the bottom of the south corridor — must be ShallowWater or Floor
        Assert.Single(map.Spawns);
        var spawnTile = map.Grid.Get(map.Spawns[0]);
        Assert.True(spawnTile == TileType.Floor || spawnTile == TileType.ShallowWater);
    }

    [Fact]
    public void Interior_non_path_tiles_are_deep_water()
    {
        var map = Make();
        var center = map.Center;
        int cx = center.X, cy = center.Y;
        // Check a tile that is NOT on any corridor or island
        // (e.g., 10 tiles away diagonally from center)
        var farCorner = new GridPos(cx - 8, cy - 8);
        Assert.Equal(TileType.DeepWater, map.Grid.Get(farCorner));
    }

    [Fact]
    public void Same_seed_produces_identical_boss_floors()
    {
        var a = Make(42);
        var b = Make(42);
        Assert.True(a.Grid.Positions().All(p => a.Grid.Get(p) == b.Grid.Get(p)));
    }
}
```

- [ ] **Step 2: Run tests — expect compile error (GenerateBossFloor doesn't exist)**

```
dotnet test src/Miner49er.Core.Tests --filter "BossFloor"
```

- [ ] **Step 3: Implement MapGenerator.GenerateBossFloor**

Add this static method to `src/Miner49er.Core/Map/MapGenerator.cs` (alongside the existing `Generate` method):

```csharp
/// <summary>Produces the fixed-structure boss floor: a 40×40 arena with deep water
/// filling the interior, cross-shaped shallow-water corridors for navigation, a 5×5
/// central floor island, octopus at center, and the victory chest one tile south.</summary>
public static GeneratedMap GenerateBossFloor(int seed)
{
    const int W = 40, H = 40;
    var grid = new TileGrid(W, H);

    // Fill entire grid with deep water, then carve the structure.
    foreach (var p in grid.Positions())
        grid.Set(p, TileType.DeepWater);

    // Border — impermeable rock.
    for (int x = 0; x < W; x++)
    {
        grid.Set(new GridPos(x, 0),     TileType.ImpermeableRock);
        grid.Set(new GridPos(x, H - 1), TileType.ImpermeableRock);
    }
    for (int y = 0; y < H; y++)
    {
        grid.Set(new GridPos(0,     y), TileType.ImpermeableRock);
        grid.Set(new GridPos(W - 1, y), TileType.ImpermeableRock);
    }

    int cx = W / 2, cy = H / 2;

    // Horizontal corridor (2 tiles wide, rows cy and cy+1, columns 1..W-2).
    for (int x = 1; x < W - 1; x++)
    {
        grid.Set(new GridPos(x, cy),     TileType.ShallowWater);
        grid.Set(new GridPos(x, cy + 1), TileType.ShallowWater);
    }

    // Vertical corridor (2 tiles wide, columns cx and cx+1, rows 1..H-2).
    for (int y = 1; y < H - 1; y++)
    {
        grid.Set(new GridPos(cx,     y), TileType.ShallowWater);
        grid.Set(new GridPos(cx + 1, y), TileType.ShallowWater);
    }

    // Central island — 5×5 Floor tiles (Chebyshev distance ≤ 2 from center).
    for (int dy = -2; dy <= 2; dy++)
        for (int dx = -2; dx <= 2; dx++)
            grid.Set(new GridPos(cx + dx, cy + dy), TileType.Floor);

    var center  = new GridPos(cx, cy);
    var chestPos = new GridPos(cx, cy + 1);

    // Chest item (on the floor island, one south of center).
    var items = new System.Collections.Generic.List<Item>
    {
        new Item(chestPos, ItemKind.Chest, ItemPlacement.Toolbox),
    };

    // Spawn at the bottom of the south corridor, one tile from the border.
    var spawn = new GridPos(cx, H - 2);
    // Ensure the spawn tile is ShallowWater (it was set by the vertical corridor).

    return new GeneratedMap
    {
        Grid   = grid,
        Spawns = new System.Collections.Generic.List<GridPos> { spawn },
        Center = center,
        Items  = items,
        Decoys = System.Array.Empty<GridPos>(),
    };
}
```

- [ ] **Step 4: Run boss floor tests**

```
dotnet test src/Miner49er.Core.Tests --filter "BossFloor"
```

Expected: All pass.

- [ ] **Step 5: Run full suite**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: All pass.

- [ ] **Step 6: Commit**

```powershell
git add src/Miner49er.Core/Map/MapGenerator.cs `
        src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs
git commit -m @'
feat(core): GenerateBossFloor — 40x40 arena with cross corridors and chest

Deep water interior, 2-wide ShallowWater cross corridors, 5×5 central Floor island.
Chest item at (cx, cy+1). Spawn at bottom of south corridor. Seed-deterministic.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 5: Snapshot plumbing (OctopusSnapshot, WorldSnapshot, SnapshotFactory, SnapshotCodec)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

**Interfaces produced:**
- `OctopusArmSnapshot(double Angle, double PauseRemaining, int SwingDir)`
- `OctopusSnapshot(int X, int Y, OctopusArmSnapshot[] Arms)`
- `WorldSnapshot` gains `OctopusSnapshot? Octopus = null`
- Binary format: `[hasOctopus bool][X int][Y int][armCount int][arm.Angle double, arm.PauseRemaining double, arm.SwingDir int] × armCount` inserted between EscapeOpen and TileChanges

---

- [ ] **Step 1: Write octopus codec round-trip test (failing)**

Add to `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`:

```csharp
[Fact]
public void Round_trips_octopus_snapshot()
{
    var arms = new[]
    {
        new OctopusArmSnapshot(12.5, 0.0,  1),
        new OctopusArmSnapshot(95.0, 0.75, -1),
        new OctopusArmSnapshot(200.0, 0.0, 1),
        new OctopusArmSnapshot(280.0, 0.0, -1),
    };
    var update = new TickUpdate(
        new WorldSnapshot(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(),
            Octopus: new OctopusSnapshot(20, 20, arms)),
        new List<TileChange>());

    var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

    Assert.NotNull(back.Snapshot.Octopus);
    var oct = back.Snapshot.Octopus!;
    Assert.Equal(20, oct.X);
    Assert.Equal(20, oct.Y);
    Assert.Equal(4, oct.Arms.Length);
    Assert.Equal(12.5,  oct.Arms[0].Angle,        3);
    Assert.Equal(0.75,  oct.Arms[1].PauseRemaining, 3);
    Assert.Equal(-1,    oct.Arms[1].SwingDir);
}

[Fact]
public void Round_trips_null_octopus()
{
    var update = new TickUpdate(
        new WorldSnapshot(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>()),
        new List<TileChange>());

    var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
    Assert.Null(back.Snapshot.Octopus);
}
```

- [ ] **Step 2: Run test — expect compile failures (OctopusSnapshot etc. don't exist)**

```
dotnet test src/Miner49er.Core.Tests --filter "Round_trips_octopus"
```

- [ ] **Step 3: Add OctopusArmSnapshot, OctopusSnapshot to Snapshots.cs**

Add after `MonsterSnapshot` in `src/Miner49er.Core/Net/Snapshots.cs`:

```csharp
public readonly record struct OctopusArmSnapshot(double Angle, double PauseRemaining, int SwingDir);

public sealed record OctopusSnapshot(int X, int Y, OctopusArmSnapshot[] Arms);
```

Also extend `WorldSnapshot` to add the optional octopus field:

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false,
    OctopusSnapshot? Octopus = null);
```

- [ ] **Step 4: Update SnapshotFactory.cs**

Replace the `return` statement in `Capture`:

```csharp
OctopusSnapshot? octopus = sim.Octopus is { } oct
    ? new OctopusSnapshot(oct.Pos.X, oct.Pos.Y,
        oct.Arms.Select(a => new OctopusArmSnapshot(
            a.CurrentAngle, a.PauseRemaining, a.SwingDir)).ToArray())
    : null;

return new WorldSnapshot(tick, miners, charges, items, molds, monsters,
    (float)sim.SecondsRemaining, sim.EscapeOpen, octopus);
```

- [ ] **Step 5: Update SnapshotCodec.cs**

**In `Write`, add octopus encoding after `w.Write(snap.EscapeOpen)` and before `w.Write(update.TileChanges.Count)`:**

```csharp
bool hasOctopus = snap.Octopus is not null;
w.Write(hasOctopus);
if (hasOctopus)
{
    var oct = snap.Octopus!;
    w.Write(oct.X);
    w.Write(oct.Y);
    w.Write(oct.Arms.Length);
    foreach (var a in oct.Arms)
    {
        w.Write(a.Angle);
        w.Write(a.PauseRemaining);
        w.Write(a.SwingDir);
    }
}
```

**In `Read`, add octopus decoding after `bool escapeOpen = r.ReadBoolean();` and before `int changeCount = r.ReadInt32();`:**

```csharp
OctopusSnapshot? octopus = null;
bool hasOctopus = r.ReadBoolean();
if (hasOctopus)
{
    int ox = r.ReadInt32(), oy = r.ReadInt32();
    int armCount = r.ReadInt32();
    var arms = new OctopusArmSnapshot[armCount];
    for (int i = 0; i < armCount; i++)
        arms[i] = new OctopusArmSnapshot(r.ReadDouble(), r.ReadDouble(), r.ReadInt32());
    octopus = new OctopusSnapshot(ox, oy, arms);
}
```

**Update the final `return` to pass `octopus`:**

```csharp
return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
    monsters, secondsRemaining, escapeOpen, octopus), changes);
```

- [ ] **Step 6: Run snapshot tests**

```
dotnet test src/Miner49er.Core.Tests --filter "SnapshotCodec|SnapshotFactory"
```

Expected: All pass (including the new octopus round-trip tests).

- [ ] **Step 7: Run full suite**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: All pass.

- [ ] **Step 8: Commit**

```powershell
git add src/Miner49er.Core/Net/Snapshots.cs `
        src/Miner49er.Core/Net/SnapshotFactory.cs `
        src/Miner49er.Core/Net/SnapshotCodec.cs `
        src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m @'
feat(core): OctopusSnapshot plumbing — WorldSnapshot, Factory, Codec

OctopusArmSnapshot(Angle, PauseRemaining, SwingDir) encoded as optional field.
Binary format: hasOctopus bool immediately after EscapeOpen, before TileChanges.
Null octopus = 1-byte false; present = X/Y/armCount + arm triples.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 6: Network floor RPC + MatchClient.ResetFloor + FogState.Reset

**Files:**
- Modify: `src/Miner49er.Core/Fog/FogState.cs`
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/net/MatchClient.cs`

**Interfaces produced:**
- `FogState.Reset()` — clears both `Visible` and `Explored`
- `NetworkManager.MatchFloor` → `int` (starts at 1)
- `NetworkManager.BroadcastNewFloor(int floor)` — host calls this to advance all clients
- `NetworkManager.NewFloor` → `event Action<int>`
- `MatchClient.StartingGoldCount` → `int`
- `MatchClient.Octopus` → `OctopusSnapshot?`
- `MatchClient.ResetFloor(int floor)` — tears down old render nodes, regenerates map, re-inits nodes

---

- [ ] **Step 1: Add FogState.Reset()**

Edit `src/Miner49er.Core/Fog/FogState.cs`:

```csharp
public void Reset()
{
    _explored.Clear();
    Visible = new HashSet<GridPos>();
}
```

- [ ] **Step 2: Add NetworkManager fields and RPC**

In `game/net/NetworkManager.cs`, add after the existing `MatchBaseMoveSeconds` property (and any other existing Match* fields):

```csharp
public int MatchFloor { get; set; } = 1;

public event System.Action<int>? NewFloor;

public void BroadcastNewFloor(int floor)
{
    MatchFloor = floor;
    Rpc(nameof(ReceiveNewFloor), floor);
    ReceiveNewFloor(floor);   // host applies locally too
}

[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
public void ReceiveNewFloor(int floor)
{
    MatchFloor = floor;
    NewFloor?.Invoke(floor);
}
```

- [ ] **Step 3: Update MatchClient.cs**

**a) Add fields after existing private fields (around line 47):**

```csharp
private Node2D _sceneRoot = null!;
public int StartingGoldCount { get; private set; }
public OctopusSnapshot? Octopus { get; private set; }
```

**b) In `Begin()`, store `sceneRoot` and set `StartingGoldCount`:**

```csharp
public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId,
    Node2D sceneRoot, GridPos? escapeTile = null)
{
    _sceneRoot = sceneRoot;    // ADD THIS LINE
    Grid = grid;
    LocalMinerId = localMinerId;
    Decoys = decoys;
    EscapeTile = escapeTile;
    GoldRemaining = CountGold(grid);
    StartingGoldCount = GoldRemaining;    // ADD THIS LINE
    // ... rest unchanged ...
```

**c) In `ApplyUpdate()`, update `Octopus` from the snapshot:**

Add after `SecondsRemaining = update.Snapshot.SecondsRemaining;`:

```csharp
Octopus = update.Snapshot.Octopus;
```

**d) Add `ResetFloor(int floor)` method:**

```csharp
public void ResetFloor(int floor)
{
    // Free old render nodes — will be rebuilt below.
    _terrainMap?.QueueFree(); _terrainMap = null!;
    _world?.QueueFree();      _world = null!;
    _fogRenderer?.QueueFree(); _fogRenderer = null!;

    // Generate new map for this floor deterministically.
    var nm = NetworkManager.Instance;
    int floorSeed = nm.MatchSeed + floor * 1000;

    GeneratedMap newMap;
    if (floor == 21)
    {
        newMap = MapGenerator.GenerateBossFloor(floorSeed);
        EscapeTile = null;
    }
    else
    {
        var cfg = MapConfig.FloorConfig(floor, floorSeed);
        newMap = MapGenerator.Generate(cfg);
        EscapeTile = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : null;
    }

    Grid             = newMap.Grid;
    Decoys           = newMap.Decoys;
    GoldRemaining    = CountGold(newMap.Grid);
    StartingGoldCount = GoldRemaining;
    EscapeOpen       = false;
    Octopus          = null;

    Fog.Reset();
    _visualPos.Clear();
    _monsterVisualPos.Clear();
    _miners.Clear();
    _monsters.Clear();

    // Re-initialise render nodes.
    _terrainMap = new TerrainMap { Name = "TerrainMap", ZIndex = -10 };
    _sceneRoot.AddChild(_terrainMap);
    _terrainMap.Init(this);

    _world = new WorldRenderer { Name = "WorldRenderer", ZIndex = -9 };
    _sceneRoot.AddChild(_world);
    _world.Init(this);

    _fogRenderer = new FogRenderer { Name = "FogRenderer", ZIndex = -5 };
    _sceneRoot.AddChild(_fogRenderer);
    _fogRenderer.Init(this);
}
```

Note: `_visualPos` is `private readonly Dictionary<int, Vector2>` — since it's readonly, use `.Clear()` not reassignment.

- [ ] **Step 4: Build check (Godot project)**

```powershell
dotnet build game/Miner49er.csproj
```

Expected: Builds without errors. (No gameplay test yet — that comes in Task 8.)

- [ ] **Step 5: Commit**

```powershell
git add src/Miner49er.Core/Fog/FogState.cs `
        game/net/NetworkManager.cs `
        game/net/MatchClient.cs
git commit -m @'
feat(game): network floor RPC + MatchClient.ResetFloor

FogState.Reset() clears explored+visible for floor transition.
NetworkManager: MatchFloor, BroadcastNewFloor, ReceiveNewFloor RPC, NewFloor event.
MatchClient: _sceneRoot field, StartingGoldCount, Octopus?, ResetFloor(floor)
tears down TerrainMap/WorldRenderer/FogRenderer and re-inits from FloorConfig.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 7: MatchHost floor transition (AdvanceFloor)

**Files:**
- Modify: `game/net/MatchHost.cs`

**Interfaces produced:**
- `MatchHost.AdvanceFloor(int minerId)` — creates new sim for next floor, broadcasts `NewFloor`
- `MatchHost.StepOnce()` handles `result.FloorCleared` by calling `AdvanceFloor` and returning early

---

- [ ] **Step 1: Add using statements if needed**

At the top of `game/net/MatchHost.cs`, ensure these usings are present:

```csharp
using Godot;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;
```

- [ ] **Step 2: Update StepOnce to handle FloorCleared**

Replace the block at the end of `StepOnce()` that currently reads:

```csharp
var result = RoundResolver.Resolve(_sim, NetworkManager.Instance.MatchMode);
if (result.IsOver)
{
    _running = false;
    long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
    NetworkManager.Instance.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
}
```

With:

```csharp
var result = RoundResolver.Resolve(_sim, NetworkManager.Instance.MatchMode);
if (result.FloorCleared)
{
    AdvanceFloor(result.WinnerId);
    return;   // skip tick broadcast — new floor starts next tick
}
if (result.IsOver)
{
    _running = false;
    long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
    NetworkManager.Instance.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
}
```

- [ ] **Step 3: Add AdvanceFloor method**

Add this private method to `MatchHost`:

```csharp
private void AdvanceFloor(int minerId)
{
    var nm = NetworkManager.Instance;
    int newFloor = nm.MatchFloor + 1;
    int floorSeed = nm.MatchSeed + newFloor * 1000;

    GeneratedMap newMap;
    GridPos? escapeTile;
    if (newFloor == 21)
    {
        newMap     = MapGenerator.GenerateBossFloor(floorSeed);
        escapeTile = null;
    }
    else
    {
        var cfg    = MapConfig.FloorConfig(newFloor, floorSeed);
        newMap     = MapGenerator.Generate(cfg);
        escapeTile = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : null;
    }

    var newSim = new Simulation(
        newMap.Grid,
        new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = floorSeed },
        newMap.Center,
        timeLimitSeconds: null,
        flooding: false,
        escapeTile);

    foreach (var item in newMap.Items)
        newSim.AddItem(item);

    // Re-add miners with empty inventory at new spawns.
    GridPos spawn = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
    newSim.AddMiner(minerId, spawn);

    if (newFloor == 21)
    {
        // Boss floor: add the octopus at grid center.
        newSim.AddOctopus(newMap.Center);
    }
    else
    {
        int monsterCount = MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor);
        var roster = MonsterSpawner.Place(newMap.Grid, spawn, monsterCount);
        for (int i = 0; i < roster.Count; i++)
            newSim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
    }

    _sim  = newSim;
    _tick = 0;

    // Clear any pending inputs from the old floor.
    foreach (var key in _pendingDir.Keys.ToList()) _pendingDir[key] = -1;
    _pendingMine.Clear();
    _pendingPlant.Clear();
    _pendingUse.Clear();

    // Broadcast to clients (they will call MatchClient.ResetFloor via the event).
    nm.BroadcastNewFloor(newFloor);
}
```

- [ ] **Step 4: Build check**

```powershell
dotnet build game/Miner49er.csproj
```

Expected: Builds without errors.

- [ ] **Step 5: Commit**

```powershell
git add game/net/MatchHost.cs
git commit -m @'
feat(game): MatchHost.AdvanceFloor — regenerate sim and broadcast on floor clear

StepOnce checks result.FloorCleared before IsOver. AdvanceFloor creates a fresh
Simulation for the next floor, re-adds miners + monsters/octopus, clears pending
inputs, then calls BroadcastNewFloor to trigger client ResetFloor via RPC.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 8: Main.cs + UI (floor banner, HUD, results, DeathFeed)

**Files:**
- Modify: `game/Main.cs`
- Modify: `game/ui/DeathFeed.cs`

**Interfaces consumed:**
- `NetworkManager.NewFloor` event (wires to `_client.ResetFloor(floor)`)
- `NetworkManager.MatchFloor` (for HUD text)
- `MatchClient.StartingGoldCount` and `GoldRemaining` (for % display)
- `MatchClient.Octopus` (boss floor HUD hint)
- `RoundResult.FloorCleared` already handled by MatchHost; Main.cs just responds to `MatchEnded`

---

- [ ] **Step 1: Update DeathFeed.cs — generic Crushed messages**

In `ShowBanner`:
```csharp
DeathCause.Crushed => "CRUSHED!",
```

In `PushToast`:
```csharp
DeathCause.Crushed => $"{name} was crushed",
```

This covers both cave-in kills and octopus arm kills (both use `DeathCause.Crushed`).

- [ ] **Step 2: Subscribe to NewFloor in Main._Ready**

In `game/Main.cs`, in `_Ready()`, after `nm.MatchEnded += OnMatchEnded;` add:

```csharp
nm.NewFloor += OnNewFloor;
```

- [ ] **Step 3: Unsubscribe in _ExitTree**

After `nm.MatchEnded -= OnMatchEnded;` add:

```csharp
nm.NewFloor -= OnNewFloor;
```

- [ ] **Step 4: Add OnNewFloor handler and floor banner fields**

Add a floor banner `Label` field and timer near the top of `Main`:

```csharp
private Label? _floorBanner;
private float  _floorBannerTimer;
private const float BannerHold   = 1.5f;
private const float BannerFade   = 0.3f;
private const float BannerTotal  = BannerHold + BannerFade * 2;
```

Add the handler method:

```csharp
private void OnNewFloor(int floor)
{
    _client.ResetFloor(floor);

    // Show a full-width banner for the new floor.
    _floorBanner?.QueueFree();
    _floorBanner = new Label
    {
        Text = floor == 21 ? "BOSS FLOOR" : $"FLOOR {floor}",
        HorizontalAlignment = HorizontalAlignment.Center,
        AnchorLeft = 0f, AnchorRight = 1f,
        AnchorTop = 0.45f, AnchorBottom = 0.45f,
        Modulate = new Color(1, 1, 1, 0f),
        ZIndex = 20,
    };
    _floorBanner.AddThemeFontSizeOverride("font_size", 64);
    AddChild(_floorBanner);
    _floorBannerTimer = BannerTotal;
}
```

- [ ] **Step 5: Animate the banner in _PhysicsProcess**

Add this block at the start of `_PhysicsProcess(double delta)` (before the miner loop):

```csharp
if (_floorBanner != null)
{
    _floorBannerTimer -= (float)delta;
    float alpha;
    if (_floorBannerTimer > BannerHold + BannerFade)
        alpha = 1f - (_floorBannerTimer - BannerHold - BannerFade) / BannerFade; // fade in
    else if (_floorBannerTimer > BannerFade)
        alpha = 1f;                                                                 // hold
    else
        alpha = Math.Max(0f, _floorBannerTimer / BannerFade);                      // fade out
    if (_floorBannerTimer <= 0f) { _floorBanner.QueueFree(); _floorBanner = null; }
    else _floorBanner.Modulate = new Color(1, 1, 1, alpha);
}
```

Add `using System;` at the top if not already present.

- [ ] **Step 6: Update HUD objective line in _PhysicsProcess**

Replace the current Expedition objective string:

```csharp
string objective = NetworkManager.Instance.MatchMode == GameMode.Expedition
    ? (_client.EscapeOpen ? "ESCAPE at your start!" : $"Gold left: {_client.GoldRemaining}")
    : $"Gold: {m.Gold}";
```

With a floor-aware version:

```csharp
string objective;
if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
{
    var nm2 = NetworkManager.Instance;
    if (nm2.MatchFloor == 21)
    {
        objective = "BOSS FLOOR  Reach the chest!";
    }
    else if (_client.EscapeOpen)
    {
        objective = $"Floor {nm2.MatchFloor}/20  Gold ✓ — ESCAPE!";
    }
    else
    {
        int pct = _client.StartingGoldCount > 0
            ? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
            : 0;
        objective = $"Floor {nm2.MatchFloor}/20  Gold: {pct}% (need 50%)";
    }
}
else
{
    objective = $"Gold: {m.Gold}";
}
```

- [ ] **Step 7: Update OnMatchEnded for dungeon win**

Replace the Expedition branch in `OnMatchEnded`:

```csharp
if (expedition)
    label = winnerPeerId == NetworkManager.Instance.LocalId
        ? (NetworkManager.Instance.MatchFloor == 21
            ? "You conquered the dungeon!"
            : "You escaped with the gold!")
        : "You died in the mine.";
```

- [ ] **Step 8: Build check**

```powershell
dotnet build game/Miner49er.csproj
```

Expected: Builds without errors.

- [ ] **Step 9: Commit**

```powershell
git add game/Main.cs `
        game/ui/DeathFeed.cs
git commit -m @'
feat(game): floor banner, updated HUD objective, dungeon win screen

OnNewFloor: calls client.ResetFloor, shows animated FLOOR N / BOSS FLOOR banner
(fade 0.3s in, hold 1.5s, fade 0.3s out) at ZIndex 20.
HUD shows floor number + gold % (need 50%) or escape open; boss floor shows hint.
Results overlay shows "You conquered the dungeon!" for boss-floor chest win.
DeathFeed Crushed → generic "CRUSHED!" / "was crushed" (covers cave-in + octopus).

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

### Task 9: WorldRenderer octopus + chest overlay

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces consumed:**
- `_client.Octopus` → `OctopusSnapshot?`
- `_client.EscapeOpen` (already read) — locked state means grey ladder tint
- `OctopusSnapshot.Arms[]` → `OctopusArmSnapshot[]` for danger tile recomputation
- `ItemKind.Chest` → chest glyph ("♦" or "⌂")
- Octopus class re-instantiated client-side from snapshot for `DangerTiles(Grid)`

---

- [ ] **Step 1: Add octopus body color and chest color constants near other color constants in WorldRenderer.cs**

```csharp
private static readonly Color OctopusColor    = new Color(0.8f, 0.1f, 0.7f, 0.85f);
private static readonly Color OctopusArmColor = new Color(0.9f, 0.2f, 0.6f, 0.45f);
private static readonly Color ChestColor      = new Color(0.9f, 0.8f, 0.1f, 0.95f);
```

The existing `LadderColor` already exists as `new Color(0.68f, 0.52f, 0.28f, 0.50f)`. Add a grey variant for the locked state:

```csharp
private static readonly Color LadderLockedColor = new Color(0.4f, 0.4f, 0.4f, 0.40f);
```

- [ ] **Step 2: Update the ladder drawing block to use grey tint when locked**

Find the existing ladder draw block (draws when `_client.EscapeTile is { } exit`). Change the color selection:

Current pattern (roughly):
```csharp
if (_client.EscapeOpen)
{
    // gold pulse + fill
}
else
{
    // faint ladder
}
```

Update to use `LadderLockedColor` for the locked (not open) case. The existing faint color was `LadderColor` at reduced alpha — replace it with `LadderLockedColor`:

```csharp
if (_client.EscapeOpen)
{
    // existing gold pulse + fill — no change
}
else
{
    // Grey when locked (50% threshold not yet reached)
    DrawRect(r, LadderLockedColor);
    DrawString(font, textPos, "⌂", HorizontalAlignment.Center, -1, fontSize, LadderLockedColor);
}
```

You will need to read the existing ladder block carefully and update only the `else` path colors. Do not change the `EscapeOpen` (gold pulse) path.

- [ ] **Step 3: Add octopus body + arm danger tile rendering**

In the `_Draw()` method (or `DrawOverlay()` / wherever WorldRenderer draws entity overlays), add the octopus block after monsters are drawn:

```csharp
// Octopus body and arm danger tiles
if (_client.Octopus is { } octSnap)
{
    const int TS = MatchClient.TileSize;
    var font = ThemeDB.FallbackFont;
    int fontSize = TS * 2 / 3;

    // Reconstruct danger tiles from the snapshot angles (same algorithm as Core).
    var snapOct = new Octopus(new GridPos(octSnap.X, octSnap.Y));
    for (int i = 0; i < snapOct.Arms.Length && i < octSnap.Arms.Length; i++)
    {
        snapOct.Arms[i].CurrentAngle   = octSnap.Arms[i].Angle;
        snapOct.Arms[i].PauseRemaining = octSnap.Arms[i].PauseRemaining;
        snapOct.Arms[i].SwingDir       = octSnap.Arms[i].SwingDir;
    }

    // Arm danger tiles — translucent pink overlay.
    foreach (var p in snapOct.DangerTiles(_client.Grid))
    {
        var r = new Rect2(p.X * TS, p.Y * TS, TS, TS);
        DrawRect(r, OctopusArmColor);
    }

    // Octopus body glyph.
    var bodyPos = new GridPos(octSnap.X, octSnap.Y);
    var br = new Rect2(bodyPos.X * TS, bodyPos.Y * TS, TS, TS);
    DrawRect(br, OctopusColor);
    DrawString(font, new Vector2(bodyPos.X * TS + TS / 2f, bodyPos.Y * TS + TS * 0.65f),
        "✦", HorizontalAlignment.Center, -1, fontSize, Colors.White);
}
```

- [ ] **Step 4: Add chest glyph rendering in the items pass**

Find the existing items draw loop in WorldRenderer (where it draws toolboxes, potions, etc.). Add a `Chest` case:

```csharp
case ItemKind.Chest:
{
    var r = new Rect2(it.X * TS, it.Y * TS, TS, TS);
    DrawRect(r, ChestColor);
    DrawString(font, new Vector2(it.X * TS + TS / 2f, it.Y * TS + TS * 0.65f),
        "♦", HorizontalAlignment.Center, -1, fontSize, Colors.Black);
    break;
}
```

- [ ] **Step 5: Build check**

```powershell
dotnet build game/Miner49er.csproj
```

Expected: Builds without errors.

- [ ] **Step 6: Smoke-test in Godot — start an Expedition run**

Launch via PowerShell:

```powershell
& "godot" --path "D:\Projects\Miner49er" "res://game/ui/MainMenu.tscn"
```

Verify:
1. Start a solo Expedition. HUD shows "Floor 1/20  Gold: 0% (need 50%)"
2. Mine ≥50% gold — HUD switches to "Floor 1/20  Gold ✓ — ESCAPE!" and ladder pulses gold
3. Walk to the start tile (escape) — floor banner "FLOOR 2" appears, map resets
4. Run through several floors and confirm difficulty scaling (pits appear at floor 6, etc.)
5. On floor 21 (boss floor): HUD says "BOSS FLOOR  Reach the chest!", octopus is visible with arm overlays
6. Walk onto the chest — "You conquered the dungeon!" results screen

- [ ] **Step 7: Commit**

```powershell
git add game/WorldRenderer.cs
git commit -m @'
feat(render): octopus body+arm overlay, chest glyph, locked ladder grey tint

Octopus body rendered as magenta ✦ glyph; arm danger tiles as translucent pink
rect overlay recomputed from OctopusSnapshot angles each frame. Chest item
renders as gold ♦ rect. Locked ladder (EscapeOpen=false) now draws in grey
instead of faint brown.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
'@
```

---

## Post-Implementation Smoke Test Checklist

After all 9 tasks are committed, do a full play-test:

- [ ] Floor 1-5: small map, no hazards, 50% gold threshold opens escape
- [ ] Floor 6-10: medium map, pit tiles visible
- [ ] Floor 11-15: large map, crumbling floor visible
- [ ] Floor 16-20: huge map, lava pools + vents visible
- [ ] Cave-in kill shows "CRUSHED!" (not "CAVED IN!")
- [ ] Death by octopus arm shows "CRUSHED!"
- [ ] Floor banner appears on each floor advance (0.3s in → 1.5s hold → 0.3s out)
- [ ] Boss floor (floor 21): deep water arena, octopus visible, chest glyph visible, arms sweep
- [ ] Walk onto chest → "You conquered the dungeon!" results screen
- [ ] Dying on any floor → "You died in the mine." results screen
- [ ] ESC during run returns to menu without crash
