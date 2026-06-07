# Phase 4b-1 — Mode Framework, Win Modes & Match Timer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a game-mode framework (Last Man Standing / Gold Rush / Reach Center), a per-mode match timer, and a host-only lobby mode picker — establishing the seam that Phase 4b-2's flood mode will plug into.

**Architecture:** `RoundResolver` becomes the single mode-aware decision point; `Simulation` only records neutral facts (map center reached, time elapsed) and never branches on the mode. The chosen `GameMode` rides the existing `BeginMatch` RPC; the timer's `SecondsRemaining` rides the per-tick `WorldSnapshot`. Last-man-standing stays universal across all modes.

**Tech Stack:** C# / .NET 8, pure-C# `Miner49er.Core` (xUnit), Godot 4.6.3 (.NET) adapter at repo root.

**Build/test/run (PowerShell, not Bash):**
- Core tests: `dotnet test src/Miner49er.Core.Tests`
- Godot build: `dotnet build Miner49er.csproj`
- Headless smoke: `godot --headless --quit-after 180` (expect exit 0, no `ERROR`/`SCRIPT ERROR` lines)

**Task dependency / parallelism:** Tasks **1 and 2 are independent** and may be fanned in parallel worktrees, then merged. Task 3 depends on Task 1 (the `GameMode` enum). Task 4 depends on Tasks 1, 2, 3. Task 5 depends on Tasks 2 and 4. Tasks 3–5 touch shared Godot files and run sequentially.

**Spec:** `docs/superpowers/specs/2026-06-07-phase4b1-modes-timer-design.md`

---

## Task 1: Core — GameMode, Simulation facts & mode-aware RoundResolver

**Files:**
- Create: `src/Miner49er.Core/Sim/GameMode.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `MinerReachedCenter`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (constructor center + time limit; `Center`, `FirstToReachCenter`, `Elapsed`, `SecondsRemaining`, `TimeExpired`; `Tick` elapsed accrual; `TryMove` center hook)
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs` (mode-aware `Resolve`)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs:236-242` (guard `.First()`)
- Modify: `src/Miner49er.Core.Tests/RoundResolverTests.cs` (update 3 existing calls; add mode tests)
- Create: `src/Miner49er.Core.Tests/GameModeTests.cs` (enum mapping + Simulation timer/center facts)

- [ ] **Step 1: Write the GameMode enum + extensions**

Create `src/Miner49er.Core/Sim/GameMode.cs`:

```csharp
namespace Miner49er.Core;

public enum GameMode { LastManStanding, GoldRush, ReachCenter }

public static class GameModeExtensions
{
    public const double GoldRushTimeLimitSeconds = 120.0;

    /// <summary>Per-mode time budget in seconds; null = untimed.</summary>
    public static double? TimeLimitSeconds(this GameMode mode) => mode switch
    {
        GameMode.GoldRush => GoldRushTimeLimitSeconds,
        _ => null,
    };
}
```

- [ ] **Step 2: Add the MinerReachedCenter event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add after the `MinerDrowned` line:

```csharp
public sealed record MinerReachedCenter(int MinerId) : SimEvent;
```

- [ ] **Step 3: Write failing tests for Simulation timer & center facts**

Create `src/Miner49er.Core.Tests/GameModeTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class GameModeTests
{
    [Fact]
    public void GoldRush_is_timed_others_are_not()
    {
        Assert.Equal(120.0, GameMode.GoldRush.TimeLimitSeconds());
        Assert.Null(GameMode.LastManStanding.TimeLimitSeconds());
        Assert.Null(GameMode.ReachCenter.TimeLimitSeconds());
    }

    [Fact]
    public void Untimed_sim_reports_minus_one_and_never_expires()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.Tick(10.0);
        Assert.Equal(-1, sim.SecondsRemaining);
        Assert.False(sim.TimeExpired);
    }

    [Fact]
    public void Timed_sim_counts_down_and_then_expires()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig(),
            timeLimitSeconds: 5.0);
        Assert.Equal(5.0, sim.SecondsRemaining);
        Assert.False(sim.TimeExpired);

        sim.Tick(2.0);
        Assert.Equal(3.0, sim.SecondsRemaining, 3);
        Assert.False(sim.TimeExpired);

        sim.Tick(4.0);                 // total 6 >= 5
        Assert.Equal(0.0, sim.SecondsRemaining);   // clamped, never negative
        Assert.True(sim.TimeExpired);
    }

    [Fact]
    public void Moving_onto_center_records_first_miner_and_emits_event()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(2, 1));
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East); // lands on (2,1) == center
        var events = sim.DrainEvents();

        Assert.Equal(1, sim.FirstToReachCenter);
        Assert.Contains(events, e => e is MinerReachedCenter rc && rc.MinerId == 1);
    }

    [Fact]
    public void Center_is_recorded_only_for_the_first_arrival()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 1));
        sim.AddMiner(1, new GridPos(0, 1));
        sim.AddMiner(2, new GridPos(2, 1));

        sim.TryMove(1, Direction.East); // miner 1 reaches center first
        sim.TryMove(2, Direction.West); // miner 2 arrives second
        Assert.Equal(1, sim.FirstToReachCenter);
    }

    [Fact]
    public void No_center_means_FirstToReachCenter_stays_minus_one()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        Assert.Equal(-1, sim.FirstToReachCenter);
    }
}
```

- [ ] **Step 4: Run the new tests — expect FAIL (compile errors)**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~GameModeTests`
Expected: build/compile FAIL — `Simulation` has no `center`/`timeLimitSeconds` ctor params, no `SecondsRemaining`/`TimeExpired`/`FirstToReachCenter`.

- [ ] **Step 5: Implement the Simulation facts**

In `src/Miner49er.Core/Sim/Simulation.cs`:

Replace the constructor (lines 15-19) and add the new members right after the existing `Charges` property (line 13). The new public members:

```csharp
    public GridPos? Center { get; }
    public int FirstToReachCenter { get; private set; } = -1;

    private readonly double? _timeLimit;
    public double Elapsed { get; private set; }
    public double SecondsRemaining => _timeLimit is { } lim ? Math.Max(0, lim - Elapsed) : -1;
    public bool TimeExpired => _timeLimit is { } lim && Elapsed >= lim;

    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null)
    {
        Grid = grid;
        Config = config;
        Center = center;
        _timeLimit = timeLimitSeconds;
    }
```

In `Tick(double dt)` (currently line 110-117), add elapsed accrual as the first statement:

```csharp
    public void Tick(double dt)
    {
        Elapsed += dt;
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        AdvanceCharges(chargesThisTick, dt);
    }
```

In `TryMove`, add the center hook after the lethal-tile block (after line 66's closing brace, before `return true;`):

```csharp
        if (Center is { } c && target == c && FirstToReachCenter < 0)
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }
        return true;
```

- [ ] **Step 6: Run the GameMode tests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~GameModeTests`
Expected: PASS (6 tests).

- [ ] **Step 7: Update existing RoundResolverTests for the new signature + add mode tests**

`RoundResolver.Resolve` is about to require a `GameMode`. Rewrite `src/Miner49er.Core.Tests/RoundResolverTests.cs` to:

```csharp
using Miner49er.Core;
using Xunit;

public class RoundResolverTests
{
    private static Simulation TwoMinerSim(GridPos? center = null, double? timeLimit = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig(),
            center, timeLimit);
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        return sim;
    }

    // --- Universal last-man-standing (applies in every mode) ---

    [Fact]
    public void Two_alive_miners_is_not_over()
    {
        var result = RoundResolver.Resolve(TwoMinerSim(), GameMode.LastManStanding);
        Assert.False(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void One_alive_miner_is_over_and_that_miner_wins()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Zero_alive_miners_is_over_with_no_winner()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(1).Alive = false;
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void Last_man_standing_applies_even_in_gold_rush()
    {
        var sim = TwoMinerSim(timeLimit: 120.0); // not expired
        sim.GetMiner(2).Alive = false;           // only miner 1 left
        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    // --- Gold Rush (timeout → most gold) ---

    [Fact]
    public void Gold_rush_not_expired_with_two_alive_is_not_over()
    {
        var sim = TwoMinerSim(timeLimit: 120.0);
        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Gold_rush_timeout_awards_the_richest_living_miner()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.GetMiner(1).GoldCollected = 2;
        sim.GetMiner(2).GoldCollected = 7;
        sim.Tick(5.0); // expire the timer

        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(2, result.WinnerId); // miner 2 has the most gold
    }

    [Fact]
    public void Gold_rush_timeout_with_a_tie_is_a_draw()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.GetMiner(1).GoldCollected = 4;
        sim.GetMiner(2).GoldCollected = 4;
        sim.Tick(5.0);

        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    // --- Reach Center (first to center wins) ---

    [Fact]
    public void Reach_center_is_not_over_until_someone_reaches_it()
    {
        var sim = TwoMinerSim(center: new GridPos(2, 2));
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Reach_center_winner_is_the_first_to_arrive()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        sim.TryMove(1, Direction.East); // miner 1 steps onto center

        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }
}
```

- [ ] **Step 8: Run RoundResolverTests — expect FAIL**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~RoundResolverTests`
Expected: compile FAIL — `Resolve` still takes one argument and has no mode logic.

- [ ] **Step 9: Implement the mode-aware RoundResolver**

Replace the body of `src/Miner49er.Core/Sim/RoundResolver.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, int WinnerId);

/// <summary>Resolves a round per game mode. Last-man-standing is universal: any
/// mode ends the instant one or zero miners remain alive. Each mode may add a
/// second terminal condition layered on top.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim, GameMode mode)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();

        // Universal last-man-standing.
        if (alive.Count <= 1)
            return new RoundResult(true, alive.Count == 1 ? alive[0].Id : -1);

        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                => new RoundResult(true, sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => new RoundResult(true, MostGoldWinner(alive)),
            _ => new RoundResult(false, -1),
        };
    }

    /// <summary>Id of the unique living miner with the strictly-highest gold;
    /// -1 if the top gold value is tied between two or more (a draw).</summary>
    private static int MostGoldWinner(List<Miner> alive)
    {
        if (alive.Count == 0) return -1;
        int max = alive.Max(m => m.GoldCollected);
        var leaders = alive.Where(m => m.GoldCollected == max).ToList();
        return leaders.Count == 1 ? leaders[0].Id : -1;
    }
}
```

- [ ] **Step 10: Run RoundResolverTests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~RoundResolverTests`
Expected: PASS (10 tests).

- [ ] **Step 11: Guard MapGenerator.NearestFloorToCenter's `.First()`**

In `src/Miner49er.Core/Map/MapGenerator.cs`, replace the `NearestFloorToCenter` body (lines 236-242):

```csharp
    private static GridPos NearestFloorToCenter(TileGrid g, HashSet<GridPos> region)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        var nearest = region.Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .Cast<GridPos?>()
            .FirstOrDefault();
        // A traversable region always contains floor in practice; fall back to the
        // raw geometric centre rather than throwing if a degenerate map has none.
        return nearest ?? c;
    }
```

- [ ] **Step 12: Run the full Core suite — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests`
Expected: PASS — all prior tests plus the new GameMode (6) and RoundResolver (10) tests. (Smart App Control may sporadically block `testhost.exe`; re-run if the run *fails to start* rather than reporting test failures.)

- [ ] **Step 13: Commit**

```bash
git add src/Miner49er.Core/Sim/GameMode.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/GameModeTests.cs src/Miner49er.Core.Tests/RoundResolverTests.cs
git commit -m "feat(core): game modes, match timer & center-reach facts with mode-aware resolver"
```

---

## Task 2: Core — carry SecondsRemaining in the per-tick snapshot

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs:13-14` (`WorldSnapshot` gains `SecondsRemaining`)
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs` (write/read one `float`)
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` (cover the new field)

*(Independent of Task 1 — pure DTO + serialization. May run in a parallel worktree.)*

- [ ] **Step 1: Update the SnapshotCodec round-trip test to assert SecondsRemaining**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, change the `Round_trips_all_fields` `WorldSnapshot` construction (line 11) to include a timer value, and add an assertion. The new construction line and a new assert:

```csharp
            new WorldSnapshot(
                Tick: 7,
                Miners: new List<MinerSnapshot>
                {
                    new(1, 3, 4, 2, true, 5, 1, 2.5),
                    new(2, 9, 0, 0, false, 0, 0, 0.0),
                },
                Charges: new List<ChargeSnapshot> { new(1, 8, 8, 1.25) },
                SecondsRemaining: 42.5f),
```

Add after the `Assert.Equal(7, back.Snapshot.Tick);` line:

```csharp
        Assert.Equal(42.5f, back.Snapshot.SecondsRemaining);
```

- [ ] **Step 2: Run the codec test — expect FAIL**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotCodecTests`
Expected: compile FAIL — `WorldSnapshot` has no `SecondsRemaining` parameter.

- [ ] **Step 3: Add SecondsRemaining to WorldSnapshot**

In `src/Miner49er.Core/Net/Snapshots.cs`, replace the `WorldSnapshot` record (lines 13-14):

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    float SecondsRemaining = -1f);
```

(The `-1f` default keeps existing 3-arg constructions compiling; `-1` is the untimed sentinel.)

- [ ] **Step 4: Serialize SecondsRemaining in the codec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`:

In `Write`, add the timer write right after `w.Write(snap.Tick);` (line 16):

```csharp
        w.Write(snap.Tick);
        w.Write(snap.SecondsRemaining);
```

In `Read`, read it right after `int tick = r.ReadInt32();` (line 46):

```csharp
        int tick = r.ReadInt32();
        float secondsRemaining = r.ReadSingle();
```

And change the final return (line 65) to pass it through:

```csharp
        return new TickUpdate(new WorldSnapshot(tick, miners, charges, secondsRemaining), changes);
```

- [ ] **Step 5: Run the codec tests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotCodecTests`
Expected: PASS (2 tests; the empty-collections test still passes because it uses the 3-arg ctor → `-1f` default round-trips).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs
git commit -m "feat(core): carry per-tick SecondsRemaining in the world snapshot"
```

---

## Task 3: Netcode + lobby — mode selection plumbing

**Files:**
- Modify: `game/net/NetworkManager.cs` (add `using Miner49er.Core;`; `MatchMode` property; `StartMatch(GameMode)`; `BeginMatch` gains `int mode`)
- Modify: `game/ui/Lobby.cs` (add `using Miner49er.Core;`; host-only mode `OptionButton`; pass selection to `StartMatch`)

*(Depends on Task 1's `GameMode` enum. Godot glue — verified by build + headless, not xUnit.)*

- [ ] **Step 1: Thread the mode through NetworkManager**

In `game/net/NetworkManager.cs`:

Add to the using block at the top (after line 4):

```csharp
using Miner49er.Core;
```

Add the `MatchMode` property next to `MatchSeed`/`MatchPlayerCount` (after line 163):

```csharp
	public int MatchPlayerCount { get; private set; }
	public GameMode MatchMode { get; private set; }
```

Replace `StartMatch()` (lines 175-182) and `BeginMatch(...)` (lines 184-191):

```csharp
	public void StartMatch(GameMode mode)
	{
		if (!IsHost) return;
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, order);
		BeginMatch(seed, order.Length, (int)mode, order); // host applies locally too
	}

	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Add the host-only mode picker to the lobby**

In `game/ui/Lobby.cs`:

Add to the using block (after line 2):

```csharp
using Miner49er.Core;
```

Add a field next to the other controls (after line 11):

```csharp
	private OptionButton _modePicker = null!;
```

In `_Ready()`, create the picker just before the Start button (insert before line 30, `_startBtn = new Button...`):

```csharp
		_modePicker = new OptionButton();
		_modePicker.AddItem("Last Man Standing", (int)GameMode.LastManStanding);
		_modePicker.AddItem("Gold Rush", (int)GameMode.GoldRush);
		_modePicker.AddItem("Reach Center", (int)GameMode.ReachCenter);
		_modePicker.Select(0);
		_modePicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_modePicker);
```

Change the Start button handler (line 31) to pass the selected mode:

```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch((GameMode)_modePicker.GetSelectedId());
```

- [ ] **Step 3: Build the Godot project — expect success**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors. (Fix any signature mismatches before proceeding.)

- [ ] **Step 4: Headless smoke test — expect clean exit**

Run: `godot --headless --quit-after 180`
Expected: exit 0, no `ERROR`/`SCRIPT ERROR` lines (the lobby scene constructs the `OptionButton` without throwing).

- [ ] **Step 5: Commit**

```bash
git add game/net/NetworkManager.cs game/ui/Lobby.cs
git commit -m "feat(net): host picks a game mode in the lobby; mode rides BeginMatch"
```

---

## Task 4: Host wiring — construct a timed/center sim, capture the timer, resolve per mode

**Files:**
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs:20` (capture `sim.SecondsRemaining`)
- Modify: `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs` (assert captured timer)
- Modify: `game/Main.cs:45` (construct the host `Simulation` with center + mode time limit)
- Modify: `game/net/MatchHost.cs:105` (mode-aware `Resolve`)

*(Depends on Tasks 1, 2, 3.)*

- [ ] **Step 1: Add a failing SnapshotFactory test for the captured timer**

In `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`, add a test (match the file's existing style — it already constructs a `Simulation` and calls `SnapshotFactory.Capture`):

```csharp
    [Fact]
    public void Capture_includes_seconds_remaining_from_a_timed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig(),
            timeLimitSeconds: 30.0);
        sim.Tick(10.0); // 20s left

        var snap = SnapshotFactory.Capture(sim, tick: 1);

        Assert.Equal(20f, snap.SecondsRemaining, 3);
    }

    [Fact]
    public void Capture_reports_minus_one_for_an_untimed_sim()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        var snap = SnapshotFactory.Capture(sim, tick: 1);
        Assert.Equal(-1f, snap.SecondsRemaining);
    }
```

- [ ] **Step 2: Run the factory tests — expect FAIL**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotFactoryTests`
Expected: FAIL — `Capture` still builds a 3-arg `WorldSnapshot`, so `SecondsRemaining` is the `-1f` default (the timed-sim assert expecting `20f` fails).

- [ ] **Step 3: Capture the timer in SnapshotFactory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, change the return (line 20):

```csharp
        return new WorldSnapshot(tick, miners, charges, (float)sim.SecondsRemaining);
```

- [ ] **Step 4: Run the factory tests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotFactoryTests`
Expected: PASS.

- [ ] **Step 5: Construct the host Simulation with center + mode time limit**

In `game/Main.cs`, replace the host sim construction (line 45):

```csharp
			var mode = nm.MatchMode;
			var sim = new Simulation(
				MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = playerCount }).Grid,
				new SimConfig(),
				map.Center,
				mode.TimeLimitSeconds());
```

(`map` is already generated at line 27 from the same seed, so `map.Center` matches the host sim's own regenerated grid.)

- [ ] **Step 6: Make MatchHost resolve per mode**

In `game/net/MatchHost.cs`, change the resolver call (line 105):

```csharp
			var result = RoundResolver.Resolve(_sim, NetworkManager.Instance.MatchMode);
```

- [ ] **Step 7: Build the Godot project — expect success**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Headless smoke test — expect clean exit**

Run: `godot --headless --quit-after 180`
Expected: exit 0, no `ERROR`/`SCRIPT ERROR` lines.

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs game/Main.cs game/net/MatchHost.cs
git commit -m "feat(host): drive a timed/center-aware sim and resolve per game mode"
```

---

## Task 5: HUD — show the match timer

**Files:**
- Modify: `game/net/MatchClient.cs` (expose `SecondsRemaining` from the snapshot)
- Modify: `game/Main.cs:95` (append `Time: {s}s` to the HUD when timed)

*(Depends on Tasks 2 and 4. Godot glue — verified by build + headless.)*

- [ ] **Step 1: Expose SecondsRemaining on MatchClient**

In `game/net/MatchClient.cs`, add a property next to the other public state (after line 21):

```csharp
	public int LocalMinerId { get; private set; }
	public float SecondsRemaining { get; private set; } = -1f;
```

In `ApplyUpdate`, store it alongside the miner/charge lists (after line 74):

```csharp
		_miners = new List<MinerSnapshot>(update.Snapshot.Miners);
		_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);
		SecondsRemaining = update.Snapshot.SecondsRemaining;
```

- [ ] **Step 2: Append the timer to the HUD line**

In `game/Main.cs`, replace the HUD `SetText` call (line 95) so a timed match shows the countdown:

```csharp
				string timeStr = _client.SecondsRemaining >= 0 ? $"    Time: {_client.SecondsRemaining:0}s" : "";
				_hud.SetText($"Gold: {m.Gold}    {status}{timeStr}");
```

- [ ] **Step 3: Build the Godot project — expect success**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Headless smoke test — expect clean exit**

Run: `godot --headless --quit-after 180`
Expected: exit 0, no `ERROR`/`SCRIPT ERROR` lines.

- [ ] **Step 5: Final full Core suite — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests`
Expected: PASS — all tests green (prior suite + Task 1's 16 new + Task 2/4 codec/factory additions).

- [ ] **Step 6: Commit**

```bash
git add game/net/MatchClient.cs game/Main.cs
git commit -m "feat(hud): show the match countdown timer"
```

---

## Done criteria

- Full `Miner49er.Core` xUnit suite green (94 prior + new mode/timer/center tests).
- `dotnet build Miner49er.csproj` 0 errors; `godot --headless --quit-after 180` exits 0 with no error lines.
- Manual play-test (user): each mode selectable in the lobby; Gold Rush counts down and awards most-gold at timeout; Reach Center ends instantly on first center step; Last Man Standing unchanged; HUD timer reads correctly on host and a client.
- Final opus code review before merge to main.

## Deferred to 4b-2 (flood) — do NOT build here

Flood driver, `TileChange`→`TileType` netcode change (fixes the hardcoded `Floor` at `MatchClient.cs:59`), the under-occupant `DrownOccupants` kill path, and the public `TileTypeExtensions.IsWater()` dedup. All architected in the spec; built in the next cycle.
