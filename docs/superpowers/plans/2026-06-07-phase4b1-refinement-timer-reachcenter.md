# Phase 4b-1 Refinement — Lobby Time Limit & Reach-Center Map Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the match time limit a host-selectable lobby option (decoupled from mode, timeout→draw for LMS/Reach Center, timeout→gold for Gold Rush) and give Reach Center a larger, less-open map.

**Architecture:** Duration moves from a per-mode constant to a lobby dropdown threaded through `BeginMatch`; the resolver gains one universal "timed-out → draw" arm. Map tuning moves into a Core `MapConfig.For(mode, seed, playerCount)` factory that both the host sim grid and the client render grid use, keeping them deterministic and identical.

**Tech Stack:** C# / .NET 8, pure-C# `Miner49er.Core` (xUnit), Godot 4.6.3 (.NET) adapter at repo root.

**Build/test/run (PowerShell, not Bash):**
- Core tests: `dotnet test src/Miner49er.Core.Tests`
- Godot build: `dotnet build Miner49er.csproj`
- Headless smoke: `godot --headless --quit-after 180` (exit 0, no `ERROR`/`SCRIPT ERROR` lines)

**Indentation:** `src/Miner49er.Core/` and the test project use **4-space** indentation; `game/` files use **TAB** indentation. Match each file's existing whitespace exactly when editing.

**Transitional build note:** Task 1 removes `GameModeExtensions.TimeLimitSeconds()` from Core, which leaves `game/Main.cs` referencing a deleted method — so the **Godot project will not build until Task 3**. The Core suite stays green throughout. Tasks 1 and 2 therefore do NOT run the full Godot build as a pass gate; Task 3 closes the build and runs build + headless.

**Task order:** sequential (1 → 2 → 3); they share `game/` files and have a build dependency chain. Continues on the existing `phase4b1-modes-timer` branch.

**Spec:** `docs/superpowers/specs/2026-06-07-phase4b1-refinement-timer-reachcenter-design.md`

---

## Task 1: Core — universal timeout→draw, remove per-mode time mapping, add `MapConfig.For`

**Files:**
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs` (add universal timeout arm)
- Modify: `src/Miner49er.Core/Sim/GameMode.cs` (remove `GameModeExtensions`)
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (add `For` factory)
- Modify: `src/Miner49er.Core.Tests/RoundResolverTests.cs` (add timeout→draw tests)
- Modify: `src/Miner49er.Core.Tests/GameModeTests.cs` (remove the now-dead timing test)
- Create: `src/Miner49er.Core.Tests/MapConfigTests.cs`

- [ ] **Step 1: Write failing resolver tests for timeout→draw**

In `src/Miner49er.Core.Tests/RoundResolverTests.cs`, add these two tests inside the `RoundResolverTests` class (the existing `TwoMinerSim(GridPos? center = null, double? timeLimit = null)` helper supports both args):

```csharp
    [Fact]
    public void Last_man_standing_timeout_is_a_draw()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.Tick(5.0); // expire the clock; both miners still alive
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void Reach_center_timeout_without_arrival_is_a_draw()
    {
        var sim = TwoMinerSim(center: new GridPos(2, 2), timeLimit: 5.0);
        sim.Tick(5.0); // clock expires, nobody reached center
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }
```

- [ ] **Step 2: Run the resolver tests — expect FAIL**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~RoundResolverTests`
Expected: FAIL — with 2 alive and no mode-specific win, the current switch falls to `_ => new RoundResult(false, -1)`, so the round is reported not-over (both new tests expect over+draw).

- [ ] **Step 3: Add the universal timeout→draw arm**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, replace the `mode switch` block (lines 21-28) with:

```csharp
        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                => new RoundResult(true, sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => new RoundResult(true, MostGoldWinner(alive)),
            _ when sim.TimeExpired
                => new RoundResult(true, -1),   // any timed mode whose clock ran out → draw
            _ => new RoundResult(false, -1),
        };
```

Switch arms evaluate top-to-bottom, so Gold Rush's timeout→gold and Reach Center's reach→winner are decided before the universal timeout→draw fallback.

- [ ] **Step 4: Run the resolver tests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~RoundResolverTests`
Expected: PASS (all RoundResolver tests, including the prior Gold-Rush-timeout-awards and Reach-Center-winner tests, plus the 2 new draw tests).

- [ ] **Step 5: Remove the now-dead per-mode time mapping**

The match duration now comes from the lobby (Task 2), so the per-mode constant is dead. Replace the entire contents of `src/Miner49er.Core/Sim/GameMode.cs` with just the enum:

```csharp
namespace Miner49er.Core;

public enum GameMode { LastManStanding, GoldRush, ReachCenter }
```

Then remove the dead test from `src/Miner49er.Core.Tests/GameModeTests.cs` — delete this whole method (the first `[Fact]` in the file):

```csharp
    [Fact]
    public void GoldRush_is_timed_others_are_not()
    {
        Assert.Equal(120.0, GameMode.GoldRush.TimeLimitSeconds());
        Assert.Null(GameMode.LastManStanding.TimeLimitSeconds());
        Assert.Null(GameMode.ReachCenter.TimeLimitSeconds());
    }
```

(The remaining `GameModeTests` — the sim timer/center-fact tests — do not reference `TimeLimitSeconds` and stay as-is.)

- [ ] **Step 6: Run the GameMode + resolver tests — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests --filter "FullyQualifiedName~GameModeTests|FullyQualifiedName~RoundResolverTests"`
Expected: PASS. (The Core library and test project no longer reference `TimeLimitSeconds`; only `game/Main.cs` still does, and that's a Godot file fixed in Task 3.)

- [ ] **Step 7: Write failing tests for `MapConfig.For`**

Create `src/Miner49er.Core.Tests/MapConfigTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class MapConfigTests
{
    [Fact]
    public void Last_man_standing_uses_base_map_settings()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 7, playerCount: 3);
        Assert.Equal(7, cfg.Seed);
        Assert.Equal(3, cfg.PlayerCount);
        Assert.Equal(24, cfg.BaseWidth);
        Assert.Equal(24, cfg.BaseHeight);
        Assert.Equal(0.45f, cfg.InitialFloorChance);
    }

    [Fact]
    public void Gold_rush_also_uses_base_map_settings()
    {
        var cfg = MapConfig.For(GameMode.GoldRush, seed: 1, playerCount: 1);
        Assert.Equal(24, cfg.BaseWidth);
        Assert.Equal(24, cfg.BaseHeight);
        Assert.Equal(0.45f, cfg.InitialFloorChance);
    }

    [Fact]
    public void Reach_center_uses_a_larger_denser_map()
    {
        var cfg = MapConfig.For(GameMode.ReachCenter, seed: 1, playerCount: 1);
        Assert.Equal(40, cfg.BaseWidth);
        Assert.Equal(40, cfg.BaseHeight);
        Assert.Equal(0.42f, cfg.InitialFloorChance);
    }
}
```

- [ ] **Step 8: Run the MapConfig tests — expect FAIL**

Run: `dotnet test src/Miner49er.Core.Tests --filter FullyQualifiedName~MapConfigTests`
Expected: compile FAIL — `MapConfig` has no `For` method.

- [ ] **Step 9: Add the `MapConfig.For` factory**

In `src/Miner49er.Core/Map/MapConfig.cs`, add this static method inside the `MapConfig` class (e.g. right after the `Seed`/`PlayerCount`/size fields, or at the end of the class body — anywhere inside the class):

```csharp
    /// <summary>Builds a map config tuned for the given mode. Reach Center gets a
    /// larger, less-open map so the run to the centre is a real journey; other
    /// modes keep the base settings. Deterministic from (mode, seed, playerCount),
    /// so host and clients regenerate identical maps.</summary>
    public static MapConfig For(GameMode mode, int seed, int playerCount)
    {
        var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount };
        if (mode == GameMode.ReachCenter)
        {
            cfg.BaseWidth = 40;
            cfg.BaseHeight = 40;
            cfg.InitialFloorChance = 0.42f;
        }
        return cfg;
    }
```

- [ ] **Step 10: Run the full Core suite — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests`
Expected: PASS — ~112 tests (prior 108, minus the 1 removed timing test, plus 2 resolver draw tests and 3 `MapConfig.For` tests). (If `dotnet test` *fails to start* due to Smart App Control blocking `testhost.exe`, re-run it.)

- [ ] **Step 11: Commit**

```bash
git add src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core/Sim/GameMode.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/RoundResolverTests.cs src/Miner49er.Core.Tests/GameModeTests.cs src/Miner49er.Core.Tests/MapConfigTests.cs
git commit -m "feat(core): timed-out modes draw; lobby-driven duration; per-mode map via MapConfig.For"
```

(The Godot project intentionally does NOT build yet — `Main.cs` still calls the removed `TimeLimitSeconds()`. Task 3 fixes it.)

---

## Task 2: Netcode + lobby — host-selectable time limit

**Files:**
- Modify: `game/net/NetworkManager.cs` (TAB indent) — `MatchTimeLimitSeconds`; `StartMatch`/`BeginMatch` carry the time limit
- Modify: `game/ui/Lobby.cs` (TAB indent) — host-only "Time limit" `OptionButton`

- [ ] **Step 1: Thread the time limit through `NetworkManager`**

In `game/net/NetworkManager.cs`, add the property after `MatchMode`. The existing lines:
```csharp
	public GameMode MatchMode { get; private set; }
	public long[] PeerOrder { get; private set; } = System.Array.Empty<long>();
```
become:
```csharp
	public GameMode MatchMode { get; private set; }
	public int MatchTimeLimitSeconds { get; private set; }
	public long[] PeerOrder { get; private set; } = System.Array.Empty<long>();
```

Replace the existing `StartMatch(GameMode mode)`:
```csharp
	public void StartMatch(GameMode mode)
	{
		if (!IsHost) return;
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, order);
		BeginMatch(seed, order.Length, (int)mode, order); // host applies locally too
	}
```
with:
```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds)
	{
		if (!IsHost) return;
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, order); // host applies locally too
	}
```

Replace the existing `BeginMatch(...)`:
```csharp
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
with:
```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 2: Add the host-only "Time limit" dropdown to `Lobby`**

In `game/ui/Lobby.cs`, add the field next to `_modePicker`:
```csharp
	private OptionButton _modePicker = null!;
	private OptionButton _timePicker = null!;
```

In `_Ready()`, the existing `_modePicker` block ends with `box.AddChild(_modePicker);`, immediately followed by the Start-button line `_startBtn = new Button { Text = "Start Match", Disabled = true };`. Insert this between them:
```csharp
		_timePicker = new OptionButton();
		_timePicker.AddItem("No Time Limit", 0);
		_timePicker.AddItem("1 min", 60);
		_timePicker.AddItem("2 min", 120);
		_timePicker.AddItem("3 min", 180);
		_timePicker.AddItem("5 min", 300);
		_timePicker.Select(1); // default 1 min
		_timePicker.Visible = NetworkManager.Instance.IsHost; // only the host chooses
		box.AddChild(_timePicker);
```
(The item ids ARE the durations in seconds, so `GetSelectedId()` returns seconds directly; `0` = none. `Select(1)` picks the "1 min" entry as the default.)

Change the Start handler:
```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch((GameMode)_modePicker.GetSelectedId());
```
to:
```csharp
		_startBtn.Pressed += () => NetworkManager.Instance.StartMatch((GameMode)_modePicker.GetSelectedId(), _timePicker.GetSelectedId());
```

- [ ] **Step 3: Build to confirm only the expected transitional error remains**

Run: `dotnet build Miner49er.csproj`
Expected: **FAIL with exactly one error** — in `game/Main.cs` (around the host sim construction), `'GameMode' does not contain a definition for 'TimeLimitSeconds'` (the method removed in Task 1). Confirm there are **no** errors originating from `NetworkManager.cs` or `Lobby.cs` (those compile cleanly). Task 3 fixes `Main.cs` and closes the build. If you see errors in NetworkManager/Lobby, fix them before committing.

- [ ] **Step 4: Commit**

```bash
git add game/net/NetworkManager.cs game/ui/Lobby.cs
git commit -m "feat(net): host selects a match time limit in the lobby"
```

---

## Task 3: Main wiring — per-mode map + synced time limit (closes the build)

**Files:**
- Modify: `game/Main.cs` (TAB indent) — both map generations use `MapConfig.For`; host sim time limit reads `nm.MatchTimeLimitSeconds`

- [ ] **Step 1: Generate the client render map via `MapConfig.For`**

In `game/Main.cs`, replace the client map-generation line (currently line 27):
```csharp
		var map = MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = playerCount });
```
with:
```csharp
		var map = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount));
```

- [ ] **Step 2: Generate the host sim map via `MapConfig.For` and read the synced time limit**

Still in `game/Main.cs`, inside the `if (nm.IsHost)` block, replace the current construction:
```csharp
			var mode = nm.MatchMode;
			var sim = new Simulation(
				MapGenerator.Generate(new MapConfig { Seed = seed, PlayerCount = playerCount }).Grid,
				new SimConfig(),
				map.Center,
				mode.TimeLimitSeconds());
```
with:
```csharp
			var sim = new Simulation(
				MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount)).Grid,
				new SimConfig(),
				map.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null);
```
(The second `MapGenerator.Generate` is intentional — the host sim must own a separate `TileGrid` from the client's render grid. Both now use the same `MapConfig.For(nm.MatchMode, …)`, so they stay byte-identical. `MatchTimeLimitSeconds == 0` → untimed `null`; otherwise the int seconds widen to `double?`.)

- [ ] **Step 3: Build the Godot project — expect success**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors (Task 1's removed method is no longer referenced).

- [ ] **Step 4: Headless smoke test — expect clean exit**

Run: `godot --headless --quit-after 180`
Expected: exit 0, no `ERROR`/`SCRIPT ERROR` lines.

- [ ] **Step 5: Run the full Core suite — expect PASS**

Run: `dotnet test src/Miner49er.Core.Tests`
Expected: PASS — ~112 tests, all green.

- [ ] **Step 6: Commit**

```bash
git add game/Main.cs
git commit -m "feat(game): per-mode map via MapConfig.For; host honors the lobby time limit"
```

---

## Done criteria

- Full `Miner49er.Core` xUnit suite green (~112 tests).
- `dotnet build Miner49er.csproj` 0 errors; `godot --headless --quit-after 180` exits 0 with no error lines.
- Play-test (user): lobby shows both a mode dropdown and a "Time limit" dropdown (default 1 min); Gold Rush scores most-gold at 0; LMS / Reach Center end in a draw at 0; "No Time Limit" = untimed; Reach Center map is noticeably larger and the run to centre is a real journey (tune `40`/`0.42f` in `MapConfig.For` if it needs more/less).
- Final opus code review, then merge the whole `phase4b1-modes-timer` branch to main.

## Untouched / still deferred to 4b-2

The Reach-Center alive-recheck trap, the host/client separate-`TileGrid` invariant, the `TileChange`→`TileType` seam, `DrownOccupants`, and the `IsWater()` dedup — all remain 4b-2 work (recorded in the phase-4 status memory).
