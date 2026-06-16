# Solo Expedition — Phase 1 (Core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the deterministic Core for the single-player Expedition mode — a `Monster` entity with three distinct AIs, monster/miner contact death, hazard- and blast-kills of monsters, all-gold-cleared escape opening, and the `Expedition` win/loss resolver — all headless-testable, no rendering.

**Architecture:** Monsters are a new mobile entity (`List<Monster>`) inside `Simulation`, advanced by a new `AdvanceMonsters(dt)` step in `Tick` (between lava and activities). A single seeded `Random` (seed from `SimConfig.Seed`) drives all monster randomness, consumed in ascending-`Id` order, keeping the sim fully deterministic. Spawn-time tile counts track remaining gold; clearing the last vein opens the escape tile. `RoundResolver` special-cases `Expedition` so the universal last-man-standing rule doesn't auto-win a solo run.

**Tech Stack:** C# / .NET 8, `Miner49er.Core` (4-space indent), xUnit. Build `dotnet build Miner49er.sln`; test `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.

**Reference (verified during planning):**
- `Direction.ToOffset()`: North `(0,-1)`, East `(1,0)`, South `(0,1)`, West `(-1,0)`.
- `TileType.IsEnterable()` = floor/water/plank/pit/crack/lava(/vent) — **rock is NOT enterable**; `IsLethal()` = DeepWater/Pit/Lava/LavaVent.
- `Simulation` ctor today: `Simulation(TileGrid grid, SimConfig config, GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false)`.
- `Simulation` has a private `static readonly Direction[] Card = { North, East, South, West }`.
- `Tick(dt)` order today: effects → molds → cooldowns → cracks → lava → (snapshot charges) → activities → pickups → charges → flood.
- `GridPos` has `ManhattanTo` and `ChebyshevTo` (no Euclidean).
- Tests construct e.g. `new Simulation(new TileGrid(w,h,TileType.Floor), new SimConfig())` and drive with `sim.Tick(dt)` / `sim.DrainEvents()`.

---

### Task 1: Monster entity, kinds, seeded RNG, registration

**Files:**
- Create: `src/Miner49er.Core/Sim/Monster.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs` (add `Seed` + monster tuning)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (add `_rng`, `_monsters`, `Monsters`, `AddMonster`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMonsterTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    [Fact]
    public void AddMonster_registers_a_living_monster()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor));
        var mo = sim.AddMonster(1, new GridPos(2, 2), MonsterKind.Slime);

        Assert.True(mo.Alive);
        Assert.Equal(MonsterKind.Slime, mo.Kind);
        Assert.Single(sim.Monsters);
        Assert.Equal(new GridPos(2, 2), sim.Monsters[0].Pos);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL to compile — `MonsterKind`, `Monster`, `AddMonster`, `Monsters` do not exist.

- [ ] **Step 3: Create the Monster entity**

Create `src/Miner49er.Core/Sim/Monster.cs`:

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

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
    }
}
```

- [ ] **Step 4: Add config (seed + monster tuning)**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the existing fields (before the closing brace):

```csharp
    // Determinism: seeds the sim's monster RNG (wander steps, goat re-aim).
    public int Seed { get; set; }

    // Monsters (Expedition). Cadence = seconds per one-tile step; lower is faster.
    public double MonsterSlimeMoveSeconds { get; set; } = 0.5;
    public double MonsterGhostMoveSeconds { get; set; } = 0.35;
    public double MonsterGoatMoveSeconds { get; set; } = 0.15;
    public int MonsterSenseRadius { get; set; } = 6;   // Manhattan range at which a monster locks on
```

- [ ] **Step 5: Wire the sim — RNG, collection, AddMonster**

In `src/Miner49er.Core/Sim/Simulation.cs`:

Add the fields near the other `private readonly List<...>` fields:

```csharp
    private readonly List<Monster> _monsters = new();
    private readonly Random _rng;
```

Add the accessor near `public IReadOnlyList<MoldPatch> Molds => _molds;`:

```csharp
    public IReadOnlyList<Monster> Monsters => _monsters;
```

In the constructor body (anywhere after the parameters are in scope; e.g. just before the LavaVent loop), add:

```csharp
        _rng = new Random(config.Seed);
```

Add the factory method near `AddMiner`:

```csharp
    public Monster AddMonster(int id, GridPos pos, MonsterKind kind)
    {
        var mo = new Monster(id, pos, kind) { MoveCooldownRemaining = MonsterCadence(kind) };
        _monsters.Add(mo);
        return mo;
    }

    private double MonsterCadence(MonsterKind kind) => kind switch
    {
        MonsterKind.Slime => Config.MonsterSlimeMoveSeconds,
        MonsterKind.Ghost => Config.MonsterGhostMoveSeconds,
        MonsterKind.Goat  => Config.MonsterGoatMoveSeconds,
        _ => Config.MonsterSlimeMoveSeconds,
    };
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/Monster.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): Monster entity + seeded RNG scaffolding for Expedition"
```
(End the commit message with the required `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.)

---

### Task 2: Slime AI — wander, chase when near, blocked by rock

**Files:**
- Create: `src/Miner49er.Core/Sim/SimEvent.cs` entry `MonsterMoved` (modify file)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`AdvanceMonsters`, `StepMonster`, `SlimeDir`, helpers, Tick wiring)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Slime_steps_toward_the_miner_when_within_sense_radius()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var sim = Sim(new TileGrid(9, 3, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);   // cooldown started at cadence (0.1) -> elapses this tick -> one step

        Assert.Equal(new GridPos(3, 1), slime.Pos);   // moved east, toward the miner
    }

    [Fact]
    public void Slime_is_blocked_by_rock()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);     // wall east of the slime
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));             // miner is east, slime wants to go east
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(2, 1), slime.Pos);     // rock blocked the step; stayed put
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL — slime does not move (no `AdvanceMonsters` yet).

- [ ] **Step 3: Add the `MonsterMoved` event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add:

```csharp
public sealed record MonsterMoved(int MonsterId, GridPos From, GridPos To) : SimEvent;
```

- [ ] **Step 4: Implement `AdvanceMonsters` and the slime**

In `src/Miner49er.Core/Sim/Simulation.cs`, add `AdvanceMonsters(dt)` to `Tick` immediately after `AdvanceLava(dt);` and before `var chargesThisTick = _charges.ToList();`:

```csharp
        AdvanceLava(dt);
        AdvanceMonsters(dt);
```

Add the methods (near `AdvanceLava`):

```csharp
    private void AdvanceMonsters(double dt)
    {
        if (_monsters.Count == 0) return;

        // Single-player: the lone living miner is the target. OrderBy(Id) keeps both the
        // target choice and the monster step order deterministic.
        Miner? target = _miners.Values.Where(m => m.Alive).OrderBy(m => m.Id).FirstOrDefault();

        foreach (var mo in _monsters.OrderBy(x => x.Id))
        {
            if (!mo.Alive) continue;
            mo.MoveCooldownRemaining -= dt;
            if (mo.MoveCooldownRemaining > 0) continue;
            mo.MoveCooldownRemaining += MonsterCadence(mo.Kind);   // += preserves sub-tick remainder
            StepMonster(mo, target);
        }
    }

    private void StepMonster(Monster mo, Miner? target)
    {
        Direction? dir = mo.Kind switch
        {
            MonsterKind.Slime => SlimeDir(mo, target),
            MonsterKind.Ghost => GhostDir(mo, target),
            MonsterKind.Goat  => GoatDir(mo, target),
            _ => null,
        };
        if (dir is not { } d) return;

        var next = mo.Pos + d.ToOffset();
        if (!CanMonsterEnter(mo, next)) return;

        var from = mo.Pos;
        mo.Pos = next;
        mo.Facing = d;
        _events.Add(new MonsterMoved(mo.Id, from, next));
    }

    // Rock blocks terrain-bound monsters; a ghost phases through anything in bounds.
    private bool CanMonsterEnter(Monster mo, GridPos p)
    {
        if (!Grid.InBounds(p)) return false;
        if (mo.Kind == MonsterKind.Ghost) return true;
        return Grid.Get(p).IsEnterable();
    }

    private Direction? SlimeDir(Monster mo, Miner? target)
    {
        if (target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius)
            return TowardDir(mo.Pos, target.Pos);
        return Card[_rng.Next(Card.Length)];
    }

    // Stubs filled in by later tasks; returning null = no move this step.
    private Direction? GhostDir(Monster mo, Miner? target) => null;
    private Direction? GoatDir(Monster mo, Miner? target) => null;

    // Greedy cardinal step that most reduces Manhattan distance (X ties broken to vertical).
    private static Direction TowardDir(GridPos from, GridPos to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? Direction.East
                 : dx < 0 ? Direction.West
                 : dy > 0 ? Direction.South : Direction.North;
        return dy > 0 ? Direction.South : Direction.North;
    }
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS (both slime tests).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): slime AI - wander, chase when near, blocked by rock"
```
(Co-Authored-By trailer.)

---

### Task 3: Ghost AI — phases through rock, relentless

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`GhostDir`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Ghost_drifts_through_rock_toward_the_miner()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);     // solid wall between ghost and miner
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

        sim.Tick(0.1);   // steps east into the rock tile (phasing)

        Assert.Equal(new GridPos(3, 1), ghost.Pos);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL — ghost stays at `(2,1)` (`GhostDir` returns null).

- [ ] **Step 3: Implement `GhostDir`**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the `GhostDir` stub with:

```csharp
    private Direction? GhostDir(Monster mo, Miner? target)
    {
        if (target is not { Alive: true }) return null;
        return TowardDir(mo.Pos, target.Pos);   // always hunts; CanMonsterEnter lets it phase rock
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): ghost AI - phases through rock, hunts relentlessly"
```
(Co-Authored-By trailer.)

---

### Task 4: Goat AI — charge in a straight line, re-aim at walls

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`GoatDir`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Goat_charges_in_a_straight_line()
    {
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 0 };
        var sim = Sim(new TileGrid(6, 3, TileType.Floor), cfg);
        var goat = sim.AddMonster(1, new GridPos(1, 1), MonsterKind.Goat);
        goat.ChargeDir = Direction.East;

        sim.Tick(0.1);
        sim.Tick(0.1);

        Assert.Equal(new GridPos(3, 1), goat.Pos);   // two straight steps east
    }

    [Fact]
    public void Goat_reaims_toward_the_miner_when_it_hits_a_wall()
    {
        // A miner due south makes the re-aim deterministic (toward = South), avoiding the
        // randomness of a wall-bounce with no target in range.
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(4, 4, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);   // wall directly east of the goat
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 3));           // due south of the goat
        var goat = sim.AddMonster(1, new GridPos(1, 1), MonsterKind.Goat);
        goat.ChargeDir = Direction.East;

        sim.Tick(0.1);   // east is blocked: re-aims toward the miner, does not move this step

        Assert.Equal(new GridPos(1, 1), goat.Pos);
        Assert.Equal(Direction.South, goat.ChargeDir);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL — goat does not move / does not re-aim (`GoatDir` returns null).

- [ ] **Step 3: Implement `GoatDir`**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the `GoatDir` stub with:

```csharp
    private Direction? GoatDir(Monster mo, Miner? target)
    {
        var ahead = mo.Pos + mo.ChargeDir.ToOffset();
        if (CanMonsterEnter(mo, ahead)) return mo.ChargeDir;

        // Slammed into a wall — turn (toward the miner if sensed, else random) and skip this step.
        mo.ChargeDir = target is { Alive: true } && mo.Pos.ManhattanTo(target.Pos) <= Config.MonsterSenseRadius
            ? TowardDir(mo.Pos, target.Pos)
            : Card[_rng.Next(Card.Length)];
        return null;
    }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): goat AI - straight-line charge, re-aim on wall hit"
```
(Co-Authored-By trailer.)

---

### Task 5: Contact kills the miner (both directions)

**Files:**
- Modify: `src/Miner49er.Core/Sim/DeathCause.cs` (add `Mauled`)
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `MinerMauled`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`MaulMiner`, contact in `StepMonster` and `TryMove`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Monster_stepping_onto_the_miner_mauls_them()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var sim = Sim(new TileGrid(5, 3, TileType.Floor), cfg);
        var miner = sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);   // one step east = onto the miner

        sim.Tick(0.1);

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Mauled, miner.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerMauled mm && mm.MinerId == 1);
    }

    [Fact]
    public void Miner_walking_into_a_monster_is_mauled()
    {
        var sim = Sim(new TileGrid(5, 3, TileType.Floor));
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);   // miner steps east into it

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Mauled, miner.DeathCause);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL to compile — `DeathCause.Mauled` / `MinerMauled` missing.

- [ ] **Step 3: Add the death cause and event**

In `src/Miner49er.Core/Sim/DeathCause.cs`, extend the enum:

```csharp
public enum DeathCause { None, Drowned, Exploded, Left, Fell, Crushed, Burned, Mauled }
```

In `src/Miner49er.Core/Sim/SimEvent.cs`, add:

```csharp
public sealed record MinerMauled(int MinerId) : SimEvent;
```

- [ ] **Step 4: Implement the kills**

In `src/Miner49er.Core/Sim/Simulation.cs`, add the helper (near `KillByTile`):

```csharp
    private void MaulMiner(Miner m)
    {
        if (!m.Alive) return;
        m.Alive = false;
        m.Activity = ActivityKind.None;
        m.DeathCause = DeathCause.Mauled;
        _events.Add(new MinerMauled(m.Id));
    }
```

In `StepMonster`, after the `_events.Add(new MonsterMoved(...))` line, append the contact check:

```csharp
        _events.Add(new MonsterMoved(mo.Id, from, next));

        if (target is { Alive: true } && mo.Pos == target.Pos)
            MaulMiner(target);
```

In `TryMove`, after the existing crumbling-collapse block and before the `if (Center is { } c ...)` block, add:

```csharp
        if (m.Alive && _monsters.Any(mo => mo.Alive && mo.Pos == target))
            MaulMiner(m);
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/DeathCause.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): monster contact kills the miner (Mauled)"
```
(Co-Authored-By trailer.)

---

### Task 6: Hazards kill monsters (ghost is immune)

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `MonsterKilled`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (hazard check in `StepMonster`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Slime_chasing_across_a_pit_falls_in_and_dies()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Pit);      // pit between slime and miner
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);   // steps east onto the pit
        // first step takes it to (3,1) the pit; needs only that one step
        Assert.False(slime.Alive);
        Assert.Equal(new GridPos(3, 1), slime.Pos);
        Assert.Contains(sim.DrainEvents(), e => e is MonsterKilled mk && mk.MonsterId == 1);
    }

    [Fact]
    public void Ghost_floats_over_a_pit_unharmed()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Pit);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

        sim.Tick(0.1);   // drifts east onto the pit tile

        Assert.True(ghost.Alive);
        Assert.Equal(new GridPos(3, 1), ghost.Pos);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL to compile — `MonsterKilled` missing; and slime would (without the check) survive on the pit.

- [ ] **Step 3: Add the event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add:

```csharp
public sealed record MonsterKilled(int MonsterId) : SimEvent;
```

- [ ] **Step 4: Implement the hazard check**

In `src/Miner49er.Core/Sim/Simulation.cs` `StepMonster`, insert the hazard check **between** the `MonsterMoved` event and the contact check (a monster that died on a hazard cannot then maul):

```csharp
        _events.Add(new MonsterMoved(mo.Id, from, next));

        if (mo.Kind != MonsterKind.Ghost && Grid.Get(mo.Pos).IsLethal())
        {
            mo.Alive = false;
            _events.Add(new MonsterKilled(mo.Id));
            return;
        }

        if (target is { Alive: true } && mo.Pos == target.Pos)
            MaulMiner(target);
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): hazards kill terrain-bound monsters; ghost floats over"
```
(Co-Authored-By trailer.)

---

### Task 7: Blasts kill monsters

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`Detonate`)
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Blast_banishes_a_monster_in_range()
    {
        var cfg = new SimConfig
        {
            FuseSeconds = 0.1, PlantSeconds = 0.1, BlastKillRadius = 1, BlastRockRadius = 1,
            MonsterGhostMoveSeconds = 999,   // hold the ghost still so it stays in blast range
        };
        var grid = new TileGrid(7, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);     // wall to plant the charge on
        var sim = Sim(grid, cfg);
        var planter = sim.AddMiner(1, new GridPos(3, 3));
        planter.Facing = Direction.North;               // faces (3,2)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);   // adjacent to the wall

        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.1);   // plant completes -> charge armed
        sim.Tick(0.1);   // fuse fires -> detonation

        Assert.False(ghost.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MonsterKilled mk && mk.MonsterId == 1);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: FAIL — ghost survives the blast.

- [ ] **Step 3: Implement the blast kill**

In `src/Miner49er.Core/Sim/Simulation.cs` `Detonate`, after the miner-kill `foreach` loop (the one that sets `DeathCause.Exploded`) and before the collapsed-cracks loop, add:

```csharp
        foreach (var mo in _monsters)
        {
            if (mo.Alive && mo.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
            {
                mo.Alive = false;
                _events.Add(new MonsterKilled(mo.Id));
            }
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "feat(core): blasts kill monsters in range"
```
(Co-Authored-By trailer.)

---

### Task 8: Remaining-gold tracking + escape opens on the last vein

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `EscapeOpened`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (ctor `escapeTile`, gold count, `OnGoldCleared`, state)
- Test: `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationExpeditionTests
{
    // Grid with two gold veins the miner can mine instantly.
    private static (Simulation sim, Miner miner) Setup()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.GoldRock);
        grid.Set(new GridPos(4, 1), TileType.GoldRock);
        var cfg = new SimConfig { PickaxeSeconds = 0.1 };
        var sim = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        return (sim, miner);
    }

    [Fact]
    public void Escape_stays_shut_until_the_last_vein_is_cleared()
    {
        var (sim, miner) = Setup();

        miner.Facing = Direction.East;                 // faces (2,1) gold
        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                 // first vein cleared

        Assert.False(sim.AllGoldCleared);
        Assert.False(sim.EscapeOpen);

        // Walk to the second vein and clear it. A Tick between moves clears the per-tile
        // move-cooldown gate (TryMove refuses a second step while the cooldown is live).
        Assert.True(sim.TryMove(1, Direction.East));   // (1,1) -> (2,1)
        sim.Tick(0.2);                                 // let the move cooldown lapse
        Assert.True(sim.TryMove(1, Direction.East));   // (2,1) -> (3,1)
        miner.Facing = Direction.East;                 // faces (4,1) gold
        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                 // second vein cleared

        Assert.True(sim.AllGoldCleared);
        Assert.True(sim.EscapeOpen);
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationExpeditionTests"`
Expected: FAIL to compile — `escapeTile` param, `AllGoldCleared`, `EscapeOpen`, `EscapeOpened` missing.

- [ ] **Step 3: Add the event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add:

```csharp
public sealed record EscapeOpened : SimEvent;
```

- [ ] **Step 4: Extend the sim with escape state**

In `src/Miner49er.Core/Sim/Simulation.cs`:

Add the constructor parameter (append to the existing signature):

```csharp
    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false,
        GridPos? escapeTile = null)
```

Add the state (near `public GridPos? Center { get; }`):

```csharp
    public GridPos? EscapeTile { get; }
    public bool EscapeOpen { get; private set; }
    private int _goldRemaining;
    public bool AllGoldCleared => _goldRemaining == 0;
```

In the constructor body, assign the field and seed the gold count (place after `Center = center;` or near it):

```csharp
        EscapeTile = escapeTile;
        foreach (var p in Grid.Positions())
            if (Grid.Get(p) == TileType.GoldRock) _goldRemaining++;
        if (EscapeTile is not null && _goldRemaining == 0) EscapeOpen = true;   // gold-less map: open at once
```

Add the helper (near `KillByTile`):

```csharp
    // Called wherever a GoldRock tile becomes Floor. When the last vein falls and an
    // escape tile is set, the exit opens (once).
    private void OnGoldCleared()
    {
        if (_goldRemaining > 0) _goldRemaining--;
        if (_goldRemaining == 0 && EscapeTile is not null && !EscapeOpen)
        {
            EscapeOpen = true;
            _events.Add(new EscapeOpened());
        }
    }
```

Wire it into the two places gold is destroyed:

In `CompleteActivity`, mining branch — change `if (wasGold) m.GoldCollected++;` to:

```csharp
            if (wasGold) { m.GoldCollected++; OnGoldCleared(); }
```

In `Detonate`, the gold branch — change:

```csharp
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                }
```
to:

```csharp
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                    OnGoldCleared();
                }
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationExpeditionTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationExpeditionTests.cs
git commit -m "feat(core): track remaining gold; escape opens on the last vein"
```
(Co-Authored-By trailer.)

---

### Task 9: `Expedition` mode + win/loss resolver

**Files:**
- Modify: `src/Miner49er.Core/Sim/GameMode.cs` (add `Expedition`)
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs` (special-case `Expedition`)
- Test: `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class RoundResolverExpeditionTests
{
    private static (Simulation sim, Miner miner) SetupNoGold(GridPos minerStart, GridPos exit)
    {
        var grid = new TileGrid(6, 3, TileType.Floor);   // no GoldRock -> AllGoldCleared from the start
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        var miner = sim.AddMiner(1, minerStart);
        return (sim, miner);
    }

    [Fact]
    public void Solo_run_is_not_won_just_because_one_miner_is_alive()
    {
        // All gold cleared, but the miner is NOT on the exit yet.
        var (sim, _) = SetupNoGold(new GridPos(2, 1), exit: new GridPos(0, 1));

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.False(result.IsOver);
    }

    [Fact]
    public void Reaching_the_exit_with_all_gold_cleared_wins()
    {
        var (sim, _) = SetupNoGold(new GridPos(0, 1), exit: new GridPos(0, 1));   // already on the exit

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Miner_death_loses_the_run()
    {
        var (sim, miner) = SetupNoGold(new GridPos(0, 1), exit: new GridPos(0, 1));
        sim.KillMiner(miner.Id);

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);   // loss
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~RoundResolverExpeditionTests"`
Expected: FAIL to compile — `GameMode.Expedition` missing; and (once added) the "alive" test would wrongly win under the universal rule.

- [ ] **Step 3: Add the mode**

In `src/Miner49er.Core/Sim/GameMode.cs`:

```csharp
public enum GameMode { LastManStanding, GoldRush, ReachCenter, Expedition }
```

- [ ] **Step 4: Special-case `Expedition` in the resolver**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, at the very top of `Resolve` (before the universal last-man-standing check), add:

```csharp
        // Solo Expedition: a single miner means last-man-standing would auto-win on tick 1.
        // Instead: lose when the miner is dead, win only on the objective (all gold + on exit).
        if (mode == GameMode.Expedition)
        {
            if (alive.Count == 0) return new RoundResult(true, -1);
            if (sim.AllGoldCleared && sim.EscapeTile is { } exit)
            {
                var winner = alive.FirstOrDefault(m => m.Pos == exit);
                if (winner is not null) return new RoundResult(true, winner.Id);
            }
            return new RoundResult(false, -1);
        }
```

(`alive` is already computed at the top of `Resolve`; `System.Linq` is already imported.)

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~RoundResolverExpeditionTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/GameMode.cs src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs
git commit -m "feat(core): Expedition mode - lose on death, win on all-gold + exit"
```
(Co-Authored-By trailer.)

---

### Task 10: Monster spawner — far from start, deterministic kind mix

**Files:**
- Create: `src/Miner49er.Core/Map/MonsterSpawner.cs`
- Test: `src/Miner49er.Core.Tests/MonsterSpawnerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/MonsterSpawnerTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MonsterSpawnerTests
{
    [Fact]
    public void Places_the_requested_count_on_floor_away_from_the_start()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);

        var spawns = MonsterSpawner.Place(grid, start, 4);

        Assert.Equal(4, spawns.Count);
        Assert.All(spawns, s => Assert.Equal(TileType.Floor, grid.Get(s.Pos)));
        Assert.DoesNotContain(spawns, s => s.Pos == start);
        // farthest-first from the start: the nearest spawn is still well away from it.
        Assert.All(spawns, s => Assert.True(s.Pos.ManhattanTo(start) >= 10,
            $"spawn too close to start: {s.Pos}"));
    }

    [Fact]
    public void Kinds_cycle_deterministically_and_results_are_stable()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);

        var a = MonsterSpawner.Place(grid, start, 3);
        var b = MonsterSpawner.Place(grid, start, 3);

        Assert.Equal(a, b);   // deterministic
        Assert.Equal(new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat },
                     a.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void Zero_or_negative_count_yields_nothing()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        Assert.Empty(MonsterSpawner.Place(grid, new GridPos(1, 1), 0));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ... --filter "FullyQualifiedName~MonsterSpawnerTests"`
Expected: FAIL to compile — `MonsterSpawner` missing.

- [ ] **Step 3: Implement the spawner**

Create `src/Miner49er.Core/Map/MonsterSpawner.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

/// <summary>Chooses monster spawn tiles for an Expedition: floor cells placed as far as
/// possible from the start (and from each other) by farthest-first dispersion seeded with
/// the start tile, then assigns kinds round-robin (Slime, Ghost, Goat). Pure and
/// deterministic from the grid + start, so host and any future client agree.</summary>
public static class MonsterSpawner
{
    private static readonly MonsterKind[] Kinds = { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat };

    public static List<(GridPos Pos, MonsterKind Kind)> Place(TileGrid grid, GridPos start, int count)
    {
        var result = new List<(GridPos, MonsterKind)>();
        if (count <= 0) return result;

        var floors = grid.Positions()
            .Where(p => grid.Get(p) == TileType.Floor && p != start)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        if (floors.Count == 0) return result;

        // Farthest-first, seeded by the start so every pick maximises its minimum distance
        // to the start and to previously chosen spawns. Ties resolve to (Y, X) order.
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

        for (int i = 0; i < chosen.Count; i++)
            result.Add((chosen[i], Kinds[i % Kinds.Length]));
        return result;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test ... --filter "FullyQualifiedName~MonsterSpawnerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MonsterSpawner.cs src/Miner49er.Core.Tests/MonsterSpawnerTests.cs
git commit -m "feat(core): MonsterSpawner - dispersed spawns, round-robin kinds"
```
(Co-Authored-By trailer.)

---

### Task 11: Determinism — same seed, identical monster paths

**Files:**
- Test: `src/Miner49er.Core.Tests/SimulationMonsterTests.cs`

- [ ] **Step 1: Write the test**

Add to `SimulationMonsterTests.cs`:

```csharp
    [Fact]
    public void Same_seed_reproduces_identical_wander_paths()
    {
        System.Collections.Generic.List<GridPos> Run()
        {
            var cfg = new SimConfig { Seed = 1234, MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 0 };
            var sim = Sim(new TileGrid(11, 11, TileType.Floor), cfg);
            var slime = sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Slime);   // no miner -> pure wander
            var path = new System.Collections.Generic.List<GridPos>();
            for (int i = 0; i < 40; i++) { sim.Tick(0.1); path.Add(slime.Pos); }
            return path;
        }

        Assert.Equal(Run(), Run());
    }
```

- [ ] **Step 2: Run to verify it passes**

Run: `dotnet test ... --filter "FullyQualifiedName~SimulationMonsterTests"`
Expected: PASS (the seeded `_rng` already makes wandering reproducible). If it fails, monster randomness is leaking from a non-seeded source — fix before continuing.

- [ ] **Step 3: Commit**

```bash
git add src/Miner49er.Core.Tests/SimulationMonsterTests.cs
git commit -m "test(core): monster wander paths are deterministic per seed"
```
(Co-Authored-By trailer.)

---

## Final verification

- [ ] Run the whole Core suite: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` — expect all green (324 existing + the new monster/expedition/spawner tests).
- [ ] Solution build clean: `dotnet build Miner49er.sln` — 0 warnings, 0 errors.
- [ ] Headless Godot smoke boot (PowerShell only): `& godot --headless --path . --quit-after 120` — exit 0, no script errors. (The Core changes are additive; the game adapter is untouched in Phase 1.)
- [ ] Then complete the branch via superpowers:finishing-a-development-branch (tests gate → options). Phase 2 (plumbing & play) and Phase 3 (art) follow as their own plans.

## Notes for the implementer

- **Indentation:** `Miner49er.Core` uses **4-space** indent (the `game/` adapter uses tabs, but Phase 1 touches no `game/` files).
- **Do not** `git add -A` — stage only the exact files listed per task. The repo has pre-existing untracked junk (`.superpowers/`, `*.png.import`, `*.uid`) that must never be committed.
- **Never run `godot` through the Bash tool** — its shim breaks headless with a false "assemblies not found". Use PowerShell.
- **Tick-order rationale:** monsters advance after lava but before activities/charges, so a charge fired this tick still catches a monster, and a monster stepping onto a freshly-spread lava tile dies. The miner-contact check is intentionally duplicated (monster→miner in `StepMonster`, miner→monster in `TryMove`) to catch closing the gap from either side.
- **`SimConfig.Seed` default 0** keeps every existing test deterministic and unchanged; only Expedition wiring sets it from the match seed (Phase 2).
