# Co-op Expedition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Expedition as a Lobby mode so 2–8 players can play dungeon floors co-operatively, sharing a lives pool of 2 per player, with floors advancing only when all living miners reach the exit.

**Architecture:** Surgical changes across six files — no new types. `RoundResolver` switches expedition exit check to `alive.All(on exit)`. `MatchHost` computes lives as `2 × playerCount` and spawns every peer on floor advance. `NetworkManager.StartMatch`/`BeginMatch` broadcasts `mapScale`. Lobby gains an Expedition option with a map-size picker that hides the time-limit row.

**Tech Stack:** Godot 4.6.3 .NET/C#; xUnit for Core tests. Run `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` after each Core change. Build Godot via PowerShell: `godot --path game --headless --build-solutions --quit-after 120` (NEVER Bash for godot). TAB indentation in `game/`; 4-space in `src/`.

## Global Constraints

- Never `git add -A`; never stage `.superpowers/`, `*.png.import`, `*.uid`
- Run Godot builds via PowerShell only (Bash shim gives false "assemblies not found")
- `game/` files use TAB indentation; `src/` files use 4-space indentation
- All `ConfigFile` integer reads require `(int)(long)` cast
- `MatchMapScale` is now set exclusively via `BeginMatch` — remove the standalone `NetworkManager.Instance.MatchMapScale = ...` assignment in `MainMenu.cs`

---

### Task 1: Core — RoundResolver co-op exit + MapConfig.FloorConfig playerCount

**Files:**
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs:25-33`
- Modify: `src/Miner49er.Core/Map/MapConfig.cs:77-87`
- Modify: `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs`
- Modify: `src/Miner49er.Core.Tests/MapConfigFloorTests.cs`

**Interfaces:**
- Produces: `MapConfig.FloorConfig(int floor, int seed, int playerCount = 1)` — Tasks 3 and 4 call this with `nm.MatchPlayerCount`
- Produces: `RoundResult.NextFloor(alive[0].Id)` fires only when ALL alive miners are on the exit tile

- [ ] **Step 1: Write failing RoundResolver tests for co-op exit**

Add to `src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs` after the last test:

```csharp
// Co-op: helper that builds a 6×3 all-floor grid with two miners
private static Simulation SetupCoopNoGold(GridPos m1Start, GridPos m2Start, GridPos exit)
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
    sim.AddMiner(1, m1Start);
    sim.AddMiner(2, m2Start);
    return sim;
}

[Fact]
public void Coop_only_one_on_exit_does_not_clear_floor()
{
    // Miner 1 on exit, miner 2 elsewhere — should NOT clear.
    var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(3, 1), exit: new GridPos(0, 1));

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.False(result.FloorCleared);
    Assert.False(result.IsOver);
}

[Fact]
public void Coop_both_on_exit_clears_floor()
{
    // Both miners on exit tile — should clear.
    var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(0, 1), exit: new GridPos(0, 1));

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.True(result.FloorCleared);
    Assert.False(result.IsOver);
}

[Fact]
public void Coop_dead_partner_does_not_block_floor_clear()
{
    // Miner 2 is dead; miner 1 on exit — alive.All() covers only the living.
    var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(3, 1), exit: new GridPos(0, 1));
    sim.KillMiner(2);

    var result = RoundResolver.Resolve(sim, GameMode.Expedition);

    Assert.True(result.FloorCleared);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "Coop" -v normal
```

Expected: 3 failures (Coop_only_one... FAIL, Coop_both... FAIL, Coop_dead... FAIL)

- [ ] **Step 3: Update RoundResolver**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, replace lines 25–34:

```csharp
        if (mode == GameMode.Expedition)
        {
            if (alive.Count == 0) return RoundResult.Loss();
            if (sim.EscapeOpen && sim.EscapeTile is { } exit)
            {
                if (alive.All(m => m.Pos == exit))
                    return RoundResult.NextFloor(alive[0].Id);
            }
            return RoundResult.Ongoing();
        }
```

- [ ] **Step 4: Write failing MapConfig test**

Add to `src/Miner49er.Core.Tests/MapConfigFloorTests.cs` after the last test:

```csharp
[Theory]
[InlineData(1, 2)] [InlineData(1, 4)] [InlineData(6, 3)]
public void FloorConfig_with_multiple_players_sets_player_count(int floor, int playerCount)
{
    var cfg = MapConfig.FloorConfig(floor, 42, playerCount);
    Assert.Equal(playerCount, cfg.PlayerCount);
}
```

- [ ] **Step 5: Run test to confirm it fails**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FloorConfig_with_multiple" -v normal
```

Expected: FAIL (FloorConfig passes playerCount=1 hardcoded)

- [ ] **Step 6: Update MapConfig.FloorConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, replace the `FloorConfig` method (lines 77–87):

```csharp
    /// <summary>Deterministic difficulty curve for Expedition dungeon floors 1–20.
    /// Size and hazards escalate in four bands; only the seed varies the layout.</summary>
    public static MapConfig FloorConfig(int floor, int seed, int playerCount = 1)
    {
        int mapScale = floor switch { <= 5 => 1, <= 10 => 2, <= 15 => 3, _ => 4 };
        bool pits    = floor >= 6;
        bool caveIns = floor >= 11;
        bool lava    = floor >= 16;
        var cfg = For(GameMode.Expedition, seed, playerCount, pits, caveIns, lava, mapScale);
        cfg.ChestCount = floor <= 10 ? 1 : 2;
        cfg.HasShop = floor % 4 == 0;
        return cfg;
    }
```

- [ ] **Step 7: Run all Core tests**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj -v normal
```

Expected: all pass (was 482, now 482 + 4 new = 486)

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/RoundResolverExpeditionTests.cs src/Miner49er.Core.Tests/MapConfigFloorTests.cs
git commit -m "feat(core): co-op expedition exit condition + FloorConfig playerCount param"
```

---

### Task 2: NetworkManager — mapScale in RPC + 8-player cap + MainMenu caller

**Files:**
- Modify: `game/net/NetworkManager.cs:55-76` (HostGame) and `game/net/NetworkManager.cs:269-293` (StartMatch/BeginMatch)
- Modify: `game/ui/MainMenu.cs:272-282` (OnSoloExpedition)

**Interfaces:**
- Produces: `StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale = 1)` — Task 5 (Lobby) calls this with mapScale
- Produces: `MatchMapScale` set by `BeginMatch` — Tasks 3 and 4 read `nm.MatchMapScale`

- [ ] **Step 1: Update NetworkManager.HostGame — 8-player cap**

In `game/net/NetworkManager.cs` line 59, change:
```csharp
		var err = peer.CreateServer(port, 7); // 7 clients + 1 host = 8 total
```
(was `peer.CreateServer(port, 8)`)

- [ ] **Step 2: Update StartMatch signature and body**

Replace the `StartMatch` method (around line 269):

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale = 1)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60; // a flooded match needs a clock
		var order = Players.Keys.ToArray(); // deterministic enough; same array sent to all
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, order); // host applies locally too
	}
```

- [ ] **Step 3: Update BeginMatch RPC signature and body**

Replace the `BeginMatch` method (around line 279):

```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale, long[] peerOrder)
	{
		MatchSeed = seed;
		MatchPlayerCount = playerCount;
		MatchMode = (GameMode)mode;
		MatchTimeLimitSeconds = timeLimitSeconds;
		MatchFlooding = flooding;
		MatchPits = pits;
		MatchCaveIns = caveIns;
		MatchLava = lava;
		MatchBaseMoveSeconds = baseMoveSeconds;
		MatchMapScale = mapScale;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 4: Update MainMenu.OnSoloExpedition — remove standalone MatchMapScale assignment**

In `game/ui/MainMenu.cs`, replace `OnSoloExpedition` (around line 272):

```csharp
	private void OnSoloExpedition()
	{
		SettingsStore.SavePlayerIdentity(_soloName.Text, _soloColor.Selected);
		SettingsStore.SaveSolo((int)_sizeSlider.Value, _soloFlood.ButtonPressed, _soloPits.ButtonPressed,
			_soloCaveIn.ButtonPressed, _soloLava.ButtonPressed);
		var err = NetworkManager.Instance.HostGame(_soloName.Text, _soloColor.Selected, overInternet: false);
		if (err != Error.Ok) return;
		NetworkManager.Instance.StartMatch(GameMode.Expedition, 0,
			_soloFlood.ButtonPressed, _soloPits.ButtonPressed, _soloCaveIn.ButtonPressed, _soloLava.ButtonPressed, 0.12f,
			(int)_sizeSlider.Value);
	}
```

- [ ] **Step 5: Build Godot to confirm no compile errors**

Run via PowerShell:
```powershell
$out = New-TemporaryFile
Start-Process -FilePath "godot" -ArgumentList "--path","game","--headless","--build-solutions","--quit-after","120" -NoNewWindow -Wait -RedirectStandardOutput $out.FullName
Get-Content $out.FullName | Where-Object { $_ -match 'error|Error|CS[0-9]' }
```

Expected: no CS errors in output (exit will say "no main scene" — that's normal)

- [ ] **Step 6: Commit**

```
git add game/net/NetworkManager.cs game/ui/MainMenu.cs
git commit -m "feat(net): broadcast mapScale in BeginMatch RPC; cap server at 8 total players"
```

---

### Task 3: MatchHost — 2×playerCount lives + spawn-all-on-advance + FloorConfig playerCount

**Files:**
- Modify: `game/net/MatchHost.cs`

**Interfaces:**
- Consumes: `MapConfig.FloorConfig(floor, seed, playerCount)` from Task 1
- Consumes: `nm.MatchPlayerCount` (already set by BeginMatch from Task 2)

- [ ] **Step 1: Update Begin() — lives = 2 × playerCount**

In `game/net/MatchHost.cs`, replace line 43:

```csharp
		_livesMax       = nm.MatchMode == GameMode.Expedition ? 2 * nm.MatchPlayerCount : 1;
```
(was `(nm.MatchMode == GameMode.Expedition && nm.MatchPlayerCount == 1) ? 3 : 1`)

- [ ] **Step 2: Update expeditionLoss block — remove soloMiner variable**

In `StepOnce()`, replace the `expeditionLoss` block (around lines 182–192):

```csharp
			bool expeditionLoss = nm.MatchMode == GameMode.Expedition && result.WinnerId == -1;
			if (expeditionLoss)
			{
				_livesRemaining--;
				if (_livesRemaining > 0)
				{
					AdvanceFloor(_peerToMiner.Values.First(), sameFloor: true);
					return;
				}
			}
```

- [ ] **Step 3: Update AdvanceFloor — spawn all miners + FloorConfig playerCount**

In `AdvanceFloor`, replace the section that builds the new sim and adds the miner. The current block (around lines 249–285) does:
```csharp
GridPos spawn = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
// nudge
newSim.AddMiner(minerId, spawn, invulRemaining: 3.0);
if (_permLevels.TryGetValue(minerId, out var levels))
    newSim.SetPermLevels(minerId, levels.Speed, levels.Vision, levels.Blast);
if (newFloor == 21) { ... }
else
{
    int monsterCount = (int)(MonsterRoster.CountFor(...) * simCfg.MonsterCountMultiplier);
    var roster = MonsterSpawner.Place(newMap.Grid, spawn, monsterCount);
    ...
}
```

Replace with:

```csharp
			// Spawn every peer; miner IDs are 1-based so spawn index = minerId - 1.
			GridPos monsterRef = newMap.Spawns.Count > 0 ? newMap.Spawns[0] : newMap.Center;
			foreach (var mid in _peerToMiner.Values)
			{
				int idx = mid - 1;
				GridPos sp = idx < newMap.Spawns.Count ? newMap.Spawns[idx] : newMap.Spawns[0];
				if (newMap.EscapeTile is GridPos escapePos && sp == escapePos)
				{
					var east = new GridPos(sp.X + 1, sp.Y);
					if (east.X < newMap.Grid.Width && newMap.Grid.Get(east) == TileType.Floor)
						sp = east;
				}
				newSim.AddMiner(mid, sp, invulRemaining: 3.0);
				if (_permLevels.TryGetValue(mid, out var lvl))
					newSim.SetPermLevels(mid, lvl.Speed, lvl.Vision, lvl.Blast);
			}

			if (newFloor == 21)
			{
				newSim.AddOctopus(newMap.Center);
			}
			else
			{
				int monsterCount = (int)(MonsterRoster.CountFor(newMap.Grid.Width, newMap.Grid.Height, newFloor)
				                         * simCfg.MonsterCountMultiplier);
				var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount);
				for (int i = 0; i < roster.Count; i++)
					newSim.AddMonster(i + 1, roster[i].Pos, roster[i].Kind);
			}
```

- [ ] **Step 4: Update FloorConfig call in AdvanceFloor to pass playerCount**

Find the line (around line 244):
```csharp
				var mapCfg = MapConfig.FloorConfig(newFloor, floorSeed);
```
Replace with:
```csharp
				var mapCfg = MapConfig.FloorConfig(newFloor, floorSeed, nm.MatchPlayerCount);
```

- [ ] **Step 5: Build Godot**

```powershell
$out = New-TemporaryFile
Start-Process -FilePath "godot" -ArgumentList "--path","game","--headless","--build-solutions","--quit-after","120" -NoNewWindow -Wait -RedirectStandardOutput $out.FullName
Get-Content $out.FullName | Where-Object { $_ -match 'error|Error|CS[0-9]' }
```

Expected: no CS errors

- [ ] **Step 6: Commit**

```
git add game/net/MatchHost.cs
git commit -m "feat(host): 2×playerCount shared lives; spawn all peers on floor advance"
```

---

### Task 4: MatchClient + Main.cs — FloorConfig playerCount + co-op win display

**Files:**
- Modify: `game/net/MatchClient.cs:146`
- Modify: `game/Main.cs:278`

**Interfaces:**
- Consumes: `nm.MatchPlayerCount` (set by BeginMatch, Task 2)
- Consumes: `MapConfig.FloorConfig(floor, seed, playerCount)` (Task 1)

- [ ] **Step 1: Update MatchClient.ResetFloor — pass playerCount to FloorConfig**

In `game/net/MatchClient.cs`, replace line 146:

```csharp
			var mapCfg = MapConfig.FloorConfig(floor, floorSeed, nm.MatchPlayerCount);
```
(was `MapConfig.FloorConfig(floor, floorSeed)`)

- [ ] **Step 2: Fix co-op expedition win display in Main.cs**

In `game/Main.cs`, replace line 278:

```csharp
			bool won = expedition ? winnerPeerId != -1 : winnerPeerId == nm.LocalId;
```
(was `bool won = winnerPeerId == nm.LocalId`)

This means: in expedition mode, any non-loss result (`winnerPeerId != -1`) is a win for all players. In competitive modes the winner check is peer-specific as before.

- [ ] **Step 3: Build Godot**

```powershell
$out = New-TemporaryFile
Start-Process -FilePath "godot" -ArgumentList "--path","game","--headless","--build-solutions","--quit-after","120" -NoNewWindow -Wait -RedirectStandardOutput $out.FullName
Get-Content $out.FullName | Where-Object { $_ -match 'error|Error|CS[0-9]' }
```

Expected: no CS errors

- [ ] **Step 4: Commit**

```
git add game/net/MatchClient.cs game/Main.cs
git commit -m "feat(client): pass playerCount to FloorConfig; fix co-op expedition win display"
```

---

### Task 5: Lobby + SettingsStore — Expedition mode, map size picker, conditional time row

**Files:**
- Modify: `game/audio/SettingsStore.cs:151-178` (LoadLobby/SaveLobby)
- Modify: `game/ui/Lobby.cs`

**Interfaces:**
- Consumes: `NetworkManager.StartMatch(..., int mapScale)` from Task 2
- Produces: `SettingsStore.LoadLobby()` returns 8-tuple including `mapScale`; `SaveLobby` takes 8 params

- [ ] **Step 1: Update SettingsStore.LoadLobby — add map_scale**

In `game/audio/SettingsStore.cs`, replace the `LoadLobby` method:

```csharp
	public static (int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale) LoadLobby()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (0, 60, false, false, false, false, 1, 1);
		int  mode    = (int)(long)cfg.GetValue(LobbySection, "game_mode",  0L);
		int  time    = (int)(long)cfg.GetValue(LobbySection, "time_limit", 60L);
		bool flood   = (bool)cfg.GetValue(LobbySection, "flood",   false);
		bool pits    = (bool)cfg.GetValue(LobbySection, "pits",    false);
		bool caveIns = (bool)cfg.GetValue(LobbySection, "caveins", false);
		bool lava    = (bool)cfg.GetValue(LobbySection, "lava",    false);
		int  speed   = (int)(long)cfg.GetValue(LobbySection, "speed",     1L);
		int  scale   = (int)(long)cfg.GetValue(LobbySection, "map_scale", 1L);
		return (Mathf.Clamp(mode, 0, 3), time, flood, pits, caveIns, lava,
		        Mathf.Clamp(speed, 0, 2), Mathf.Clamp(scale, 1, 4));
	}
```

(Note: `Mathf.Clamp(mode, 0, 3)` now allows mode 3 = Expedition)

- [ ] **Step 2: Update SettingsStore.SaveLobby — add mapScale parameter**

Replace the `SaveLobby` method:

```csharp
	public static void SaveLobby(int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale)
	{
		var cfg = new ConfigFile();
		cfg.Load(Path);
		cfg.SetValue(LobbySection, "game_mode",  (long)gameMode);
		cfg.SetValue(LobbySection, "time_limit", (long)timeLimit);
		cfg.SetValue(LobbySection, "flood",      flood);
		cfg.SetValue(LobbySection, "pits",       pits);
		cfg.SetValue(LobbySection, "caveins",    caveIns);
		cfg.SetValue(LobbySection, "lava",       lava);
		cfg.SetValue(LobbySection, "speed",      (long)speed);
		cfg.SetValue(LobbySection, "map_scale",  (long)mapScale);
		cfg.Save(Path);
	}
```

- [ ] **Step 3: Add `_mapSizePicker` field to Lobby.cs**

At the top of `game/ui/Lobby.cs`, add a field alongside the other private fields:

```csharp
	private OptionButton _mapSizePicker = null!;
```

- [ ] **Step 4: Update Lobby._Ready — add Expedition to mode picker + add map size picker**

In `Lobby._Ready()`, after the existing `_modePicker` items, add Expedition:

```csharp
		_modePicker.AddItem("Expedition", (int)GameMode.Expedition);
```

(After the existing `_modePicker.AddItem("Reach Center", (int)GameMode.ReachCenter);` line)

Update the `LoadLobby` destructuring (the `var (savedMode, ...)` line) to include `savedMapScale`:

```csharp
		var (savedMode, savedTime, savedFlood, savedPits, savedCaveIn, savedLava, savedSpeed, savedMapScale) = SettingsStore.LoadLobby();
```

After adding `_timePicker` to `box`, add `_mapSizePicker` immediately after:

```csharp
		_mapSizePicker = new OptionButton();
		_mapSizePicker.AddItem("Small",  1);
		_mapSizePicker.AddItem("Medium", 2);
		_mapSizePicker.AddItem("Large",  3);
		_mapSizePicker.AddItem("Huge",   4);
		int sizeIdx = Enumerable.Range(0, _mapSizePicker.ItemCount)
			.FirstOrDefault(i => _mapSizePicker.GetItemId(i) == savedMapScale, 0);
		_mapSizePicker.Select(sizeIdx);
		_mapSizePicker.Visible = NetworkManager.Instance.IsHost && savedMode == (int)GameMode.Expedition;
		box.AddChild(_mapSizePicker);
```

- [ ] **Step 5: Wire mode-change to show/hide time vs map-size pickers**

After the line where `_modePicker.Visible = NetworkManager.Instance.IsHost;`, add a `RefreshModeControls` call and hook `ItemSelected`:

```csharp
		_modePicker.ItemSelected += _ => RefreshModeControls();
		RefreshModeControls(); // apply saved mode on first load
```

Add a private helper method at the bottom of the `Lobby` class (before the closing `}`):

```csharp
	private void RefreshModeControls()
	{
		bool isHost = NetworkManager.Instance.IsHost;
		bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
		_timePicker.Visible    = isHost && !expedition;
		_mapSizePicker.Visible = isHost && expedition;
	}
```

- [ ] **Step 6: Update Start Match handler to pass mapScale and updated SaveLobby signature**

Replace the entire `_startBtn.Pressed += () => { ... };` lambda:

```csharp
		_startBtn.Pressed += () =>
		{
			bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
			int mapScale = expedition ? _mapSizePicker.GetSelectedId() : 1;
			SettingsStore.SaveLobby(_modePicker.GetSelectedId(), _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed, _pitsCheck.ButtonPressed, _caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed, _speedPicker.Selected, mapScale);
			NetworkManager.Instance.StartMatch(
				(GameMode)_modePicker.GetSelectedId(),
				expedition ? 0 : _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed,
				_pitsCheck.ButtonPressed,
				_caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed,
				new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected],
				mapScale);
		};
```

- [ ] **Step 7: Build Godot**

```powershell
$out = New-TemporaryFile
Start-Process -FilePath "godot" -ArgumentList "--path","game","--headless","--build-solutions","--quit-after","120" -NoNewWindow -Wait -RedirectStandardOutput $out.FullName
Get-Content $out.FullName | Where-Object { $_ -match 'error|Error|CS[0-9]' }
```

Expected: no CS errors

- [ ] **Step 8: Run all Core tests one final time**

```
dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj -v normal
```

Expected: 486 passing

- [ ] **Step 9: Commit**

```
git add game/audio/SettingsStore.cs game/ui/Lobby.cs
git commit -m "feat(lobby): add Expedition mode with map-size picker; save/restore map_scale"
```

---

## Self-review checklist (for implementer)

After all tasks:
- [ ] Solo expedition (MainMenu → Solo panel) still works: enter, pick size, start — no Lobby involved
- [ ] Lobby shows "Expedition" in mode picker; selecting it hides time limit, shows map size
- [ ] 2-player co-op: both must step on exit to advance floor
- [ ] If one player dies (killed by monster), the other can still clear the floor and both advance
- [ ] Team wipe (both dead) decrements shared lives; floor retries with both respawned
- [ ] HUD shows shared hearts (♥♥♥♥ for 2 players) decrementing for all clients together
- [ ] Results show "You escaped with the gold!" for ALL players on expedition win
- [ ] Deathmatch modes (Last Man Standing, Gold Rush, Reach Center) unchanged
