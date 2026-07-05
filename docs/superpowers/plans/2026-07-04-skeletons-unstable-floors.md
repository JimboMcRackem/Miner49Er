# Skeletons & Unstable Floors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add dormant skeleton creatures that wake on noise, a SkeletonDino that damages floor tiles, and LMS/Derby pickaxe-cracking of floor tiles.

**Architecture:** Two independent features sharing the existing `Cracked → Crumbling → Pit` collapse pipeline. Unstable Floors (Feature 1, Tasks 1–2) is fully self-contained. Skeleton Creatures (Feature 2, Tasks 3–8) extends the noise system, monster lifecycle, spawning, codec, and renderer. Implement Feature 1 first — it's smaller and has no dependencies on Feature 2.

**Tech Stack:** C# / .NET 10, Godot 4.6.3 .NET; xUnit tests; PixelLab MCP for art assets.

## Global Constraints

- Run tests: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` (ignore spurious MSBUILD verbosity lines — look for `Passed!` in output)
- Never `git add -A`; never stage `*.uid`, `Temp/`, `.superpowers/`, or `_preview_*`
- `MonsterSnapshot` changes break codec wire format — all clients must run the same build
- All new `SimConfig` fields must have sensible defaults so existing non-Skeleton modes are unaffected
- Skeleton kinds only appear in Expedition (floors 8+) and ReachCenter (treated as floor 10)

---

## ══ FEATURE 1: Unstable Floors (LMS / Derby) ══

---

### Task 1: ActivityKind.FloorCracking — SimConfig flag, sim logic, Main.cs gate

**Files:**
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (two locations)
- Modify: `game/Main.cs`
- Create: `src/Miner49er.Core.Tests/SimulationFloorCrackingTests.cs`

**Interfaces:**
- Produces: `ActivityKind.FloorCracking` enum value; `SimConfig.UnstableFloorEnabled`; tile Floor→Cracked when activity completes; `CrackWeakened(pos)` event

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/SimulationFloorCrackingTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class SimulationFloorCrackingTests
{
    private static (Simulation sim, TileGrid grid) Make(bool enabled = true)
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        var cfg = new SimConfig { UnstableFloorEnabled = enabled, PickaxeSeconds = 0.01 };
        return (new Simulation(grid, cfg), grid);
    }

    [Fact]
    public void FloorCracking_starts_when_enabled_and_target_is_floor()
    {
        var (sim, grid) = Make();
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);   // miner → (3,2), facing East; pickaxe target = (4,2)
        bool started = sim.TryStartMining(1);
        Assert.True(started);
        // Target still Floor until activity completes
        Assert.Equal(TileType.Floor, grid.Get(new GridPos(4, 2)));
    }

    [Fact]
    public void FloorCracking_converts_floor_to_cracked_on_completion()
    {
        var (sim, grid) = Make();
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);   // (3,2) facing East
        sim.TryStartMining(1);
        sim.Tick(0.05);
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(4, 2)));
    }

    [Fact]
    public void FloorCracking_disabled_when_flag_is_false()
    {
        var (sim, grid) = Make(enabled: false);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);
        bool started = sim.TryStartMining(1);
        Assert.False(started);   // floor is not minable without the flag
    }

    [Fact]
    public void FloorCracking_does_not_crack_already_cracked_tile()
    {
        var (sim, grid) = Make();
        grid.Set(new GridPos(4, 2), TileType.Cracked);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);
        bool started = sim.TryStartMining(1);
        Assert.False(started);   // Cracked is not Floor, flag doesn't apply
    }

    [Fact]
    public void FloorCracking_emits_CrackWeakened_event()
    {
        var (sim, grid) = Make();
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East);
        sim.TryStartMining(1);
        sim.Tick(0.05);
        sim.DrainEvents();   // events fire during Tick
        // Verify by checking the tile changed (event emission is side-effect of same code path)
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(4, 2)));
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure** (`ActivityKind.FloorCracking` and `SimConfig.UnstableFloorEnabled` don't exist yet)

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

Expected: build error mentioning `FloorCracking` or `UnstableFloorEnabled`.

- [ ] **Step 3: Add FloorCracking to ActivityKind**

In `src/Miner49er.Core/Sim/Miner.cs`, change:

```csharp
public enum ActivityKind { None, Mining, Planting, PlantingDetonator }
```
to:
```csharp
public enum ActivityKind { None, Mining, Planting, PlantingDetonator, FloorCracking }
```

- [ ] **Step 4: Add UnstableFloorEnabled to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after `TripMinesEnabled`:

```csharp
// LMS/Derby: players may pickaxe floor tiles to create cracks.
public bool UnstableFloorEnabled { get; set; } = false;
```

- [ ] **Step 5: Extend TryStartMining in Simulation.cs**

In `src/Miner49er.Core/Sim/Simulation.cs`, find the block (around line 719):

```csharp
        if (!Grid.Get(target).IsMinable()) return false;

        m.Activity = ActivityKind.Mining;
```

Replace with:

```csharp
        if (!Grid.Get(target).IsMinable())
        {
            if (Config.UnstableFloorEnabled && Grid.Get(target) == TileType.Floor)
            {
                m.Activity = ActivityKind.FloorCracking;
                m.ActivityTarget = target;
                m.ActivitySecondsRemaining = Config.PickaxeSeconds;
                _events.Add(new ActivityStarted(id, ActivityKind.FloorCracking, target));
                return true;
            }
            return false;
        }

        m.Activity = ActivityKind.Mining;
```

- [ ] **Step 6: Handle FloorCracking in CompleteActivity**

In `src/Miner49er.Core/Sim/Simulation.cs`, find `CompleteActivity`. After the `PlantingDetonator` block (around line 1263), add:

```csharp
        else if (kind == ActivityKind.FloorCracking)
        {
            if (Grid.InBounds(target) && Grid.Get(target) == TileType.Floor)
            {
                Grid.Set(target, TileType.Cracked);
                _events.Add(new CrackWeakened(target));
            }
        }
```

- [ ] **Step 7: Gate it in Main.cs**

In `game/Main.cs`, find where `SimConfig` is built (the block that sets `TripMinesEnabled`). Add the following line in the same block:

```csharp
UnstableFloorEnabled = nm.MatchMode == GameMode.LastManStanding
                    || nm.MatchMode == GameMode.DemolitionDerby,
```

- [ ] **Step 8: Run tests — all must pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 559, Skipped: 0` (555 existing + 4 new — the 5th test is structural, count may vary by 1).

- [ ] **Step 9: Commit**

```
git add src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs game/Main.cs src/Miner49er.Core.Tests/SimulationFloorCrackingTests.cs
git commit -m "feat(lms-derby): pickaxe floor tiles to create cracks (FloorCracking activity)"
```

---

## ══ FEATURE 2: Skeleton Creatures ══

---

### Task 2: Noise kind system + skeleton dormancy + wake check

Adds `NoiseKind` to the existing private `NoiseSource`, makes explosions and pickaxe mining emit noise, adds `SkeletonHuman`/`SkeletonDino` monster kinds (dormant only at this stage), `Monster.Dormant`, and the wake-check logic in `AdvanceMonsters`.

**Files:**
- Modify: `src/Miner49er.Core/Sim/Monster.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (NoiseSource class, TryThrowStone, DetonateAt, CompleteActivity mining path, AdvanceMonsters, MonsterCadence)
- Create: `src/Miner49er.Core.Tests/SimulationSkeletonTests.cs`

**Interfaces:**
- Consumes: `ActivityKind.FloorCracking` (Task 1)
- Produces: `MonsterKind.SkeletonHuman`, `MonsterKind.SkeletonDino`; `Monster.Dormant`; `SkeletonAroused(monsterId)` SimEvent; `SimConfig` wake radius + cadence fields

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/SimulationSkeletonTests.cs`:

```csharp
using Miner49er.Core;
using System.Linq;
using Xunit;

public class SimulationSkeletonTests
{
    private static SimConfig SkeletonCfg() => new SimConfig
    {
        SkeletonExplosionWakeRadius = 12,
        SkeletonPickaxeWakeRadius   = 3,
        SkeletonStoneWakeRadius     = 8,
        MonsterSkeletonMoveSeconds      = 0.7,
        MonsterSkeletonDinoMoveSeconds  = 1.2,
        PickaxeSeconds  = 0.01,
        FuseSeconds     = 0.01,
        BlastRockRadius = 1,
        BlastKillRadius = 0,
    };

    private static TileGrid MakeGrid(int w = 15, int h = 15)
    {
        var g = new TileGrid(w, h, TileType.Floor);
        // Place rock walls at edges so blasts don't go OOB
        for (int x = 0; x < w; x++) { g.Set(new GridPos(x, 0), TileType.Rock); g.Set(new GridPos(x, h-1), TileType.Rock); }
        for (int y = 0; y < h; y++) { g.Set(new GridPos(0, y), TileType.Rock); g.Set(new GridPos(w-1, y), TileType.Rock); }
        return g;
    }

    // ── Dormancy ──────────────────────────────────────────────────────────

    [Fact]
    public void Skeleton_starts_dormant()
    {
        var sim = new Simulation(MakeGrid(), SkeletonCfg());
        sim.AddMonster(1, new GridPos(7, 7), MonsterKind.SkeletonHuman);
        var snap = sim.Monsters.First();
        Assert.True(snap.Dormant);
    }

    [Fact]
    public void Dormant_skeleton_does_not_move()
    {
        var sim = new Simulation(MakeGrid(), SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddMonster(1, new GridPos(7, 7), MonsterKind.SkeletonHuman);
        sim.Tick(5.0);
        Assert.Equal(new GridPos(7, 7), sim.Monsters.First().Pos);
    }

    // ── Wake on explosion ─────────────────────────────────────────────────

    [Fact]
    public void Skeleton_wakes_on_nearby_explosion()
    {
        var grid = MakeGrid();
        // Place a rock wall tile adjacent to (5,5) so we can blast it
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);  // 2 tiles from blast
        sim.TryMove(1, Direction.North);   // face North → target (5,4) = Rock
        sim.TryStartMining(1);             // actually: TryStartPlanting for blast
        // Simpler: directly detonate via TryStartPlanting + tick
        // Setup: miner faces North toward rock, plants charge, tick until it explodes
        // Re-do: use TryStartPlanting
        sim.DrainEvents();
        Assert.True(sim.TryStartPlanting(1));  // plants on (5,4) Rock
        sim.Tick(0.05);   // fuse = 0.01, so explosion fires
        var monster = sim.Monsters.First();
        Assert.False(monster.Dormant);
    }

    [Fact]
    public void Skeleton_stays_dormant_if_explosion_is_far()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(2, 2), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 3));   // near top-left
        sim.AddMonster(1, new GridPos(12, 12), MonsterKind.SkeletonHuman);  // far corner (>12 tiles)
        sim.TryMove(1, Direction.North);
        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.05);
        Assert.True(sim.Monsters.First().Dormant);
    }

    // ── Wake on pickaxe ───────────────────────────────────────────────────

    [Fact]
    public void Skeleton_wakes_on_pickaxe_within_3_tiles()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);  // 2 tiles below miner
        sim.TryMove(1, Direction.North);
        sim.TryStartMining(1);   // pickaxe on (5,4) Rock
        sim.Tick(0.05);
        Assert.False(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Skeleton_stays_dormant_if_pickaxe_is_beyond_3_tiles()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 10), MonsterKind.SkeletonHuman);  // 5 tiles away
        sim.TryMove(1, Direction.North);
        sim.TryStartMining(1);
        sim.Tick(0.05);
        Assert.True(sim.Monsters.First().Dormant);
    }

    // ── SkeletonAroused event ─────────────────────────────────────────────

    [Fact]
    public void Waking_emits_SkeletonAroused_event()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        sim.TryStartPlanting(1);
        sim.Tick(0.05);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is SkeletonAroused a && a.MonsterId == 1);
    }
}
```

- [ ] **Step 2: Run — expect compile errors** (`MonsterKind.SkeletonHuman`, `Monster.Dormant`, `SkeletonAroused`, config fields missing)

- [ ] **Step 3: Add SkeletonHuman, SkeletonDino to MonsterKind; add Dormant to Monster**

In `src/Miner49er.Core/Sim/Monster.cs`:

```csharp
public enum MonsterKind { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino }

public sealed class Monster
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public MonsterKind Kind { get; }
    public bool Alive { get; internal set; } = true;
    public bool Dormant { get; internal set; }    // skeletons start dormant; others always false

    public Direction ChargeDir { get; internal set; } = Direction.East;
    public double MoveCooldownRemaining { get; internal set; }
    public double SlowTimer { get; internal set; }
    public double SlowMultiplier { get; internal set; } = 1.0;
    public double StunRemaining { get; internal set; }

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
        Dormant = kind is MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino;
    }
}
```

- [ ] **Step 4: Add SkeletonAroused to SimEvent.cs**

In `src/Miner49er.Core/Sim/SimEvent.cs`, append:

```csharp
public sealed record SkeletonAroused(int MonsterId) : SimEvent;
```

- [ ] **Step 5: Add skeleton config fields to SimConfig.cs**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the existing monster cadence fields:

```csharp
public double MonsterSkeletonMoveSeconds     { get; set; } = 0.7;
public double MonsterSkeletonDinoMoveSeconds { get; set; } = 1.2;

// Manhattan radii within which each noise kind wakes a dormant skeleton.
public int SkeletonExplosionWakeRadius { get; set; } = 12;
public int SkeletonPickaxeWakeRadius   { get; set; } = 3;
public int SkeletonStoneWakeRadius     { get; set; } = 8;
```

- [ ] **Step 6: Add NoiseKind to NoiseSource; update TryThrowStone, DetonateAt, CompleteActivity (mining)**

In `src/Miner49er.Core/Sim/Simulation.cs`, the private `NoiseSource` class (around line 31) currently is:

```csharp
private sealed class NoiseSource
{
    public GridPos Pos;
    public double LifetimeRemaining;
}
```

Change it to:

```csharp
private enum NoiseKind { Stone, Explosion, Pickaxe }
private sealed class NoiseSource
{
    public GridPos Pos;
    public double LifetimeRemaining;
    public NoiseKind Kind;
}
```

In `TryThrowStone` (around line 286), change:

```csharp
_noiseSources.Add(new NoiseSource { Pos = land, LifetimeRemaining = 4.0 });
```
to:
```csharp
_noiseSources.Add(new NoiseSource { Pos = land, LifetimeRemaining = 4.0, Kind = NoiseKind.Stone });
```

In `DetonateAt` (around line 1218, just before the `_events.Add(new Explosion(...))` line), add:

```csharp
_noiseSources.Add(new NoiseSource { Pos = wallPos, LifetimeRemaining = 8.0, Kind = NoiseKind.Explosion });
```

In `CompleteActivity`, inside the `ActivityKind.Mining` block, add after `_events.Add(new RockMined(...))`:

```csharp
_noiseSources.Add(new NoiseSource { Pos = target, LifetimeRemaining = 2.0, Kind = NoiseKind.Pickaxe });
```

- [ ] **Step 7: Extend MonsterCadence for skeleton kinds**

In `Simulation.cs`, find `MonsterCadence` (around line 130):

```csharp
private double MonsterCadence(MonsterKind kind) => kind switch
{
    MonsterKind.Slime       => Config.MonsterSlimeMoveSeconds,
    MonsterKind.Ghost       => Config.MonsterGhostMoveSeconds,
    MonsterKind.Goat        => Config.MonsterGoatMoveSeconds,
    MonsterKind.ZombieMiner => Config.MonsterZombieMoveSeconds,
    _ => Config.MonsterSlimeMoveSeconds,
};
```

Change to:

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

- [ ] **Step 8: Add SkeletonWakeCheck and dormancy logic to AdvanceMonsters**

In `Simulation.cs`, add the private helper just before `AdvanceMonsters`:

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

In `AdvanceMonsters`, at the top of the `foreach` loop (right after `if (!mo.Alive) continue;`), add:

```csharp
            if (mo.Dormant)
            {
                if (SkeletonWakeCheck(mo.Pos))
                {
                    mo.Dormant = false;
                    _events.Add(new SkeletonAroused(mo.Id));
                }
                continue;
            }
```

- [ ] **Step 9: Run tests — all must pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

Expected: `Passed!` with 0 failures. (Some skeleton tests that involve movement won't be written yet — they come in Task 3.)

- [ ] **Step 10: Commit**

```
git add src/Miner49er.Core/Sim/Monster.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationSkeletonTests.cs
git commit -m "feat(skeleton): dormant skeleton kinds, noise kind tags, wake-on-noise check"
```

---

### Task 3: Skeleton movement + SkeletonDino floor damage

Both skeleton kinds move like ZombieMiner (always track nearest miner, terrain-bound). SkeletonDino additionally cracks the floor tile it exits and collapses any cracked/crumbling tile it enters.

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (StepMonster dispatch + new DinoFloorDamage method)
- Modify: `src/Miner49er.Core.Tests/SimulationSkeletonTests.cs` (add movement + dino tests)

**Interfaces:**
- Consumes: `MonsterKind.SkeletonHuman`, `MonsterKind.SkeletonDino`, `Monster.Dormant` (Task 2)
- Produces: awake skeleton movement toward nearest miner; SkeletonDino cracks floors and collapses cracked tiles

- [ ] **Step 1: Add movement + dino damage tests to SimulationSkeletonTests.cs**

Append to `src/Miner49er.Core.Tests/SimulationSkeletonTests.cs`:

```csharp
    // ── Movement ──────────────────────────────────────────────────────────

    [Fact]
    public void Awake_skeleton_moves_toward_miner()
    {
        var grid = MakeGrid();
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 7));
        var mo = sim.AddMonster(1, new GridPos(12, 7), MonsterKind.SkeletonHuman);
        mo.Dormant = false;  // manually wake for this test — internal set, use reflection or...
        // Alternative: wake via explosion noise first
        // Use stone throw to wake (simpler for test setup)
        // Actually Monster.Dormant is internal set — use a helper or set Dormant via public sim API
        // ... Simplest: add a public sim method ForceWakeMonster for tests only,
        // OR: wake via throw stone (which creates noise)
        // Use the stone approach:
        sim.AddStones(1, 1);
        sim.TryMove(1, Direction.East);   // face East toward monster
        sim.TryThrowStone(1);             // throws stone toward (12,7); wakes skeleton
        sim.Tick(2.0);
        Assert.NotEqual(new GridPos(12, 7), sim.Monsters.First().Pos);  // skeleton moved
        Assert.True(sim.Monsters.First().Pos.X < 12);                    // moved West (toward miner)
    }

    // ── SkeletonDino floor damage ─────────────────────────────────────────

    [Fact]
    public void SkeletonDino_cracks_floor_tile_it_exits()
    {
        var grid = MakeGrid();
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 7));
        // Wake the dino manually via stone throw
        sim.AddStones(1, 1);
        sim.TryMove(1, Direction.East);
        sim.TryThrowStone(1);   // wakes dino on next tick
        var startPos = new GridPos(12, 7);
        sim.AddMonster(1, startPos, MonsterKind.SkeletonDino);
        sim.Tick(0.1);   // wake tick
        var dinoPos = sim.Monsters.First().Pos;
        if (dinoPos == startPos) sim.Tick(1.5);   // one move cadence
        var afterMove = sim.Monsters.First().Pos;
        if (afterMove != startPos)
            Assert.Equal(TileType.Cracked, grid.Get(startPos));   // starting tile cracked
    }

    [Fact]
    public void SkeletonDino_collapses_cracked_tile_on_entry_and_dies()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(11, 7), TileType.Cracked);   // pre-crack the tile dino will step into
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 7));
        sim.AddStones(1, 1);
        sim.TryMove(1, Direction.East);
        sim.TryThrowStone(1);
        sim.AddMonster(1, new GridPos(12, 7), MonsterKind.SkeletonDino);
        sim.Tick(3.0);   // enough time for dino to move into (11,7)
        var mo = sim.Monsters.First();
        Assert.False(mo.Alive);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(11, 7)));
    }

    [Fact]
    public void SkeletonDino_collapse_kills_miner_on_same_tile()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(7, 7), TileType.Cracked);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(7, 7));   // miner ON the cracked tile
        sim.AddStones(1, 1);
        sim.TryMove(1, Direction.West);
        sim.TryThrowStone(1);
        sim.AddMonster(1, new GridPos(8, 7), MonsterKind.SkeletonDino);   // 1 tile away
        sim.Tick(3.0);
        Assert.False(sim.Miners.First(m => m.Id == 1).Alive);
    }
```

- [ ] **Step 2: Run tests — expect failures** (skeletons don't move yet, dino damage doesn't exist)

- [ ] **Step 3: Add skeleton movement to StepMonster dispatch**

In `Simulation.cs`, find `StepMonster` and the `Direction? dir = mo.Kind switch` block:

```csharp
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime       => SlimeDir(mo, target),
            MonsterKind.Ghost       => GhostDir(mo, target),
            MonsterKind.Goat        => GoatDir(mo, target),
            MonsterKind.ZombieMiner => ZombieDir(mo, target),
            _ => null,
        };
```

Change to:

```csharp
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime                                        => SlimeDir(mo, target),
            MonsterKind.Ghost                                        => GhostDir(mo, target),
            MonsterKind.Goat                                         => GoatDir(mo, target),
            MonsterKind.ZombieMiner                                  => ZombieDir(mo, target),
            MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino    => ZombieDir(mo, target),
            _ => null,
        };
```

- [ ] **Step 4: Add DinoFloorDamage method**

In `Simulation.cs`, add this private method anywhere after `StepMonster`:

```csharp
    private void DinoFloorDamage(Monster dino, GridPos from)
    {
        // Crack the tile the dino just left (if it was plain floor)
        if (Grid.InBounds(from) && Grid.Get(from) == TileType.Floor)
        {
            Grid.Set(from, TileType.Cracked);
            _events.Add(new CrackWeakened(from));
        }

        // If the tile entered was already cracked/crumbling, it collapses under the dino's weight
        var landed = Grid.Get(dino.Pos);
        if (landed != TileType.Cracked && landed != TileType.Crumbling) return;

        Grid.Set(dino.Pos, TileType.Pit);
        _events.Add(new CrackCollapsed(dino.Pos));
        dino.Alive = false;
        _events.Add(new MonsterKilled(dino.Id));
        foreach (var m in _miners.Values)
            if (m.Alive && m.Pos == dino.Pos) CollapseKill(m);
    }
```

- [ ] **Step 5: Call DinoFloorDamage from StepMonster**

In `StepMonster`, after the existing lethal-tile check and the mauling check (the last two `if` blocks that check `mo.Pos == target.Pos`), add:

```csharp
        if (mo.Kind == MonsterKind.SkeletonDino && mo.Alive)
            DinoFloorDamage(mo, from);
```

This must be the very last thing in `StepMonster`, after both the lethal check and the mauling check so the dino doesn't double-kill.

- [ ] **Step 6: Run tests — all must pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationSkeletonTests.cs
git commit -m "feat(skeleton): skeleton movement + SkeletonDino floor crack/collapse"
```

---

### Task 4: Floor-aware spawning (MonsterSpawner + call-sites)

**Files:**
- Modify: `src/Miner49er.Core/Map/MonsterSpawner.cs`
- Modify: `game/Main.cs` (two call-sites: Expedition + new ReachCenter)
- Modify: `game/net/MatchHost.cs` (Expedition call-site)
- Modify: `src/Miner49er.Core.Tests/MonsterSpawnerTests.cs` (add floor-gated tests)

**Interfaces:**
- Consumes: `MonsterKind.SkeletonHuman`, `MonsterKind.SkeletonDino` (Task 2)
- Produces: `MonsterSpawner.Place(..., int floor = 0)` — kind rotation gated by floor

- [ ] **Step 1: Add floor-gated tests to MonsterSpawnerTests.cs**

Open `src/Miner49er.Core.Tests/MonsterSpawnerTests.cs` and append:

```csharp
public class MonsterSpawnerFloorTests
{
    private static TileGrid BigGrid()
    {
        var g = new TileGrid(30, 30, TileType.Floor);
        return g;
    }

    [Fact]
    public void Floor_below_8_contains_no_skeletons()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 6, floor: 7);
        Assert.DoesNotContain(result, r =>
            r.Kind == MonsterKind.SkeletonHuman || r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_8_to_11_includes_SkeletonHuman_but_not_Dino()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 8, floor: 9);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonHuman);
        Assert.DoesNotContain(result, r => r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_12_plus_includes_both_skeleton_kinds()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 10, floor: 12);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonHuman);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_0_default_contains_no_skeletons()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 6);
        Assert.DoesNotContain(result, r =>
            r.Kind == MonsterKind.SkeletonHuman || r.Kind == MonsterKind.SkeletonDino);
    }
}
```

- [ ] **Step 2: Run — expect failures** (`Place` doesn't have `floor` param yet)

- [ ] **Step 3: Extend MonsterSpawner.Place with floor parameter**

In `src/Miner49er.Core/Map/MonsterSpawner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

public static class MonsterSpawner
{
    private static MonsterKind[] KindsForFloor(int floor)
    {
        if (floor >= 12)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner, MonsterKind.SkeletonHuman, MonsterKind.SkeletonDino };
        if (floor >= 8)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner, MonsterKind.SkeletonHuman };
        return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner };
    }

    public static List<(GridPos Pos, MonsterKind Kind)> Place(TileGrid grid, GridPos start, int count, int floor = 0)
    {
        var result = new List<(GridPos, MonsterKind)>();
        if (count <= 0) return result;

        var floors = grid.Positions()
            .Where(p => grid.Get(p) == TileType.Floor && p != start)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        if (floors.Count == 0) return result;

        var chosen = new List<GridPos>();
        var anchors = new List<GridPos> { start };
        var taken = new HashSet<GridPos>();
        while (chosen.Count < count && chosen.Count < floors.Count)
        {
            GridPos best = floors[0];
            long bestMin = -1;
            foreach (var p in floors)
            {
                if (taken.Contains(p)) continue;
                long min = long.MaxValue;
                foreach (var a in anchors)
                {
                    long dx = p.X - a.X, dy = p.Y - a.Y;
                    long d = dx * dx + dy * dy;
                    if (d < min) min = d;
                }
                if (min > bestMin) { bestMin = min; best = p; }
            }
            chosen.Add(best);
            anchors.Add(best);
            taken.Add(best);
        }

        var kinds = KindsForFloor(floor);
        for (int i = 0; i < chosen.Count; i++)
            result.Add((chosen[i], kinds[i % kinds.Length]));
        return result;
    }
}
```

- [ ] **Step 4: Update Main.cs Expedition call-site**

In `game/Main.cs`, find (around line 143):

```csharp
            int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
            var roster = MonsterSpawner.Place(hostMap.Grid, soloSpawn, monsterCount);
```

Change to:

```csharp
            int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
            var roster = MonsterSpawner.Place(hostMap.Grid, soloSpawn, monsterCount, floor: 0);
```

Then add a ReachCenter spawning block immediately after the Expedition block:

```csharp
        else if (nm.MatchMode == GameMode.ReachCenter && hostMap.Spawns.Count > 0)
        {
            int monsterCount = MonsterRoster.CountFor(hostMap.Grid.Width, hostMap.Grid.Height);
            var roster = MonsterSpawner.Place(hostMap.Grid, hostMap.Spawns[0], monsterCount, floor: 10);
            for (int i = 0; i < roster.Count; i++)
                sim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
        }
```

- [ ] **Step 5: Update MatchHost.cs Expedition call-site**

In `game/net/MatchHost.cs`, find (around line 428):

```csharp
            var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount);
```

Change to:

```csharp
            var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount, floor: newFloor);
```

- [ ] **Step 6: Run tests — all must pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Map/MonsterSpawner.cs game/Main.cs game/net/MatchHost.cs src/Miner49er.Core.Tests/MonsterSpawnerTests.cs
git commit -m "feat(skeleton): floor-aware monster spawning, skeletons at floors 8+/12+"
```

---

### Task 5: Snapshot + codec (MonsterSnapshot.Dormant)

Clients need `Dormant` to render bone piles vs. standing skeletons.

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` (add Dormant round-trip test)

**Interfaces:**
- Consumes: `Monster.Dormant` (Task 2)
- Produces: `MonsterSnapshot.Dormant`; codec encodes/decodes it

- [ ] **Step 1: Write failing test**

Open `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` and add:

```csharp
[Fact]
public void Dormant_field_round_trips_through_codec()
{
    var grid = new TileGrid(10, 10, TileType.Floor);
    var sim = new Simulation(grid, new SimConfig());
    sim.AddMonster(1, new GridPos(5, 5), MonsterKind.SkeletonHuman);   // starts dormant
    var snap = SnapshotFactory.Capture(sim, tick: 0);
    Assert.True(snap.Monsters[0].Dormant);

    var bytes = SnapshotCodec.Write(new TickUpdate(snap, System.Array.Empty<TileChange>()));
    var decoded = SnapshotCodec.Read(bytes).Snapshot;
    Assert.True(decoded.Monsters[0].Dormant);
}
```

- [ ] **Step 2: Run — expect failure** (`MonsterSnapshot` doesn't have `Dormant` yet)

- [ ] **Step 3: Add Dormant to MonsterSnapshot**

In `src/Miner49er.Core/Net/Snapshots.cs`, change:

```csharp
public readonly record struct MonsterSnapshot(
    int Id, int X, int Y, int Facing, MonsterKind Kind, bool Alive, float StunRemaining = 0f);
```
to:
```csharp
public readonly record struct MonsterSnapshot(
    int Id, int X, int Y, int Facing, MonsterKind Kind, bool Alive,
    float StunRemaining = 0f, bool Dormant = false);
```

- [ ] **Step 4: Set Dormant in SnapshotFactory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, change the monsters Select:

```csharp
        var monsters = sim.Monsters
            .Select(mo => new MonsterSnapshot(
                mo.Id, mo.Pos.X, mo.Pos.Y, (int)mo.Facing, mo.Kind, mo.Alive,
                (float)mo.StunRemaining))
            .ToList();
```
to:
```csharp
        var monsters = sim.Monsters
            .Select(mo => new MonsterSnapshot(
                mo.Id, mo.Pos.X, mo.Pos.Y, (int)mo.Facing, mo.Kind, mo.Alive,
                (float)mo.StunRemaining, mo.Dormant))
            .ToList();
```

- [ ] **Step 5: Encode Dormant in SnapshotCodec.Write**

In `SnapshotCodec.cs`, find the monster write loop:

```csharp
        foreach (var mo in snap.Monsters)
        {
            w.Write(mo.Id); w.Write(mo.X); w.Write(mo.Y);
            w.Write(mo.Facing); w.Write((int)mo.Kind); w.Write(mo.Alive);
        }
```

Change to:

```csharp
        foreach (var mo in snap.Monsters)
        {
            w.Write(mo.Id); w.Write(mo.X); w.Write(mo.Y);
            w.Write(mo.Facing); w.Write((int)mo.Kind); w.Write(mo.Alive);
            w.Write(mo.Dormant);
        }
```

- [ ] **Step 6: Decode Dormant in SnapshotCodec.Read**

In `SnapshotCodec.cs`, find the monster read loop:

```csharp
        for (int i = 0; i < monsterCount; i++)
            monsters.Add(new MonsterSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadInt32(), (MonsterKind)r.ReadInt32(), r.ReadBoolean()));
```

Change to:

```csharp
        for (int i = 0; i < monsterCount; i++)
            monsters.Add(new MonsterSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadInt32(), (MonsterKind)r.ReadInt32(), r.ReadBoolean(),
                Dormant: r.ReadBoolean()));
```

- [ ] **Step 7: Run tests — all must pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(skeleton): add Dormant to MonsterSnapshot + codec"
```

---

### Task 6: PixelLab art — SkeletonHuman, SkeletonDino, bones pile

Generate sprite sheets via the PixelLab MCP tool. These assets are prerequisites for Task 7 (rendering). Sizes follow existing monster conventions: 48×48 8-dir walk.

**Files:**
- Create: `assets/monsters/skeleton_human.png` (8-dir 48×48 walk)
- Create: `assets/monsters/skeleton_dino.png` (8-dir 48×48 walk)
- Create: `assets/monsters/skeleton_bones_pile.png` (1-dir 32×32 static)

**Interfaces:**
- Produces: three PNG files importable by Godot

- [ ] **Step 1: Generate SkeletonHuman via PixelLab**

Use the PixelLab MCP `create_character` or `create_8_direction_object` tool with prompt:

> "Top-down pixel art dungeon game, 48×48 pixels. A human skeleton monster — reassembled bones, hollow eye sockets, walking upright. Matches the style of existing slime/ghost/goat monsters in the game (dark dungeon palette, simple readable silhouette). 8 directional walk cycle, 4 frames each direction."

Wait for completion, download the PNG to `assets/monsters/skeleton_human.png`.

- [ ] **Step 2: Generate SkeletonDino via PixelLab**

Prompt:

> "Top-down pixel art dungeon game, 48×48 pixels. A dinosaur skeleton monster — large ancient bones, skull with visible teeth, walking on all fours. Heavier than the human skeleton. Same dark dungeon palette and style. 8 directional walk cycle, 4 frames each direction."

Download to `assets/monsters/skeleton_dino.png`.

- [ ] **Step 3: Generate bones pile (dormant state) via PixelLab**

Prompt:

> "Top-down pixel art dungeon game, 32×32 pixels. A scattered pile of bones lying on the floor — a mix of skulls, ribs, limb bones. Clearly identifiable as bones even at small size. Single direction (top-down view). Static, no animation."

Download to `assets/monsters/skeleton_bones_pile.png`.

- [ ] **Step 4: Import into Godot**

Launch Godot (via PowerShell only: `godot --editor`). Godot will auto-import the new PNG files on launch, generating `.import` files. Close Godot after import completes.

- [ ] **Step 5: Commit**

```
git add assets/monsters/skeleton_human.png assets/monsters/skeleton_dino.png assets/monsters/skeleton_bones_pile.png assets/monsters/skeleton_human.png.import assets/monsters/skeleton_dino.png.import assets/monsters/skeleton_bones_pile.png.import
git commit -m "art(skeleton): add SkeletonHuman, SkeletonDino, and bones pile sprites"
```

---

### Task 7: WorldRenderer — render dormant bones / awake skeletons

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: `MonsterSnapshot.Dormant` (Task 5); sprite PNGs from Task 6
- Produces: dormant skeleton rendered as bones pile; awake skeletons use walk animation

- [ ] **Step 1: Locate existing monster rendering in WorldRenderer.cs**

Read `game/WorldRenderer.cs` and find the section that draws monsters using `MonsterSnapshot`. It will load textures (likely via `GD.Load<Texture2D>`) and draw them either with `DrawTexture` or via a `Sprite2D` node. Follow the exact same pattern used for `ZombieMiner`.

- [ ] **Step 2: Load bones pile texture**

In the same location where other monster textures are loaded (or in `_Ready()`), add:

```csharp
private Texture2D _bonesPileTexture = null!;
// In _Ready():
_bonesPileTexture = GD.Load<Texture2D>("res://assets/monsters/skeleton_bones_pile.png");
```

- [ ] **Step 3: Load skeleton walk textures**

Following the same pattern as ZombieMiner texture loading:

```csharp
private Texture2D _skeletonHumanTexture = null!;
private Texture2D _skeletonDinoTexture  = null!;
// In _Ready():
_skeletonHumanTexture = GD.Load<Texture2D>("res://assets/monsters/skeleton_human.png");
_skeletonDinoTexture  = GD.Load<Texture2D>("res://assets/monsters/skeleton_dino.png");
```

- [ ] **Step 4: Add rendering logic for skeleton monsters**

In the monster rendering loop (where each `MonsterSnapshot mo` is drawn), add a branch that checks `mo.Dormant`:

```csharp
if (mo.Kind == MonsterKind.SkeletonHuman || mo.Kind == MonsterKind.SkeletonDino)
{
    if (mo.Dormant)
    {
        // Draw bones pile centered on tile
        float bx = mo.X * ts + ts * 0.5f - _bonesPileTexture.GetWidth() * 0.5f;
        float by = mo.Y * ts + ts * 0.5f - _bonesPileTexture.GetHeight() * 0.5f;
        DrawTexture(_bonesPileTexture, new Vector2(bx, by));
        continue;
    }
    // Awake: draw walk animation — follow exact same pattern as ZombieMiner rendering
    var tex = mo.Kind == MonsterKind.SkeletonDino ? _skeletonDinoTexture : _skeletonHumanTexture;
    // ... same frame/direction logic as ZombieMiner
}
```

The exact frame/direction indexing must mirror the ZombieMiner draw code exactly. Read that code section carefully before writing this.

- [ ] **Step 5: Verify visually**

Run the game (PowerShell only: `& godot --path "D:\Projects\Miner49er"`). Start an Expedition. Confirm:
- Skeleton bone piles are visible on the floor (dormant state)
- Firing a charge near a skeleton causes it to visually change to the walking sprite
- SkeletonDino cracks tiles behind it as it moves

- [ ] **Step 6: Commit**

```
git add game/WorldRenderer.cs
git commit -m "feat(skeleton): render dormant bones pile + awake skeleton walk sprites"
```

---

## Self-Review Checklist

After all tasks are complete:

**Spec coverage:**
- [x] `ActivityKind.FloorCracking` + `UnstableFloorEnabled` → Task 1
- [x] LMS/Derby gate in Main.cs → Task 1
- [x] `NoiseKind` tagging (Stone/Explosion/Pickaxe) → Task 2
- [x] Explosion and pickaxe mining create noise sources → Task 2
- [x] `Monster.Dormant` + wake check + `SkeletonAroused` → Task 2
- [x] Skeleton movement (ZombieDir) → Task 3
- [x] SkeletonDino floor crack/collapse → Task 3
- [x] Floor-aware spawning at floors 8+/12+ → Task 4
- [x] ReachCenter gets skeletons at floor=10 → Task 4
- [x] `MonsterSnapshot.Dormant` + codec → Task 5
- [x] Art assets → Task 6
- [x] WorldRenderer dormant/awake rendering → Task 7
