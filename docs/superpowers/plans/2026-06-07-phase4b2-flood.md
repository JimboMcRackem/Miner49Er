# Phase 4b-2 — Flood (Rising-Water Modifier) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add flooding as a host-selectable modifier on any mode — deep water rises inward from the map edges over the match clock, drowning miners caught in it — plus the netcode and carry-forward fixes it needs.

**Architecture:** The flood runs in Core `Simulation` (deterministic, unit-tested), paced by the existing `Elapsed`/`TimeLimit`; it mutates the grid and emits a `TileFlooded` event. The host maps that to a `TileChange` (now carrying a `TileType`) so clients render the flood purely from synced deltas. A `DrownOccupants` pass kills miners under the rising water.

**Tech Stack:** C# / .NET 8, pure-C# `Miner49er.Core` (xUnit), Godot 4.6.3 (.NET) adapter at repo root.

**Build/test/run (PowerShell, not Bash):**
- Core tests: `dotnet test src/Miner49er.Core.Tests`
- Godot build: `dotnet build Miner49er.csproj`
- Headless smoke: `godot --headless --quit-after 180` (exit 0, no `ERROR`/`SCRIPT ERROR` lines)

**Indentation:** `src/Miner49er.Core/` + test project use **4-space**; `game/` files use **TAB**. Match each file's existing whitespace exactly.

**Task order:** sequential (1 → 2 → 3). Each task keeps the build green (Task 1's new `Simulation` ctor param is optional, so `Main.cs` still compiles; Task 2 is self-contained; Task 3 wires it up).

**Spec:** `docs/superpowers/specs/2026-06-07-phase4b2-flood-design.md`

---

## Task 1: Core — flood, drowning, `IsWater()`, reach-center alive guard

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs` (add `IsWater()` extension)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (use the public `IsWater()`, drop the private one)
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `TileFlooded`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (ctor `flooding` flag; `AdvanceFlood`/`EdgeDistance`/`DrownOccupants`; `Tick` call; `TryMove` latch alive guard)
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs` (reach-center alive guard)
- Modify: `src/Miner49er.Core.Tests/TileTypeWaterTests.cs` (IsWater test)
- Modify: `src/Miner49er.Core.Tests/RoundResolverTests.cs` (drowned-reacher test)
- Create: `src/Miner49er.Core.Tests/FloodTests.cs`

- [ ] **Step 1: Add the `IsWater()` extension (test first)**

In `src/Miner49er.Core.Tests/TileTypeWaterTests.cs`, add a test inside the existing class:

```csharp
    [Fact]
    public void IsWater_is_true_only_for_water_tiles()
    {
        Assert.True(TileType.ShallowWater.IsWater());
        Assert.True(TileType.DeepWater.IsWater());
        Assert.False(TileType.Floor.IsWater());
        Assert.False(TileType.Rock.IsWater());
        Assert.False(TileType.ImpermeableRock.IsWater());
    }
```

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~TileTypeWaterTests` → expect compile FAIL (no `IsWater`).

In `src/Miner49er.Core/Grid/TileType.cs`, add to `TileTypeExtensions` (e.g. after `MoveCostMultiplier`):

```csharp
    /// <summary>Shallow or deep water (used by water placement and the flood).</summary>
    public static bool IsWater(this TileType t) => t is TileType.ShallowWater or TileType.DeepWater;
```

Run the same filter → expect PASS.

- [ ] **Step 2: Dedup `MapGenerator.IsWater` onto the public helper**

In `src/Miner49er.Core/Map/MapGenerator.cs`, delete the private method:

```csharp
    private static bool IsWater(TileType t) => t is TileType.ShallowWater or TileType.DeepWater;
```

and update its two call sites to use the extension:
- In `PromoteDeep`, `if (!g.InBounds(n) || !IsWater(g.Get(n))) { allWater = false; break; }` becomes `if (!g.InBounds(n) || !g.Get(n).IsWater()) { allWater = false; break; }`
- In `IsWaterAdjacent`, `if (g.InBounds(n) && IsWater(g.Get(n))) return true;` becomes `if (g.InBounds(n) && g.Get(n).IsWater()) return true;`

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~MapGenerator` → expect PASS (water-generation behavior unchanged).

- [ ] **Step 3: Add the `TileFlooded` sim event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add after the `MinerReachedCenter` line:

```csharp
public sealed record TileFlooded(GridPos Pos, TileType Type) : SimEvent;
```

- [ ] **Step 4: Write failing flood tests**

Create `src/Miner49er.Core.Tests/FloodTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class FloodTests
{
    // 11x11 all-floor grid: maxDist = (11-1)/2 = 5; centre (5,5) has edge-distance 5.
    private static Simulation FloodSim(double timeLimit = 10.0, bool flooding = true)
        => new(new TileGrid(11, 11, TileType.Floor), new SimConfig(),
               timeLimitSeconds: timeLimit, flooding: flooding);

    [Fact]
    public void Front_floods_border_first_centre_last()
    {
        var sim = FloodSim();
        sim.Tick(2.0); // progress .2 -> floodedMaxDist = 1
        Assert.Equal(TileType.ShallowWater, sim.Grid.Get(new GridPos(1, 5))); // edge-distance 1
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(5, 5)));        // centre still dry
    }

    [Fact]
    public void Leading_ring_is_shallow_and_everything_behind_is_deep()
    {
        var sim = FloodSim();
        sim.Tick(4.0); // progress .4 -> floodedMaxDist = 2
        Assert.Equal(TileType.DeepWater, sim.Grid.Get(new GridPos(1, 5)));    // d=1 < 2 -> deep
        Assert.Equal(TileType.ShallowWater, sim.Grid.Get(new GridPos(2, 5))); // d=2 == front -> shallow
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(5, 5)));        // d=5 dry
    }

    [Fact]
    public void Rock_does_not_flood()
    {
        var sim = FloodSim();
        sim.Grid.Set(new GridPos(1, 5), TileType.Rock);
        sim.Tick(4.0); // d=1 would be deep, but it's a wall
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(1, 5)));
    }

    [Fact]
    public void Open_space_revealed_inside_the_zone_re_floods()
    {
        var sim = FloodSim();
        sim.Grid.Set(new GridPos(1, 5), TileType.Rock);
        sim.Tick(4.0);
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(1, 5))); // wall holds
        sim.Grid.Set(new GridPos(1, 5), TileType.Floor); // simulate a mined reveal inside the zone
        sim.Tick(0.1);
        Assert.Equal(TileType.DeepWater, sim.Grid.Get(new GridPos(1, 5))); // flood reasserts
    }

    [Fact]
    public void A_standing_miner_drowns_when_the_front_reaches_them()
    {
        var sim = FloodSim();
        var m = sim.AddMiner(1, new GridPos(1, 5)); // edge-distance 1
        sim.Tick(4.0); // floodedMaxDist 2 -> (1,5) becomes deep
        Assert.False(m.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MinerDrowned d && d.MinerId == 1);
    }

    [Fact]
    public void Flooding_is_inert_when_disabled()
    {
        var sim = FloodSim(flooding: false);
        sim.Tick(8.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(1, 5)));
    }

    [Fact]
    public void Flooding_is_inert_without_a_time_limit()
    {
        var sim = new Simulation(new TileGrid(11, 11, TileType.Floor), new SimConfig(),
            flooding: true); // no timeLimitSeconds
        sim.Tick(8.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(1, 5)));
    }
}
```

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~FloodTests` → expect compile FAIL (`Simulation` has no `flooding` param; no flood behavior).

- [ ] **Step 5: Add the flooding flag to `Simulation`**

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the timer-field + constructor block (current lines 18-30):

```csharp
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

with:

```csharp
    private readonly double? _timeLimit;
    private readonly bool _flooding;
    public double Elapsed { get; private set; }
    public double SecondsRemaining => _timeLimit is { } lim ? Math.Max(0, lim - Elapsed) : -1;
    public bool TimeExpired => _timeLimit is { } lim && Elapsed >= lim;

    public Simulation(TileGrid grid, SimConfig config,
        GridPos? center = null, double? timeLimitSeconds = null, bool flooding = false)
    {
        Grid = grid;
        Config = config;
        Center = center;
        _timeLimit = timeLimitSeconds;
        _flooding = flooding;
    }
```

- [ ] **Step 6: Drive the flood from `Tick` and add the flood/drown methods**

In `src/Miner49er.Core/Sim/Simulation.cs`, add `AdvanceFlood();` as the last line of `Tick`:

```csharp
    public void Tick(double dt)
    {
        Elapsed += dt;
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        AdvanceCharges(chargesThisTick, dt);
        AdvanceFlood();
    }
```

Then add these three methods immediately after the `AdvanceCharges` method (before `Detonate`):

```csharp
    // --- Flood (rising-water modifier) -----------------------------------
    // Deep water rises inward from the map edges, paced by the match clock: a
    // tile floods when its edge-distance <= floor(progress * maxDist). The current
    // front ring is shallow (a one-ring warning); everything behind it is deep
    // (lethal). Only open space floods; rock stays a wall until mined. Idempotent
    // on progress, so it also re-floods open tiles freshly exposed inside the zone.
    private void AdvanceFlood()
    {
        if (!_flooding || _timeLimit is not { } lim) return;
        int maxDist = (Math.Min(Grid.Width, Grid.Height) - 1) / 2;
        if (maxDist < 1) return;
        double progress = Math.Min(1.0, Elapsed / lim);
        int floodedMaxDist = (int)(progress * maxDist);
        if (floodedMaxDist < 1) return;

        foreach (var p in Grid.Positions())
        {
            int d = EdgeDistance(p);
            if (d < 1 || d > floodedMaxDist) continue;
            var cur = Grid.Get(p);
            if (cur != TileType.Floor && !cur.IsWater()) continue; // walls don't flood
            var target = d == floodedMaxDist ? TileType.ShallowWater : TileType.DeepWater;
            if (cur != target)
            {
                Grid.Set(p, target);
                _events.Add(new TileFlooded(p, target));
            }
        }
        DrownOccupants();
    }

    private int EdgeDistance(GridPos p) =>
        Math.Min(Math.Min(p.X, p.Y), Math.Min(Grid.Width - 1 - p.X, Grid.Height - 1 - p.Y));

    // Kills any living miner standing on a now-lethal (deep) tile. Covers water
    // rising *under* a stationary miner; move-time drowning stays in TryMove.
    private void DrownOccupants()
    {
        foreach (var m in _miners.Values)
        {
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                _events.Add(new MinerDrowned(m.Id));
            }
        }
    }
```

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~FloodTests` → expect PASS (7 tests).

- [ ] **Step 7: Guard the reach-center latch and resolution against drowned miners (test first)**

In `src/Miner49er.Core.Tests/RoundResolverTests.cs`, add:

```csharp
    [Fact]
    public void Reach_center_does_not_award_a_drowned_reacher()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        sim.AddMiner(3, new GridPos(0, 4)); // a third so LMS doesn't fire when miner 1 dies
        sim.TryMove(1, Direction.East);     // miner 1 reaches centre alive (latches)
        sim.GetMiner(1).Alive = false;      // then dies (e.g. flooded)
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.False(result.IsOver);        // a dead reacher does not win; 2 still alive
    }
```

In `src/Miner49er.Core.Tests/FloodTests.cs`, add:

```csharp
    [Fact]
    public void Drowning_onto_the_centre_tile_does_not_latch_a_reacher()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.DeepWater); // centre is lethal
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 1));
        sim.AddMiner(1, new GridPos(0, 1));
        sim.TryMove(1, Direction.East); // steps onto centre and drowns on entry
        Assert.False(sim.GetMiner(1).Alive);
        Assert.Equal(-1, sim.FirstToReachCenter); // not latched — drowned on arrival
    }
```

Run: `dotnet test src/Miner49er.Core.Tests --filter "FullyQualifiedName~RoundResolverTests|FullyQualifiedName~FloodTests"` → expect FAIL (latch fires for a drowned miner; resolver awards a dead reacher).

- [ ] **Step 8: Implement both guards**

In `src/Miner49er.Core/Sim/Simulation.cs`, `TryMove`, add `&& m.Alive` to the centre-latch condition (current lines 79-83):

```csharp
        if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive)
        {
            FirstToReachCenter = id;
            _events.Add(new MinerReachedCenter(id));
        }
```

In `src/Miner49er.Core/Sim/RoundResolver.cs`, tighten the ReachCenter arm:

```csharp
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                                      && sim.GetMiner(sim.FirstToReachCenter).Alive
                => new RoundResult(true, sim.FirstToReachCenter),
```

Run the same filter from Step 7 → expect PASS. Then the prior `Reach_center_winner_is_the_first_to_arrive` test still passes (the reacher is alive).

- [ ] **Step 9: Full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests`
Expected: PASS — ~121 tests (112 prior + IsWater + 8 flood/reach-center). (Re-run if `testhost.exe` is blocked by Smart App Control.)

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core.Tests/TileTypeWaterTests.cs src/Miner49er.Core.Tests/RoundResolverTests.cs src/Miner49er.Core.Tests/FloodTests.cs
git commit -m "feat(core): rising-water flood, under-occupant drowning, IsWater helper, reach-center alive guard"
```

---

## Task 2: Netcode — typed tile deltas

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs` (`TileChange` gains `NewType`)
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs` (write/read the type)
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` (cover `NewType`)
- Modify: `game/net/MatchHost.cs` (TAB) — map sim events → typed `TileChange`
- Modify: `game/net/MatchClient.cs` (TAB) — apply `t.NewType`

- [ ] **Step 1: Update the codec round-trip test (test first)**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, add `using Miner49er.Core;` to the usings (for `TileType`). Change the first `TileChange` in `Round_trips_all_fields` to carry a type, and assert it:

`TileChanges: new List<TileChange> { new(8, 8, true), new(2, 2, false) });` becomes
```csharp
            TileChanges: new List<TileChange> { new(8, 8, true, TileType.DeepWater), new(2, 2, false) });
```
and after the existing `Assert.Equal(2, back.TileChanges.Count);` line add:
```csharp
        Assert.Equal(TileType.DeepWater, back.TileChanges[0].NewType);
```

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotCodecTests` → expect compile FAIL (`TileChange` has no `NewType`).

- [ ] **Step 2: Add `NewType` to `TileChange`**

In `src/Miner49er.Core/Net/Snapshots.cs`, replace the `TileChange` record:

```csharp
/// <summary>One floor cell that changed; FromBlast drives the flash, NewType is
/// the tile it became (Floor for mining/blasts, water for the flood).</summary>
public readonly record struct TileChange(int X, int Y, bool FromBlast, TileType NewType = TileType.Floor);
```

(The `= TileType.Floor` default keeps existing 3-arg constructions compiling. `TileType` resolves from the enclosing `Miner49er.Core` namespace.)

- [ ] **Step 3: Serialize `NewType`**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`:
- In `Write`, the tile-change loop becomes:
```csharp
        foreach (var t in update.TileChanges)
        {
            w.Write(t.X); w.Write(t.Y); w.Write(t.FromBlast); w.Write((int)t.NewType);
        }
```
- In `Read`, the tile-change construction becomes:
```csharp
            changes.Add(new TileChange(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), (TileType)r.ReadInt32()));
```

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~SnapshotCodecTests` → expect PASS.

- [ ] **Step 4: Map sim events → typed `TileChange` on the host**

In `game/net/MatchHost.cs`, replace the event-drain switch (current lines 90-99) so each case sets a type and the new `TileFlooded` is handled:

```csharp
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
				}
```

- [ ] **Step 5: Apply the typed delta on the client**

In `game/net/MatchClient.cs`, in `ApplyUpdate`, change the hardcoded floor (current line 60):

```csharp
			if (Grid.InBounds(p)) Grid.Set(p, t.NewType);
```

- [ ] **Step 6: Build + headless**

Run: `dotnet build Miner49er.csproj` → expect Build succeeded, 0 errors.
Run: `godot --headless --quit-after 180` → expect exit 0, no `ERROR`/`SCRIPT ERROR` lines.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs game/net/MatchHost.cs game/net/MatchClient.cs
git commit -m "feat(net): TileChange carries a TileType so the flood syncs to clients"
```

---

## Task 3: Lobby + wiring — the Flooding toggle

**Files:**
- Modify: `game/net/NetworkManager.cs` (TAB) — `MatchFlooding`; thread `flooding` through `StartMatch`/`BeginMatch`; force a time limit
- Modify: `game/ui/Lobby.cs` (TAB) — host-only "Flooding" `CheckBox`
- Modify: `game/Main.cs` (TAB) — construct the host sim with `Flooding`

- [ ] **Step 1: Thread `flooding` through `NetworkManager`**

In `game/net/NetworkManager.cs`, add the property after `MatchTimeLimitSeconds`:
```csharp
	public int MatchTimeLimitSeconds { get; private set; }
	public bool MatchFlooding { get; private set; }
```

Replace `StartMatch(GameMode mode, int timeLimitSeconds)`:
```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, order); // host applies locally too
	}
```

Replace `BeginMatch(...)`:
```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Add the host-only "Flooding" checkbox to the lobby**

In `game/ui/Lobby.cs`, add the field next to `_timePicker`:
```csharp
	private OptionButton _timePicker = null!;
	private CheckBox _floodCheck = null!;
```

In `_Ready()`, after the `_timePicker` block (`box.AddChild(_timePicker);`) and before the Start button, insert:
```csharp
		_floodCheck = new CheckBox { Text = "Flooding" };
		_floodCheck.Visible = NetworkManager.Instance.IsHost;
		// Flooding needs a clock: bump "No Time Limit" -> 1 min when enabled.
		_floodCheck.Toggled += (bool on) => { if (on && _timePicker.Selected == 0) _timePicker.Select(1); };
		box.AddChild(_floodCheck);
```

Change the Start handler to pass the checkbox state:
```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch(
			(GameMode)_modePicker.GetSelectedId(), _timePicker.GetSelectedId(), _floodCheck.ButtonPressed);
```

- [ ] **Step 3: Construct the host sim with `Flooding`**

In `game/Main.cs`, in the `if (nm.IsHost)` block, add the flooding arg to the `Simulation` construction (it currently passes 4 args ending with the time-limit ternary):

```csharp
			var sim = new Simulation(
				MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount)).Grid,
				new SimConfig(),
				map.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding);
```

- [ ] **Step 4: Build + headless**

Run: `dotnet build Miner49er.csproj` → expect Build succeeded, 0 errors.
Run: `godot --headless --quit-after 180` → expect exit 0, no `ERROR`/`SCRIPT ERROR` lines.

- [ ] **Step 5: Final full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests` → expect all PASS (~121).

- [ ] **Step 6: Commit**

```bash
git add game/net/NetworkManager.cs game/ui/Lobby.cs game/Main.cs
git commit -m "feat(game): lobby Flooding toggle threads through to the host sim"
```

---

## Done criteria

- Full `Miner49er.Core` xUnit suite green (~121).
- `dotnet build Miner49er.csproj` 0 errors; `godot --headless --quit-after 180` exits 0 with no error lines.
- Play-test (user): tick **Flooding** in the lobby (the time dropdown jumps off "No Time Limit"); deep water creeps inward from the edges behind a one-ring shallow warning, drowning miners who linger; the dry centre shrinks to nothing by the clock's end; flooding layers sensibly on each mode (Reach Center becomes a race against the closing water, and a miner who drowns reaching the centre does NOT win); mining/explosion reveals still render via the typed-delta path.
- Final opus code review, then merge `phase4b2-flood` to main.

## Out of scope (later)

Bottomless pit (4d), items & status effects (4c). No new map-gen knobs for flooding (works on existing maps; Reach Center keeps its 4b-1 larger map).
