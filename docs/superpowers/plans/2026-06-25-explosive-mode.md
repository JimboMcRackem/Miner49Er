# Explosive Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a three-way explosive mode setting (Dynamite / Detonator Specials / Detonators Only) to the multiplayer lobby, propagated via the existing RPC pipeline so every peer generates the same map and the host simulation enforces the rule.

**Architecture:** `ExplosiveMode` enum lives in Core alongside the other simulation types. `SimConfig.DynamiteEnabled` gates timed-charge planting in `Simulation`. `MapConfig.For()` accepts the mode and sets `DetonatorCount` accordingly. `NetworkManager.BeginMatch` RPC carries the value to all peers so deterministic map generation stays in sync. The lobby adds an `OptionButton` (host-only) wired to `SettingsStore` persistence.

**Tech Stack:** C# / .NET 8, Godot 4 (game layer), xUnit (Core tests). Run tests with `dotnet test src/Miner49er.Core.Tests`. Run Godot headless via PowerShell ONLY (Bash shim breaks assemblies).

## Global Constraints

- `game/` files use TAB indentation; `src/` files use 4-space indentation.
- Never `git add -A`; never stage `.superpowers/`, `*.png.import`, `*.uid` files.
- All 499 existing Core tests must stay green after every task.
- `Expedition` mode map generation (`MapConfig.FloorConfig`) is **not** modified — it manages `DetonatorCount` by floor number independently.

---

## File Map

| File | Role |
|------|------|
| `src/Miner49er.Core/Sim/ExplosiveMode.cs` | **New** — enum definition |
| `src/Miner49er.Core/Sim/SimConfig.cs` | Add `DynamiteEnabled` property |
| `src/Miner49er.Core/Sim/Simulation.cs` | Guard in `TryStartPlanting` |
| `src/Miner49er.Core/Map/MapConfig.cs` | `For()` gains `explosive` param |
| `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs` | 2 new tests |
| `src/Miner49er.Core.Tests/MapConfigTests.cs` | 3 new tests |
| `game/net/NetworkManager.cs` | `StartMatch`, `BeginMatch` RPC, `MatchExplosive` property |
| `game/Main.cs` | Pass `MatchExplosive` to `MapConfig.For`; set `DynamiteEnabled` on SimConfig |
| `game/ui/Lobby.cs` | Add `_explosivePicker` OptionButton |
| `game/audio/SettingsStore.cs` | `LoadLobby`/`SaveLobby` gain `explosive` field |

---

### Task 1: Core — `ExplosiveMode` enum + `DynamiteEnabled` guard

**Files:**
- Create: `src/Miner49er.Core/Sim/ExplosiveMode.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (line ~589, `TryStartPlanting`)
- Test: `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`

**Interfaces:**
- Produces: `ExplosiveMode` enum with values `Dynamite = 0`, `DetonatorSpecials = 1`, `DetonatorsOnly = 2` — used by Tasks 2, 3, 4.
- Produces: `SimConfig.DynamiteEnabled` bool (default `true`) — used by Task 3.
- Produces: `Simulation.TryStartPlanting` returns `false` when `DynamiteEnabled` is `false` — tested here.

- [ ] **Step 1: Write the two failing tests**

Add to the bottom of `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`, before the final `}`:

```csharp
    [Fact]
    public void TryStartPlanting_blocked_when_dynamite_disabled()
    {
        var sim = FacingRockEast(out _, new SimConfig { DynamiteEnabled = false });
        Assert.False(sim.TryStartPlanting(1));
        Assert.Empty(sim.Charges);
    }

    [Fact]
    public void TryStartPlanting_succeeds_when_dynamite_enabled_explicitly()
    {
        var sim = FacingRockEast(out _, new SimConfig { DynamiteEnabled = true });
        Assert.True(sim.TryStartPlanting(1));
    }
```

- [ ] **Step 2: Run tests — expect 2 failures**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Failed: 2` (property `DynamiteEnabled` not found).

- [ ] **Step 3: Create `ExplosiveMode.cs`**

New file `src/Miner49er.Core/Sim/ExplosiveMode.cs`:

```csharp
namespace Miner49er.Core;

public enum ExplosiveMode { Dynamite = 0, DetonatorSpecials = 1, DetonatorsOnly = 2 }
```

- [ ] **Step 4: Add `DynamiteEnabled` to `SimConfig`**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add after the `RequireChestForEscape` line (currently line 57):

```csharp
    public bool DynamiteEnabled { get; set; } = true;
```

- [ ] **Step 5: Guard `TryStartPlanting` in `Simulation.cs`**

In `src/Miner49er.Core/Sim/Simulation.cs`, `TryStartPlanting` currently reads:

```csharp
    public bool TryStartPlanting(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
```

Change to:

```csharp
    public bool TryStartPlanting(int id)
    {
        var m = _miners[id];
        if (!m.Alive) return false;
        if (!Config.DynamiteEnabled) return false;
```

- [ ] **Step 6: Run tests — expect all passing**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Passed: 501, Failed: 0`

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/ExplosiveMode.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationExplosiveTests.cs
git commit -m "feat(core): ExplosiveMode enum + DynamiteEnabled guard in TryStartPlanting"
```

---

### Task 2: `MapConfig.For()` sets `DetonatorCount` from `ExplosiveMode`

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (line ~56, `For()` method)
- Test: `src/Miner49er.Core.Tests/MapConfigTests.cs`

**Interfaces:**
- Consumes: `ExplosiveMode` enum from Task 1.
- Produces: `MapConfig.For(... ExplosiveMode explosive = ExplosiveMode.Dynamite)` — used by Tasks 3 and 4.

- [ ] **Step 1: Write the three failing tests**

Add to the bottom of `src/Miner49er.Core.Tests/MapConfigTests.cs`, before the final `}`:

```csharp
    [Fact]
    public void For_dynamite_mode_sets_zero_detonators()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 3,
            explosive: ExplosiveMode.Dynamite);
        Assert.Equal(0, cfg.DetonatorCount);
    }

    [Fact]
    public void For_detonator_specials_sets_one_detonator_per_player()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 3,
            explosive: ExplosiveMode.DetonatorSpecials);
        Assert.Equal(3, cfg.DetonatorCount);
    }

    [Fact]
    public void For_detonators_only_sets_one_detonator_per_player()
    {
        var cfg = MapConfig.For(GameMode.LastManStanding, seed: 1, playerCount: 2,
            explosive: ExplosiveMode.DetonatorsOnly);
        Assert.Equal(2, cfg.DetonatorCount);
    }
```

- [ ] **Step 2: Run tests — expect 3 failures**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Failed: 3` (named argument `explosive` not found).

- [ ] **Step 3: Update `MapConfig.For()` signature and body**

In `src/Miner49er.Core/Map/MapConfig.cs`, the current `For()` signature is:

```csharp
    public static MapConfig For(GameMode mode, int seed, int playerCount,
                                bool pits = false, bool caveIns = false, bool lava = false, int mapScale = 1)
```

Change to:

```csharp
    public static MapConfig For(GameMode mode, int seed, int playerCount,
                                bool pits = false, bool caveIns = false, bool lava = false,
                                int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite)
```

Then, inside the method body, add this line just before the `return cfg;` at the end of `For()`. The current method body ends with:

```csharp
        if (mapScale > 1)
        {
            cfg.BaseWidth  = 32 + (mapScale - 1) * 16;
            cfg.BaseHeight = 32 + (mapScale - 1) * 16;
            float areaFactor = (float)(cfg.BaseWidth * cfg.BaseHeight) / (32f * 32f);
            cfg.GoldVeinCount = (int)System.Math.Round(cfg.GoldVeinCount * areaFactor);
        }
        return cfg;
```

Change to:

```csharp
        if (mapScale > 1)
        {
            cfg.BaseWidth  = 32 + (mapScale - 1) * 16;
            cfg.BaseHeight = 32 + (mapScale - 1) * 16;
            float areaFactor = (float)(cfg.BaseWidth * cfg.BaseHeight) / (32f * 32f);
            cfg.GoldVeinCount = (int)System.Math.Round(cfg.GoldVeinCount * areaFactor);
        }
        cfg.DetonatorCount = explosive == ExplosiveMode.Dynamite ? 0 : playerCount;
        return cfg;
```

- [ ] **Step 4: Run tests — expect all passing**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Passed: 504, Failed: 0`

- [ ] **Step 5: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/MapConfigTests.cs
git commit -m "feat(core): MapConfig.For gains explosive param, sets DetonatorCount per player"
```

---

### Task 3: Network propagation — `NetworkManager` + `Main.cs`

**Files:**
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: `ExplosiveMode` (Task 1), updated `MapConfig.For()` signature (Task 2).
- Produces: `NetworkManager.MatchExplosive: ExplosiveMode` — read by Lobby (Task 4) and both `MapConfig.For()` calls in `Main.cs`.

No automated tests for Godot network layer. Verify by launching a hosted match and confirming no compile errors / no crash on scene load.

- [ ] **Step 1: Add `MatchExplosive` property to `NetworkManager`**

In `game/net/NetworkManager.cs`, the existing match-state properties are around line 231:

```csharp
	public bool MatchPits { get; private set; }
	public bool MatchCaveIns { get; private set; }
	public bool MatchLava { get; private set; }
	public float MatchBaseMoveSeconds { get; private set; } = 0.12f;
	public int MatchMapScale { get; set; } = 1;
```

Add after `MatchLava`:

```csharp
	public ExplosiveMode MatchExplosive { get; private set; }
```

- [ ] **Step 2: Update `StartMatch` to accept and forward `explosive`**

Current `StartMatch` (line ~269):

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale = 1)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60;
		var order = Players.Keys.ToArray();
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, order);
	}
```

Change to:

```csharp
	public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite)
	{
		if (!IsHost) return;
		if (flooding && timeLimitSeconds <= 0) timeLimitSeconds = 60;
		var order = Players.Keys.ToArray();
		int seed = System.Random.Shared.Next();
		Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, (int)explosive, order);
		BeginMatch(seed, order.Length, (int)mode, timeLimitSeconds, flooding, pits, caveIns, lava, baseMoveSeconds, mapScale, (int)explosive, order);
	}
```

- [ ] **Step 3: Update `BeginMatch` RPC to accept and store `explosive`**

Current `BeginMatch` (line ~280):

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

Change to:

```csharp
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding, bool pits, bool caveIns, bool lava, float baseMoveSeconds, int mapScale, int explosive, long[] peerOrder)
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
		MatchExplosive = (ExplosiveMode)explosive;
		PeerOrder = peerOrder;
		MatchStarting?.Invoke();
	}
```

- [ ] **Step 4: Update both `MapConfig.For()` calls in `Main.cs`**

In `game/Main.cs`, the client call (line ~43) currently reads:

```csharp
		var clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
```

Change to:

```csharp
		var clientMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
```

The host call (line ~64) currently reads:

```csharp
		var hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale);
```

Change to:

```csharp
		var hostMapCfg = MapConfig.For(nm.MatchMode, seed, playerCount, nm.MatchPits, nm.MatchCaveIns, nm.MatchLava, nm.MatchMapScale, nm.MatchExplosive);
```

- [ ] **Step 5: Set `DynamiteEnabled` on the host `SimConfig`**

In `game/Main.cs`, the host `SimConfig` construction (line ~65) currently reads:

```csharp
		var f1SimCfg = new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds, Seed = seed };
```

Change to:

```csharp
		var f1SimCfg = new SimConfig
		{
			BaseMoveSeconds = nm.MatchBaseMoveSeconds,
			Seed = seed,
			DynamiteEnabled = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
		};
```

- [ ] **Step 6: Build to verify no compile errors**

```
dotnet build src/Miner49er.Core.sln -q
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

Also build the Godot project via PowerShell (not Bash):

```powershell
godot --headless --build-solutions --quit-after 2
```

Expected: exits cleanly, no assembly errors.

- [ ] **Step 7: Commit**

```
git add game/net/NetworkManager.cs game/Main.cs
git commit -m "feat: propagate ExplosiveMode through BeginMatch RPC and apply to MapConfig + SimConfig"
```

---

### Task 4: Lobby UI + SettingsStore persistence

**Files:**
- Modify: `game/ui/Lobby.cs`
- Modify: `game/audio/SettingsStore.cs`

**Interfaces:**
- Consumes: `ExplosiveMode` (Task 1), `NetworkManager.StartMatch` new signature (Task 3).

No automated tests. Verify by launching Lobby as host: picker appears with three options; re-launch to confirm the selection persists.

- [ ] **Step 1: Update `SettingsStore.LoadLobby` to return `explosive`**

In `game/audio/SettingsStore.cs`, the current `LoadLobby` return type and body (lines ~151–166):

```csharp
	public static (int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale) LoadLobby()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (0, 60, false, false, false, false, 1, 1);
		int  mode    = (int)(long)cfg.GetValue(LobbySection, "game_mode",   0L);
		int  time    = (int)(long)cfg.GetValue(LobbySection, "time_limit",  60L);
		bool flood   = (bool)cfg.GetValue(LobbySection, "flood",   false);
		bool pits    = (bool)cfg.GetValue(LobbySection, "pits",    false);
		bool caveIns = (bool)cfg.GetValue(LobbySection, "caveins", false);
		bool lava    = (bool)cfg.GetValue(LobbySection, "lava",    false);
		int  speed   = (int)(long)cfg.GetValue(LobbySection, "speed",      1L);
		int  scale   = (int)(long)cfg.GetValue(LobbySection, "map_scale",  1L);
		return (Mathf.Clamp(mode, 0, 3), time, flood, pits, caveIns, lava,
		        Mathf.Clamp(speed, 0, 2), Mathf.Clamp(scale, 1, 4));
	}
```

Change to:

```csharp
	public static (int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive) LoadLobby()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(Path) != Error.Ok)
			return (0, 60, false, false, false, false, 1, 1, 0);
		int  mode      = (int)(long)cfg.GetValue(LobbySection, "game_mode",   0L);
		int  time      = (int)(long)cfg.GetValue(LobbySection, "time_limit",  60L);
		bool flood     = (bool)cfg.GetValue(LobbySection, "flood",   false);
		bool pits      = (bool)cfg.GetValue(LobbySection, "pits",    false);
		bool caveIns   = (bool)cfg.GetValue(LobbySection, "caveins", false);
		bool lava      = (bool)cfg.GetValue(LobbySection, "lava",    false);
		int  speed     = (int)(long)cfg.GetValue(LobbySection, "speed",      1L);
		int  scale     = (int)(long)cfg.GetValue(LobbySection, "map_scale",  1L);
		int  explosive = (int)(long)cfg.GetValue(LobbySection, "explosive",  0L);
		return (Mathf.Clamp(mode, 0, 3), time, flood, pits, caveIns, lava,
		        Mathf.Clamp(speed, 0, 2), Mathf.Clamp(scale, 1, 4), Mathf.Clamp(explosive, 0, 2));
	}
```

- [ ] **Step 2: Update `SettingsStore.SaveLobby` to accept and persist `explosive`**

Current `SaveLobby` (lines ~168–181):

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

Change to:

```csharp
	public static void SaveLobby(int gameMode, int timeLimit, bool flood, bool pits, bool caveIns, bool lava, int speed, int mapScale, int explosive)
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
		cfg.SetValue(LobbySection, "explosive",  (long)explosive);
		cfg.Save(Path);
	}
```

- [ ] **Step 3: Add `_explosivePicker` field to `Lobby.cs`**

In `game/ui/Lobby.cs`, the field declarations at the top of the class currently end with:

```csharp
	private OptionButton _speedPicker = null!;
	private OptionButton _mapSizePicker = null!;
	private Label _codeLabel = null!;
	private Button _copyBtn = null!;
```

Add after `_lavaCheck`:

```csharp
	private OptionButton _explosivePicker = null!;
```

So the field list becomes:

```csharp
	private CheckBox _pitsCheck = null!;
	private CheckBox _caveInCheck = null!;
	private CheckBox _lavaCheck = null!;
	private OptionButton _explosivePicker = null!;
	private OptionButton _speedPicker = null!;
	private OptionButton _mapSizePicker = null!;
	private Label _codeLabel = null!;
	private Button _copyBtn = null!;
```

- [ ] **Step 4: Destructure `savedExplosive` from `LoadLobby` in `_Ready()`**

In `_Ready()`, the current destructuring (line ~51):

```csharp
		var (savedMode, savedTime, savedFlood, savedPits, savedCaveIn, savedLava, savedSpeed, savedMapScale) = SettingsStore.LoadLobby();
```

Change to:

```csharp
		var (savedMode, savedTime, savedFlood, savedPits, savedCaveIn, savedLava, savedSpeed, savedMapScale, savedExplosive) = SettingsStore.LoadLobby();
```

- [ ] **Step 5: Add `_explosivePicker` to the UI after `_lavaCheck`**

In `_Ready()`, after the `_lavaCheck` block (currently lines ~100–102):

```csharp
		_lavaCheck = new CheckBox { Text = "Lava", ButtonPressed = savedLava };
		_lavaCheck.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_lavaCheck);
```

Add:

```csharp
		_explosivePicker = new OptionButton();
		_explosivePicker.AddItem("Dynamite",           (int)ExplosiveMode.Dynamite);
		_explosivePicker.AddItem("Detonator Specials", (int)ExplosiveMode.DetonatorSpecials);
		_explosivePicker.AddItem("Detonators Only",    (int)ExplosiveMode.DetonatorsOnly);
		_explosivePicker.Select(savedExplosive);
		_explosivePicker.Visible = NetworkManager.Instance.IsHost;
		box.AddChild(_explosivePicker);
```

- [ ] **Step 6: Hide `_explosivePicker` for Expedition mode**

In `RefreshModeControls()`, currently:

```csharp
	private void RefreshModeControls()
	{
		bool isHost = NetworkManager.Instance.IsHost;
		bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
		_timePicker.Visible    = isHost && !expedition;
		_mapSizePicker.Visible = isHost && expedition;
	}
```

Change to:

```csharp
	private void RefreshModeControls()
	{
		bool isHost = NetworkManager.Instance.IsHost;
		bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
		_timePicker.Visible       = isHost && !expedition;
		_mapSizePicker.Visible    = isHost && expedition;
		_explosivePicker.Visible  = isHost && !expedition;
	}
```

- [ ] **Step 7: Wire `_explosivePicker` into `_startBtn.Pressed`**

In `_Ready()`, the `_startBtn.Pressed` handler currently reads:

```csharp
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
```

Change to:

```csharp
			bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
			int mapScale = expedition ? _mapSizePicker.GetSelectedId() : 1;
			int explosive = expedition ? 0 : _explosivePicker.GetSelectedId();
			SettingsStore.SaveLobby(_modePicker.GetSelectedId(), _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed, _pitsCheck.ButtonPressed, _caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed, _speedPicker.Selected, mapScale, explosive);
			NetworkManager.Instance.StartMatch(
				(GameMode)_modePicker.GetSelectedId(),
				expedition ? 0 : _timePicker.GetSelectedId(),
				_floodCheck.ButtonPressed,
				_pitsCheck.ButtonPressed,
				_caveInCheck.ButtonPressed,
				_lavaCheck.ButtonPressed,
				new[] { 0.20f, 0.12f, 0.07f }[_speedPicker.Selected],
				mapScale,
				(ExplosiveMode)explosive);
```

- [ ] **Step 8: Run Core tests to confirm no regressions**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Passed: 504, Failed: 0`

- [ ] **Step 9: Build Godot project (PowerShell)**

```powershell
godot --headless --build-solutions --quit-after 2
```

Expected: exits cleanly, 0 errors.

- [ ] **Step 10: Commit**

```
git add game/ui/Lobby.cs game/audio/SettingsStore.cs
git commit -m "feat(lobby): Explosive Mode picker — Dynamite / Detonator Specials / Detonators Only"
```

---

## Self-Review

**Spec coverage:**
- ✅ `ExplosiveMode` enum — Task 1
- ✅ `SimConfig.DynamiteEnabled` — Task 1
- ✅ `TryStartPlanting` guard — Task 1
- ✅ `MapConfig.For()` sets `DetonatorCount = explosive == Dynamite ? 0 : playerCount` — Task 2
- ✅ Both host + client `MapConfig.For()` calls updated — Task 3
- ✅ Host `SimConfig.DynamiteEnabled` set — Task 3
- ✅ `BeginMatch` RPC carries `int explosive` before `long[] peerOrder` — Task 3
- ✅ `NetworkManager.MatchExplosive` property — Task 3
- ✅ Lobby `OptionButton` with 3 items, host-only — Task 4
- ✅ Hidden for Expedition mode — Task 4
- ✅ SettingsStore `LoadLobby`/`SaveLobby` persist `explosive` — Task 4
- ✅ `FloorConfig` (Expedition) untouched — not in any task (correct)

**Placeholder scan:** No TBDs, all code complete.

**Type consistency:** `ExplosiveMode` defined in Task 1, used by name in Tasks 2, 3, 4. `int explosive` in RPC is cast to `(int)` on send and `(ExplosiveMode)` on receive consistently.
