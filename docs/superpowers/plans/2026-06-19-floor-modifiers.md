# Floor Modifiers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-floor modifiers to Expedition mode — each non-clean floor rolls one random twist (Dark Mine, Unstable, Monster Surge, Flooded, or Haste) derived deterministically from the match seed.

**Architecture:** A `FloorModifier` enum + static helpers live in Core. `Pick(matchSeed, floor)` derives the modifier from the seed; `Apply(modifier, mapConfig, simConfig)` mutates both configs before the floor is generated. Both host and client call `Pick` independently — no network changes needed. Display (banner + HUD) reads from the same `Pick` call in `Main.cs`.

**Tech Stack:** C# / .NET 8, Godot 4.6.3, xUnit tests.

## Global Constraints

- Core project (`src/Miner49er.Core/`) uses **4-space** indentation.
- Game project (`game/`) uses **TAB** indentation.
- Floor modifiers apply in **Expedition mode only**. Other game modes are unaffected.
- Clean floors: any floor where `floor % 4 == 0` (4, 8, 12, 16, 20) and floor 21 (boss) → `FloorModifier.None`.
- `MonsterCountMultiplier` default is `1.0f` — existing non-Expedition paths are unaffected.
- Run tests: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- Run build: `dotnet build` from repo root.

---

### Task 1: FloorModifier Core type + SimConfig field + tests

**Files:**
- Create: `src/Miner49er.Core/Sim/FloorModifier.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Create: `src/Miner49er.Core.Tests/FloorModifierTests.cs`

**Interfaces:**
- Produces:
  - `enum FloorModifier { None, DarkMine, Unstable, MonsterSurge, Flooded, Haste }`
  - `static class FloorModifiers`
    - `static FloorModifier Pick(int matchSeed, int floor)`
    - `static void Apply(FloorModifier mod, MapConfig map, SimConfig sim)`
    - `static string DisplayName(FloorModifier mod)`
  - `SimConfig.MonsterCountMultiplier` — `float`, default `1.0f`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/FloorModifierTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class FloorModifierTests
{
    [Theory]
    [InlineData(4)] [InlineData(8)] [InlineData(12)] [InlineData(16)] [InlineData(20)]
    public void Pick_returns_None_for_clean_floors(int floor)
        => Assert.Equal(FloorModifier.None, FloorModifiers.Pick(42, floor));

    [Fact]
    public void Pick_returns_None_for_boss_floor()
        => Assert.Equal(FloorModifier.None, FloorModifiers.Pick(42, 21));

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(5)] [InlineData(7)] [InlineData(19)]
    public void Pick_returns_modifier_for_non_clean_floors(int floor)
        => Assert.NotEqual(FloorModifier.None, FloorModifiers.Pick(42, floor));

    [Fact]
    public void Pick_is_deterministic()
    {
        var a = FloorModifiers.Pick(123, 7);
        var b = FloorModifiers.Pick(123, 7);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Pick_varies_by_seed()
    {
        var results = Enumerable.Range(0, 20)
            .Select(s => FloorModifiers.Pick(s * 100, 1))
            .Distinct().ToList();
        Assert.True(results.Count > 1);
    }

    [Fact]
    public void All_five_modifiers_appear_across_seeds_and_floors()
    {
        var seen = new HashSet<FloorModifier>();
        for (int seed = 0; seed < 200; seed++)
            for (int floor = 1; floor <= 20; floor++)
            {
                var m = FloorModifiers.Pick(seed, floor);
                if (m != FloorModifier.None) seen.Add(m);
            }
        Assert.Contains(FloorModifier.DarkMine,     seen);
        Assert.Contains(FloorModifier.Unstable,      seen);
        Assert.Contains(FloorModifier.MonsterSurge,  seen);
        Assert.Contains(FloorModifier.Flooded,       seen);
        Assert.Contains(FloorModifier.Haste,         seen);
    }

    [Fact]
    public void Apply_None_is_noop()
    {
        var map = new MapConfig { PoolCount = 3, RiverCount = 2 };
        var sim = new SimConfig();
        int origPool   = map.PoolCount;
        double origVis = sim.VisionRadius;
        FloorModifiers.Apply(FloorModifier.None, map, sim);
        Assert.Equal(origPool, map.PoolCount);
        Assert.Equal(origVis,  sim.VisionRadius);
    }

    [Fact]
    public void Apply_DarkMine_halves_vision_radius()
    {
        var sim = new SimConfig { VisionRadius = 6 };
        FloorModifiers.Apply(FloorModifier.DarkMine, new MapConfig(), sim);
        Assert.Equal(3, sim.VisionRadius);
    }

    [Fact]
    public void Apply_DarkMine_floors_vision_at_2()
    {
        var sim = new SimConfig { VisionRadius = 3 };
        FloorModifiers.Apply(FloorModifier.DarkMine, new MapConfig(), sim);
        Assert.Equal(2, sim.VisionRadius); // 3/2=1, floored to 2
    }

    [Fact]
    public void Apply_Unstable_enables_caveins_and_doubles_crack_sites()
    {
        var map = new MapConfig { CaveIns = false, CrackSiteCount = 4 };
        FloorModifiers.Apply(FloorModifier.Unstable, map, new SimConfig());
        Assert.True(map.CaveIns);
        Assert.Equal(8, map.CrackSiteCount);
    }

    [Fact]
    public void Apply_MonsterSurge_sets_monster_count_multiplier()
    {
        var sim = new SimConfig();
        FloorModifiers.Apply(FloorModifier.MonsterSurge, new MapConfig(), sim);
        Assert.Equal(1.5f, sim.MonsterCountMultiplier);
    }

    [Fact]
    public void Apply_Flooded_increases_pool_and_river_counts()
    {
        var map = new MapConfig { PoolCount = 3, RiverCount = 2 };
        FloorModifiers.Apply(FloorModifier.Flooded, map, new SimConfig());
        Assert.Equal(6, map.PoolCount);
        Assert.Equal(4, map.RiverCount);
    }

    [Fact]
    public void Apply_Haste_reduces_all_move_cadences_by_30_percent()
    {
        var sim = new SimConfig
        {
            BaseMoveSeconds         = 0.12,
            MonsterSlimeMoveSeconds = 0.5,
            MonsterGhostMoveSeconds = 1.0,
            MonsterGoatMoveSeconds  = 0.15,
        };
        FloorModifiers.Apply(FloorModifier.Haste, new MapConfig(), sim);
        Assert.Equal(0.12 * 0.7, sim.BaseMoveSeconds,         precision: 6);
        Assert.Equal(0.5  * 0.7, sim.MonsterSlimeMoveSeconds, precision: 6);
        Assert.Equal(1.0  * 0.7, sim.MonsterGhostMoveSeconds, precision: 6);
        Assert.Equal(0.15 * 0.7, sim.MonsterGoatMoveSeconds,  precision: 6);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FloorModifier"
```

Expected: build error — `FloorModifier` and `FloorModifiers` do not exist yet.

- [ ] **Step 3: Add `MonsterCountMultiplier` to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the `RequireChestForEscape` line:

```csharp
    public float MonsterCountMultiplier { get; set; } = 1.0f;
```

- [ ] **Step 4: Create FloorModifier.cs**

Create `src/Miner49er.Core/Sim/FloorModifier.cs`:

```csharp
namespace Miner49er.Core;

public enum FloorModifier { None, DarkMine, Unstable, MonsterSurge, Flooded, Haste }

public static class FloorModifiers
{
    private static readonly FloorModifier[] Pool =
    {
        FloorModifier.DarkMine,
        FloorModifier.Unstable,
        FloorModifier.MonsterSurge,
        FloorModifier.Flooded,
        FloorModifier.Haste,
    };

    public static FloorModifier Pick(int matchSeed, int floor)
    {
        if (floor >= 21 || floor % 4 == 0) return FloorModifier.None;
        var rng = new System.Random(matchSeed ^ (floor * 7919));
        return Pool[rng.Next(Pool.Length)];
    }

    public static void Apply(FloorModifier mod, MapConfig map, SimConfig sim)
    {
        switch (mod)
        {
            case FloorModifier.DarkMine:
                sim.VisionRadius = System.Math.Max(2, sim.VisionRadius / 2);
                break;
            case FloorModifier.Unstable:
                map.CaveIns = true;
                map.CrackSiteCount *= 2;
                break;
            case FloorModifier.MonsterSurge:
                sim.MonsterCountMultiplier = 1.5f;
                break;
            case FloorModifier.Flooded:
                map.PoolCount  += 3;
                map.RiverCount += 2;
                break;
            case FloorModifier.Haste:
                sim.BaseMoveSeconds         *= 0.7;
                sim.MonsterSlimeMoveSeconds *= 0.7;
                sim.MonsterGhostMoveSeconds *= 0.7;
                sim.MonsterGoatMoveSeconds  *= 0.7;
                break;
        }
    }

    public static string DisplayName(FloorModifier mod) => mod switch
    {
        FloorModifier.DarkMine     => "DARK MINE",
        FloorModifier.Unstable     => "UNSTABLE",
        FloorModifier.MonsterSurge => "MONSTER SURGE",
        FloorModifier.Flooded      => "FLOODED",
        FloorModifier.Haste        => "HASTE",
        _                          => "",
    };
}
```

- [ ] **Step 5: Run tests — verify all pass**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FloorModifier"
```

Expected: all 13 tests PASS.

- [ ] **Step 6: Run full test suite**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

Expected: all tests pass, no regressions.

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/FloorModifier.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core.Tests/FloorModifierTests.cs
git commit -m "feat(core): FloorModifier — Pick/Apply/DisplayName + MonsterCountMultiplier"
```

---

### Task 2: MatchHost wiring — apply modifier in AdvanceFloor

**Files:**
- Modify: `game/net/MatchHost.cs` (lines 171–244, the `AdvanceFloor` method)

**Interfaces:**
- Consumes: `FloorModifiers.Pick(int, int)`, `FloorModifiers.Apply(FloorModifier, MapConfig, SimConfig)` from Task 1.
- Consumes: `SimConfig.MonsterCountMultiplier` from Task 1.

- [ ] **Step 1: Build to confirm baseline**

```
dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Replace the map-generation and sim-creation block in `AdvanceFloor`**

In `game/net/MatchHost.cs`, replace the section from `GeneratedMap newMap;` through `var newSim = new Simulation(...)` (currently lines ~188–205) with:

```csharp
		var modifier = FloorModifiers.Pick(nm.MatchSeed, newFloor);

		var simCfg = new SimConfig
		{
			BaseMoveSeconds       = nm.MatchBaseMoveSeconds,
			Seed                  = floorSeed,
			RequireChestForEscape = newFloor == 21,
		};

		GeneratedMap newMap;
		if (newFloor == 21)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(newFloor, floorSeed);
			FloorModifiers.Apply(modifier, mapCfg, simCfg);
			newMap = MapGenerator.Generate(mapCfg);
		}

		var newSim = new Simulation(
			newMap.Grid,
			simCfg,
			newMap.Center,
			timeLimitSeconds: null,
			flooding: false,
			newMap.EscapeTile);
```

- [ ] **Step 3: Apply `MonsterCountMultiplier` when placing monsters**

Find the monster-placement block in `AdvanceFloor` (currently `int monsterCount = MonsterRoster.CountFor(...)`) and replace:

```csharp
			int monsterCount = MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor);
```

with:

```csharp
			int monsterCount = (int)(MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor)
			                         * simCfg.MonsterCountMultiplier);
```

- [ ] **Step 4: Build**

```
dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```
git add game/net/MatchHost.cs
git commit -m "feat(game): apply floor modifier in MatchHost.AdvanceFloor"
```

---

### Task 3: Client map gen + display (banner, HUD, remove "need 50%")

**Files:**
- Modify: `game/net/MatchClient.cs` (`ResetFloor` method)
- Modify: `game/Main.cs` (`_Ready`, `OnNewFloor`, `_PhysicsProcess`)

**Interfaces:**
- Consumes: `FloorModifiers.Pick`, `FloorModifiers.Apply`, `FloorModifiers.DisplayName`, `FloorModifier` enum from Task 1.

- [ ] **Step 1: MatchClient.ResetFloor — apply modifier to client-side map gen**

In `game/net/MatchClient.cs`, replace the single-line map generation in `ResetFloor`:

```csharp
		GeneratedMap newMap = (floor == 21)
			? MapGenerator.GenerateBossFloor(floorSeed)
			: MapGenerator.Generate(MapConfig.FloorConfig(floor, floorSeed));
```

with:

```csharp
		GeneratedMap newMap;
		if (floor == 21)
		{
			newMap = MapGenerator.GenerateBossFloor(floorSeed);
		}
		else
		{
			var mapCfg = MapConfig.FloorConfig(floor, floorSeed);
			FloorModifiers.Apply(FloorModifiers.Pick(nm.MatchSeed, floor), mapCfg, new SimConfig());
			newMap = MapGenerator.Generate(mapCfg);
		}
```

- [ ] **Step 2: Main.cs `_Ready` — apply modifier to floor-1 client map**

In `game/Main.cs`, in `_Ready()`, replace the client-side map generation line:

```csharp
		var map = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale));
```

with:

```csharp
		var f1Modifier = nm.MatchMode == GameMode.Expedition ? FloorModifiers.Pick(seed, 1) : FloorModifier.None;
		var clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
		FloorModifiers.Apply(f1Modifier, clientMapCfg, new SimConfig());
		var map = MapGenerator.Generate(clientMapCfg);
```

- [ ] **Step 3: Main.cs `_Ready` — apply modifier to floor-1 host sim**

Inside the `if (nm.IsHost)` block, replace the host map generation and `Simulation` construction. Currently it reads:

```csharp
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale));
			GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.EscapeTile : null;
			var sim = new Simulation(
				hostMap.Grid,
				new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = seed },
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding,
				escapeTile);
```

Replace with:

```csharp
			var hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
			var f1SimCfg = new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = seed };
			FloorModifiers.Apply(f1Modifier, hostMapCfg, f1SimCfg);
			var hostMap = MapGenerator.Generate(hostMapCfg);
			GridPos? escapeTile = nm.MatchMode == GameMode.Expedition ? hostMap.EscapeTile : null;
			var sim = new Simulation(
				hostMap.Grid,
				f1SimCfg,
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding,
				escapeTile);
```

Note: `f1Modifier` was computed in Step 2 (before the `if (nm.IsHost)` block) so it is in scope here.

- [ ] **Step 4: Main.cs `OnNewFloor` — add modifier suffix to floor banner**

In `OnNewFloor`, replace the `Text` assignment inside the new `Label { ... }`:

```csharp
			Text = floor == 21 ? "BOSS FLOOR" : $"FLOOR {floor}",
```

with:

```csharp
			Text = floor == 21
				? "BOSS FLOOR"
				: (FloorModifiers.Pick(NetworkManager.Instance.MatchSeed, floor) is var bMod && bMod != FloorModifier.None
					? $"FLOOR {floor}: {FloorModifiers.DisplayName(bMod)}"
					: $"FLOOR {floor}"),
```

- [ ] **Step 5: Main.cs `_PhysicsProcess` — update HUD objective**

In `_PhysicsProcess`, find the Expedition HUD block. Currently it reads:

```csharp
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
```

Replace with:

```csharp
					if (NetworkManager.Instance.MatchMode == GameMode.Expedition)
					{
						var nm2 = NetworkManager.Instance;
						string hearts = new string('♥', Math.Max(0, _client.Lives));
						var hudMod = FloorModifiers.Pick(nm2.MatchSeed, nm2.MatchFloor);
						string modTag = hudMod != FloorModifier.None ? $"  [{FloorModifiers.DisplayName(hudMod)}]" : "";
						if (nm2.MatchFloor == 21)
						{
							objective = $"{hearts}  BOSS FLOOR  Reach the chest!";
						}
						else if (_client.EscapeOpen)
						{
							objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold ✓ — ESCAPE!{modTag}";
						}
						else
						{
							int pct = _client.StartingGoldCount > 0
								? (int)(100.0 * (_client.StartingGoldCount - _client.GoldRemaining) / _client.StartingGoldCount)
								: 0;
							objective = $"{hearts}  Floor {nm2.MatchFloor}/20  Gold: {pct}%{modTag}";
						}
					}
```

- [ ] **Step 6: Build**

```
dotnet build
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Run full test suite**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 8: Commit**

```
git add game/net/MatchClient.cs game/Main.cs
git commit -m "feat(game): wire floor modifiers into client map gen and HUD/banner display"
```
