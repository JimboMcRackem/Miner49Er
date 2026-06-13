# Phase 4d-2 — Cave-Ins Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a weakened-floor hazard — patches of "cracked" floor that you can cross once but that collapse into a lethal hole if you linger, re-cross, or blast them.

**Architecture:** Pure-C# `Miner49er.Core` first (engine-free, xUnit-tested): two new tile types (`Cracked`, `Crumbling`) with a global per-tile lifecycle `Cracked → Crumbling → Pit`, driven by `Simulation.TryMove` (crossing weakens; re-entry collapses), a per-tick dwell pass (lingering collapses), and `Detonate` (blast collapses cracks in its disc). Collapse reuses the existing `TileType.Pit`. A thin Godot adapter then threads the `CaveIns` lobby toggle to both peers, maps the new transition events onto the existing `TileChange` sync path, and adds rendering/audio/feed. No snapshot schema change — `DeathCause.Crushed` rides the existing `MinerSnapshot.Cause` byte.

**Tech Stack:** C# / .NET 8, Godot 4.6.3 (.NET/Mono), xUnit. Core indent = 4 spaces; `game/` indent = TAB.

**Spec:** `docs/superpowers/specs/2026-06-13-phase4d2-cave-ins-design.md`

**Conventions for every task:**
- Build: `dotnet build Miner49er.sln` (expect `0 Warning(s) 0 Error(s)`).
- Core tests: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.
- Godot headless smoke (game tasks only) MUST be run from **PowerShell**, never the Bash tool: `godot --headless --quit-after 5` (expect clean exit, no C# exceptions). The Bash `godot` shim breaks headless with a false "assemblies not found".
- Commit messages end with the required `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- Stage only the exact files each task names. Never `git add -A` (the repo has long-standing pre-existing untracked `assets/Splash.png*`, `.uid` files, `.superpowers/`, and CRLF-only `project.godot`/`game/Splash.tscn` that must stay unstaged).
- Baseline before this plan: Core **228/228** passing on `main` @ `4e913d7`. Branch for this work: `phase4d2-cave-ins` (already created).

---

### Task 1: Crack tile types & predicates (Core)

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Create: `src/Miner49er.Core.Tests/TileTypeCrackTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/TileTypeCrackTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TileTypeCrackTests
{
    [Theory]
    [InlineData(TileType.Cracked)]
    [InlineData(TileType.Crumbling)]
    public void Cracks_are_safe_walkable_floor_not_instant_death(TileType t)
    {
        Assert.True(t.IsWalkable());     // spawns/fog/reachability treat a crack as ground
        Assert.True(t.IsEnterable());    // you can step onto it
        Assert.False(t.IsLethal());      // ...and it does not kill you on contact
    }

    [Theory]
    [InlineData(TileType.Cracked)]
    [InlineData(TileType.Crumbling)]
    public void Cracks_are_open_floor_not_rock(TileType t)
    {
        Assert.False(t.BlocksSight());   // open floor — transparent
        Assert.False(t.IsMinable());
        Assert.False(t.IsBlastable());
        Assert.False(t.IsWater());
        Assert.Equal(1.0, t.MoveCostMultiplier());
    }

    [Theory]
    [InlineData(TileType.Cracked, true)]
    [InlineData(TileType.Crumbling, true)]
    [InlineData(TileType.Pit, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.Plank, false)]
    public void IsBridgeable_now_includes_cracks(TileType t, bool expected)
        => Assert.Equal(expected, t.IsBridgeable());
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: compile error — `TileType` has no member `Cracked`/`Crumbling`.

- [ ] **Step 3: Implement the tile types and predicates**

In `src/Miner49er.Core/Grid/TileType.cs`, add the two values to the enum:

```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit, Cracked, Crumbling }
```

Update the predicates in `TileTypeExtensions` so cracks count as walkable, enterable floor (but **not** lethal). Replace the `IsWalkable`, `IsEnterable`, and `IsBridgeable` methods:

```csharp
    /// <summary>Safe to stand on (used for spawns, fog, drip placement, reachability).
    /// Cracks are floor you stand on; they only give way on a second loading or via dwell.</summary>
    public static bool IsWalkable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.Plank
          or TileType.Cracked or TileType.Crumbling;

    /// <summary>A miner may move onto this tile. Deep water and pits are enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank
          or TileType.Pit or TileType.Cracked or TileType.Crumbling;
```

```csharp
    /// <summary>A held water-plank can be laid here (water, a pit, or a crack) to form a safe Plank tile.</summary>
    public static bool IsBridgeable(this TileType t) =>
        t.IsWater() || t is TileType.Pit or TileType.Cracked or TileType.Crumbling;
```

`IsLethal`, `MoveCostMultiplier`, `IsMinable`, `IsBlastable`, `BlocksSight`, and `IsWater` are unchanged — cracks correctly fall through them (not lethal, cost 1.0, not minable/blastable, not sight-blocking, not water).

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (existing 228 + the new crack predicate tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core.Tests/TileTypeCrackTests.cs
git commit -m "$(printf 'feat(core): Cracked/Crumbling tile types + predicates\n\nWeakened-floor tiles: walkable, enterable, transparent, bridgeable;\nnot lethal/minable/blastable. Collapse will reuse the existing Pit.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 2: Crushed death + entering Crumbling collapses (Core)

Introduces the collapse death path and the "go over again" rule: stepping onto an already-`Crumbling` tile drops it to a `Pit` and kills the miner with the new `DeathCause.Crushed`.

**Files:**
- Modify: `src/Miner49er.Core/Sim/DeathCause.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Create: `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationCaveInTests
{
    [Fact]
    public void Entering_a_crumbling_tile_collapses_it_and_crushes_you()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Crumbling);   // already weakened
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                                // the move resolves, then collapses
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));   // floor gave way to a hole
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is CrackCollapsed cc && cc.Pos == new GridPos(2, 1));
        Assert.Contains(events, e => e is MinerCrushed mc && mc.MinerId == 1);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: compile error — `DeathCause.Crushed`, `CrackCollapsed`, `MinerCrushed` do not exist.

- [ ] **Step 3: Add the enum value and events**

In `src/Miner49er.Core/Sim/DeathCause.cs`:

```csharp
public enum DeathCause { None, Drowned, Exploded, Left, Fell, Crushed }
```

In `src/Miner49er.Core/Sim/SimEvent.cs`, add after the `MinerFell` line:

```csharp
public sealed record MinerCrushed(int MinerId) : SimEvent;
public sealed record CrackWeakened(GridPos Pos) : SimEvent;
public sealed record CrackCollapsed(GridPos Pos) : SimEvent;
```

- [ ] **Step 4: Add `CollapseKill` and the TryMove enter-collapse branch**

In `src/Miner49er.Core/Sim/Simulation.cs`, add a helper next to `KillByTile` (after the `KillByTile` method, ~line 427):

```csharp
    // Kills a miner caught in a collapsing crack (distinct from KillByTile, which
    // assigns Fell/Drowned for stepping onto an already-lethal pit/deep-water tile).
    private void CollapseKill(Miner m)
    {
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Crushed;
        _events.Add(new MinerCrushed(m.Id));
    }
```

In `TryMove`, insert the crack-entry branch immediately **after** the existing lethal block and **before** the center-reach block (after line 170, `KillByTile(m);`):

```csharp
        // Stepping onto an already-weakened (Crumbling) tile is the "second loading":
        // the floor gives way to a hole and crushes you.
        if (m.Alive && Grid.Get(target) == TileType.Crumbling)
        {
            Grid.Set(target, TileType.Pit);
            _events.Add(new CrackCollapsed(target));
            CollapseKill(m);
        }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: PASS.

- [ ] **Step 6: Extend the codec round-trip test for Crushed**

`DeathCause.Crushed` replicates over the existing `MinerSnapshot.Cause` byte (the codec already writes `(byte)m.Cause` / reads `(DeathCause)r.ReadByte()`), so no codec change is needed — but pin it with a test. In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, in `Round_trips_death_cause`, add a fifth miner and assertion. Change the miner list (currently miners 1–4) to add:

```csharp
                    new(5, 4, 4, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Crushed),
```

(insert it right after the `DeathCause.Left` miner line, before the `Alive: true` miner `new(4, …)`), and add the matching assertion after the `Miners[3]` one:

```csharp
        Assert.Equal(DeathCause.Crushed, back.Snapshot.Miners[3].Cause);
        Assert.Equal(DeathCause.None, back.Snapshot.Miners[4].Cause);
```

Remove the old `Miners[3]` None assertion (the None miner moved to index 4). The final block of four assertions becomes indices 0=Drowned, 1=Exploded, 2=Left, 3=Crushed, 4=None.

- [ ] **Step 7: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green, including the extended codec test).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Sim/DeathCause.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCaveInTests.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "$(printf 'feat(core): entering a Crumbling tile collapses it (DeathCause.Crushed)\n\nAdds Crushed cause + MinerCrushed/CrackWeakened/CrackCollapsed events and\nCollapseKill. Stepping onto a Crumbling tile drops it to a Pit and crushes\nthe miner. Crushed round-trips over the existing snapshot Cause byte.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 3: Crossing a fresh crack weakens it (Core)

Implements "walking over 1 is ok": stepping **off** a fresh `Cracked` tile promotes it to `Crumbling`. Combined with Task 2, re-crossing a crack now collapses it.

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`:

```csharp
    [Fact]
    public void Crossing_a_fresh_crack_weakens_it_to_crumbling_but_you_survive()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        Assert.True(sim.TryMove(1, Direction.East));        // step ONTO the crack
        Assert.True(m.Alive);
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(2, 1))); // still fresh while you're on it
        sim.DrainEvents();

        m.MoveCooldownRemaining = 0;                          // clear cadence gate for the test
        Assert.True(sim.TryMove(1, Direction.East));        // step OFF it
        Assert.True(m.Alive);
        Assert.Equal(TileType.Crumbling, grid.Get(new GridPos(2, 1))); // worn down behind you
        Assert.Contains(sim.DrainEvents(), e => e is CrackWeakened cw && cw.Pos == new GridPos(2, 1));
    }

    [Fact]
    public void Re_crossing_a_crack_collapses_it()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);                      // onto crack (now standing on it)
        m.MoveCooldownRemaining = 0;
        sim.TryMove(1, Direction.East);                      // off it -> Crumbling
        m.MoveCooldownRemaining = 0;
        sim.DrainEvents();

        sim.TryMove(1, Direction.West);                      // back onto the Crumbling tile
        Assert.False(m.Alive);                               // "going over again" collapses it
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: the new tests FAIL — the crossed tile stays `Cracked` (no promotion yet), so the weaken assertion and the re-cross collapse both fail.

- [ ] **Step 3: Add the leave-promote branch**

In `src/Miner49er.Core/Sim/Simulation.cs` `TryMove`, insert immediately **after** the enter-collapse branch added in Task 2 (still before the center-reach block):

```csharp
        // Stepping OFF a fresh crack wears it down to Crumbling (you survived the first
        // crossing, but the floor is now weak for the next loading).
        if (Grid.Get(from) == TileType.Cracked)
        {
            Grid.Set(from, TileType.Crumbling);
            _events.Add(new CrackWeakened(from));
        }
```

(`from` is the pre-move position captured at `var from = m.Pos;`.)

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCaveInTests.cs
git commit -m "$(printf 'feat(core): crossing a fresh crack wears it to Crumbling\n\nStepping off a Cracked tile promotes it (CrackWeakened); a later entry\nthen collapses it. Walking over once is safe; going over again is fatal.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 4: Lingering on a crack collapses it (Core)

Implements "staying on the crack triggers the fall": a miner who dwells on a `Cracked`/`Crumbling` tile past `CrackDwellSeconds` drops through it.

**Files:**
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`:

```csharp
    [Fact]
    public void Lingering_on_a_crack_collapses_it_under_you()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var cfg = new SimConfig { CrackDwellSeconds = 0.5 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);        // onto the crack, then stand still
        sim.DrainEvents();

        sim.Tick(0.3);
        Assert.True(m.Alive);                  // under the dwell threshold so far
        sim.Tick(0.3);                         // total 0.6 >= 0.5 -> gives way
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Walking_straight_across_a_crack_does_not_collapse_under_you()
    {
        var grid = new TileGrid(4, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Cracked);
        var cfg = new SimConfig { CrackDwellSeconds = 0.5 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);        // onto the crack
        sim.Tick(0.1);                         // brief dwell, well under threshold
        m.MoveCooldownRemaining = 0;
        sim.TryMove(1, Direction.East);        // keep moving off it
        sim.Tick(0.1);

        Assert.True(m.Alive);                  // you kept moving, so you live
        Assert.Equal(new GridPos(3, 1), m.Pos);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: compile error — `SimConfig.CrackDwellSeconds` does not exist.

- [ ] **Step 3: Add the dwell field and config knob**

In `src/Miner49er.Core/Sim/Miner.cs`, add after the `MoveCooldownRemaining` property (line 20):

```csharp
    public double CrackDwell { get; internal set; }   // seconds stood on the current crack tile
```

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the `MoldSlowSeconds` line:

```csharp

    public double CrackDwellSeconds { get; set; } = 0.75;   // linger this long on a crack and it gives way
```

- [ ] **Step 4: Add the dwell pass and reset**

In `src/Miner49er.Core/Sim/Simulation.cs`, add a dwell pass method (place it next to `AdvanceMolds`, ~line 144):

```csharp
    // A miner who lingers on a crack rather than crossing it loads the floor a
    // second time and falls through. Crossing miners reset CrackDwell on each move,
    // so a normal walk-through never trips this.
    private void AdvanceCracks(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (!m.Alive) continue;
            var t = Grid.Get(m.Pos);
            if (t == TileType.Cracked || t == TileType.Crumbling)
            {
                m.CrackDwell += dt;
                if (m.CrackDwell >= Config.CrackDwellSeconds)
                {
                    Grid.Set(m.Pos, TileType.Pit);
                    _events.Add(new CrackCollapsed(m.Pos));
                    CollapseKill(m);
                }
            }
            else m.CrackDwell = 0;
        }
    }
```

Call it in `Tick`, right after `AdvanceCooldowns(dt);` (line 150):

```csharp
        AdvanceCooldowns(dt);
        AdvanceCracks(dt);
```

In `TryMove`, reset the dwell timer on every successful move. Add immediately **before** `return true;` (after `m.MoveCooldownRemaining = EffectiveMoveSeconds(m);`):

```csharp
        m.CrackDwell = 0;   // moving resets the linger timer; only standing still trips a crack
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: PASS.

- [ ] **Step 6: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCaveInTests.cs
git commit -m "$(printf 'feat(core): lingering on a crack collapses it (dwell pass)\n\nMiner.CrackDwell accrues each tick on a crack tile and resets on move;\npast SimConfig.CrackDwellSeconds (0.75s) the floor gives way. Walking\nstraight across stays safe.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 5: Blasts collapse cracks in their disc (Core)

A detonation shakes loose every crack in its rock-destruction disc, turning them into holes and crushing any miner standing on one (unless the blast already killed them as `Exploded`).

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationCaveInTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `src/Miner49er.Core.Tests/SimulationCaveInTests.cs` (note the helper that fast-forwards a planted charge):

```csharp
    [Fact]
    public void Blast_collapses_cracks_in_its_disc_and_crushes_those_outside_the_kill_radius()
    {
        // Wide rock radius, tight kill radius: a crack at Manhattan distance 2 is inside
        // the destruction disc but a miner on it is outside the Chebyshev-1 kill radius.
        var grid = new TileGrid(7, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);          // wall to plant the charge on
        grid.Set(new GridPos(5, 2), TileType.Cracked);       // distance 2 east of the wall
        var cfg = new SimConfig { BlastRockRadius = 2, BlastKillRadius = 1, FuseSeconds = 0.1, PlantSeconds = 0.1 };
        var sim = new Simulation(grid, cfg);

        var planter = sim.AddMiner(1, new GridPos(3, 3));     // adjacent to the wall, faces it
        planter.Facing = Direction.North;
        var victim = sim.AddMiner(2, new GridPos(5, 2));      // standing on the crack, far from the wall

        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.1);   // planting completes -> charge armed
        sim.Tick(0.1);   // fuse expires -> detonation

        Assert.Equal(TileType.Pit, grid.Get(new GridPos(5, 2)));   // crack shaken into a hole
        Assert.False(victim.Alive);
        Assert.Equal(DeathCause.Crushed, victim.DeathCause);       // outside kill radius -> crushed, not exploded
        Assert.Contains(sim.DrainEvents(), e => e is CrackCollapsed cc && cc.Pos == new GridPos(5, 2));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: FAIL — the crack tile stays `Cracked` (the disc loop only touches blastable rock), so the victim survives.

- [ ] **Step 3: Extend `Detonate` to collapse cracks**

In `src/Miner49er.Core/Sim/Simulation.cs`, `Detonate` (~line 441). First, declare a collected-positions list alongside `destroyed` at the top of the disc loop:

```csharp
        var destroyed = new List<GridPos>();
        var collapsedCracks = new List<GridPos>();
        int r = Config.BlastRockRadius + charge.BlastBonus;
```

Inside the `for`/`for` disc loop, the existing body skips non-blastable tiles via `if (!Grid.InBounds(p) || !Grid.Get(p).IsBlastable()) continue;`. Add a crack branch **before** that guard (so cracks, which are not blastable, are still processed). Replace:

```csharp
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p) || !Grid.Get(p).IsBlastable()) continue;
```

with:

```csharp
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;        // Manhattan disc
                if (!Grid.InBounds(p)) continue;
                if (Grid.Get(p) is TileType.Cracked or TileType.Crumbling)
                {
                    Grid.Set(p, TileType.Pit);                        // the blast shakes the weak floor down
                    _events.Add(new CrackCollapsed(p));
                    collapsedCracks.Add(p);
                    continue;
                }
                if (!Grid.Get(p).IsBlastable()) continue;
```

Then, **after** the existing miner-kill loop (the `foreach (var m in _miners.Values)` that assigns `Exploded`, ending ~line 473) and before `_events.Add(new Explosion(...))`, add:

```csharp
        // Any miner still alive but standing on a crack the blast just dropped falls in.
        foreach (var m in _miners.Values)
            if (m.Alive && collapsedCracks.Contains(m.Pos))
                CollapseKill(m);
```

(The `Exploded` pass runs first, so a miner inside the kill radius is already dead and is skipped here — explosion wins over cave-in for the cause.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationCaveInTests`
Expected: PASS.

- [ ] **Step 5: Run the full Core suite (regression — explosive tests unchanged)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — existing `SimulationExplosiveTests` still green (no cracks in those maps, so the new branch is inert).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCaveInTests.cs
git commit -m "$(printf 'feat(core): blasts collapse cracks in their disc\n\nDetonate now drops any Cracked/Crumbling tile in the rock-destruction\ndisc to a Pit and crushes a miner on it (unless already killed in the\nblast kill radius). Makes blasting near a crack field an area-denial tool.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 6: Map generation — `PlaceCracks` (Core)

Scatters crack patches on Floor when the `CaveIns` toggle is on, after every objective-placement pass so nothing important lands on a crack.

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Create: `src/Miner49er.Core.Tests/MapGeneratorCaveInTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/MapGeneratorCaveInTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCaveInTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed, bool caveIns) =>
        new() { Seed = seed, PlayerCount = 4, CaveIns = caveIns };

    private static List<GridPos> CracksOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) is TileType.Cracked or TileType.Crumbling).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void No_cracks_when_toggle_is_off(int seed)
        => Assert.Empty(CracksOf(MapGenerator.Generate(Config(seed, caveIns: false)).Grid));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Cracks_are_generated_when_toggle_is_on(int seed)
        => Assert.NotEmpty(CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid));

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Generation_is_deterministic_with_cracks(int seed)
    {
        var a = CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid);
        var b = CracksOf(MapGenerator.Generate(Config(seed, caveIns: true)).Grid);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Cracks_only_replace_floor_never_spawns_center_or_items(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, caveIns: true));
        var crackSet = new HashSet<GridPos>(CracksOf(map.Grid));
        Assert.DoesNotContain(map.Center, crackSet);
        foreach (var s in map.Spawns) Assert.DoesNotContain(s, crackSet);
        foreach (var it in map.Items) Assert.DoesNotContain(it.Pos, crackSet);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Initial_map_stays_connected_spawns_reach_center(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, caveIns: true));
        var g = map.Grid;
        // Cracks are walkable at gen time, so connectivity must hold over walkable tiles.
        var seen = new HashSet<GridPos> { map.Spawns[0] };
        var q = new Queue<GridPos>();
        q.Enqueue(map.Spawns[0]);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n).IsWalkable() && seen.Add(n)) q.Enqueue(n);
            }
        }
        Assert.Contains(map.Center, seen);
        foreach (var s in map.Spawns) Assert.Contains(s, seen);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MapGeneratorCaveInTests`
Expected: compile error — `MapConfig.CaveIns` does not exist.

- [ ] **Step 3: Add the config knobs**

In `src/Miner49er.Core/Map/MapConfig.cs`, add after the pit block (after `PitClusterMax`, line 25):

```csharp

    // Cave-ins (Phase 4d) — host lobby toggle, off by default.
    public bool CaveIns { get; set; } = false;             // gates the whole PlaceCracks pass
    public int CrackSiteCount { get; set; } = 4;            // base number of crack patches (light per-player scaling)
    public int CrackPatchMax { get; set; } = 8;             // max tiles in a grown patch ("areas", larger than pits)
    public double CrackPatchGrowChance { get; set; } = 0.7; // chance a site grows beyond one tile
```

Update `MapConfig.For` to accept and apply the flag. Change the signature and the constructor line:

```csharp
    public static MapConfig For(GameMode mode, int seed, int playerCount, bool pits = false, bool caveIns = false)
    {
        var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount, Pits = pits, CaveIns = caveIns };
```

- [ ] **Step 4: Add the `PlaceCracks` pass**

In `src/Miner49er.Core/Map/MapGenerator.cs`, call it in `Generate` after `PlaceDecoys` (after line 30) and before `return`:

```csharp
        var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);
        if (config.CaveIns)
            PlaceCracks(grid, rng, config.CrackSiteCount + (config.PlayerCount - 1),
                        config.CrackPatchGrowChance, config.CrackPatchMax,
                        region, spawns, center, items);
```

Add the pass (place it after `GrowPit`, ~line 159). It mirrors `PlacePits` but carves `Cracked`, runs after the objective passes, and so must explicitly avoid spawns / center / item tiles:

```csharp
    // Carves cracked-floor "areas" over Floor for the cave-in hazard. Runs AFTER the
    // spawn/center/gold/item passes (cracks are walkable, so they don't change
    // reachability), and explicitly skips those objective tiles. Each site flood-grows
    // a small blob, biased toward multi-tile patches since these are deliberately areas.
    private static void PlaceCracks(TileGrid g, Random rng, int siteCount,
                                    double growChance, int patchMax,
                                    HashSet<GridPos> region, List<GridPos> spawns,
                                    GridPos center, List<Item> items)
    {
        var blocked = new HashSet<GridPos>(spawns) { center };
        foreach (var it in items) blocked.Add(it.Pos);

        var floors = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !blocked.Contains(p))
            .ToList();
        Shuffle(floors, rng);

        int placed = 0;
        foreach (var seed in floors)
        {
            if (placed >= siteCount) break;
            if (g.Get(seed) != TileType.Floor) continue;   // consumed by a prior patch
            g.Set(seed, TileType.Cracked);
            if (rng.NextDouble() < growChance)
                GrowCrack(g, rng, seed, rng.Next(2, patchMax + 1), blocked);
            placed++;
        }
    }

    // Grows a crack patch to `size` total tiles by random flood over adjacent Floor,
    // never overrunning a blocked (spawn/center/item) tile.
    private static void GrowCrack(TileGrid g, Random rng, GridPos seed, int size, HashSet<GridPos> blocked)
    {
        var frontier = new List<GridPos> { seed };
        int count = 1;
        while (count < size && frontier.Count > 0)
        {
            int fromIdx = rng.Next(frontier.Count);
            var from = frontier[fromIdx];
            var nbrs = Card.Select(d => from + d.ToOffset())
                           .Where(n => g.InBounds(n) && g.Get(n) == TileType.Floor && !blocked.Contains(n))
                           .ToList();
            if (nbrs.Count == 0) { frontier.RemoveAt(fromIdx); continue; }
            var n = nbrs[rng.Next(nbrs.Count)];
            g.Set(n, TileType.Cracked);
            frontier.Add(n);
            count++;
        }
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MapGeneratorCaveInTests`
Expected: PASS.

- [ ] **Step 6: Run the full Core suite (regression — determinism/water/pit gen unchanged when toggle off)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — `MapDeterminismTests` and the pit/water gen tests are unaffected (the pass is gated off by default).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorCaveInTests.cs
git commit -m "$(printf 'feat(core): PlaceCracks map-gen pass + cave-in config knobs\n\nCarves cracked-floor areas on Floor when CaveIns is on, after the\nobjective passes (cracks are walkable, so reachability is preserved and\nspawns/center/items are skipped). MapConfig.For gains a caveIns flag.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 7: Thread the `CaveIns` toggle + sync transitions (game)

Wires the host lobby checkbox to both peers' map generation and maps the new transition events onto the existing `TileChange` sync path. **`game/` files use TAB indentation** — match the surrounding code exactly.

**Files:**
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/ui/Lobby.cs`
- Modify: `game/Main.cs`
- Modify: `game/net/MatchHost.cs`

- [ ] **Step 1: Add `MatchCaveIns` and thread it through the RPC**

In `game/net/NetworkManager.cs`, add the property after `MatchPits` (line 169):

```csharp
	public bool MatchCaveIns { get; private set; }
```

Change `StartMatch` (line 182) to accept and forward `caveIns`:

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, float baseMoveSeconds)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, baseMoveSeconds, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, baseMoveSeconds, order);
	}
```

Change `BeginMatch` (line 193) to accept and store it:

```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, float baseMoveSeconds, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		MatchPits = pits;
		MatchCaveIns = caveIns;
		MatchBaseMoveSeconds = baseMoveSeconds;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Add the lobby checkbox**

In `game/ui/Lobby.cs`, add a field next to `_pitsCheck` (line 16):

```csharp
	private CheckBox _caveInCheck = null!;
```

Add the checkbox after the `_pitsCheck` block (after line 65):

```csharp
		_caveInCheck = new CheckBox { Text = "Cave-ins" };
		_caveInCheck.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_caveInCheck);
```

Pass its value in the `StartMatch` call (line 76):

```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch(
			(GameMode)_modePicker.GetSelectedId(),
			_timePicker.GetSelectedId(),
			_floodCheck.ButtonPressed,
			_pitsCheck.ButtonPressed,
			_caveInCheck.ButtonPressed,
			new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected]);
```

- [ ] **Step 3: Pass the flag into both map generations**

In `game/Main.cs`, both `MapConfig.For(...)` calls must honor the toggle. The client render map (line 29):

```csharp
		var map = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns));
```

The host sim map (line 47):

```csharp
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns));
```

- [ ] **Step 4: Map the transition events to TileChanges**

In `game/net/MatchHost.cs`, add two cases to the event switch (after the `PlankPlaced` case, line 98):

```csharp
				case CrackWeakened cw:
					changes.Add(new TileChange(cw.Pos.X, cw.Pos.Y, false, TileType.Crumbling));
					break;
				case CrackCollapsed cc:
					changes.Add(new TileChange(cc.Pos.X, cc.Pos.Y, false, TileType.Pit));
					break;
```

(`MatchClient.ApplyUpdate` already applies `t.NewType`, so clients render the weakening and the hole with no further change. `MinerCrushed` needs no mapping — the death replicates via `MinerSnapshot.Cause`.)

- [ ] **Step 5: Build and smoke-test**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then, **from PowerShell** (not the Bash tool): `godot --headless --quit-after 5`
Expected: clean exit, no C# exceptions in the output.

- [ ] **Step 6: Commit**

```bash
git add game/net/NetworkManager.cs game/ui/Lobby.cs game/Main.cs game/net/MatchHost.cs
git commit -m "$(printf 'feat(game): host Cave-ins lobby toggle + crack transition sync\n\nThreads bool caveIns through BeginMatch into both peers map-gen, adds the\nhost-only Cave-ins checkbox, and maps CrackWeakened/CrackCollapsed onto\nthe existing TileChange path so clients see the floor weaken and drop.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

### Task 8: Render, audio & death feed (game)

Makes cracks readable, audible, and named on death. **TAB indentation.**

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/audio/SfxLibrary.cs`
- Modify: `game/net/MatchAudio.cs`
- Modify: `game/ui/DeathFeed.cs`

- [ ] **Step 1: Render the crack states**

In `game/WorldRenderer.cs`, add colors after `PitColor` (line 37):

```csharp
	private static readonly Color CrackedColor = new("3a342b");    // floor with thin fractures — subtly off from FloorColor
	private static readonly Color CrumblingColor = new("4a3b28");  // heavier fractures/rubble — visibly "used", warmer
```

Add the two cases to `TargetColor` (after the `TileType.Pit => PitColor,` line 97):

```csharp
		TileType.Cracked => CrackedColor,
		TileType.Crumbling => CrumblingColor,
```

- [ ] **Step 2: Add the cave-in SFX**

In `game/audio/SfxLibrary.cs`, add after the `Fall` entry (line 28):

```csharp
	public static AudioStream CaveIn => Get("cavein", () => Noise(0.45f, 90f, decay: true)); // low rumble — collapsing floor
```

- [ ] **Step 3: Play the rumble on collapse and on Crushed deaths**

In `game/net/MatchAudio.cs`, select the cave-in SFX for a `Crushed` death. In the death-SFX `switch` (line 78), add the `Crushed` arm:

```csharp
				if (prevAlive && !m.Alive)
					OneShot(m.Cause switch
					{
						DeathCause.Drowned => SfxLibrary.Splash,
						DeathCause.Fell    => SfxLibrary.Fall,
						DeathCause.Crushed => SfxLibrary.CaveIn,
						_                  => SfxLibrary.Death,
					}, WorldOf(m.X, m.Y));
```

(The collapse already produces a `MinerCrushed` death the moment a miner falls, so the cave-in rumble fires on every fatal collapse; a separate ambient collapse SFX for empty-tile blast collapses is out of scope for v1 — YAGNI.)

- [ ] **Step 4: Add the Crushed death messages**

In `game/ui/DeathFeed.cs`, add the banner arm in `ShowBanner` (after the `Fell` arm, line 75):

```csharp
			DeathCause.Crushed => "CAVED IN!",
```

And the toast arm in `PushToast` (after the `Fell` arm, line 96):

```csharp
			DeathCause.Crushed => $"{name} was caught in a cave-in",
```

- [ ] **Step 5: Build and smoke-test**

Run: `dotnet build Miner49er.sln`
Expected: `0 Warning(s) 0 Error(s)`.

Then, **from PowerShell**: `godot --headless --quit-after 5`
Expected: clean exit, no C# exceptions.

- [ ] **Step 6: Commit**

```bash
git add game/WorldRenderer.cs game/audio/SfxLibrary.cs game/net/MatchAudio.cs game/ui/DeathFeed.cs
git commit -m "$(printf 'feat(game): render cracks, cave-in rumble, Crushed banner/feed\n\nCracked/Crumbling tile colors (readable weakening), a low-rumble CaveIn\nSFX selected for Crushed deaths, and CAVED IN! banner + "caught in a\ncave-in" kill-feed line.\n\nCo-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>')"
```

---

## After all tasks

- [ ] Run the full Core suite once more: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` — expect all green (228 baseline + the new crack/cave-in tests).
- [ ] `dotnet build Miner49er.sln` — `0 Warning(s) 0 Error(s)`.
- [ ] **Play-test gate before merge** (per project workflow): host a match with the **Cave-ins** toggle on and verify — a fresh crack crosses safely and visibly turns to heavy rubble; pausing on a crack or re-crossing drops you with the "CAVED IN!" banner; a rival's collapse shows "{name} was caught in a cave-in"; a plank bridges a crack; a charge near a crack field opens holes. Tune `CrackDwellSeconds`, `CrackSiteCount`, `CrackPatchMax`, and the two crack colors as needed.
- [ ] Then invoke **superpowers:finishing-a-development-branch** to merge `phase4d2-cave-ins` → `main` (only on explicit user authorization).

## Self-review notes (spec coverage)

- Spec §1 tiles/predicates → Task 1. §1 `DeathCause.Crushed` + events → Task 2.
- Spec §2 collapse mechanics: enter-Crumbling → Task 2; cross-weaken → Task 3; dwell → Task 4; `CollapseKill` vs `KillByTile` → Task 2 (+ existing `Fell` path unchanged).
- Spec §3 blast interaction → Task 5.
- Spec §4 map-gen (`CaveIns`, `PlaceCracks`, `MapConfig.For`, reachability, objective exclusion) → Task 6.
- Spec §5 netcode (toggle threading, `TileChange` mapping, `Crushed` over `Cause` byte) → Task 7 (+ codec test in Task 2).
- Spec §6 render/audio/UI → Task 8.
- Spec §7 tests → distributed across Tasks 1–6 (Core) as TDD.
