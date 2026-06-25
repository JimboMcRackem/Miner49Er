# Lives, Permanent Buffs & High Score Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a lives system (3 in solo, 1 in multi), permanent stackable buffs (SpeedPotion/LongerVision/BiggerBlast), chest loot table (LifePotion/buffs), BossChest that opens the boss exit, invulnerability grace on spawn, and a local top-10 high score table.

**Architecture:** Core layer (pure C#) handles all sim rules — permanent buff counters on `Miner`, invulnerability timer, loot tables, new `ItemKind` values. The game layer (`MatchHost`) tracks lives, perm levels, and cumulative gold across floors; the client reads lives from the tick snapshot. `ScoreStore` is a static Godot class writing `user://scores.cfg` via `ConfigFile`.

**Tech Stack:** C# 10 / .NET 8, Godot 4.6.3 .NET, xUnit for core tests. Run tests: `dotnet test src/Miner49er.Core.Tests` from repo root `D:\Projects\Miner49er`. Build: `dotnet build Miner49er.csproj`. Run Godot via **PowerShell only** — never Bash.

## Global Constraints

- 4-space indent in `src/Miner49er.Core/`, TAB indent in `game/`
- Never stage `.superpowers/`, `*.png.import`, `*.uid` files
- Never `git add -A`; always name specific files when staging
- Co-authored commit footers: `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
- Run godot via PowerShell only (Bash tool has a broken shim for headless)
- This is solo Expedition only for lives/score features; multiplayer keeps 1 life (no respawn)

---

## File Map

**Core (modified):**
- `src/Miner49er.Core/Sim/Miner.cs` — add perm levels + invul timer
- `src/Miner49er.Core/Sim/SimConfig.cs` — add perm config + RequireChestForEscape
- `src/Miner49er.Core/Sim/Simulation.cs` — ApplyBuff, Effective*, SetPermLevels, invul guards, AdvanceInvulnerability, chest loot, remove ChestGrabbedBy
- `src/Miner49er.Core/Sim/SimEvent.cs` — add LifeRestored
- `src/Miner49er.Core/Sim/RoundResolver.cs` — remove ChestGrabbedBy Win condition
- `src/Miner49er.Core/Map/Item.cs` — add LifePotion, BossChest; add IsPlaceable()
- `src/Miner49er.Core/Map/MapConfig.cs` — add ChestCount; update FloorConfig
- `src/Miner49er.Core/Map/MapGenerator.cs` — PlaceItems cycle, PlaceChests, GenerateBossFloor update
- `src/Miner49er.Core/Map/GeneratedMap.cs` — add EscapeTile property
- `src/Miner49er.Core/Net/Snapshots.cs` — InvulRemaining on MinerSnapshot, Lives on WorldSnapshot
- `src/Miner49er.Core/Net/SnapshotFactory.cs` — capture InvulRemaining, lives param
- `src/Miner49er.Core/Net/SnapshotCodec.cs` — encode/decode new fields

**Core (new):**
- `src/Miner49er.Core/Sim/ChestLootTable.cs` — static Roll() method

**Game layer (modified):**
- `game/net/MatchHost.cs` — lives, perm levels, cumulative gold, respawn, boss win, score
- `game/net/MatchClient.cs` — Lives property, invul flash, EscapeTile from GeneratedMap
- `game/Main.cs` — HUD hearts, EscapeTile from GeneratedMap
- `game/WorldRenderer.cs` — LifePotion ♥ and BossChest ★ glyphs
- `game/ui/ResultsOverlay.cs` — second label for score
- `game/ui/MainMenu.cs` — High Scores button

**Game layer (new):**
- `game/ScoreStore.cs` — top-10 persistence via Godot ConfigFile
- `game/ui/HighScorePanel.cs` — overlay showing top 10

**Tests (new):**
- `src/Miner49er.Core.Tests/PermBuffTests.cs`
- `src/Miner49er.Core.Tests/InvulnerabilityTests.cs`
- `src/Miner49er.Core.Tests/ChestLootTests.cs`

**Tests (updated):**
- `src/Miner49er.Core.Tests/SimulationItemsTests.cs` — perm buff assertions replace StatusEffect assertions
- `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs` — remove obsolete Chest_grabbed_wins_the_dungeon
- `src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs` — BossChest kind + EscapeTile assertions
- `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` — add InvulRemaining and Lives round-trip assertions

---

### Task 1: Core – Permanent buff levels

**Files:**
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/PermBuffTests.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationItemsTests.cs`

**Interfaces:**
- Produces: `Miner.PermSpeedLevel`, `Miner.PermVisionLevel`, `Miner.PermBlastLevel` (int, `internal set`); `Simulation.SetPermLevels(int minerId, int speed, int vision, int blast)`; `SimConfig.PermSpeedFactor`, `PermVisionBonus`, `PermBlastBonus`, `MaxPermSpeedLevel`, `MaxPermVisionLevel`, `MaxPermBlastLevel`

- [ ] **Step 1: Add perm level fields to Miner**

In `src/Miner49er.Core/Sim/Miner.cs`, add after the `Effects` members:

```csharp
public int PermSpeedLevel  { get; internal set; }
public int PermVisionLevel { get; internal set; }
public int PermBlastLevel  { get; internal set; }
```

- [ ] **Step 2: Add perm config to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the `BlastSeconds` line:

```csharp
public double PermSpeedFactor   { get; set; } = 0.85;  // per-level move-cadence multiplier
public int    PermVisionBonus   { get; set; } = 1;      // +tiles of fog radius per level
public int    PermBlastBonus    { get; set; } = 1;      // +radius per level
public int    MaxPermSpeedLevel  { get; set; } = 5;
public int    MaxPermVisionLevel { get; set; } = 5;
public int    MaxPermBlastLevel  { get; set; } = 3;
```

- [ ] **Step 3: Write the failing tests**

Create `src/Miner49er.Core.Tests/PermBuffTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class PermBuffTests
{
    private static Simulation Sim()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void Perm_levels_start_at_zero()
    {
        var sim = Sim();
        var m = sim.GetMiner(1);
        Assert.Equal(0, m.PermSpeedLevel);
        Assert.Equal(0, m.PermVisionLevel);
        Assert.Equal(0, m.PermBlastLevel);
    }

    [Fact]
    public void Picking_up_SpeedPotion_increments_PermSpeedLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermSpeedLevel);
    }

    [Fact]
    public void EffectiveMoveSeconds_decreases_each_perm_speed_level()
    {
        var sim = Sim();
        double baseline = sim.EffectiveMoveSeconds(1);
        sim.SetPermLevels(1, 1, 0, 0);
        double one = sim.EffectiveMoveSeconds(1);
        sim.SetPermLevels(1, 2, 0, 0);
        double two = sim.EffectiveMoveSeconds(1);
        Assert.True(one < baseline);
        Assert.True(two < one);
    }

    [Fact]
    public void SetPermLevels_clamps_to_config_max()
    {
        var sim = Sim();
        sim.SetPermLevels(1, 99, 99, 99);
        var m = sim.GetMiner(1);
        Assert.Equal(sim.Config.MaxPermSpeedLevel,  m.PermSpeedLevel);
        Assert.Equal(sim.Config.MaxPermVisionLevel, m.PermVisionLevel);
        Assert.Equal(sim.Config.MaxPermBlastLevel,  m.PermBlastLevel);
    }

    [Fact]
    public void PermSpeed_and_mold_slow_stack_multiplicatively()
    {
        // Perm speed (via SetPermLevels) AND a StatusEffect slow (simulating mold)
        // should both reduce/increase the cadence multiplicatively.
        var sim = Sim();
        sim.SetPermLevels(1, 1, 0, 0);
        double perm = sim.EffectiveMoveSeconds(1);
        // Apply a slow via StatusEffect (mold slow channel reuses MoveSpeed)
        sim.ApplyEffect(1, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, 2.0, 5.0);
        double both = sim.EffectiveMoveSeconds(1);
        Assert.True(both > perm); // slow wins because mult *= 2.0
    }

    [Fact]
    public void LongerVision_increments_PermVisionLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermVisionLevel);
        Assert.Equal(6, sim.EffectiveVisionRadius(1)); // 5 base + 1*1 bonus
    }

    [Fact]
    public void BiggerBlast_increments_PermBlastLevel()
    {
        var sim = Sim();
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(1, sim.GetMiner(1).PermBlastLevel);
        Assert.Equal(1, sim.EffectiveBlastBonus(1)); // 1 level * 1 bonus
    }
}
```

- [ ] **Step 4: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests --filter "Class=PermBuffTests" -v minimal
```

Expected: all 7 tests FAIL (missing PermSpeedLevel, SetPermLevels, etc.)

- [ ] **Step 5: Update ApplyBuff in Simulation.cs**

Find `ApplyBuff` in `src/Miner49er.Core/Sim/Simulation.cs` (around line 633). Replace the three buff cases so they increment perm levels instead of calling `ApplyEffect`:

Old:
```csharp
case ItemKind.SpeedPotion:
    ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                Config.SpeedPotionFactor, Config.SpeedPotionSeconds);
    break;
case ItemKind.LongerVision:
    ApplyEffect(minerId, EffectKind.LongerVision, EffectChannel.VisionRadius,
                Config.VisionBonus, Config.VisionSeconds);
    break;
case ItemKind.BiggerBlast:
    ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                Config.BlastBonus, Config.BlastSeconds);
    break;
```

New:
```csharp
case ItemKind.SpeedPotion:
    m.PermSpeedLevel = Math.Min(m.PermSpeedLevel + 1, Config.MaxPermSpeedLevel);
    break;
case ItemKind.LongerVision:
    m.PermVisionLevel = Math.Min(m.PermVisionLevel + 1, Config.MaxPermVisionLevel);
    break;
case ItemKind.BiggerBlast:
    m.PermBlastLevel = Math.Min(m.PermBlastLevel + 1, Config.MaxPermBlastLevel);
    break;
```

(The `Miner m = _miners[minerId];` line at the top of `ApplyBuff` is already there — no change needed.)

- [ ] **Step 6: Update Effective* methods in Simulation.cs**

Replace `EffectiveMoveSeconds(Miner m)` (line ~151):

```csharp
private double EffectiveMoveSeconds(Miner m)
{
    double mult = Math.Pow(Config.PermSpeedFactor, m.PermSpeedLevel);
    foreach (var e in m.EffectsInternal)
        if (e.Channel == EffectChannel.MoveSpeed) mult *= e.Magnitude;
    double tile = Grid.Get(m.Pos).MoveCostMultiplier();
    return Math.Clamp(Config.BaseMoveSeconds * tile * mult,
                      Config.MinMoveSeconds, Config.MaxMoveSeconds);
}
```

Replace `EffectiveVisionRadius(Miner m)` (line ~163):

```csharp
private int EffectiveVisionRadius(Miner m)
{
    int bonus = m.PermVisionLevel * Config.PermVisionBonus;
    foreach (var e in m.EffectsInternal)
        if (e.Channel == EffectChannel.VisionRadius) bonus += (int)e.Magnitude;
    return Config.VisionRadius + bonus;
}
```

Replace `EffectiveBlastBonus(Miner m)` (line ~173):

```csharp
private int EffectiveBlastBonus(Miner m)
{
    int bonus = m.PermBlastLevel * Config.PermBlastBonus;
    foreach (var e in m.EffectsInternal)
        if (e.Channel == EffectChannel.BlastRadius) bonus += (int)e.Magnitude;
    return bonus;
}
```

- [ ] **Step 7: Add SetPermLevels to Simulation.cs**

After the `EffectiveBlastBonus` methods, add:

```csharp
public void SetPermLevels(int minerId, int speed, int vision, int blast)
{
    if (!_miners.TryGetValue(minerId, out var m)) return;
    m.PermSpeedLevel  = Math.Clamp(speed,  0, Config.MaxPermSpeedLevel);
    m.PermVisionLevel = Math.Clamp(vision, 0, Config.MaxPermVisionLevel);
    m.PermBlastLevel  = Math.Clamp(blast,  0, Config.MaxPermBlastLevel);
}
```

Also expose `Config` publicly for tests (it may already exist). Verify `public SimConfig Config { get; }` or `public SimConfig Config => _config;` exists. If not, add:

```csharp
public SimConfig Config { get; }
```

and assign it from the constructor parameter (already done as `config` field, just expose).

- [ ] **Step 8: Update existing SimulationItemsTests.cs for perm buff assertions**

In `src/Miner49er.Core.Tests/SimulationItemsTests.cs`, replace the failing tests:

Replace `Walking_onto_an_item_collects_it_and_applies_the_buff`:
```csharp
[Fact]
public void Walking_onto_an_item_collects_it_and_applies_the_buff()
{
    var sim = Sim(out var m);
    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);
    Assert.Empty(sim.Items);
    Assert.Equal(1, m.PermSpeedLevel); // permanent, not timed
    Assert.Empty(m.Effects);           // no StatusEffect added
}
```

Replace `LongerVision_item_raises_effective_vision_radius`:
```csharp
[Fact]
public void LongerVision_item_raises_effective_vision_radius()
{
    var sim = Sim(out _);
    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);
    Assert.Equal(6, sim.EffectiveVisionRadius(1)); // 5 base + 1 perm level * 1 bonus
}
```

Replace `A_loose_item_is_collected_on_walk_over`:
```csharp
[Fact]
public void A_loose_item_is_collected_on_walk_over()
{
    var sim = Sim(out var m);
    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Loose));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);
    Assert.Empty(sim.Items);
    Assert.Equal(1, m.PermSpeedLevel); // perm buff applied
}
```

Also update `A_dead_miner_does_not_collect_an_item_under_it` and `A_buried_item_is_not_collected_by_walking` — they check `m.Effects` is empty. Change them to also verify `m.PermSpeedLevel == 0`:
```csharp
Assert.Empty(m.Effects);
Assert.Equal(0, m.PermSpeedLevel);
```

- [ ] **Step 9: Run all tests**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass. The `StatusEffectTests` still pass because `ApplyEffect` is called directly in those tests (not via `ApplyBuff`) and the Effective* methods still read StatusEffects.

- [ ] **Step 10: Commit**

```
git add src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/PermBuffTests.cs src/Miner49er.Core.Tests/SimulationItemsTests.cs
git commit -m "$(cat <<'EOF'
feat(core): permanent buff levels for SpeedPotion/LongerVision/BiggerBlast

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Core – Invulnerability grace period

**Files:**
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/InvulnerabilityTests.cs`

**Interfaces:**
- Produces: `Miner.InvulnerableRemaining` (double, internal set); kill paths in `KillByTile`, `CollapseKill`, `MaulMiner` respect it; `AddMiner` seeds it to 3.0 seconds

- [ ] **Step 1: Add InvulnerableRemaining to Miner**

In `src/Miner49er.Core/Sim/Miner.cs`, add after `PermBlastLevel`:

```csharp
public double InvulnerableRemaining { get; internal set; }
```

- [ ] **Step 2: Write failing tests**

Create `src/Miner49er.Core.Tests/InvulnerabilityTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class InvulnerabilityTests
{
    private static (Simulation sim, Miner miner) Setup()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Pit);
        grid.Set(new GridPos(4, 2), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 2));
        return (sim, m);
    }

    [Fact]
    public void AddMiner_grants_three_seconds_of_invulnerability()
    {
        var (_, m) = Setup();
        Assert.Equal(3.0, m.InvulnerableRemaining, 3);
    }

    [Fact]
    public void Invulnerability_ticks_down_each_Tick()
    {
        var (sim, m) = Setup();
        sim.Tick(1.0);
        Assert.Equal(2.0, m.InvulnerableRemaining, 3);
    }

    [Fact]
    public void Invulnerability_does_not_go_below_zero()
    {
        var (sim, m) = Setup();
        sim.Tick(5.0);
        Assert.Equal(0.0, m.InvulnerableRemaining, 3);
    }

    [Fact]
    public void KillByTile_is_blocked_while_invulnerable()
    {
        var (sim, m) = Setup();
        // Pit is at (3,2); miner starts invulnerable
        sim.TryMove(1, Direction.East); // (2,2)
        sim.Tick(0.0);
        sim.TryMove(1, Direction.East); // (3,2) — pit
        sim.Tick(0.0);
        Assert.True(m.Alive); // invul blocks the fall
    }

    [Fact]
    public void CollapseKill_is_blocked_while_invulnerable()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Crumbling);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        // Dwell immediately past collapse threshold
        sim.Tick(sim.Config.CrackDwellSeconds + 0.1);
        Assert.True(m.Alive); // invul blocked the collapse kill
    }

    [Fact]
    public void Miner_can_die_after_invulnerability_expires()
    {
        var (sim, m) = Setup();
        sim.Tick(3.1); // expire invul
        sim.TryMove(1, Direction.East); // (2,2)
        sim.Tick(0.0);
        sim.TryMove(1, Direction.East); // (3,2) — pit
        sim.Tick(0.0);
        Assert.False(m.Alive);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests --filter "Class=InvulnerabilityTests" -v minimal
```

Expected: all 6 FAIL.

- [ ] **Step 4: Implement invulnerability in Simulation.cs**

**(a) Update AddMiner** (line ~68) to set invul on the new miner:

```csharp
public Miner AddMiner(int id, GridPos pos)
{
    var m = new Miner(id, pos) { InvulnerableRemaining = 3.0 };
    _miners[id] = m;
    return m;
}
```

**(b) Add AdvanceInvulnerability** — add this private method near the other Advance* methods:

```csharp
private void AdvanceInvulnerability(double dt)
{
    foreach (var m in _miners.Values)
        if (m.InvulnerableRemaining > 0)
            m.InvulnerableRemaining = Math.Max(0, m.InvulnerableRemaining - dt);
}
```

**(c) Call it in Tick** — in the `Tick(double dt)` method, add `AdvanceInvulnerability(dt);` after `AdvanceEffects(dt);`. (Find the Tick method; AdvanceEffects is the first call.)

**(d) Guard KillByTile** (line ~760) — add early return if invulnerable:

```csharp
private void KillByTile(Miner m)
{
    if (m.InvulnerableRemaining > 0) return;
    m.Alive = false;
    // ... rest unchanged
```

**(e) Guard CollapseKill** (line ~784) — add early return if invulnerable:

```csharp
private void CollapseKill(Miner m)
{
    if (!m.Alive || m.InvulnerableRemaining > 0) return;
    m.Alive = false;
    // ... rest unchanged
```

**(f) Guard MaulMiner** (line ~730) — add early return if invulnerable:

```csharp
private void MaulMiner(Miner m, MonsterKind kind)
{
    if (!m.Alive || m.InvulnerableRemaining > 0) return;
    // ... rest unchanged
```

- [ ] **Step 5: Run tests**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass, including InvulnerabilityTests.

- [ ] **Step 6: Commit**

```
git add src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/InvulnerabilityTests.cs
git commit -m "$(cat <<'EOF'
feat(core): invulnerability grace period on spawn (3s, blocks all kill paths)

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Core – Chest loot, BossChest, map updates

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Create: `src/Miner49er.Core/Sim/ChestLootTable.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs`
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Modify: `src/Miner49er.Core/Map/GeneratedMap.cs`
- Create: `src/Miner49er.Core.Tests/ChestLootTests.cs`
- Modify: `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`
- Modify: `src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs`

**Interfaces:**
- Produces: `ItemKind.LifePotion`, `ItemKind.BossChest`, `ItemKindExtensions.IsPlaceable()`, `ChestLootTable.Roll(Random rng)`, `SimEvent.LifeRestored`, `SimConfig.RequireChestForEscape`, `GeneratedMap.EscapeTile`, `MapConfig.ChestCount`
- BossChest placed at `(cx, cy+1)` on boss floor; escape tile at `(cx, 1)` via `GeneratedMap.EscapeTile`
- Regular floors place `ChestCount` Chests as visible toolboxes (ChestCount = 1 for floors 1-10, 2 for 11-20)

- [ ] **Step 1: Add new ItemKind values and IsPlaceable**

Replace full `src/Miner49er.Core/Map/Item.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind
{
    SpeedPotion, LongerVision, BiggerBlast,
    WaterPlank, SlowMold, Lantern,
    Chest,      // loot container: rolls ChestLootTable on pickup
    LifePotion, // from Chest loot only; fires LifeRestored event
    BossChest,  // boss floor only; opens the escape tile
}

public static class ItemKindExtensions
{
    /// <summary>Carried kinds go into the 1-slot inventory; triggered with Use verb.</summary>
    public static bool IsCarried(this ItemKind k) =>
        k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern;

    /// <summary>Placeable kinds cycle in PlaceItems (random floor placement).</summary>
    public static bool IsPlaceable(this ItemKind k) =>
        k is ItemKind.SpeedPotion or ItemKind.LongerVision or ItemKind.BiggerBlast;
}

public enum ItemPlacement
{
    Toolbox,
    Buried,
    Loose,
}

public readonly record struct Item(GridPos Pos, ItemKind Kind, ItemPlacement Placement = ItemPlacement.Toolbox);
```

- [ ] **Step 2: Create ChestLootTable**

Create `src/Miner49er.Core/Sim/ChestLootTable.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>Pure loot roll for a chest pickup. Probabilities:
/// LifePotion 40%, SpeedPotion 20%, LongerVision 20%, BiggerBlast 20%.</summary>
public static class ChestLootTable
{
    public static ItemKind Roll(Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.40) return ItemKind.LifePotion;
        if (r < 0.60) return ItemKind.SpeedPotion;
        if (r < 0.80) return ItemKind.LongerVision;
        return ItemKind.BiggerBlast;
    }
}
```

- [ ] **Step 3: Add LifeRestored event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, append before the final newline:

```csharp
public sealed record LifeRestored(int MinerId) : SimEvent;
```

- [ ] **Step 4: Add RequireChestForEscape to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, append before the closing brace:

```csharp
/// <summary>When true, escape tile does not auto-open even if there is no gold.
/// Used on the boss floor so BossChest must be grabbed to open the ladder.</summary>
public bool RequireChestForEscape { get; set; } = false;
```

- [ ] **Step 5: Update Simulation constructor + ApplyBuff + remove ChestGrabbedBy**

**(a) Constructor** — the auto-open line currently reads (line ~65):
```csharp
if (EscapeTile is not null && _goldRemaining == 0) EscapeOpen = true;
```

Change to:
```csharp
if (EscapeTile is not null && _goldRemaining == 0 && !Config.RequireChestForEscape)
    EscapeOpen = true;
```

**(b) Remove ChestGrabbedBy** — delete the property declaration near line 37:
```csharp
public int ChestGrabbedBy { get; private set; } = -1;
```

**(c) Update ApplyBuff** — add new cases for `Chest`, `BossChest`, `LifePotion` and remove the old `Chest → ChestGrabbedBy` case. The full switch should now look like:

```csharp
private void ApplyBuff(int minerId, ItemKind kind)
{
    var m = _miners[minerId];
    switch (kind)
    {
        case ItemKind.SpeedPotion:
            m.PermSpeedLevel = Math.Min(m.PermSpeedLevel + 1, Config.MaxPermSpeedLevel);
            break;
        case ItemKind.LongerVision:
            m.PermVisionLevel = Math.Min(m.PermVisionLevel + 1, Config.MaxPermVisionLevel);
            break;
        case ItemKind.BiggerBlast:
            m.PermBlastLevel = Math.Min(m.PermBlastLevel + 1, Config.MaxPermBlastLevel);
            break;
        case ItemKind.Chest:
            var loot = ChestLootTable.Roll(_rng);
            ApplyBuff(minerId, loot);   // recursive: applies the rolled kind
            break;
        case ItemKind.BossChest:
            if (!EscapeOpen && EscapeTile is not null)
            {
                EscapeOpen = true;
                _events.Add(new EscapeOpened());
            }
            break;
        case ItemKind.LifePotion:
            _events.Add(new LifeRestored(minerId));
            break;
    }
}
```

Note: `_rng` is the simulation's internal `Random` (it already exists for monster RNG — verify the field name by grepping: `grep -n "_rng\|private Random" src/Miner49er.Core/Sim/Simulation.cs`). Use whichever name is found.

- [ ] **Step 6: Update RoundResolver to remove ChestGrabbedBy**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, remove line:
```csharp
if (sim.ChestGrabbedBy >= 0) return RoundResult.Win(sim.ChestGrabbedBy);
```

The file should now read (expedition block):
```csharp
if (mode == GameMode.Expedition)
{
    if (alive.Count == 0) return RoundResult.Loss();
    if (sim.EscapeOpen && sim.EscapeTile is { } exit)
    {
        var winner = alive.FirstOrDefault(m => m.Pos == exit);
        if (winner is not null) return RoundResult.NextFloor(winner.Id);
    }
    return RoundResult.Ongoing();
}
```

- [ ] **Step 7: Add ChestCount to MapConfig and update FloorConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, add after `LanternCount`:

```csharp
public int ChestCount { get; set; } = 0;   // visible Chest toolboxes per floor
```

At the bottom of `FloorConfig`, before the `return For(...)` line, set ChestCount:

```csharp
public static MapConfig FloorConfig(int floor, int seed)
{
    int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, _ => 4 };
    bool pits    = floor >= 6;
    bool caveIns = floor >= 11;
    bool lava    = floor >= 16;
    var cfg = For(GameMode.Expedition, seed, 1, pits, caveIns, lava, mapScale);
    cfg.ChestCount = floor <= 10 ? 1 : 2;
    return cfg;
}
```

- [ ] **Step 8: Update MapGenerator — PlaceItems cycle and PlaceChests**

**(a) Change PlaceItems cycle** — find the line that builds the `kinds` array (around line 448):

Old:
```csharp
var kinds = Enum.GetValues<ItemKind>().Where(k => !k.IsCarried()).ToArray();
```

New:
```csharp
var kinds = new[] { ItemKind.SpeedPotion, ItemKind.LongerVision, ItemKind.BiggerBlast };
```

**(b) Add PlaceChests helper** — add this private static method near PlaceCarriedItems:

```csharp
private static List<Item> PlaceChests(TileGrid g, Random rng, int count,
    HashSet<GridPos> region, List<GridPos> spawns, IEnumerable<Item> existing)
{
    var taken = new HashSet<GridPos>(existing.Select(it => it.Pos));
    var spawnSet = new HashSet<GridPos>(spawns);
    var cands = g.Positions()
        .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor
                    && !spawnSet.Contains(p) && !taken.Contains(p))
        .ToList();
    Shuffle(cands, rng);
    var result = new List<Item>();
    for (int i = 0; i < count && i < cands.Count; i++)
        result.Add(new Item(cands[i], ItemKind.Chest, ItemPlacement.Toolbox));
    return result;
}
```

**(c) Call PlaceChests in Generate** — in `Generate()`, after the `items.AddRange(PlaceCarriedItems(...))` line, add:

```csharp
items.AddRange(PlaceChests(grid, rng, config.ChestCount, region, spawns, items));
```

**(d) Update return value** — in `Generate()`, update the return to include `EscapeTile`:

```csharp
return new GeneratedMap
{
    Grid = grid, Spawns = spawns, Center = center, Items = items, Decoys = decoys,
    EscapeTile = spawns.Count > 0 ? spawns[0] : null,
};
```

**(e) Update GenerateBossFloor** — change `ItemKind.Chest` to `ItemKind.BossChest` in the items list (line ~563):

```csharp
var items = new List<Item>
{
    new Item(chestPos, ItemKind.BossChest, ItemPlacement.Toolbox),
};
```

And update the return to include `EscapeTile`:

```csharp
return new GeneratedMap
{
    Grid   = grid,
    Spawns = new List<GridPos> { spawn },
    Center = center,
    Items  = items,
    Decoys = new List<GridPos>(),
    EscapeTile = new GridPos(cx, 1),   // top of north corridor
};
```

- [ ] **Step 9: Add EscapeTile to GeneratedMap**

Replace `src/Miner49er.Core/Map/GeneratedMap.cs` entirely:

```csharp
namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
    public required IReadOnlyList<GridPos> Decoys { get; init; }
    public GridPos? EscapeTile { get; init; }
}
```

- [ ] **Step 10: Write tests**

Create `src/Miner49er.Core.Tests/ChestLootTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class ChestLootTests
{
    // ---- ChestLootTable ----

    [Fact]
    public void Roll_returns_only_valid_loot_kinds()
    {
        var rng = new Random(42);
        var valid = new[] { ItemKind.LifePotion, ItemKind.SpeedPotion, ItemKind.LongerVision, ItemKind.BiggerBlast };
        for (int i = 0; i < 200; i++)
            Assert.Contains(ChestLootTable.Roll(rng), valid);
    }

    [Fact]
    public void Roll_produces_LifePotion_roughly_40_percent()
    {
        var rng = new Random(0);
        int lifePotions = Enumerable.Range(0, 1000).Count(_ => ChestLootTable.Roll(rng) == ItemKind.LifePotion);
        Assert.InRange(lifePotions, 300, 500); // ~40%, allow variance
    }

    // ---- Chest pickup ----

    [Fact]
    public void Chest_pickup_applies_a_rolled_buff_not_wins_the_run()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig { Seed = 1 });
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.Chest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        // Item is consumed
        Assert.Empty(sim.Items);
        // No ChestGrabbedBy: the round is not over
        var result = RoundResolver.Resolve(sim, GameMode.Expedition);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void LifePotion_from_chest_fires_LifeRestored_event()
    {
        // Force the RNG seed so ChestLootTable.Roll returns LifePotion.
        // Roll returns LifePotion when rng.NextDouble() < 0.40.
        // Seed 0 first double: find a seed where first roll < 0.40.
        // Use Simulation's internal seed to control chest loot roll.
        // Simplest: call ApplyBuff indirectly via a LifePotion item directly.
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LifePotion, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Contains(sim.DrainEvents(), e => e is LifeRestored lr && lr.MinerId == 1);
    }

    // ---- BossChest ----

    [Fact]
    public void BossChest_opens_escape_when_grabbed()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var escape = new GridPos(0, 2);
        var sim = new Simulation(grid, new SimConfig { RequireChestForEscape = true },
                                 escapeTile: escape);
        Assert.False(sim.EscapeOpen); // not auto-opened
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BossChest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.True(sim.EscapeOpen);
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }

    [Fact]
    public void RequireChestForEscape_prevents_auto_open_on_zero_gold_map()
    {
        var grid = new TileGrid(5, 5, TileType.Floor); // no gold
        var sim = new Simulation(grid, new SimConfig { RequireChestForEscape = true },
                                 escapeTile: new GridPos(0, 0));
        Assert.False(sim.EscapeOpen);
    }

    [Fact]
    public void Normal_zero_gold_map_still_auto_opens_escape_without_flag()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(),
                                 escapeTile: new GridPos(0, 0));
        Assert.True(sim.EscapeOpen); // default behavior unchanged
    }

    // ---- MapGenerator ----

    [Fact]
    public void FloorConfig_sets_ChestCount_1_for_early_floors()
    {
        var cfg = MapConfig.FloorConfig(3, 1);
        Assert.Equal(1, cfg.ChestCount);
    }

    [Fact]
    public void FloorConfig_sets_ChestCount_2_for_late_floors()
    {
        var cfg = MapConfig.FloorConfig(15, 1);
        Assert.Equal(2, cfg.ChestCount);
    }

    [Fact]
    public void Generate_places_ChestCount_Chest_items_as_toolboxes()
    {
        var cfg = MapConfig.FloorConfig(5, 42);
        var map = MapGenerator.Generate(cfg);
        var chests = map.Items.Where(i => i.Kind == ItemKind.Chest && i.Placement == ItemPlacement.Toolbox).ToList();
        Assert.Equal(cfg.ChestCount, chests.Count);
    }

    [Fact]
    public void BossFloor_has_BossChest_not_regular_Chest()
    {
        var map = MapGenerator.GenerateBossFloor(1);
        Assert.Contains(map.Items, i => i.Kind == ItemKind.BossChest);
        Assert.DoesNotContain(map.Items, i => i.Kind == ItemKind.Chest);
    }

    [Fact]
    public void BossFloor_EscapeTile_is_at_top_of_north_corridor()
    {
        var map = MapGenerator.GenerateBossFloor(1);
        Assert.NotNull(map.EscapeTile);
        // cx = 40/2 = 20, escape at (20, 1)
        Assert.Equal(new GridPos(20, 1), map.EscapeTile!.Value);
    }

    [Fact]
    public void GeneratedMap_EscapeTile_equals_Spawns0_for_normal_maps()
    {
        var cfg = MapConfig.FloorConfig(1, 7);
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(map.Spawns[0], map.EscapeTile);
    }
}
```

- [ ] **Step 11: Update RoundResolverExpeditionTests.cs**

Remove the now-obsolete `Chest_grabbed_wins_the_dungeon` test and replace with a test for BossChest:

```csharp
[Fact]
public void Chest_pickup_does_not_win_the_dungeon()
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    var sim  = new Simulation(grid, new SimConfig(), escapeTile: null);
    sim.AddItem(new Item(new GridPos(1, 1), ItemKind.Chest, ItemPlacement.Toolbox));
    sim.AddMiner(1, new GridPos(1, 1));
    sim.Tick(0.1);
    var result = RoundResolver.Resolve(sim, GameMode.Expedition);
    Assert.False(result.IsOver);
}
```

- [ ] **Step 12: Update MapGeneratorBossFloorTests.cs**

Find `Chest_is_one_south_of_center` and change it to check `ItemKind.BossChest`:

```csharp
[Fact]
public void BossChest_is_one_south_of_center()
{
    var map    = Make();
    var center = map.Center;
    var chest  = map.Items.FirstOrDefault(i => i.Kind == ItemKind.BossChest);
    Assert.Equal(new GridPos(center.X, center.Y + 1), chest.Pos);
}
```

Also add:
```csharp
[Fact]
public void EscapeTile_is_at_top_of_north_corridor()
{
    var map = Make();
    Assert.NotNull(map.EscapeTile);
    Assert.Equal(new GridPos(20, 1), map.EscapeTile!.Value);
}
```

- [ ] **Step 13: Run all tests**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass.

- [ ] **Step 14: Commit**

```
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Sim/ChestLootTable.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core.Tests/ChestLootTests.cs src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs src/Miner49er.Core.Tests/MapGeneratorBossFloorTests.cs
git commit -m "$(cat <<'EOF'
feat(core): chest loot table, BossChest, LifePotion, floor chests, escape tile on GeneratedMap

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Snapshot plumbing

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

**Interfaces:**
- Produces: `MinerSnapshot.InvulRemaining` (float, default 0f); `WorldSnapshot.Lives` (int, default 3); `SnapshotFactory.Capture(sim, tick, lives)`
- Binary layout change: after `EscapeOpen` bool write `Lives` (int), then octopus flag; after `Cause` byte write `InvulRemaining` (float)

- [ ] **Step 1: Update MinerSnapshot and WorldSnapshot**

Replace the relevant lines in `src/Miner49er.Core/Net/Snapshots.cs`:

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held,
    DeathCause Cause = DeathCause.None, float InvulRemaining = 0f);
```

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false,
    OctopusSnapshot? Octopus = null, int Lives = 3);
```

- [ ] **Step 2: Update SnapshotFactory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, change the method signature and miner capture:

```csharp
public static WorldSnapshot Capture(Simulation sim, int tick, int lives = 3)
{
    var miners = sim.Miners
        .Select(m => new MinerSnapshot(
            m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
            m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
            sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
            m.Held is { } h ? (int)h : -1, m.DeathCause, (float)m.InvulnerableRemaining))
        .ToList();
    // ... charges/items/molds/monsters unchanged ...
    return new WorldSnapshot(tick, miners, charges, items, molds, monsters,
        (float)sim.SecondsRemaining, sim.EscapeOpen, octopus, lives);
}
```

- [ ] **Step 3: Update SnapshotCodec**

**(a) Write side** — in `Write`, after `w.Write(snap.EscapeOpen);` add:

```csharp
w.Write(snap.Lives);
```

Also, in the miner write loop, after `w.Write((byte)m.Cause);` add:

```csharp
w.Write(m.InvulRemaining);
```

**(b) Read side** — in `Read`, after `bool escapeOpen = r.ReadBoolean();` add:

```csharp
int lives = r.ReadInt32();
```

Also, in the miner read loop, after the `(DeathCause)r.ReadByte()` argument, add `r.ReadSingle()` to reconstruct with InvulRemaining:

Old miner read:
```csharp
miners.Add(new MinerSnapshot(
    r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
    r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
    r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte()));
```

New:
```csharp
miners.Add(new MinerSnapshot(
    r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
    r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
    r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte(), r.ReadSingle()));
```

Update the final `new WorldSnapshot(...)` return to include `lives`:

```csharp
return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
    monsters, secondsRemaining, escapeOpen, octopus, lives), changes);
```

- [ ] **Step 4: Add assertions to SnapshotCodecTests.cs**

In `Round_trips_all_fields`, the first miner is constructed without explicit `InvulRemaining` (defaults 0f) and `Lives` defaults to 3 in the `WorldSnapshot`. Add assertions at the end of the test:

```csharp
Assert.Equal(0f, back.Snapshot.Miners[0].InvulRemaining, 3);
Assert.Equal(3, back.Snapshot.Lives); // default
```

Add a new dedicated test:

```csharp
[Fact]
public void Round_trips_invul_remaining_and_lives()
{
    var miners = new System.Collections.Generic.List<MinerSnapshot>
    {
        new(1, 0, 0, 0, true, 0, 0, 0.0, 0.12, 5, -1, DeathCause.None, 1.5f),
        new(2, 1, 1, 0, true, 0, 0, 0.0, 0.12, 5, -1, DeathCause.None, 0f),
    };
    var update = new TickUpdate(
        new WorldSnapshot(1, miners,
            new System.Collections.Generic.List<ChargeSnapshot>(),
            new System.Collections.Generic.List<ItemSnapshot>(),
            new System.Collections.Generic.List<MoldSnapshot>(),
            new System.Collections.Generic.List<MonsterSnapshot>(),
            Lives: 2),
        new System.Collections.Generic.List<TileChange>());

    var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

    Assert.Equal(1.5f, back.Snapshot.Miners[0].InvulRemaining, 3);
    Assert.Equal(0f,   back.Snapshot.Miners[1].InvulRemaining, 3);
    Assert.Equal(2,    back.Snapshot.Lives);
}
```

- [ ] **Step 5: Run all tests**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "$(cat <<'EOF'
feat(net): snapshot carries InvulRemaining per miner and Lives per world

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Network / MatchHost wiring

**Files:**
- Modify: `game/net/MatchHost.cs`
- Modify: `game/net/MatchClient.cs`
- Modify: `game/Main.cs`

**Interfaces:**
- `MatchClient.Lives` (int, public, set from snapshot)
- `MatchHost` manages `_livesRemaining` (3 solo, 1 multi), `_cumulativeGold`, `_permLevels` dict; calls `ScoreStore.Submit` on match end
- `GeneratedMap.EscapeTile` replaces `Spawns[0]` in all escape tile usage

- [ ] **Step 1: Add Lives property to MatchClient**

In `game/net/MatchClient.cs`, add after `public OctopusSnapshot? Octopus`:

```csharp
public int Lives { get; private set; } = 3;
```

In `ApplyUpdate`, after `Octopus = update.Snapshot.Octopus;`, add:

```csharp
Lives = update.Snapshot.Lives;
```

Also update `ResetFloor` to use `EscapeTile` from `GeneratedMap`:

Replace the two branches that compute `EscapeTile`:
```csharp
// Old (remove this):
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
```

```csharp
// New:
newMap = (floor == 21)
    ? MapGenerator.GenerateBossFloor(floorSeed)
    : MapGenerator.Generate(MapConfig.FloorConfig(floor, floorSeed));
EscapeTile = newMap.EscapeTile;
```

- [ ] **Step 2: Update Main._Ready to use EscapeTile**

In `game/Main.cs`, update two places that reference `map.Spawns[0]` / `hostMap.Spawns[0]` for escape:

Old (line ~40):
```csharp
GridPos? clientEscape = nm.MatchMode == GameMode.Expedition && map.Spawns.Count > 0
    ? map.Spawns[0] : null;
```
New:
```csharp
GridPos? clientEscape = nm.MatchMode == GameMode.Expedition ? map.EscapeTile : null;
```

Old (line ~57):
```csharp
GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.Spawns[0] : null;
```
New:
```csharp
GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.EscapeTile : null;
```

- [ ] **Step 3: Rewrite MatchHost with lives, perm levels, cumulative gold, boss win, score**

Replace `game/net/MatchHost.cs` entirely:

```csharp
using Godot;
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Host-only authoritative simulation driver. Steps a fixed 30 Hz tick,
/// applies queued inputs, and broadcasts a TickUpdate each step via NetworkManager.
/// Manages lives, permanent buff levels, and cumulative gold across floors.</summary>
public partial class MatchHost : Node
{
	public const double TickSeconds = 1.0 / 30.0;

	private Simulation _sim = null!;
	private readonly Dictionary<long, int> _peerToMiner = new();
	private readonly Dictionary<int, int> _pendingDir = new();
	private readonly HashSet<int> _pendingMine  = new();
	private readonly HashSet<int> _pendingPlant = new();
	private readonly HashSet<int> _pendingUse   = new();

	private int _tick;
	private double _accum;
	private bool _running;

	private int _livesRemaining;
	private int _livesMax;
	private int _cumulativeGold;
	private readonly Dictionary<int, (int Speed, int Vision, int Blast)> _permLevels = new();

	public void Begin(Simulation sim, Dictionary<long, int> peerToMiner)
	{
		_sim = sim;
		foreach (var (peer, miner) in peerToMiner)
		{
			_peerToMiner[peer] = miner;
			_pendingDir[miner] = -1;
		}
		var nm = NetworkManager.Instance;
		_livesMax       = (nm.MatchMode == GameMode.Expedition && nm.MatchPlayerCount == 1) ? 3 : 1;
		_livesRemaining = _livesMax;
		_running = true;
	}

	public void SetDir(long peerId, int dir)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _pendingDir[minerId] = dir;
	}

	public void SetAction(long peerId, bool mine, bool plant, bool use)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine)  _pendingMine.Add(minerId);
		if (plant) _pendingPlant.Add(minerId);
		if (use)   _pendingUse.Add(minerId);
	}

	public void EliminatePeer(long peerId)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId)) _sim.KillMiner(minerId);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_running) return;
		_accum += delta;
		while (_accum >= TickSeconds) { _accum -= TickSeconds; StepOnce(); }
	}

	private void StepOnce()
	{
		foreach (var (minerId, dir) in _pendingDir)
		{
			if (dir < 0) continue;
			_sim.TryMove(minerId, (Direction)dir);
		}

		foreach (var minerId in _pendingMine)  _sim.TryStartMining(minerId);
		_pendingMine.Clear();
		foreach (var minerId in _pendingPlant) _sim.TryStartPlanting(minerId);
		_pendingPlant.Clear();
		foreach (var minerId in _pendingUse)   _sim.TryUseItem(minerId);
		_pendingUse.Clear();

		_sim.Tick(TickSeconds);
		_tick++;

		var changes = new List<TileChange>();
		foreach (var e in _sim.DrainEvents())
		{
			switch (e)
			{
				case RockMined rm:
					changes.Add(new TileChange(rm.Pos.X, rm.Pos.Y, false, TileType.Floor));
					break;
				case Explosion ex:
					foreach (var d in ex.DestroyedRock)
						changes.Add(new TileChange(d.X, d.Y, true, TileType.Floor));
					break;
				case TileFlooded tf:
					changes.Add(new TileChange(tf.Pos.X, tf.Pos.Y, false, tf.Type));
					break;
				case PlankPlaced pp:
					changes.Add(new TileChange(pp.Pos.X, pp.Pos.Y, false, TileType.Plank));
					break;
				case CrackWeakened cw:
					changes.Add(new TileChange(cw.Pos.X, cw.Pos.Y, false, TileType.Crumbling));
					break;
				case CrackCollapsed cc:
					changes.Add(new TileChange(cc.Pos.X, cc.Pos.Y, false, TileType.Pit));
					break;
				case LavaSpread ls:
					changes.Add(new TileChange(ls.Pos.X, ls.Pos.Y, false, TileType.Lava));
					break;
				case LavaQuenched lq:
					changes.Add(new TileChange(lq.Pos.X, lq.Pos.Y, false, TileType.Cracked));
					break;
				case LifeRestored lr:
					_ = lr;   // consume; handled by incrementing lives
					_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax);
					break;
			}
		}

		var update = new TickUpdate(SnapshotFactory.Capture(_sim, _tick, _livesRemaining), changes);
		NetworkManager.Instance.BroadcastTick(SnapshotCodec.Write(update));

		var nm     = NetworkManager.Instance;
		var result = RoundResolver.Resolve(_sim, nm.MatchMode);

		if (result.FloorCleared)
		{
			foreach (var m in _sim.Miners) _cumulativeGold += m.GoldCollected;
			SavePermLevels();
			AdvanceFloor(result.WinnerId);
			return;
		}

		if (result.IsOver)
		{
			bool expeditionLoss = nm.MatchMode == GameMode.Expedition && result.WinnerId == -1;
			if (expeditionLoss)
			{
				_livesRemaining--;
				if (_livesRemaining > 0)
				{
					int soloMiner = _peerToMiner.Values.First();
					AdvanceFloor(soloMiner, sameFloor: true);
					return;
				}
			}
			_running = false;
			if (nm.MatchMode == GameMode.Expedition)
			{
				int score = 100 * nm.MatchFloor + _cumulativeGold;
				string name = nm.Players.TryGetValue(nm.LocalId, out var info) ? info.Name : "Player";
				ScoreStore.Submit(name, score, nm.MatchFloor);
			}
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == result.WinnerId).Key;
			nm.BroadcastResult(result.WinnerId == -1 ? -1 : winnerPeer);
		}
	}

	private void SavePermLevels()
	{
		foreach (var m in _sim.Miners)
			_permLevels[m.Id] = (m.PermSpeedLevel, m.PermVisionLevel, m.PermBlastLevel);
	}

	private void AdvanceFloor(int minerId, bool sameFloor = false)
	{
		var nm = NetworkManager.Instance;
		int newFloor  = sameFloor ? nm.MatchFloor : nm.MatchFloor + 1;
		int floorSeed = nm.MatchSeed + newFloor * 1000;

		if (newFloor > 21)
		{
			// Dungeon cleared — boss floor exit reached.
			int score = 100 * nm.MatchFloor + _cumulativeGold;
			string name = nm.Players.TryGetValue(nm.LocalId, out var info) ? info.Name : "Player";
			ScoreStore.Submit(name, score, nm.MatchFloor);
			_running = false;
			long winnerPeer = _peerToMiner.FirstOrDefault(kv => kv.Value == minerId).Key;
			nm.BroadcastResult(winnerPeer);
			return;
		}

		GeneratedMap newMap;
		GridPos? escapeTile;
		if (newFloor == 21)
		{
			newMap     = MapGenerator.GenerateBossFloor(floorSeed);
			escapeTile = newMap.EscapeTile;
		}
		else
		{
			var cfg    = MapConfig.FloorConfig(newFloor, floorSeed);
			newMap     = MapGenerator.Generate(cfg);
			escapeTile = newMap.EscapeTile;
		}

		var newSim = new Simulation(
			newMap.Grid,
			new SimConfig
			{
				BaseMoveSeconds      = nm.MatchBaseMoveSeconds,
				Seed                 = floorSeed,
				RequireChestForEscape = newFloor == 21,
			},
			newMap.Center,
			timeLimitSeconds: null,
			flooding: false,
			escapeTile);

		foreach (var item in newMap.Items)
			newSim.AddItem(item);

		GridPos spawn = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
		newSim.AddMiner(minerId, spawn);

		if (_permLevels.TryGetValue(minerId, out var levels))
			newSim.SetPermLevels(minerId, levels.Speed, levels.Vision, levels.Blast);

		if (newFloor == 21)
		{
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

		foreach (var key in _pendingDir.Keys.ToList()) _pendingDir[key] = -1;
		_pendingMine.Clear();
		_pendingPlant.Clear();
		_pendingUse.Clear();

		nm.BroadcastNewFloor(newFloor);
	}
}
```

- [ ] **Step 4: Verify build**

```
dotnet build Miner49er.csproj
```

Expected: 0 errors. (ScoreStore doesn't exist yet — add a temporary stub if needed, or check that Task 6 can be done immediately after.)

If `ScoreStore` reference causes error, create a minimal stub first:

```csharp
// game/ScoreStore.cs temporary stub
using System.Collections.Generic;
namespace Miner49er;
public static class ScoreStore
{
    public static void Submit(string name, int score, int floor) { }
    public static List<ScoreEntry> Load() => new();
}
public sealed record ScoreEntry(string Name, int Score, int Floor, string Date);
```

- [ ] **Step 5: Run all core tests**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass (no regressions from Godot layer changes).

- [ ] **Step 6: Commit**

```
git add game/net/MatchHost.cs game/net/MatchClient.cs game/Main.cs game/ScoreStore.cs
git commit -m "$(cat <<'EOF'
feat(game): lives system, perm level restoration, boss win, score submission in MatchHost

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: ScoreStore + High Score UI

**Files:**
- Modify: `game/ScoreStore.cs` (flesh out the stub from Task 5)
- Create: `game/ui/HighScorePanel.cs`
- Modify: `game/ui/ResultsOverlay.cs`
- Modify: `game/ui/MainMenu.cs`

**Interfaces:**
- `ScoreStore.Submit(string name, int score, int floor)` — saves to top-10 list in `user://scores.cfg`
- `ScoreStore.Load() → List<ScoreEntry>` — returns sorted descending by score
- `HighScorePanel` — CanvasLayer overlay that renders top 10; opened by MainMenu button
- `ResultsOverlay.Show(string text, bool hostControls, string buttonText, string scoreText)` — scoreText shown in a second label

- [ ] **Step 1: Write ScoreStore**

Replace `game/ScoreStore.cs` with the full implementation:

```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace Miner49er;

public sealed record ScoreEntry(string Name, int Score, int Floor, string Date);

/// <summary>Persists a top-10 high score list to user://scores.cfg using Godot ConfigFile.</summary>
public static class ScoreStore
{
	private const string Path     = "user://scores.cfg";
	private const int    MaxCount = 10;

	public static List<ScoreEntry> Load()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok) return new List<ScoreEntry>();

		var entries = new List<ScoreEntry>();
		foreach (var section in cfg.GetSections())
		{
			string name  = (string)cfg.GetValue(section, "name",  "");
			int    score = (int)   cfg.GetValue(section, "score", 0);
			int    floor = (int)   cfg.GetValue(section, "floor", 0);
			string date  = (string)cfg.GetValue(section, "date",  "");
			entries.Add(new ScoreEntry(name, score, floor, date));
		}

		entries.Sort((a, b) => b.Score.CompareTo(a.Score));
		return entries;
	}

	public static void Submit(string name, int score, int floor)
	{
		var entries = Load();
		entries.Add(new ScoreEntry(name, score, floor, DateTime.Now.ToString("yyyy-MM-dd")));
		entries.Sort((a, b) => b.Score.CompareTo(a.Score));
		if (entries.Count > MaxCount) entries.RemoveRange(MaxCount, entries.Count - MaxCount);

		var cfg = new ConfigFile();
		for (int i = 0; i < entries.Count; i++)
		{
			string section = $"score_{i}";
			cfg.SetValue(section, "name",  entries[i].Name);
			cfg.SetValue(section, "score", entries[i].Score);
			cfg.SetValue(section, "floor", entries[i].Floor);
			cfg.SetValue(section, "date",  entries[i].Date);
		}
		cfg.Save(Path);
	}
}
```

- [ ] **Step 2: Create HighScorePanel**

Create `game/ui/HighScorePanel.cs`:

```csharp
using Godot;

namespace Miner49er;

/// <summary>Overlay showing the top-10 high scores. Toggle via Open/Close.</summary>
public partial class HighScorePanel : CanvasLayer
{
	private VBoxContainer _rows = null!;
	private bool _built;

	public bool IsOpen { get; private set; }

	public override void _Ready()
	{
		Layer = 30;
		Visible = false;

		var bg = new ColorRect
		{
			Color = new Color(0, 0, 0, 0.85f),
			AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
		};
		AddChild(bg);

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var box = new VBoxContainer { CustomMinimumSize = new Vector2(480, 0) };
		center.AddChild(box);

		var title = new Label { Text = "HIGH SCORES", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 36);
		box.AddChild(title);

		box.AddChild(new HSeparator());

		_rows = new VBoxContainer();
		box.AddChild(_rows);

		box.AddChild(new HSeparator());

		var closeBtn = new Button { Text = "Close" };
		closeBtn.Pressed += Close;
		box.AddChild(closeBtn);
	}

	public void Open()
	{
		Rebuild();
		Visible  = true;
		IsOpen   = true;
	}

	public void Close()
	{
		Visible = false;
		IsOpen  = false;
	}

	private void Rebuild()
	{
		foreach (Node child in _rows.GetChildren()) child.QueueFree();

		var entries = ScoreStore.Load();
		if (entries.Count == 0)
		{
			var none = new Label { Text = "(no scores yet)", HorizontalAlignment = HorizontalAlignment.Center };
			_rows.AddChild(none);
			return;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			var e = entries[i];
			var row = new Label
			{
				Text = $"{i + 1,2}. {e.Name,-14} {e.Score,8}   Floor {e.Floor,-3}  {e.Date}",
				HorizontalAlignment = HorizontalAlignment.Left,
			};
			row.AddThemeFontSizeOverride("font_size", 18);
			_rows.AddChild(row);
		}
	}
}
```

- [ ] **Step 3: Update ResultsOverlay with score label**

Replace `game/ui/ResultsOverlay.cs`:

```csharp
using Godot;

namespace Miner49er;

public partial class ResultsOverlay : CanvasLayer
{
	private Label _label      = null!;
	private Label _scoreLabel = null!;
	private Button _return    = null!;

	public override void _Ready()
	{
		Layer = 50;
		var center = new CenterContainer();
		center.AnchorLeft = 0f; center.AnchorRight = 1f;
		center.AnchorTop = 0.05f; center.AnchorBottom = 0.40f;
		AddChild(center);

		var box = new VBoxContainer();
		center.AddChild(box);

		_label = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_label.AddThemeFontSizeOverride("font_size", 40);
		box.AddChild(_label);

		_scoreLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_scoreLabel.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(_scoreLabel);

		_return = new Button { Text = "Return to Lobby" };
		_return.Pressed += () => NetworkManager.Instance.ReturnToLobby();
		box.AddChild(_return);
	}

	public void Show(string text, bool hostControls, string buttonText = "Return to Lobby",
	                 string scoreText = "")
	{
		_label.Text      = text;
		_scoreLabel.Text = scoreText;
		_scoreLabel.Visible = scoreText.Length > 0;
		_return.Text    = buttonText;
		_return.Visible = hostControls;
	}
}
```

- [ ] **Step 4: Update Main.OnMatchEnded to pass score text**

In `game/Main.cs`, update `OnMatchEnded` to pass a score string for Expedition. Find the `_results.Show(label, ...)` call and update:

```csharp
private void OnMatchEnded(long winnerPeerId)
{
    if (_results != null) return;
    _results = new ResultsOverlay { Name = "ResultsOverlay" };
    AddChild(_results);
    bool expedition = NetworkManager.Instance.MatchMode == GameMode.Expedition;
    bool won = winnerPeerId == NetworkManager.Instance.LocalId;
    string label;
    string scoreText = "";
    if (expedition)
    {
        label = won
            ? (NetworkManager.Instance.MatchFloor == 21
                ? "You conquered the dungeon!"
                : "You escaped with the gold!")
            : "You died in the mine.";
        // Build score text from client's cumulative state (floor + gold proxy)
        int floor = NetworkManager.Instance.MatchFloor;
        scoreText = $"Floor {floor}  (score submitted)";
    }
    else
    {
        label = winnerPeerId == -1
            ? "Draw — no survivors"
            : $"Winner: {NameOf(winnerPeerId)}";
    }
    _results.Show(label, NetworkManager.Instance.IsHost,
        expedition ? "Return to Menu" : "Return to Lobby", scoreText);
}
```

- [ ] **Step 5: Add High Scores button to MainMenu**

In `game/ui/MainMenu.cs`, add the panel field after `_audioPanel`:

```csharp
private HighScorePanel _highScorePanel = null!;
```

In `_Ready`, after the `settingsBtn` lines and before `_status = new Label...`, insert:

```csharp
var scoresBtn = new Button { Text = "High Scores" };
scoresBtn.Pressed += () => _highScorePanel.Open();
box.AddChild(scoresBtn);
```

At the end of `_Ready`, after `_audioPanel = new SettingsPanel ...`:

```csharp
_highScorePanel = new HighScorePanel { Name = "HighScorePanel" };
AddChild(_highScorePanel);
```

Also update `_UnhandledInput` so ESC closes the high score panel too:

```csharp
public override void _UnhandledInput(InputEvent @event)
{
    if (@event.IsActionPressed(InputBindings.Exit))
    {
        GetViewport().SetInputAsHandled();
        if (_audioPanel.IsOpen)      { _audioPanel.Close();      return; }
        if (_highScorePanel.IsOpen)  { _highScorePanel.Close();  return; }
        GetTree().Quit();
    }
}
```

- [ ] **Step 6: Build**

```
dotnet build Miner49er.csproj
```

Expected: 0 errors.

- [ ] **Step 7: Commit**

```
git add game/ScoreStore.cs game/ui/HighScorePanel.cs game/ui/ResultsOverlay.cs game/ui/MainMenu.cs game/Main.cs
git commit -m "$(cat <<'EOF'
feat(game): ScoreStore top-10, HighScorePanel, score on ResultsOverlay, HiScores menu button

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

### Task 7: WorldRenderer + HUD visual polish

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/net/MatchClient.cs` (invulnerability flash in `_Draw`)
- Modify: `game/Main.cs` (HUD hearts)

**Interfaces:**
- `LifePotion` item renders as red ♥ glyph
- `BossChest` renders as gold ★ glyph (distinct from regular ♦ Chest)
- Invulnerable miners flash: alpha alternates 1.0/0.2 at 4 Hz, becoming mostly-solid as 3s elapses
- HUD prepends `♥ × _client.Lives` hearts in Expedition mode

- [ ] **Step 1: Add LifePotion and BossChest glyphs to WorldRenderer**

In `game/WorldRenderer.cs`, find the items rendering loop. The `Chest` item already draws a gold `♦`. Add cases for the new kinds.

Find the block that handles `ItemKind.Chest` (around the items loop early-exit). Add `ItemKind.LifePotion` and `ItemKind.BossChest` cases directly before or after the `Chest` case:

```csharp
// LifePotion — red heart
if (it.Kind == ItemKind.LifePotion && it.Placement != ItemPlacement.Buried)
{
    DrawString(font, pos + new Vector2(-8, 8), "♥",
               modulate: new Color(1f, 0.15f, 0.15f, 0.95f));
    continue;
}
// BossChest — gold star (boss floor only)
if (it.Kind == ItemKind.BossChest && it.Placement != ItemPlacement.Buried)
{
    DrawRect(new Rect2(pos.X - 12, pos.Y - 12, 24, 24),
             new Color(0.9f, 0.75f, 0.1f, 0.9f));
    DrawString(font, pos + new Vector2(-8, 8), "★",
               modulate: new Color(0, 0, 0, 1f));
    continue;
}
```

(These go inside the items loop, before the `continue` / general item rendering path. Match the style of the existing Chest case already in this file.)

- [ ] **Step 2: Add invulnerability flash in MatchClient._Draw**

In `game/net/MatchClient.cs`, update the `_Draw` method's miner-drawing loop:

```csharp
public override void _Draw()
{
    foreach (var m in _miners)
    {
        if (!m.Alive) continue;
        var p = _visualPos.TryGetValue(m.Id, out var v) ? v : Vector2.Zero;

        float alpha = 1f;
        if (m.InvulRemaining > 0f)
        {
            float fraction = 1f - (m.InvulRemaining / 3f);   // 0→1 as invul expires
            float phase    = (float)(Time.GetTicksMsec() * 0.001 * 4.0) % 1f;
            alpha = phase < fraction ? 1f : 0.2f;
        }

        int colorIdx = (m.Id - 1) % PlayerColors.Palette.Length;
        int facing   = m.Facing;
        var tex      = _minerTex?[colorIdx, facing];
        if (tex != null)
        {
            var modulate = new Color(1, 1, 1, alpha);
            DrawTextureRect(tex, new Rect2(p.X - 16, p.Y - 16, 32, 32), false, modulate);
        }
        else
        {
            var col = PlayerColors.At(m.Id - 1);
            col.A = alpha;
            DrawRect(new Rect2(p.X - 10, p.Y - 10, 20, 20), col);
        }
    }
}
```

- [ ] **Step 3: Add HUD hearts in Main._PhysicsProcess**

In `game/Main.cs`, inside the `_PhysicsProcess` loop where `objective` is built for Expedition mode, prepend the hearts string. Find the HUD text assembly (around line 166) and update:

```csharp
string objective;
if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
{
    var nm2 = NetworkManager.Instance;
    string hearts = new string('♥', Math.Max(0, _client.Lives));
    if (nm2.MatchFloor == 21)
    {
        objective = $"{hearts}  BOSS FLOOR  Reach the chest!";
    }
    else if (_client.EscapeOpen)
    {
        objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold ✓ — ESCAPE!";
    }
    else
    {
        int pct = _client.StartingGoldCount > 0
            ? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
            : 0;
        objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold: {pct}% (need 50%)";
    }
}
else
{
    objective = $"Gold: {m.Gold}";
}
```

- [ ] **Step 4: Build**

```
dotnet build Miner49er.csproj
```

Expected: 0 errors.

- [ ] **Step 5: Run all core tests one final time**

```
dotnet test src/Miner49er.Core.Tests -v minimal
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add game/WorldRenderer.cs game/net/MatchClient.cs game/Main.cs
git commit -m "$(cat <<'EOF'
feat(game): invul flash, LifePotion and BossChest glyphs, HUD hearts display

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review

**Spec coverage check:**

| Requirement | Task |
|---|---|
| 3 lives solo, 1 multi | Task 5 (MatchHost._livesMax) |
| Retry same floor on death | Task 5 (AdvanceFloor sameFloor=true) |
| LifePotion restores heart (from chest) | Tasks 3 + 5 (LifeRestored event → _livesRemaining++) |
| SpeedPotion/LongerVision/BiggerBlast permanent | Task 1 |
| Perm levels preserved across floors | Task 5 (_permLevels dict + SetPermLevels) |
| Chest = loot container (LifePotion 40%, buffs 60%) | Task 3 (ChestLootTable) |
| BossChest opens exit | Task 3 (ApplyBuff BossChest) |
| Floor 21 exit → dungeon win | Task 5 (AdvanceFloor newFloor > 21) |
| RequireChestForEscape prevents auto-open | Task 3 (SimConfig flag + constructor) |
| 3s invulnerability on spawn | Task 2 (AddMiner, AdvanceInvulnerability) |
| Invul blocks all kill paths | Task 2 (KillByTile, CollapseKill, MaulMiner guards) |
| Invul flash (mostly-off → mostly-on) | Task 7 |
| Score = 100×floor + gold | Task 5 (submitted on win and loss) |
| Top-10 local high scores | Task 6 (ScoreStore + HighScorePanel) |
| High Scores on main menu | Task 6 |
| Score on results screen | Task 6 |
| HUD hearts | Task 7 |
| LifePotion ♥ glyph | Task 7 |
| BossChest ★ glyph | Task 7 |
| Chest count scales with floor band | Task 3 (FloorConfig ChestCount) |
| EscapeTile on GeneratedMap (boss floor top) | Task 3 |
| MatchClient.Lives from snapshot | Task 4 + 5 |

**Placeholder scan:** None found.

**Type consistency check:**
- `SnapshotFactory.Capture(sim, tick, lives)` — matches call in MatchHost.StepOnce.
- `ChestLootTable.Roll(rng)` — `_rng` field used; verify field name in Simulation.cs before writing.
- `SetPermLevels(minerId, speed, vision, blast)` — matches MatchHost call and Task 1 implementation.
- `LifeRestored(int MinerId)` — matches MatchHost switch case.
- `GeneratedMap.EscapeTile` — matches MatchHost.AdvanceFloor, MatchClient.ResetFloor, Main._Ready.
- `ResultsOverlay.Show(text, hostControls, buttonText, scoreText)` — 4-param overload; existing callsite in Main passes 3 → add `scoreText = ""` default. ✓
