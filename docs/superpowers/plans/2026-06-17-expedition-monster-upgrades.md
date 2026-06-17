# Expedition Monster Upgrades Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add direction-aware code-drawn monster sprites, mold that slows terrain-bound monsters, deep water that blocks ghosts, and a Lantern item that kills/repels ghosts via AOE light.

**Architecture:** All monster-hazard logic lives in `Simulation.cs` (Core); the lantern is an `ItemKind` using the existing carried-item system (pickup/drop via `TryUseItem`). Client-side rendering of sprites, glow, and the lantern glyph are all draw-API calls in `WorldRenderer.cs` — no new textures.

**Tech Stack:** C# (.NET 8), Godot 4.6 CanvasItem draw API, xUnit

---

## File Map

| File | Change |
|---|---|
| `src/Miner49er.Core/Sim/Monster.cs` | Add `SlowTimer`, `SlowMultiplier` fields |
| `src/Miner49er.Core/Map/Item.cs` | Add `Lantern` to `ItemKind`; add `Lantern` to `IsCarried()` |
| `src/Miner49er.Core/Sim/SimConfig.cs` | Add `LanternRadius = 3` |
| `src/Miner49er.Core/Sim/Simulation.cs` | Mold-slow monsters; ghost+deep water; lantern AOE (InLanternLight, ghost kill, repel); DropLantern; TryUseItem case |
| `src/Miner49er.Core/Map/MapConfig.cs` | Add `LanternCount = 1` |
| `src/Miner49er.Core/Map/MapGenerator.cs` | Pass `LanternCount` to `PlaceCarriedItems`; place lanterns |
| `game/WorldRenderer.cs` | Direction-aware monster sprites; lantern glow overlay; lantern item glyph |
| `src/Miner49er.Core.Tests/SimulationMonsterTests.cs` | Add mold-slow and deep-water-ghost tests |
| `src/Miner49er.Core.Tests/SimulationLanternTests.cs` | New file: lantern AOE kill/repel/drop/pickup tests |
| `src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs` | Add lantern count test |

---

### Task 1: Mold slows terrain-bound monsters

**Files:**
- Modify: `src/Miner49er.Core/Sim/Monster.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (around lines 260–275)
- Modify: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

**Background:** `Monster.MoveCooldownRemaining` counts down to 0, then `AdvanceMonsters` calls `StepMonster` and resets it by `+= MonsterCadence(mo.Kind)`. We multiply that reset by `SlowMultiplier` (≥1) when the monster is slowed. `SlowTimer` tracks how long the slow lasts; it ticks down each sim tick. After a terrain-bound monster steps onto a mold tile, both fields are set (same values as miners use: `Config.MoldSlowFactor` and `Config.MoldSlowSeconds`).

- [ ] **Step 1: Write the failing tests**

Add to `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`:

```csharp
[Fact]
public void Slime_on_mold_tile_is_slowed()
{
    // cadence 0.1 normally; slowed by MoldSlowFactor (1.6) → effective cadence 0.16
    var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 99,
                              MoldSlowFactor = 1.6, MoldSlowSeconds = 3.0 };
    var grid = new TileGrid(9, 3, TileType.Floor);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(8, 1));
    var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);
    // Seed a mold patch at (3,1) — the tile the slime will step onto
    sim.DropMoldAt(new GridPos(3, 1));   // helper we add in Step 3

    sim.Tick(0.1);   // slime moves from (2,1) to (3,1) — lands on mold

    // After landing on mold, cooldown resets to 0.1 * 1.6 = 0.16
    // Tick another 0.1 — cooldown goes from 0.16 to 0.06 — slime does NOT move
    sim.Tick(0.1);
    Assert.Equal(new GridPos(3, 1), slime.Pos);   // still on mold tile
}

[Fact]
public void Goat_on_mold_tile_is_slowed()
{
    var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.15, MonsterSenseRadius = 99,
                              MoldSlowFactor = 1.6, MoldSlowSeconds = 3.0 };
    var grid = new TileGrid(9, 3, TileType.Floor);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(8, 1));
    var goat = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Goat);
    sim.DropMoldAt(new GridPos(3, 1));

    sim.Tick(0.15);   // goat steps onto (3,1) — lands on mold; cooldown resets to 0.15 * 1.6 = 0.24

    sim.Tick(0.15);   // 0.24 - 0.15 = 0.09 remaining — no move yet
    Assert.Equal(new GridPos(3, 1), goat.Pos);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Slime_on_mold|Goat_on_mold" -v normal
```
Expected: compile error (DropMoldAt missing) or FAIL.

- [ ] **Step 3: Add `SlowTimer` and `SlowMultiplier` to Monster**

Replace `Monster.cs` content:

```csharp
namespace Miner49er.Core;

public enum MonsterKind { Slime, Ghost, Goat }

public sealed class Monster
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public MonsterKind Kind { get; }
    public bool Alive { get; internal set; } = true;

    public Direction ChargeDir { get; internal set; } = Direction.East;   // Goat heading
    public double MoveCooldownRemaining { get; internal set; }            // per-kind cadence gate
    public double SlowTimer { get; internal set; }                        // seconds of mold-slow remaining
    public double SlowMultiplier { get; internal set; } = 1.0;           // >1 = slower; 1.0 = normal

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
    }
}
```

- [ ] **Step 4: Add `DropMoldAt` test helper to Simulation**

Add a single `internal` method to `Simulation.cs` just below `AddMonster` (around line 73):

```csharp
/// <summary>Test helper: plants a mold patch at a specific tile without going through item pickup.</summary>
internal void DropMoldAt(GridPos pos) =>
    _molds.Add(new MoldPatch(pos, Config.MoldSeconds));
```

- [ ] **Step 5: Update `AdvanceMonsters` to slow terrain-bound monsters on mold**

In `Simulation.cs`, replace the `AdvanceMonsters` method body (around lines 260–276):

```csharp
private void AdvanceMonsters(double dt)
{
    if (_monsters.Count == 0) return;

    Miner? target = _miners.Values.Where(m => m.Alive).OrderBy(m => m.Id).FirstOrDefault();

    foreach (var mo in _monsters.OrderBy(x => x.Id))
    {
        if (!mo.Alive) continue;

        // Tick down mold slow
        if (mo.SlowTimer > 0)
        {
            mo.SlowTimer = Math.Max(0, mo.SlowTimer - dt);
            if (mo.SlowTimer <= 0) mo.SlowMultiplier = 1.0;
        }

        mo.MoveCooldownRemaining -= dt;
        if (mo.MoveCooldownRemaining > 0) continue;

        // Step first, THEN check mold so that the cadence reset below uses
        // the multiplier current AFTER landing (slow takes effect immediately).
        StepMonster(mo, target);

        if (mo.Alive && mo.Kind != MonsterKind.Ghost && _molds.Any(mp => mp.Pos == mo.Pos))
        {
            mo.SlowTimer = Config.MoldSlowSeconds;
            mo.SlowMultiplier = Config.MoldSlowFactor;
        }

        mo.MoveCooldownRemaining += MonsterCadence(mo.Kind) * mo.SlowMultiplier;
    }
}
```

- [ ] **Step 6: Run the tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 351` (349 + 2 new).

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/Monster.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(sim): mold slows terrain-bound monsters (slime + goat)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Deep water blocks ghosts

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`CanMonsterEnter`, around line 309)
- Modify: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

**Background:** `CanMonsterEnter` currently lets ghosts into any in-bounds tile. Add a `DeepWater` exception.

- [ ] **Step 1: Write the failing test**

Add to `SimulationMonsterTests.cs`:

```csharp
[Fact]
public void Ghost_cannot_enter_deep_water()
{
    var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1, MonsterSenseRadius = 99 };
    var grid = new TileGrid(5, 3, TileType.Floor);
    grid.Set(new GridPos(3, 1), TileType.DeepWater);   // deep water east of ghost
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(4, 1));                 // miner is east, ghost wants to go east
    var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

    sim.Tick(0.1);

    Assert.Equal(new GridPos(2, 1), ghost.Pos);   // deep water blocked the ghost
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Ghost_cannot_enter_deep_water" -v normal
```
Expected: FAIL (ghost moves through deep water currently).

- [ ] **Step 3: Edit `CanMonsterEnter`**

Find the method in `Simulation.cs` (around line 308). Replace:

```csharp
// Rock blocks terrain-bound monsters; a ghost phases through anything in bounds.
private bool CanMonsterEnter(Monster mo, GridPos p)
{
    if (!Grid.InBounds(p)) return false;
    if (mo.Kind == MonsterKind.Ghost) return true;
    return Grid.Get(p).IsEnterable();
}
```

With:

```csharp
// Rock blocks terrain-bound monsters; ghosts phase rock but are stopped by deep water.
private bool CanMonsterEnter(Monster mo, GridPos p)
{
    if (!Grid.InBounds(p)) return false;
    if (mo.Kind == MonsterKind.Ghost) return Grid.Get(p) != TileType.DeepWater;
    return Grid.Get(p).IsEnterable();
}
```

- [ ] **Step 4: Run all tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 352`.

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(sim): deep water blocks ghosts

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Lantern item type and SimConfig radius

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`

**Background:** `ItemKind.Lantern` must be in `IsCarried()` so the auto-apply walk-over pickup doesn't trigger; the miner must explicitly use `TryUseItem` to pick it up. `PlaceItems` already excludes carried kinds from the random toolbox/buried pool (`Where(k => !k.IsCarried())`).

- [ ] **Step 1: Add `Lantern` to `ItemKind` and `IsCarried`**

In `src/Miner49er.Core/Map/Item.cs`, replace:

```csharp
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold }
```

With:

```csharp
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold, Lantern }
```

And replace:

```csharp
public static bool IsCarried(this ItemKind k) => k is ItemKind.WaterPlank or ItemKind.SlowMold;
```

With:

```csharp
public static bool IsCarried(this ItemKind k) =>
    k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern;
```

- [ ] **Step 2: Add `LanternRadius` to `SimConfig`**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the monster cadence block (after `MonsterSenseRadius`):

```csharp
public int LanternRadius { get; set; } = 3;   // Chebyshev radius — ghosts in range die; ghosts won't enter
```

- [ ] **Step 3: Build to verify no compile errors**

```
dotnet build Miner49er.sln --nologo -v quiet
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run all tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 352` (no new tests yet; existing tests still pass).

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Sim/SimConfig.cs
git commit -m "feat(core): add Lantern item kind and LanternRadius config

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Lantern simulation logic

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationLanternTests.cs`

**Background:**
- `InLanternLight(GridPos pos)` is a private helper that returns true if `pos` is within `Config.LanternRadius` (Chebyshev) of any held or placed lantern.
- Ghost kill: at the end of `AdvanceMonsters`, kill any living ghost inside the AOE.
- Ghost repel: in `GhostDir`, if the computed next tile is in lantern light, return `null` (ghost skips its turn).
- Lantern drop: `DropLantern(Miner m)` adds a `Loose` item on the miner's tile, clears `Held`.
- `TryUseItem` held-item switch gains `ItemKind.Lantern => DropLantern(m)`.

Note: `MinerSnapshot.Held` is `-1` when nothing is held; `(int)ItemKind.Lantern` otherwise. Miner pickup is handled by the existing `TryUseItem` step-1 (standing on a carried item with the Use verb). No new events are needed — the placed lantern appears in the `ItemSnapshot` list on the next tick.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/SimulationLanternTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class SimulationLanternTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig { MonsterGhostMoveSeconds = 999 });

    private static void GiveLantern(Simulation sim, int minerId, GridPos minerPos)
    {
        sim.AddItem(new Item(minerPos, ItemKind.Lantern, ItemPlacement.Loose));
        sim.TryUseItem(minerId);
    }

    [Fact]
    public void Ghost_inside_lantern_aoe_dies_on_tick()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(11, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        var miner = sim.AddMiner(1, new GridPos(5, 1));
        GiveLantern(sim, 1, new GridPos(5, 1));
        // Ghost at distance 2 (inside radius 3)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);   // ghost can't move (999s cadence) — kill pass runs

        Assert.False(ghost.Alive);
    }

    [Fact]
    public void Ghost_outside_lantern_aoe_survives()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(15, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(7, 1));
        GiveLantern(sim, 1, new GridPos(7, 1));
        // Ghost at distance 4 (outside radius 3)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);

        Assert.True(ghost.Alive);
    }

    [Fact]
    public void Ghost_does_not_step_into_lantern_light()
    {
        // Ghost starts just outside AOE and would normally step toward miner (into AOE)
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 0.1,
                                  MonsterSenseRadius = 99 };
        var grid = new TileGrid(15, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(7, 1));
        GiveLantern(sim, 1, new GridPos(7, 1));
        // Ghost at (3,1): distance from miner (7,1) = Chebyshev 4 (outside radius 3)
        // One step east → (4,1): distance 3 — inside AOE. Ghost should NOT take this step.
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(3, 1), ghost.Pos);   // halted at boundary
        Assert.True(ghost.Alive);                      // didn't step in, not killed
    }

    [Fact]
    public void Placed_lantern_kills_ghost_in_its_aoe()
    {
        var cfg = new SimConfig { LanternRadius = 3, MonsterGhostMoveSeconds = 999 };
        var grid = new TileGrid(11, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(5, 1));
        GiveLantern(sim, 1, new GridPos(5, 1));
        // Drop the lantern at (5,1)
        sim.TryUseItem(1);   // miner holds lantern → drops at (5,1)
        // Ghost at (3,1) = distance 2 from lantern at (5,1)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);

        sim.Tick(0.01);

        Assert.False(ghost.Alive);
    }

    [Fact]
    public void Dropped_lantern_appears_as_loose_item_and_can_be_picked_back_up()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(3, 1));
        GiveLantern(sim, 1, new GridPos(3, 1));

        sim.TryUseItem(1);   // drop
        Assert.Null(sim.GetMiner(1).Held);
        Assert.Single(sim.Items, it => it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose);

        sim.TryUseItem(1);   // pick back up
        Assert.Equal(ItemKind.Lantern, sim.GetMiner(1).Held);
        Assert.Empty(sim.Items.Where(it => it.Kind == ItemKind.Lantern));
    }
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "SimulationLanternTests" -v normal
```
Expected: all FAIL (methods not yet implemented).

- [ ] **Step 3: Add `InLanternLight` private helper to `Simulation.cs`**

Add the following private method in `Simulation.cs`, after `CanMonsterEnter`:

```csharp
private bool InLanternLight(GridPos pos)
{
    foreach (var m in _miners.Values)
        if (m.Alive && m.Held == ItemKind.Lantern)
            if (pos.ChebyshevTo(m.Pos) <= Config.LanternRadius) return true;
    foreach (var it in _items)
        if (it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose)
            if (pos.ChebyshevTo(it.Pos) <= Config.LanternRadius) return true;
    return false;
}
```

- [ ] **Step 4: Add ghost kill pass at end of `AdvanceMonsters`**

In `AdvanceMonsters`, after the `foreach` loop (after the existing closing brace), add:

```csharp
    // Kill any ghost inside a lantern's AOE
    foreach (var mo in _monsters)
    {
        if (!mo.Alive || mo.Kind != MonsterKind.Ghost) continue;
        if (InLanternLight(mo.Pos))
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
        }
    }
```

- [ ] **Step 5: Update `GhostDir` to repel from lit tiles**

Find `GhostDir` in `Simulation.cs` (around line 323). Replace:

```csharp
private Direction? GhostDir(Monster mo, Miner? target)
{
    if (target is not { Alive: true }) return null;
    return TowardDir(mo.Pos, target.Pos);   // always hunts; CanMonsterEnter lets it phase rock
}
```

With:

```csharp
private Direction? GhostDir(Monster mo, Miner? target)
{
    if (target is not { Alive: true }) return null;
    var d = TowardDir(mo.Pos, target.Pos);
    var next = mo.Pos + d.ToOffset();
    if (InLanternLight(next)) return null;   // halt at AOE boundary rather than entering
    return d;
}
```

- [ ] **Step 6: Add `DropLantern` and wire into `TryUseItem`**

Add private method after `DropMold` in `Simulation.cs`:

```csharp
private bool DropLantern(Miner m)
{
    _items.Add(new Item(m.Pos, ItemKind.Lantern, ItemPlacement.Loose));
    m.Held = null;
    return true;
}
```

In `TryUseItem`, extend the held-item switch (around line 519):

```csharp
return held switch
{
    ItemKind.WaterPlank => TryPlacePlank(m),
    ItemKind.SlowMold   => DropMold(m),
    ItemKind.Lantern    => DropLantern(m),
    _ => false,
};
```

- [ ] **Step 7: Run all tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 357` (352 + 5 new).

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationLanternTests.cs
git commit -m "feat(sim): lantern item kills and repels ghosts via AOE light

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Map seeding — lanterns on generated maps

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Modify: `src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs`

**Background:** `PlaceCarriedItems` already handles plank + mold with a single candidate pool. Lanterns join that pool after molds. `MapConfig` gets `LanternCount = 1` (default 1 for all modes including Expedition). The existing test for `Buff_items_are_unaffected_by_the_carried_item_pass` stays valid because `IsCarried()` now includes `Lantern`.

- [ ] **Step 1: Write the failing test**

Add to `MapGeneratorCarriedItemsTests.cs`:

```csharp
[Fact]
public void Generates_the_configured_number_of_lanterns()
{
    var cfg = new MapConfig { Seed = 77, PlayerCount = 2, LanternCount = 2 };
    var map = MapGenerator.Generate(cfg);
    Assert.Equal(2, map.Items.Count(i => i.Kind == ItemKind.Lantern));
}
```

- [ ] **Step 2: Run to confirm FAIL**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Generates_the_configured_number_of_lanterns" -v normal
```
Expected: FAIL (LanternCount property missing).

- [ ] **Step 3: Add `LanternCount` to `MapConfig`**

In `src/Miner49er.Core/Map/MapConfig.cs`, add after `SlowMoldCount`:

```csharp
public int LanternCount { get; set; } = 1;     // visible carried lanterns scattered on Floor
```

- [ ] **Step 4: Update `MapGenerator.Generate` and `PlaceCarriedItems`**

In `MapGenerator.Generate`, replace:

```csharp
items.AddRange(PlaceCarriedItems(grid, rng, config.WaterPlankCount, config.SlowMoldCount, region, spawns, items));
```

With:

```csharp
items.AddRange(PlaceCarriedItems(grid, rng, config.WaterPlankCount, config.SlowMoldCount, config.LanternCount, region, spawns, items));
```

Replace the `PlaceCarriedItems` method signature and body:

```csharp
private static List<Item> PlaceCarriedItems(TileGrid g, Random rng, int plankCount, int moldCount,
    int lanternCount, HashSet<GridPos> region, List<GridPos> spawns, IEnumerable<Item> existing)
{
    var taken = new HashSet<GridPos>(existing.Select(it => it.Pos));
    var spawnSet = new HashSet<GridPos>(spawns);
    var cands = g.Positions()
        .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor
                    && !spawnSet.Contains(p) && !taken.Contains(p))
        .ToList();
    Shuffle(cands, rng);

    var result = new List<Item>();
    int idx = 0;
    for (int i = 0; i < plankCount && idx < cands.Count; i++, idx++)
        result.Add(new Item(cands[idx], ItemKind.WaterPlank, ItemPlacement.Toolbox));
    for (int i = 0; i < moldCount && idx < cands.Count; i++, idx++)
        result.Add(new Item(cands[idx], ItemKind.SlowMold, ItemPlacement.Toolbox));
    for (int i = 0; i < lanternCount && idx < cands.Count; i++, idx++)
        result.Add(new Item(cands[idx], ItemKind.Lantern, ItemPlacement.Toolbox));
    return result;
}
```

- [ ] **Step 5: Run all tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 358`.

- [ ] **Step 6: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs
git commit -m "feat(map): seed lantern items into generated maps

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Direction-aware monster sprites

**Files:**
- Modify: `game/WorldRenderer.cs`

**Background:** Uses Godot CanvasItem draw API only — no textures. `MonsterSnapshot.Facing` (0=N, 1=E, 2=S, 3=W) drives eye/horn placement via two static helpers. The existing placeholder switch block is replaced in full. TAB indentation throughout (`game/` convention).

The new colours needed:
- `SlimeOutlineColor = new Color(0.23f, 0.56f, 0.16f)` — darker ring around slime body
- `GoatHornColor = new Color(0.42f, 0.28f, 0.16f)` — darker brown horns

- [ ] **Step 1: Add colour constants and helper methods to `WorldRenderer`**

In the constants block at the top of `WorldRenderer.cs` (after `ExitColor`), add:

```csharp
	private static readonly Color SlimeOutlineColor = new("3a8f2a");
	private static readonly Color GoatHornColor     = new("6a4828");
	private static readonly Color LanternItemColor  = new("ffe090");
	private static readonly Color LanternGlowColor  = new Color(1f, 0.9f, 0.3f, 0.18f);
	private const int LanternRadius = 3;   // mirrors SimConfig.LanternRadius
```

Add two private static helpers anywhere before `_Draw` (e.g. after `DrawShimmer`):

```csharp
	// Returns a unit-scaled offset in the facing direction (N=up, E=right, S=down, W=left).
	private static Vector2 FacingOffset(int facing, float scale) => facing switch
	{
		0 => new Vector2(0f, -scale),
		1 => new Vector2(scale, 0f),
		2 => new Vector2(0f,  scale),
		3 => new Vector2(-scale, 0f),
		_ => Vector2.Zero,
	};

	// Returns a perpendicular offset 90° clockwise from facing.
	private static Vector2 PerpendicularOffset(int facing, float scale) => facing switch
	{
		0 => new Vector2(scale, 0f),
		1 => new Vector2(0f,  scale),
		2 => new Vector2(-scale, 0f),
		3 => new Vector2(0f, -scale),
		_ => Vector2.Zero,
	};
```

- [ ] **Step 2: Replace monster rendering with direction-aware sprites**

In `_Draw()`, replace the existing monster rendering block:

```csharp
		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			switch (mo.Kind)
			{
				case MonsterKind.Slime:
					DrawCircle(c, ts * 0.34f, SlimeColor);
					break;
				case MonsterKind.Ghost:
					DrawCircle(c, ts * 0.36f, GhostColor with { A = 0.6f });
					break;
				case MonsterKind.Goat:
					DrawRect(new Rect2(c.X - ts * 0.3f, c.Y - ts * 0.3f, ts * 0.6f, ts * 0.6f), GoatColor);
					break;
			}
		}
```

With:

```csharp
		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			var fwd  = FacingOffset(mo.Facing, ts * 0.12f);
			var side = PerpendicularOffset(mo.Facing, ts * 0.10f);
			switch (mo.Kind)
			{
				case MonsterKind.Slime:
				{
					// Body + dark outline
					DrawCircle(c, ts * 0.34f, SlimeColor);
					DrawCircle(c, ts * 0.34f, SlimeOutlineColor, false, 1.5f);
					// Eyes toward facing
					var eye1 = c + fwd + side;
					var eye2 = c + fwd - side;
					DrawCircle(eye1, ts * 0.07f, Colors.White);
					DrawCircle(eye1, ts * 0.04f, Colors.Black);
					DrawCircle(eye2, ts * 0.07f, Colors.White);
					DrawCircle(eye2, ts * 0.04f, Colors.Black);
					break;
				}
				case MonsterKind.Ghost:
				{
					var ghostCol = GhostColor with { A = 0.6f };
					var headOff  = new Vector2(0, -ts * 0.10f);
					// Rounded head
					DrawCircle(c + headOff, ts * 0.28f, ghostCol);
					// Body
					DrawRect(new Rect2(c.X - ts * 0.28f, c.Y - ts * 0.10f, ts * 0.56f, ts * 0.28f), ghostCol);
					// Three wavy tail points
					for (int i = 0; i < 3; i++)
					{
						float xOff = (i - 1) * ts * 0.19f;
						DrawColoredPolygon(new Vector2[] {
							c + new Vector2(xOff - ts * 0.09f, ts * 0.18f),
							c + new Vector2(xOff + ts * 0.09f, ts * 0.18f),
							c + new Vector2(xOff,              ts * 0.36f),
						}, ghostCol);
					}
					// Dark eyes toward facing
					var eFwd  = FacingOffset(mo.Facing, ts * 0.08f);
					var eSide = PerpendicularOffset(mo.Facing, ts * 0.09f);
					var eyeBase = c + headOff + eFwd;
					var eyeCol  = new Color(0.1f, 0.1f, 0.2f, 0.85f);
					DrawCircle(eyeBase + eSide,  ts * 0.065f, eyeCol);
					DrawCircle(eyeBase - eSide,  ts * 0.065f, eyeCol);
					break;
				}
				case MonsterKind.Goat:
				{
					// Body circle
					DrawCircle(c, ts * 0.28f, GoatColor);
					// Head toward facing
					var headPos = c + FacingOffset(mo.Facing, ts * 0.22f);
					DrawCircle(headPos, ts * 0.16f, GoatColor);
					// Horns as lines from sides of head
					var hSide = PerpendicularOffset(mo.Facing, ts * 0.10f);
					var hFwd  = FacingOffset(mo.Facing, ts * 0.14f);
					DrawLine(headPos + hSide,          headPos + hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
					DrawLine(headPos - hSide,          headPos - hSide * 1.8f + hFwd, GoatHornColor, 2.5f);
					// Eyes
					DrawCircle(headPos + PerpendicularOffset(mo.Facing, ts * 0.05f), ts * 0.04f, Colors.Black);
					DrawCircle(headPos - PerpendicularOffset(mo.Facing, ts * 0.05f), ts * 0.04f, Colors.Black);
					break;
				}
			}
		}
```

- [ ] **Step 3: Build**

```
dotnet build Miner49er.sln --nologo -v quiet
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Commit**

```
git add game/WorldRenderer.cs
git commit -m "feat(render): direction-aware code-drawn monster sprites

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Lantern glow overlay and item glyph

**Files:**
- Modify: `game/WorldRenderer.cs`

**Background:** Two visual additions:
1. **Glow overlay**: a dim yellow-gold rect drawn over every fog-visible tile within Chebyshev-3 of any held or placed lantern. Drawn before monsters so glow appears underneath.
2. **Lantern glyph**: when the lantern item is on the floor (as Toolbox or Loose), draw a warm-yellow filled circle with a dark ring — replacing the generic `ItemColor` fallback circle. The `LanternItemColor` constant is already added in Task 6.

The client-side helper `IsInLanternLight` reads `_client.Miners` (checking `m.Held == (int)ItemKind.Lantern`) and `_client.Items` (checking `Kind == ItemKind.Lantern && Placement == ItemPlacement.Loose`).

- [ ] **Step 1: Add `IsInLanternLight` helper to `WorldRenderer`**

Add after `PerpendicularOffset`:

```csharp
	private bool IsInLanternLight(GridPos pos)
	{
		foreach (var m in _client.Miners)
			if (m.Alive && m.Held == (int)ItemKind.Lantern)
				if (Math.Max(Math.Abs(pos.X - m.X), Math.Abs(pos.Y - m.Y)) <= LanternRadius) return true;
		foreach (var it in _client.Items)
			if (it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose)
				if (Math.Max(Math.Abs(pos.X - it.X), Math.Abs(pos.Y - it.Y)) <= LanternRadius) return true;
		return false;
	}
```

- [ ] **Step 2: Add glow overlay pass in `_Draw()` before the monster loop**

In `_Draw()`, insert the following block immediately before `foreach (var mo in _client.Monsters)`:

```csharp
		// Lantern light: dim glow over all fog-visible tiles within AOE of held or placed lanterns
		foreach (var p in grid.Positions())
		{
			if (!_client.Fog.IsVisible(p)) continue;
			if (IsInLanternLight(p))
				DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), LanternGlowColor);
		}
```

- [ ] **Step 3: Add lantern glyph rendering**

In `_Draw()`, inside the item rendering loop, after the `if (_itemTex.TryGetValue(it.Kind, out var itex)) ... else DrawCircle(...)` fallback, the lantern needs a distinct glyph. The cleanest approach: add it to the `_itemTex` miss path by checking kind before the generic circle. In the non-Toolbox branch replace:

```csharp
				if (_itemTex.TryGetValue(it.Kind, out var itex))
					DrawTextureRect(itex, r, false);
				else
					DrawCircle(icenter, ts * 0.22f, ItemColor(it.Kind));
```

With:

```csharp
				if (_itemTex.TryGetValue(it.Kind, out var itex))
					DrawTextureRect(itex, r, false);
				else if (it.Kind == ItemKind.Lantern)
				{
					DrawCircle(icenter, ts * 0.22f, LanternItemColor);
					DrawCircle(icenter, ts * 0.22f, new Color(0.5f, 0.4f, 0.1f), false, 1.5f);
				}
				else
					DrawCircle(icenter, ts * 0.22f, ItemColor(it.Kind));
```

Do the same replacement in the Toolbox inner item rendering (the `inner` rect branch):

```csharp
				if (_itemTex.TryGetValue(it.Kind, out var itex2))
					DrawTextureRect(itex2, inner, false);
				else
					DrawCircle(icenter, ts * 0.15f, ItemColor(it.Kind));
```

Becomes:

```csharp
				if (_itemTex.TryGetValue(it.Kind, out var itex2))
					DrawTextureRect(itex2, inner, false);
				else if (it.Kind == ItemKind.Lantern)
				{
					DrawCircle(icenter, ts * 0.15f, LanternItemColor);
					DrawCircle(icenter, ts * 0.15f, new Color(0.5f, 0.4f, 0.1f), false, 1.5f);
				}
				else
					DrawCircle(icenter, ts * 0.15f, ItemColor(it.Kind));
```

- [ ] **Step 4: Build**

```
dotnet build Miner49er.sln --nologo -v quiet
```
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Run all tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --nologo -v quiet
```
Expected: `Passed! - Failed: 0, Passed: 358`.

- [ ] **Step 6: Commit**

```
git add game/WorldRenderer.cs
git commit -m "feat(render): lantern glow overlay and item glyph

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```
