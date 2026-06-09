# Phase 4c-1 — Status-Effect Engine & Move-Cadence Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the §3.5 status-effect mechanism and move-speed model in pure-C# Core, migrate movement cadence out of `MatchHost` into `Simulation`, and surface it via a base-speed lobby preset + slide-matching + a throwaway debug key.

**Architecture:** Effects live as a `List<StatusEffect>` on each `Miner`, ticked by `Simulation`. `TryMove` self-gates on a per-miner `MoveCooldownRemaining` set from a clamped effective-seconds-per-tile formula (base × tile-cost × ∏ MoveSpeed magnitudes). `MatchHost` becomes a thin driver; clients render pace from a new `MoveSeconds` snapshot field. Core changes are TDD'd in xUnit; Godot-adapter changes are build- + headless-verified.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, .NET 8, xUnit. Core lib uses 4-space indent; `game/` uses tabs.

**Spec:** `docs/superpowers/specs/2026-06-09-phase4c1-status-effects-cadence-design.md`

---

## File Structure

**Core (`src/Miner49er.Core/`):**
- `Sim/StatusEffect.cs` *(new)* — `EffectChannel`, `EffectKind`, `StatusEffect`.
- `Sim/Miner.cs` — effects list + accessor; `MoveCooldownRemaining`.
- `Sim/SimConfig.cs` — `BaseMoveSeconds`/`MinMoveSeconds`/`MaxMoveSeconds`.
- `Sim/Simulation.cs` — `ApplyEffect`, `EffectiveMoveSeconds`, `AdvanceEffects`, `AdvanceCooldowns`, `TryMove` gate, `Tick` wiring.
- `Net/Snapshots.cs`, `Net/SnapshotCodec.cs`, `Net/SnapshotFactory.cs` — `MoveSeconds` field.

**Godot adapter (`game/`):**
- `net/MatchHost.cs` — drop cadence gate; `ApplyDebugSpeed`.
- `net/MatchClient.cs` — per-miner slide from `MoveSeconds`.
- `net/NetworkManager.cs` — `MatchBaseMoveSeconds`; `StartMatch`/`BeginMatch`; debug RPC.
- `ui/Lobby.cs` — Base Speed picker.
- `Main.cs` — `SimConfig.BaseMoveSeconds`; debug-key edge detect.

**Tests (`src/Miner49er.Core.Tests/`):** `StatusEffectTests.cs` *(new)*, `MovementCadenceTests.cs` *(new)*, extend `SnapshotCodecTests` + `SnapshotFactoryTests`, fix `SimulationExplosiveTests.Charge_cap_blocks_extra_plants`.

**Task order:** 1 → 2 → 3 are Core (each independently green); 4 → 5 → 6 are Godot adapter (build + headless). Do them in order: 4 depends on 3's `MoveSeconds`; 5 and 6 depend on 4's thin `MatchHost`.

---

### Task 1: Status-effect engine (Core)

**Files:**
- Create: `src/Miner49er.Core/Sim/StatusEffect.cs`
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test (create): `src/Miner49er.Core.Tests/StatusEffectTests.cs`

> Note: `EffectKind` includes a second value `DebugSlow` beyond the spec's lone `DebugSpeed`. It is a **test-only** debug kind — no key is bound to it — and exists solely to validate the "different kinds coexist / multiply" rules in 4c-1. 4c-2 replaces both debug kinds with real item kinds.

- [ ] **Step 1: Create the status-effect types**

Create `src/Miner49er.Core/Sim/StatusEffect.cs`:

```csharp
namespace Miner49er.Core;

public enum EffectChannel { MoveSpeed }            // 4c-2 adds MiningSpeed, VisionRadius, …
public enum EffectKind { DebugSpeed, DebugSlow }   // 4c-2 replaces these with SpeedPotion, SlowMold, …

public sealed class StatusEffect
{
    public EffectKind Kind { get; internal set; }
    public EffectChannel Channel { get; internal set; }
    public double Magnitude { get; internal set; }       // MoveSpeed: <1 faster, >1 slower
    public double RemainingSeconds { get; internal set; }
}
```

- [ ] **Step 2: Add the effects list to `Miner`**

In `src/Miner49er.Core/Sim/Miner.cs`, add these members inside the `Miner` class (after `ActivitySecondsRemaining`):

```csharp
    private readonly List<StatusEffect> _effects = new();
    public IReadOnlyList<StatusEffect> Effects => _effects;
    internal List<StatusEffect> EffectsInternal => _effects;
```

(The Core project has ImplicitUsings enabled — `System.Collections.Generic` is already in scope, matching the existing namespace-only file style.)

- [ ] **Step 3: Write the failing tests**

Create `src/Miner49er.Core.Tests/StatusEffectTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class StatusEffectTests
{
    private static Simulation Sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        return sim;
    }

    [Fact]
    public void ApplyEffect_adds_an_active_effect()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
        var e = Assert.Single(sim.GetMiner(1).Effects);
        Assert.Equal(EffectKind.DebugSpeed, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
        Assert.Equal(0.6, e.Magnitude, 3);
        Assert.Equal(5.0, e.RemainingSeconds, 3);
    }

    [Fact]
    public void Tick_expires_an_effect_after_its_duration()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 1.0);
        sim.Tick(1.1);
        Assert.Empty(sim.GetMiner(1).Effects);
    }

    [Fact]
    public void Tick_keeps_an_unexpired_effect_and_decrements_it()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 2.0);
        sim.Tick(0.5);
        var e = Assert.Single(sim.GetMiner(1).Effects);
        Assert.Equal(1.5, e.RemainingSeconds, 3);
    }

    [Fact]
    public void Reapplying_same_kind_refreshes_without_compounding()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 2.0);
        sim.Tick(1.5); // 0.5 left
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 2.0); // refresh
        var e = Assert.Single(sim.GetMiner(1).Effects); // still a single instance
        Assert.Equal(2.0, e.RemainingSeconds, 3);       // refreshed to full
    }

    [Fact]
    public void Different_kinds_coexist_as_separate_instances()
    {
        var sim = Sim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
        sim.ApplyEffect(1, EffectKind.DebugSlow,  EffectChannel.MoveSpeed, 1.8, 5.0);
        Assert.Equal(2, sim.GetMiner(1).Effects.Count);
    }

    [Fact]
    public void ApplyEffect_on_a_dead_miner_is_a_noop()
    {
        var sim = Sim();
        sim.KillMiner(1);
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
        Assert.Empty(sim.GetMiner(1).Effects);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~StatusEffectTests"`
Expected: FAIL — `Simulation` has no `ApplyEffect` (compile error).

- [ ] **Step 5: Implement `ApplyEffect` + `AdvanceEffects` and wire into `Tick`**

In `src/Miner49er.Core/Sim/Simulation.cs`, add these methods (place `ApplyEffect` near `KillMiner`; `AdvanceEffects` near the other `Advance*` helpers):

```csharp
    public void ApplyEffect(int minerId, EffectKind kind, EffectChannel channel,
        double magnitude, double durationSeconds)
    {
        var m = _miners[minerId];
        if (!m.Alive) return;
        var existing = m.EffectsInternal.FirstOrDefault(e => e.Kind == kind);
        if (existing is not null)
        {
            existing.Channel = channel;
            existing.Magnitude = magnitude;
            existing.RemainingSeconds = durationSeconds;   // refresh, never compound
        }
        else
        {
            m.EffectsInternal.Add(new StatusEffect
            {
                Kind = kind, Channel = channel,
                Magnitude = magnitude, RemainingSeconds = durationSeconds,
            });
        }
    }

    private void AdvanceEffects(double dt)
    {
        foreach (var m in _miners.Values)
        {
            var fx = m.EffectsInternal;
            for (int i = fx.Count - 1; i >= 0; i--)
            {
                fx[i].RemainingSeconds -= dt;
                if (fx[i].RemainingSeconds <= 0) fx.RemoveAt(i);
            }
        }
    }
```

Then add `AdvanceEffects(dt);` as the first call in `Tick` (right after `Elapsed += dt;`):

```csharp
    public void Tick(double dt)
    {
        Elapsed += dt;
        AdvanceEffects(dt);
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        AdvanceCharges(chargesThisTick, dt);
        AdvanceFlood();
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~StatusEffectTests"`
Expected: PASS (6/6).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/StatusEffect.cs src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/StatusEffectTests.cs
git commit -m "feat(core): generic timed status-effect mechanism on Miner"
```

---

### Task 2: Move-cadence migration into the sim (Core)

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Miner.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test (create): `src/Miner49er.Core.Tests/MovementCadenceTests.cs`
- Test (fix): `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`

- [ ] **Step 1: Add cadence config to `SimConfig`**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add inside the class:

```csharp
    public double BaseMoveSeconds { get; set; } = 0.12;  // Standard preset (seconds per tile)
    public double MinMoveSeconds { get; set; } = 0.05;   // clamp floor — no teleporting
    public double MaxMoveSeconds { get; set; } = 0.40;   // clamp ceiling — never frozen
```

- [ ] **Step 2: Add the per-miner cooldown field**

In `src/Miner49er.Core/Sim/Miner.cs`, add (after `ActivitySecondsRemaining`):

```csharp
    public double MoveCooldownRemaining { get; internal set; }
```

- [ ] **Step 3: Write the failing tests**

Create `src/Miner49er.Core.Tests/MovementCadenceTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class MovementCadenceTests
{
    private static Simulation FloorSim(SimConfig? cfg = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg ?? new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        return sim;
    }

    [Fact]
    public void Second_move_within_cooldown_is_rejected()
    {
        var sim = FloorSim();
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.False(sim.TryMove(1, Direction.East));         // cooldown still active
        Assert.Equal(new GridPos(3, 2), sim.GetMiner(1).Pos); // did not advance
    }

    [Fact]
    public void Move_allowed_again_after_cooldown_elapses()
    {
        var sim = FloorSim();
        sim.TryMove(1, Direction.East);
        sim.Tick(0.2); // > 0.12 standard cadence
        Assert.True(sim.TryMove(1, Direction.East));
        Assert.Equal(new GridPos(4, 2), sim.GetMiner(1).Pos);
    }

    [Fact]
    public void Standard_floor_cadence_equals_base_move_seconds()
    {
        var sim = FloorSim();
        sim.TryMove(1, Direction.East);
        Assert.Equal(0.12, sim.GetMiner(1).MoveCooldownRemaining, 3);
    }

    [Fact]
    public void Shallow_water_doubles_the_cadence()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.ShallowWater);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.TryMove(1, Direction.East); // onto shallow (3,2)
        Assert.Equal(0.24, sim.GetMiner(1).MoveCooldownRemaining, 3); // 0.12 * 2.0
    }

    [Fact]
    public void Speed_effect_reduces_the_cadence()
    {
        var sim = FloorSim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
        sim.TryMove(1, Direction.East);
        Assert.Equal(0.072, sim.GetMiner(1).MoveCooldownRemaining, 3); // 0.12 * 0.6
    }

    [Fact]
    public void Two_move_speed_effects_multiply()
    {
        var sim = FloorSim();
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.5, 5.0);
        sim.ApplyEffect(1, EffectKind.DebugSlow,  EffectChannel.MoveSpeed, 1.5, 5.0);
        Assert.Equal(0.09, sim.EffectiveMoveSeconds(1), 3); // 0.12 * 0.5 * 1.5
    }

    [Fact]
    public void Cadence_is_clamped_to_min()
    {
        var sim = FloorSim(new SimConfig { MinMoveSeconds = 0.05 });
        sim.ApplyEffect(1, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.1, 5.0); // 0.012 < 0.05
        Assert.Equal(0.05, sim.EffectiveMoveSeconds(1), 3);
    }

    [Fact]
    public void Cadence_is_clamped_to_max()
    {
        var sim = FloorSim(new SimConfig { MaxMoveSeconds = 0.40 });
        sim.ApplyEffect(1, EffectKind.DebugSlow, EffectChannel.MoveSpeed, 5.0, 5.0); // 0.6 > 0.40
        Assert.Equal(0.40, sim.EffectiveMoveSeconds(1), 3);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MovementCadenceTests"`
Expected: FAIL — `Simulation` has no `EffectiveMoveSeconds`, and `TryMove` does not gate (compile + assertion failures).

- [ ] **Step 5: Implement the formula, the gate, and the cooldown decrement**

In `src/Miner49er.Core/Sim/Simulation.cs`:

(a) Add the formula (place near `ApplyEffect`):

```csharp
    public double EffectiveMoveSeconds(int minerId) => EffectiveMoveSeconds(_miners[minerId]);

    private double EffectiveMoveSeconds(Miner m)
    {
        double mult = 1.0;
        foreach (var e in m.EffectsInternal)
            if (e.Channel == EffectChannel.MoveSpeed) mult *= e.Magnitude;
        double tile = Grid.Get(m.Pos).MoveCostMultiplier();   // shallow water = ×2
        return Math.Clamp(Config.BaseMoveSeconds * tile * mult,
                          Config.MinMoveSeconds, Config.MaxMoveSeconds);
    }
```

(b) In `TryMove`, add the gate as the first check after the alive check, and set the cooldown on success. The method becomes:

```csharp
    public bool TryMove(int id, Direction dir)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
        if (m.MoveCooldownRemaining > 0) return false;   // gate before facing/activity

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

        if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive)
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }

        m.MoveCooldownRemaining = EffectiveMoveSeconds(m);   // set from destination tile
        return true;
    }
```

(c) Add the cooldown decrement helper (near `AdvanceEffects`):

```csharp
    private void AdvanceCooldowns(double dt)
    {
        foreach (var m in _miners.Values)
            if (m.MoveCooldownRemaining > 0)
                m.MoveCooldownRemaining = Math.Max(0, m.MoveCooldownRemaining - dt);
    }
```

(d) Call it in `Tick`, right after `AdvanceEffects(dt);`:

```csharp
        Elapsed += dt;
        AdvanceEffects(dt);
        AdvanceCooldowns(dt);
```

- [ ] **Step 6: Run the cadence tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MovementCadenceTests"`
Expected: PASS (8/8).

- [ ] **Step 7: Run the full suite to surface the gate regression**

Run: `dotnet test`
Expected: one FAIL — `SimulationExplosiveTests.Charge_cap_blocks_extra_plants` (its `TryMove(East); TryMove(East);` chains are now blocked by the cooldown gate, so charges land at the wrong walls / count is off).

- [ ] **Step 8: Fix the one regressed test**

In `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`, replace the body of `Charge_cap_blocks_extra_plants` (the lines from the first `sim.TryMove` through the two asserts) with a version that ticks between chained moves to clear the cooldown (PlantSeconds=0; FuseSeconds defaults to 3.0, and the cumulative ticks stay well under it so both charges persist):

```csharp
        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.2);
        sim.TryMove(1, Direction.East); sim.Tick(0.2);
        sim.TryMove(1, Direction.East); sim.Tick(0.2);
        sim.TryMove(1, Direction.North); sim.TryStartPlanting(1); sim.Tick(0.2);
        sim.TryMove(1, Direction.East); sim.Tick(0.2);
        sim.TryMove(1, Direction.East); sim.Tick(0.2);
        sim.TryMove(1, Direction.North);
        Assert.False(sim.TryStartPlanting(1));
        Assert.Equal(2, sim.Charges.Count);
```

- [ ] **Step 9: Run the full suite to verify green**

Run: `dotnet test`
Expected: PASS (all — prior 122 + 6 status + 8 cadence = 136).

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/MovementCadenceTests.cs src/Miner49er.Core.Tests/SimulationExplosiveTests.cs
git commit -m "feat(core): move cadence into Simulation with effective-speed formula"
```

---

### Task 3: `MoveSeconds` on the snapshot (Core)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test (modify): `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`
- Test (modify): `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

- [ ] **Step 1: Write the failing tests**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, update the two `MinerSnapshot` constructors in `Round_trips_all_fields` to add a 9th positional arg and assert it round-trips. Replace the `Miners` list and add one assertion:

```csharp
                Miners: new List<MinerSnapshot>
                {
                    new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09),
                    new(2, 9, 0, 0, false, 0, 0, 0.0, 0.24),
                },
```

and after the existing `Assert.Equal(42.5f, back.Snapshot.SecondsRemaining);` line add:

```csharp
        Assert.Equal(0.09, back.Snapshot.Miners[0].MoveSeconds, 3);
```

In `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`, add a new test:

```csharp
    [Fact]
    public void Captures_effective_move_seconds()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(0.12, Assert.Single(snap.Miners).MoveSeconds, 3); // standard floor cadence
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SnapshotCodecTests|FullyQualifiedName~SnapshotFactoryTests"`
Expected: FAIL — `MinerSnapshot` has no `MoveSeconds` (compile error).

- [ ] **Step 3: Add `MoveSeconds` to the snapshot record**

In `src/Miner49er.Core/Net/Snapshots.cs`, change `MinerSnapshot` to:

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds);
```

- [ ] **Step 4: Serialize the new field**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`:

In `Write`, inside the miners loop, after `w.Write(m.Activity); w.Write(m.ActivityRemaining);` add:

```csharp
            w.Write(m.MoveSeconds);
```

In `Read`, change the `miners.Add(new MinerSnapshot(...))` call to read one more `double` at the end:

```csharp
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble()));
```

- [ ] **Step 5: Populate it in the factory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, change the `MinerSnapshot` projection to pass the effective pace:

```csharp
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id)))
            .ToList();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SnapshotCodecTests|FullyQualifiedName~SnapshotFactoryTests"`
Expected: PASS.

- [ ] **Step 7: Run the full suite + build**

Run: `dotnet test`
Expected: PASS (all).
Run: `dotnet build`
Expected: 0 errors. (Note: `game/net/MatchClient.cs` still compiles — it ignores `MoveSeconds` until Task 4.)

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(net): snapshot carries each miner's effective move-seconds"
```

---

### Task 4: Thin `MatchHost` + slide-matching `MatchClient` (Godot)

**Files:**
- Modify: `game/net/MatchHost.cs`
- Modify: `game/net/MatchClient.cs`

> Godot adapter — not unit-tested. Verify via build + headless run.

- [ ] **Step 1: Confirm nothing else depends on the symbols being removed**

Run: `git -C D:/Projects/Miner49er grep -n "MoveStepSeconds\|MoveSpeedPixels\|_moveCooldown"`
Expected: references only in `game/net/MatchHost.cs` and `game/net/MatchClient.cs`. If any other file references them, update it too in this task.

- [ ] **Step 2: Strip the cadence gate from `MatchHost`**

In `game/net/MatchHost.cs`:

Delete the constant `public const double MoveStepSeconds = 0.12; // grid cadence; matches Phase 1 feel` and the field `private readonly Dictionary<int, double> _moveCooldown = new();`.

In `Begin`, delete the line `_moveCooldown[miner] = 0;` (leave the rest of the loop body).

In `StepOnce`, delete the cooldown-decrement block:

```csharp
            foreach (var id in _moveCooldown.Keys.ToList())
                _moveCooldown[id] = Mathf.Max(0, (float)(_moveCooldown[id] - TickSeconds));
```

and replace the move-application block:

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

with the thin driver (the sim self-gates):

```csharp
            foreach (var (minerId, dir) in _pendingDir)
            {
                if (dir < 0) continue;
                _sim.TryMove(minerId, (Direction)dir);
            }
```

- [ ] **Step 3: Drive the client slide from `MoveSeconds`**

In `game/net/MatchClient.cs`:

Delete the static field:

```csharp
	public static readonly float MoveSpeedPixels = TileSize / (float)MatchHost.MoveStepSeconds;
```

In `_PhysicsProcess`, replace the `MoveToward` line to use each miner's authoritative pace:

```csharp
		foreach (var m in _miners)
		{
			var target = new Vector2(m.X * TileSize + TileSize / 2f, m.Y * TileSize + TileSize / 2f);
			var cur = _visualPos.TryGetValue(m.Id, out var v) ? v : target;
			float pixelsPerSec = TileSize / (float)m.MoveSeconds;
			_visualPos[m.Id] = cur.MoveToward(target, pixelsPerSec * (float)delta);

			if (m.Id == LocalMinerId)
				_camera.Position = _visualPos[m.Id];
		}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Headless smoke-run**

Run: `godot --headless --quit-after 180`
Expected: exits 0, no `ERROR` lines.

- [ ] **Step 6: Commit**

```bash
git add game/net/MatchHost.cs game/net/MatchClient.cs
git commit -m "refactor(game): MatchHost drives self-gating sim; client slides at authoritative pace"
```

---

### Task 5: Base-speed lobby preset (Godot)

**Files:**
- Modify: `game/ui/Lobby.cs`
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/Main.cs`

- [ ] **Step 1: Add `MatchBaseMoveSeconds` and thread it through the RPCs**

In `game/net/NetworkManager.cs`:

Add the property (next to `MatchFlooding`):

```csharp
	public float MatchBaseMoveSeconds { get; private set; } = 0.12f;
```

Change `StartMatch` to take and forward the preset:

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, float baseMoveSeconds)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, baseMoveSeconds, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, baseMoveSeconds, order); // host applies locally too
	}
```

Change `BeginMatch` to accept and store it:

```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, float baseMoveSeconds, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		MatchBaseMoveSeconds = baseMoveSeconds;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Add the lobby picker**

In `game/ui/Lobby.cs`:

Add the field (next to `_floodCheck`):

```csharp
	private OptionButton _speedPicker = null!;
```

After the `_floodCheck` block in `_Ready` (before `_startBtn`), add:

```csharp
		_speedPicker = new OptionButton();
		_speedPicker.AddItem("Slow", 0);
		_speedPicker.AddItem("Standard", 1);
		_speedPicker.AddItem("Fast", 2);
		_speedPicker.Select(1); // default Standard
		_speedPicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_speedPicker);
```

Change the Start handler to pass the preset's seconds (index → seconds map):

```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch(
			(GameMode)_modePicker.GetSelectedId(),
			_timePicker.GetSelectedId(),
			_floodCheck.ButtonPressed,
			new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected]);
```

- [ ] **Step 3: Build the host sim with the chosen base speed**

In `game/Main.cs`, change the host `Simulation` construction to pass a configured `SimConfig`:

```csharp
				var sim = new Simulation(
					MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount)).Grid,
					new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds },
					map.Center,
					nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
					nm.MatchFlooding);
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Headless smoke-run**

Run: `godot --headless --quit-after 180`
Expected: exits 0, no `ERROR` lines.

- [ ] **Step 6: Commit**

```bash
git add game/ui/Lobby.cs game/net/NetworkManager.cs game/Main.cs
git commit -m "feat(game): host base-speed preset (Slow/Standard/Fast) threads into the sim"
```

---

### Task 6: Temporary debug keybind (Godot, throwaway)

**Files:**
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/net/MatchHost.cs`
- Modify: `game/Main.cs`

> Every line added here is tagged `// DEBUG(4c-1): remove in 4c-2`.

- [ ] **Step 1: Host-side effect application on `MatchHost`**

In `game/net/MatchHost.cs`, add a public method (e.g. after `EliminatePeer`):

```csharp
	// DEBUG(4c-1): remove in 4c-2
	public void ApplyDebugSpeed(long peerId)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId))
			_sim.ApplyEffect(minerId, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
	}
```

- [ ] **Step 2: Transport on `NetworkManager`**

In `game/net/NetworkManager.cs`, add (e.g. near `SendAction`/`ReceiveAction`):

```csharp
	// DEBUG(4c-1): remove in 4c-2
	public void SendDebugSpeed()
	{
		if (IsHost) { _matchHost?.ApplyDebugSpeed(LocalId); return; }
		RpcId(1, nameof(ReceiveDebugSpeed));
	}

	// DEBUG(4c-1): remove in 4c-2
	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveDebugSpeed() => _matchHost?.ApplyDebugSpeed(Multiplayer.GetRemoteSenderId());
```

- [ ] **Step 3: Edge-detected key in `Main`**

In `game/Main.cs`, add the latch field (with the other fields near `_wasListening`):

```csharp
	private bool _debugBoostPressed; // DEBUG(4c-1): remove in 4c-2
```

In `_PhysicsProcess`, add at the end of the method (after the mute check):

```csharp
		// DEBUG(4c-1): remove in 4c-2 — press B to self-apply a ×0.6 speed buff for 5s
		bool boost = Input.IsPhysicalKeyPressed(Key.B);
		if (boost && !_debugBoostPressed) NetworkManager.Instance.SendDebugSpeed();
		_debugBoostPressed = boost;
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Headless smoke-run**

Run: `godot --headless --quit-after 180`
Expected: exits 0, no `ERROR` lines.

- [ ] **Step 6: Commit**

```bash
git add game/net/NetworkManager.cs game/net/MatchHost.cs game/Main.cs
git commit -m "feat(game): DEBUG(4c-1) B-key self-applies a speed buff to exercise the engine"
```

---

## Final verification (before play-test handoff)

- [ ] `dotnet test` — all green (expect 137: prior 122 + 6 status + 8 cadence + 1 factory).
- [ ] `dotnet build` — 0 errors, 0 warnings.
- [ ] `godot --headless --quit-after 180` — exits 0, no `ERROR` lines.
- [ ] Hand to user for the play-test gate (per the established workflow): three base-speed presets feel distinct; the on-screen glide matches pace (notably slower through shallow water); the `B` key gives a clear ~5 s speed burst that refreshes on re-press; the lobby controls are all on-screen (the layout fix already committed at `3d5505d`). Then run finishing-a-development-branch.

---

## Notes / carry-forward to 4c-2

- The `EffectKind.DebugSlow` value and the entire §7 debug path (B key, `SendDebugSpeed`/`ReceiveDebugSpeed`, `ApplyDebugSpeed`) are throwaway — remove when real item kinds land.
- 4c-2 adds `EffectChannel.MiningSpeed` / `VisionRadius` and their consumers (e.g. client fog reading a VisionRadius effect; mining duration reading a MiningSpeed effect), plus item entities, deterministic map placement, and the pickup/use verb.
- The base-speed preset's `0.20 / 0.12 / 0.07` and the `Min/MaxMoveSeconds` clamps (`0.05 / 0.40`) are tunable starting values.
