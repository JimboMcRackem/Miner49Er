# Bottomless Pit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a static, lethal "bottomless pit" terrain hazard — a fog-only-telegraphed hole placed at map generation, opt-in via a host lobby toggle, bridgeable by the carried water-plank.

**Architecture:** Pits ride the existing water/flood/death-cause seams. A new `TileType.Pit` is enterable-but-lethal (like deep water) with a new `DeathCause.Fell`. A new `MapGenerator.PlacePits` pass carves scattered single/cluster holes on Floor; reachability is preserved structurally because the largest traversable region is recomputed *after* carving. There is **no per-tile netcode** — host and client both regenerate the identical map from `(seed, MapConfig)`; only the boolean toggle threads through `BeginMatch`. Pits never move, so the only death path is moving onto one.

**Tech Stack:** C# (.NET) Core library + xUnit tests; Godot 4 C# game layer (no unit tests — verified by build + headless boot).

**Spec:** `docs/superpowers/specs/2026-06-13-phase4d-bottomless-pit-design.md`

**Conventions:**
- Build the solution: `dotnet build Miner49er.sln`
- Run Core tests: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- Run one test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~<TestClass>"`
- Headless boot (PowerShell ONLY — never the Bash `godot` shim): `godot --headless --quit-after 180`
- Test files live flat in `src/Miner49er.Core.Tests/`, `public class XTests`, xUnit `[Fact]`/`[Theory]`, `using Miner49er.Core; using Xunit;`.
- Commit after each task.

---

## Task 1: `TileType.Pit` tile predicates (Core)

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Test: `src/Miner49er.Core.Tests/TileTypePitTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/TileTypePitTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TileTypePitTests
{
    [Fact]
    public void Pit_is_enterable_and_lethal()
    {
        Assert.True(TileType.Pit.IsEnterable());   // you can step onto it...
        Assert.True(TileType.Pit.IsLethal());      // ...and you die
    }

    [Fact]
    public void Deep_water_is_still_lethal()       // regression: lethal set widened, not replaced
        => Assert.True(TileType.DeepWater.IsLethal());

    [Fact]
    public void Pit_is_not_safe_ground()
    {
        Assert.False(TileType.Pit.IsWalkable());   // spawns/fog/reachability never treat it as safe
        Assert.False(TileType.Pit.IsMinable());
        Assert.False(TileType.Pit.IsBlastable());
        Assert.False(TileType.Pit.IsWater());
    }

    [Fact]
    public void Pit_is_transparent_to_sight()      // an open hole — you can see across it
        => Assert.False(TileType.Pit.BlocksSight());

    [Theory]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.Pit, true)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.Plank, false)]
    public void IsBridgeable_is_water_or_pit(TileType t, bool expected)
        => Assert.Equal(expected, t.IsBridgeable());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~TileTypePitTests"`
Expected: FAIL to compile — `TileType.Pit` and `IsBridgeable` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `src/Miner49er.Core/Grid/TileType.cs`, add `Pit` to the enum:

```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank, Pit }
```

Update the predicates in `TileTypeExtensions`:

```csharp
    /// <summary>A miner may move onto this tile. Deep water and pits are enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank or TileType.Pit;

    /// <summary>Entering this tile kills the miner (drowning in deep water, falling into a pit).</summary>
    public static bool IsLethal(this TileType t) => t is TileType.DeepWater or TileType.Pit;
```

Add a new predicate (place it just below `IsWater`):

```csharp
    /// <summary>A held water-plank can be laid here (water or a pit) to form a safe Plank tile.</summary>
    public static bool IsBridgeable(this TileType t) => t.IsWater() || t == TileType.Pit;
```

Leave `IsWalkable`, `IsMinable`, `IsBlastable`, `IsWater`, `BlocksSight`, and `MoveCostMultiplier` unchanged — `Pit` is correctly excluded from all of them already (it isn't Floor/Shallow/Plank, isn't Rock/GoldRock, isn't water).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~TileTypePitTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core.Tests/TileTypePitTests.cs
git commit -m "feat(core): TileType.Pit — enterable, lethal, bridgeable"
```

---

## Task 2: Death model — `DeathCause.Fell`, `MinerFell`, `KillByTile` (Core)

**Files:**
- Modify: `src/Miner49er.Core/Sim/DeathCause.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`TryMove` lethal block ~lines 169-175; `DrownOccupants` ~lines 418-430)
- Test: `src/Miner49er.Core.Tests/SimulationPitTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationPitTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationPitTests
{
    [Fact]
    public void Moving_onto_a_pit_kills_with_Fell_and_emits_MinerFell()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                       // the move resolves (then kills)
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Fell, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerFell f && f.MinerId == 1);
    }

    [Fact]
    public void Moving_onto_deep_water_still_kills_with_Drowned()   // regression
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);

        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Drowned, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerDrowned d && d.MinerId == 1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationPitTests"`
Expected: FAIL to compile — `DeathCause.Fell` and `MinerFell` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `src/Miner49er.Core/Sim/DeathCause.cs` add `Fell`:

```csharp
public enum DeathCause { None, Drowned, Exploded, Left, Fell }
```

In `src/Miner49er.Core/Sim/SimEvent.cs`, add next to `MinerDrowned`:

```csharp
public sealed record MinerFell(int MinerId) : SimEvent;
```

In `src/Miner49er.Core/Sim/Simulation.cs`, replace the lethal block inside `TryMove` (currently):

```csharp
        if (Grid.Get(target).IsLethal())
        {
            m.Alive = false;
            m.Activity = ActivityKind.None;
            m.DeathCause = DeathCause.Drowned;
            _events.Add(new MinerDrowned(id));
        }
```

with:

```csharp
        if (Grid.Get(target).IsLethal())
            KillByTile(m);
```

Replace the inner kill in `DrownOccupants` (currently):

```csharp
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
            {
                m.Alive = false;
                m.Activity = ActivityKind.None;
                m.DeathCause = DeathCause.Drowned;
                _events.Add(new MinerDrowned(m.Id));
            }
```

with:

```csharp
            if (m.Alive && Grid.Get(m.Pos).IsLethal())
                KillByTile(m);
```

Add the shared helper (place it just above `DrownOccupants`):

```csharp
    // Kills a miner on a lethal tile, picking the cause/event from the tile under them:
    // a pit makes you Fall, deep water makes you Drown.
    private void KillByTile(Miner m)
    {
        m.Alive = false;
        m.Activity = ActivityKind.None;
        if (Grid.Get(m.Pos) == TileType.Pit)
        {
            m.DeathCause = DeathCause.Fell;
            _events.Add(new MinerFell(m.Id));
        }
        else
        {
            m.DeathCause = DeathCause.Drowned;
            _events.Add(new MinerDrowned(m.Id));
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationPitTests"`
Expected: PASS (2 tests).

Then run the full Core suite to confirm no regression in existing drown/flood tests:
Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/DeathCause.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationPitTests.cs
git commit -m "feat(core): DeathCause.Fell + MinerFell; KillByTile picks cause by tile"
```

---

## Task 3: Water-plank bridges a pit (Core)

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`TryPlacePlank` ~lines 328-336)
- Test: `src/Miner49er.Core.Tests/SimulationPitTests.cs` (add to the file from Task 2)

- [ ] **Step 1: Write the failing test**

Append to `src/Miner49er.Core.Tests/SimulationPitTests.cs` (inside the class):

```csharp
    [Fact]
    public void Plank_can_bridge_a_faced_pit_and_then_it_is_safe_to_enter()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));
        m.Held = ItemKind.WaterPlank;
        m.Facing = Direction.East;                 // facing the pit

        Assert.True(sim.TryUseItem(1));            // lay the plank over the pit
        Assert.Equal(TileType.Plank, grid.Get(new GridPos(2, 1)));
        Assert.Null(m.Held);                       // plank consumed
        Assert.Contains(sim.DrainEvents(), e => e is PlankPlaced p && p.Pos == new GridPos(2, 1));

        sim.TryMove(1, Direction.East);            // walk onto the bridged tile
        Assert.True(m.Alive);                      // no longer lethal
        Assert.Equal(new GridPos(2, 1), m.Pos);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationPitTests.Plank_can_bridge"`
Expected: FAIL — `TryPlacePlank` rejects the pit (its guard only allows water), so `TryUseItem` returns false.

- [ ] **Step 3: Write minimal implementation**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `TryPlacePlank`, change the guard from `IsWater()` to `IsBridgeable()`:

```csharp
    // Lays a permanent, flood-immune Plank tile on the faced water-or-pit tile.
    private bool TryPlacePlank(Miner m)
    {
        var target = m.Pos + m.Facing.ToOffset();
        if (!Grid.InBounds(target) || !Grid.Get(target).IsBridgeable()) return false;
        Grid.Set(target, TileType.Plank);
        m.Held = null;
        _events.Add(new PlankPlaced(target));
        return true;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationPitTests"`
Expected: PASS (3 tests). Also re-run existing plank tests to confirm water still bridges:
Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationUseVerbTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationPitTests.cs
git commit -m "feat(core): water-plank bridges pits via IsBridgeable"
```

---

## Task 4: Map generation — `PlacePits` pass + config knobs (Core)

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (knobs + `For` param)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (`Generate` ~lines 8-30; add `PlacePits`/`GrowPit`)
- Test: `src/Miner49er.Core.Tests/MapGeneratorPitTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/MapGeneratorPitTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorPitTests
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    private static MapConfig Config(int seed, bool pits) =>
        new() { Seed = seed, PlayerCount = 4, Pits = pits };

    private static List<GridPos> PitsOf(TileGrid g) =>
        g.Positions().Where(p => g.Get(p) == TileType.Pit).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void No_pits_when_toggle_is_off(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: false)).Grid;
        Assert.Empty(PitsOf(grid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_are_generated_when_toggle_is_on(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        Assert.NotEmpty(PitsOf(grid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Generation_is_deterministic_with_pits(int seed)
    {
        var a = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        var b = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        Assert.Equal(PitsOf(a), PitsOf(b));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_never_touch_the_impermeable_border(int seed)
    {
        var grid = MapGenerator.Generate(Config(seed, pits: true)).Grid;
        foreach (var p in PitsOf(grid))
            Assert.False(p.X == 0 || p.Y == 0 || p.X == grid.Width - 1 || p.Y == grid.Height - 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Pits_never_sit_on_spawns_center_or_items(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, pits: true));
        var pitSet = new HashSet<GridPos>(PitsOf(map.Grid));
        Assert.DoesNotContain(map.Center, pitSet);
        foreach (var s in map.Spawns) Assert.DoesNotContain(s, pitSet);
        foreach (var it in map.Items) Assert.DoesNotContain(it.Pos, pitSet);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    public void Spawns_can_still_reach_center_with_pits(int seed)
    {
        var map = MapGenerator.Generate(Config(seed, pits: true));
        var g = map.Grid;

        // BFS over safe ground (walkable: Floor/Shallow/Plank — pits excluded).
        var seen = new HashSet<GridPos> { map.Spawns[0] };
        var q = new Queue<GridPos>();
        q.Enqueue(map.Spawns[0]);
        while (q.Count > 0)
        {
            var p = q.Dequeue();
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n).IsWalkable() && seen.Add(n))
                    q.Enqueue(n);
            }
        }

        Assert.Contains(map.Center, seen);
        foreach (var s in map.Spawns) Assert.Contains(s, seen);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorPitTests"`
Expected: FAIL to compile — `MapConfig.Pits` does not exist.

- [ ] **Step 3: Write minimal implementation**

In `src/Miner49er.Core/Map/MapConfig.cs`, add the knobs (near the other gameplay counts, after `SlowMoldCount`):

```csharp
    // Bottomless pits (Phase 4d) — host lobby toggle, off by default.
    public bool Pits { get; set; } = false;            // gates the whole PlacePits pass
    public int PitSiteCount { get; set; } = 6;          // base number of pit sites (light per-player scaling)
    public double PitClusterChance { get; set; } = 0.3; // chance a site grows beyond one tile
    public int PitClusterMax { get; set; } = 5;         // max tiles in a grown cluster
```

Add a `pits` parameter to `MapConfig.For` (default false so existing call sites/tests are unaffected):

```csharp
    public static MapConfig For(GameMode mode, int seed, int playerCount, bool pits = false)
    {
        var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount, Pits = pits };
        if (mode == GameMode.ReachCenter)
        {
            cfg.BaseWidth = 40;
            cfg.BaseHeight = 40;
            cfg.InitialFloorChance = 0.42f;
        }
        return cfg;
    }
```

In `src/Miner49er.Core/Map/MapGenerator.cs`, insert the pit pass **after** `PlaceWater` and **before** the region is computed, so the recomputed region naturally excludes pits and all later placement avoids them. Change the `Generate` body from:

```csharp
        KeepLargestRegion(grid);
        PlaceWater(grid, rng, config);
        var region = LargestTraversableRegion(grid);
```

to:

```csharp
        KeepLargestRegion(grid);
        PlaceWater(grid, rng, config);
        if (config.Pits)
            PlacePits(grid, rng, config.PitSiteCount + (config.PlayerCount - 1),
                      config.PitClusterChance, config.PitClusterMax);
        var region = LargestTraversableRegion(grid);
```

Add the two new methods (place them just below `PlaceWater`, near the other carve helpers):

```csharp
    // Carves scattered bottomless pits over Floor: mostly single tiles, with an
    // occasional small cluster. Pits are not traversable, so the region recomputed
    // after this pass excludes them and every later placement pass avoids them.
    // Reachability is preserved structurally — the chosen region is always the
    // single largest connected traversable component.
    private static void PlacePits(TileGrid g, Random rng, int siteCount,
                                  double clusterChance, int clusterMax)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
        Shuffle(floors, rng);
        int placed = 0;
        foreach (var seed in floors)
        {
            if (placed >= siteCount) break;
            if (g.Get(seed) != TileType.Floor) continue;   // consumed by a prior cluster
            g.Set(seed, TileType.Pit);
            if (rng.NextDouble() < clusterChance)
                GrowPit(g, rng, seed, rng.Next(2, clusterMax + 1));
            placed++;
        }
    }

    // Grows a pit cluster to `size` total tiles by random flood over adjacent Floor.
    private static void GrowPit(TileGrid g, Random rng, GridPos seed, int size)
    {
        var frontier = new List<GridPos> { seed };
        int count = 1;                                       // seed tile already a pit
        while (count < size && frontier.Count > 0)
        {
            var from = frontier[rng.Next(frontier.Count)];
            var nbrs = Card.Select(d => from + d.ToOffset())
                           .Where(n => g.InBounds(n) && g.Get(n) == TileType.Floor)
                           .ToList();
            if (nbrs.Count == 0) { frontier.Remove(from); continue; }
            var n = nbrs[rng.Next(nbrs.Count)];
            g.Set(n, TileType.Pit);
            frontier.Add(n);
            count++;
        }
    }
```

Note: `Shuffle`, `Card`, and `using System.Linq` are already present in this file (used by the existing item/water passes).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorPitTests"`
Expected: PASS (all). Then run the full suite to confirm existing map/determinism tests still pass (they use `Pits=false` by default):
Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorPitTests.cs
git commit -m "feat(core): PlacePits map-gen pass + pit config knobs"
```

---

## Task 5: Lobby toggle plumbing (game — no unit test)

The game layer has no unit-test project; verify with `dotnet build`. Thread a `bool pits` from the lobby checkbox through `BeginMatch` into both peers' `MapConfig`.

**Files:**
- Modify: `game/net/NetworkManager.cs` (`MatchPits` prop ~line 168; `StartMatch` ~line 181; `BeginMatch` ~line 192)
- Modify: `game/ui/Lobby.cs` (checkbox ~line 56; `StartMatch` call ~line 71)
- Modify: `game/Main.cs` (`MapConfig.For` calls at lines 29 and 47)

- [ ] **Step 1: NetworkManager — property + signature threading**

In `game/net/NetworkManager.cs`, add the property next to `MatchFlooding`:

```csharp
	public bool MatchFlooding { get; private set; }
	public bool MatchPits { get; private set; }
```

Change `StartMatch` to accept and forward `pits`:

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, float baseMoveSeconds)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, baseMoveSeconds, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, baseMoveSeconds, order); // host applies locally too
	}
```

Change `BeginMatch` to accept and store `pits`:

```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, float baseMoveSeconds, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		MatchPits = pits;
		MatchBaseMoveSeconds = baseMoveSeconds;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Lobby — add the Pits checkbox and pass it**

In `game/ui/Lobby.cs`, declare the field directly below the existing `_floodCheck` field (line 15):

```csharp
	private CheckBox _floodCheck = null!;
	private CheckBox _pitsCheck = null!;
	private OptionButton _speedPicker = null!;
```

Then add the checkbox right after the `_floodCheck` block (after line 60):

```csharp
		_pitsCheck = new CheckBox { Text = "Pits" };
		_pitsCheck.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_pitsCheck);
```

Update the `StartMatch` call (lines 71-75) to pass the pits flag:

```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch(
			(GameMode)_modePicker.GetSelectedId(),
			_timePicker.GetSelectedId(),
			_floodCheck.ButtonPressed,
			_pitsCheck.ButtonPressed,
			new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected]);
```

- [ ] **Step 3: Main — pass the toggle into both map generations**

In `game/Main.cs`, update both `MapConfig.For` calls so host and client generate identical maps with pits. Line 29 (client):

```csharp
		var map = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits));
```

Line 47 (host):

```csharp
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits));
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors. (Warnings unrelated to this change are acceptable.)

- [ ] **Step 5: Commit**

```bash
git add game/net/NetworkManager.cs game/ui/Lobby.cs game/Main.cs
git commit -m "feat(game): host Pits lobby toggle threads to both peers' map-gen"
```

---

## Task 6: Render, audio, and death feed (game — no unit test)

**Files:**
- Modify: `game/WorldRenderer.cs` (color consts ~lines 20-36; `TargetColor` switch ~lines 87-96)
- Modify: `game/audio/SfxLibrary.cs` (add `Fall` ~line 27)
- Modify: `game/net/MatchAudio.cs` (death SFX selection ~lines 77-79)
- Modify: `game/ui/DeathFeed.cs` (`ShowBanner` ~lines 71-76; `PushToast` ~lines 90-96)

- [ ] **Step 1: WorldRenderer — draw the pit**

In `game/WorldRenderer.cs`, add a color constant alongside the others (after `MoldItemColor`):

```csharp
	private static readonly Color PitColor = new("070709");        // near-black hole, distinct from deep water
```

Add the case to `TargetColor` (before the `_ => FloorColor` default):

```csharp
		TileType.DeepWater => DeepWaterColor,
		TileType.Plank => PlankColor,
		TileType.Pit => PitColor,
		_ => FloorColor,
```

- [ ] **Step 2: SfxLibrary — falling-scream placeholder**

In `game/audio/SfxLibrary.cs`, add next to the other one-shots (after `Squelch`):

```csharp
	public static AudioStream Fall => Get("fall", () => Tone(0.50f, 700f, 90f)); // long descending wail — falling
```

- [ ] **Step 3: MatchAudio — play the fall SFX on a Fell death**

In `game/net/MatchAudio.cs`, replace the death-SFX selection (currently the ternary on lines 77-79):

```csharp
				bool prevAlive = !_prevAlive.TryGetValue(m.Id, out var al) || al;
				if (prevAlive && !m.Alive)
					OneShot(m.Cause switch
					{
						DeathCause.Drowned => SfxLibrary.Splash,
						DeathCause.Fell    => SfxLibrary.Fall,
						_                  => SfxLibrary.Death,
					}, WorldOf(m.X, m.Y));
				_prevAlive[m.Id] = m.Alive;
```

- [ ] **Step 4: DeathFeed — banner + toast for Fell**

In `game/ui/DeathFeed.cs`, add the `Fell` arm to `ShowBanner`'s switch:

```csharp
			DeathCause.Drowned => "YOU HAVE DROWNED",
			DeathCause.Exploded => "YOU WERE BLOWN UP",
			DeathCause.Fell => "YOU FELL INTO A PIT",
			_ => "YOU DIED",
```

And to `PushToast`'s switch:

```csharp
			DeathCause.Drowned => $"{name} drowned",
			DeathCause.Exploded => $"{name} was blown up",
			DeathCause.Fell => $"{name} fell into a pit",
			DeathCause.Left => $"{name} left",
			_ => $"{name} died",
```

- [ ] **Step 5: Build and headless-boot to verify**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors.

Then (PowerShell ONLY — not the Bash `godot` shim):
Run: `godot --headless --quit-after 180`
Expected: boots and quits cleanly (exit 0), no C# exceptions in output.

- [ ] **Step 6: Commit**

```bash
git add game/WorldRenderer.cs game/audio/SfxLibrary.cs game/net/MatchAudio.cs game/ui/DeathFeed.cs
git commit -m "feat(game): render pits, fall SFX, pit death banner/feed"
```

---

## Final verification (after all tasks)

- [ ] Full Core suite green: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` — expect all pass (prior 184 + new pit tests).
- [ ] Solution builds clean: `dotnet build Miner49er.sln` — 0 errors.
- [ ] Headless boot clean: `godot --headless --quit-after 180` (PowerShell) — exit 0.
- [ ] Hand off to the user for play-test: enable the **Pits** checkbox in the lobby, confirm pits render as black holes, are revealed only by fog, kill on entry with the "fell into a pit" feed + fall SFX, can be bridged by a held water-plank, and that maps stay traversable (you can always reach the center). Verify a normal match with Pits **off** is unchanged.

## Notes for the implementer

- **Why no snapshot/codec changes:** pits are static map terrain regenerated identically on both peers from `(seed, MapConfig)`; `DeathCause.Fell` rides the existing `MinerSnapshot.Cause`; plank-over-pit rides the existing `PlankPlaced`/`TileChange` path. Do not add an `ItemSnapshot`-style sync for pits.
- **Pre-existing untracked working-tree clutter** (`assets/Splash.png*`, CRLF-only `project.godot`/`game/Splash.tscn`, `.uid` files, `.superpowers/`) is unrelated — leave it alone, do not stage it.
- **Optional polish (skipped for simplicity):** a faint rim around pit tiles. The current renderer fills each tile with a single color via `DrawRect`; a rim would need extra per-tile draw logic. The near-black fill is enough to read as a hole — add a rim later only if play-test readability calls for it.
