# Pizazz Round 3 Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add seven client-side visual-juice effects — floor-transition wipe, pickup burst, monster death spark, miner death poof, dynamite fuse sparks, water entry splash, screen vignette.

**Architecture:** All presentation. Reuses the existing `Miner49er.Core.Fx.ParticleSystem` (already tested) via new `WorldRenderer` emitter methods; `MatchClient` fires them on detected events (pickups, deaths); `Main` owns the wipe + vignette `CanvasLayer`s. No sim/snapshot/netcode/fog changes; no new Core logic ⇒ no new unit tests.

**Tech Stack:** C# / .NET 10, Godot 4.6 (Mono). Godot `game/` presentation only.

## Global Constraints

- Client-side only. No sim / `MinerSnapshot` / codec / netcode / `Fog` changes.
- Particle effects go through the existing capped `ParticleSystem` (pool 400) and are fog/viewport-culled.
- Wall-clock / frame `delta`, per-client, cosmetic.
- Pickup burst + floating number are **local-miner only**; miner death poof shows for **all visible** miners.
- Monster effect fires on **death** (monsters carry no per-hit HP).
- The floor wipe `CanvasLayer` must sit **below** the floor banner (layer 20) so the banner reads over it.
- All new tuning values are named constants.
- Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Stage only the explicit paths per Commit step — never `git add -A`.

---

## File Structure

- **Modify:** `game/WorldRenderer.cs` — `EmitPickupBurst`, `EmitMonsterDeath`, `EmitMinerDeath`; fuse-spark scan + water-splash branch in `EmitParticleEffects` (Task 1).
- **Modify:** `game/net/MatchClient.cs` — pickup-burst call, monster-death diff, miner-death diff + `_prevMinerAlive` (Task 2).
- **Modify:** `game/Main.cs` — floor-wipe `CanvasLayer` + drive (Task 3); vignette `CanvasLayer` + texture builder (Task 4).

**Testing note:** every task is Godot presentation — no new unit tests. Each task's gate is a clean `dotnet build`, the existing Core suite staying green, and a visual run.

---

### Task 1: `WorldRenderer` emitters — bursts, fuse sparks, water splash

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: existing `Spawn(...)`, `_fxRng`, `_particles`, `EmitParticleEffects`, `Particle` (from round 2).
- Produces:
  - `void EmitPickupBurst(Vector2 pos, Color color)`
  - `void EmitMonsterDeath(Vector2 pos, MonsterKind kind)`
  - `void EmitMinerDeath(Vector2 pos)`

- [ ] **Step 1: Add the `FuseSparkRate` constant**

In `game/WorldRenderer.cs`, beside the other particle fields (below `_moteTimer`):

```csharp
	private const float FuseSparkRate = 10f; // avg sparks/sec per lit fuse
```

- [ ] **Step 2: Add the three burst emitter methods**

Add next to `EmitExplosionDebris` in `WorldRenderer`:

```csharp
	public void EmitPickupBurst(Vector2 pos, Color color)
	{
		for (int i = 0; i < 8; i++)
		{
			float ang = -Mathf.Pi / 2f + (_fxRng.NextSingle() - 0.5f) * Mathf.Pi; // upward fan
			float spd = 30f + _fxRng.NextSingle() * 50f;
			Spawn(pos.X, pos.Y, Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd,
				  120f, 0.4f + _fxRng.NextSingle() * 0.3f, 1.3f,
				  new Color(color.R, color.G, color.B, 1f));
		}
	}

	public void EmitMonsterDeath(Vector2 pos, MonsterKind kind)
	{
		Color c = kind switch
		{
			MonsterKind.Slime      => new Color(0.5f, 0.9f, 0.5f, 1f),
			MonsterKind.WaterSnake => new Color(0.4f, 0.85f, 0.8f, 1f),
			_                      => new Color(0.95f, 0.95f, 1f, 1f),
		};
		for (int i = 0; i < 12; i++)
		{
			float ang = _fxRng.NextSingle() * Mathf.Tau;
			float spd = 30f + _fxRng.NextSingle() * 70f;
			Spawn(pos.X, pos.Y, Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd,
				  60f, 0.35f + _fxRng.NextSingle() * 0.3f, 1.5f, c);
		}
	}

	public void EmitMinerDeath(Vector2 pos)
	{
		for (int i = 0; i < 12; i++)
		{
			float ang = _fxRng.NextSingle() * Mathf.Tau;
			float spd = 12f + _fxRng.NextSingle() * 38f;
			Spawn(pos.X, pos.Y - 4f, Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd - 20f,
				  -6f, 0.6f + _fxRng.NextSingle() * 0.5f, 2.0f,
				  new Color(0.55f, 0.55f, 0.58f, 0.85f));
		}
	}
```

- [ ] **Step 3: Make footsteps splash on water**

In `EmitParticleEffects`, replace the footstep block:

```csharp
			// Footstep puff on tile change.
			if (_lastFootTile.TryGetValue(m.Id, out var last) && (last.X != m.X || last.Y != m.Y))
			{
				float fx = m.X * ts + ts / 2f, fy = m.Y * ts + ts * 0.8f;
				for (int i = 0; i < 3; i++)
					Spawn(fx + (_fxRng.NextSingle() - 0.5f) * 8f, fy,
						  (_fxRng.NextSingle() - 0.5f) * 14f, -6f - _fxRng.NextSingle() * 8f,
						  40f, 0.35f + _fxRng.NextSingle() * 0.2f, 1.3f,
						  new Color(0.62f, 0.58f, 0.5f, 0.7f));
			}
```

With (water tiles splash blue droplets, dry tiles keep the brown puff):

```csharp
			// Footstep: splash on water, dust puff on dry ground.
			if (_lastFootTile.TryGetValue(m.Id, out var last) && (last.X != m.X || last.Y != m.Y))
			{
				float fx = m.X * ts + ts / 2f, fy = m.Y * ts + ts * 0.8f;
				if (grid.Get(mp).IsWater())
					for (int i = 0; i < 5; i++)
						Spawn(fx + (_fxRng.NextSingle() - 0.5f) * 10f, fy - 4f,
							  (_fxRng.NextSingle() - 0.5f) * 22f, -22f - _fxRng.NextSingle() * 18f,
							  70f, 0.3f + _fxRng.NextSingle() * 0.2f, 1.3f,
							  new Color(0.6f, 0.82f, 1f, 0.9f));
				else
					for (int i = 0; i < 3; i++)
						Spawn(fx + (_fxRng.NextSingle() - 0.5f) * 8f, fy,
							  (_fxRng.NextSingle() - 0.5f) * 14f, -6f - _fxRng.NextSingle() * 8f,
							  40f, 0.35f + _fxRng.NextSingle() * 0.2f, 1.3f,
							  new Color(0.62f, 0.58f, 0.5f, 0.7f));
			}
```

- [ ] **Step 4: Add dynamite fuse sparks**

In `EmitParticleEffects`, immediately before the method's closing brace (after the ambient-motes block), add:

```csharp
		// Dynamite fuse crackle: a small spark off each lit, visible charge.
		foreach (var ch in _client.Charges)
		{
			if (ch.FuseRemaining <= 0) continue;
			var cp = new GridPos(ch.X, ch.Y);
			if (!spectating && !_client.Fog.IsVisible(cp)) continue;
			if (_fxRng.NextSingle() >= dt * FuseSparkRate) continue;
			float sx = ch.X * ts + ts / 2f + (_fxRng.NextSingle() - 0.5f) * 4f;
			float sy = ch.Y * ts + ts * 0.3f;
			Spawn(sx, sy, (_fxRng.NextSingle() - 0.5f) * 20f, -25f - _fxRng.NextSingle() * 20f,
				  40f, 0.2f + _fxRng.NextSingle() * 0.15f, 1.0f, new Color(1f, 0.8f, 0.25f, 1f));
		}
```

- [ ] **Step 5: Build + Core suite**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all green (no test count change).

- [ ] **Step 6: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — confirm stepping into water splashes blue droplets (dry ground still puffs dust), and a placed/lit dynamite charge crackles orange sparks. (Bursts wire up in Task 2.)

- [ ] **Step 7: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(fx): burst emitters, dynamite fuse sparks, water splash

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `MatchClient` wiring — pickup burst + death detection

**Files:**
- Modify: `game/net/MatchClient.cs`

**Interfaces:**
- Consumes: `WorldRenderer.EmitPickupBurst`, `EmitMonsterDeath`, `EmitMinerDeath` (Task 1); existing `Fog`, `FogRenderer`, `_world`, `TileSize`, `_miners`, `_monsters`.
- Produces: nothing new.

- [ ] **Step 1: Add the previous-alive tracker field**

In `game/net/MatchClient.cs`, beside `_lastLocalGold` / `_lastLocalIdols`:

```csharp
	private readonly Dictionary<int, bool> _prevMinerAlive = new();
```

- [ ] **Step 2: Reset it on floor change**

In `ResetFloor`, next to `_lastLocalGold = -1;`:

```csharp
		_lastLocalGold  = -1;
		_lastLocalIdols = -1;
		_prevMinerAlive.Clear();
```

- [ ] **Step 3: Fire a pickup burst alongside the floating numbers**

In `ApplyUpdate`, in the local-miner floating-numbers block, add a burst after each `AddFloatingText`. Change the gold line:

```csharp
				if (_lastLocalGold >= 0 && lm.Gold > _lastLocalGold)
					_world?.AddFloatingText(basePos + new Vector2(0f, -20f),
						$"+{lm.Gold - _lastLocalGold}g", new Color(1f, 0.85f, 0.3f));
				_lastLocalGold = lm.Gold;
```

to:

```csharp
				if (_lastLocalGold >= 0 && lm.Gold > _lastLocalGold)
				{
					_world?.AddFloatingText(basePos + new Vector2(0f, -20f),
						$"+{lm.Gold - _lastLocalGold}g", new Color(1f, 0.85f, 0.3f));
					_world?.EmitPickupBurst(basePos, new Color(1f, 0.85f, 0.3f));
				}
				_lastLocalGold = lm.Gold;
```

and the idol line:

```csharp
				if (_lastLocalIdols >= 0 && idols > _lastLocalIdols)
					_world?.AddFloatingText(basePos + new Vector2(0f, -28f),
						$"+{idols - _lastLocalIdols} idol", new Color(1f, 0.8f, 0.2f));
				_lastLocalIdols = idols;
```

to:

```csharp
				if (_lastLocalIdols >= 0 && idols > _lastLocalIdols)
				{
					_world?.AddFloatingText(basePos + new Vector2(0f, -28f),
						$"+{idols - _lastLocalIdols} idol", new Color(1f, 0.8f, 0.2f));
					_world?.EmitPickupBurst(basePos, new Color(1f, 0.8f, 0.2f));
				}
				_lastLocalIdols = idols;
```

- [ ] **Step 4: Spark monsters that just died**

In `ApplyUpdate`, immediately **before** `_monsters = new List<MonsterSnapshot>(update.Snapshot.Monsters);`, add:

```csharp
			// Monster death spark: any currently-alive monster the new snapshot no longer has alive.
			foreach (var oldMo in _monsters)
			{
				if (!oldMo.Alive) continue;
				bool stillAlive = false;
				foreach (var nmo in update.Snapshot.Monsters)
					if (nmo.Id == oldMo.Id && nmo.Alive) { stillAlive = true; break; }
				if (stillAlive) continue;
				var dp = new GridPos(oldMo.X, oldMo.Y);
				if (FogRenderer?.SpectatorMode == true || Fog.IsVisible(dp))
					_world?.EmitMonsterDeath(
						new Vector2(oldMo.X * TileSize + TileSize / 2f, oldMo.Y * TileSize + TileSize / 2f),
						oldMo.Kind);
			}
```

- [ ] **Step 5: Poof miners that just died**

In `ApplyUpdate`, immediately after the local-death shake line `if (_localWasAlive && !localAliveNow) AddShake(0.6f);`, add:

```csharp
			foreach (var mm2 in _miners)
			{
				bool wasAlive = _prevMinerAlive.TryGetValue(mm2.Id, out var pa) && pa;
				if (wasAlive && !mm2.Alive)
				{
					var dp = new GridPos(mm2.X, mm2.Y);
					if (FogRenderer?.SpectatorMode == true || Fog.IsVisible(dp))
						_world?.EmitMinerDeath(
							new Vector2(mm2.X * TileSize + TileSize / 2f, mm2.Y * TileSize + TileSize / 2f));
				}
				_prevMinerAlive[mm2.Id] = mm2.Alive;
			}
```

- [ ] **Step 6: Build + Core suite**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all green.

- [ ] **Step 7: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — confirm a gold/idol pickup pops a coloured sparkle over your miner, killing a monster sparks at its tile, and a dying miner (yours or a rival) poofs grey smoke.

- [ ] **Step 8: Commit**

```bash
git add game/net/MatchClient.cs
git commit -m "feat(fx): pickup bursts + monster/miner death particles

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Floor-transition wipe

**Files:**
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: existing `OnNewFloor(int)`, `_PhysicsProcess`.
- Produces: nothing new.

- [ ] **Step 1: Add the wipe fields**

In `game/Main.cs`, beside `_fadeOverlay` / `_fadeAlpha`:

```csharp
	private CanvasLayer _wipeLayer = null!;
	private ColorRect   _wipeRect  = null!;
	private float _wipeTimer;
	private const float WipeDuration = 0.45f; // seconds, black recedes left→right
```

- [ ] **Step 2: Build the wipe layer in `_Ready`**

In `_Ready`, right after the `_fadeOverlay` is added (`AddChild(_fadeOverlay);`), add:

```csharp
		// Floor-transition wipe — sits BELOW the floor banner (layer 20) so the banner reads over it.
		_wipeLayer = new CanvasLayer { Layer = 18, Name = "WipeLayer" };
		_wipeRect  = new ColorRect { Color = new Color(0f, 0f, 0f, 1f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_wipeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_wipeLayer.AddChild(_wipeRect);
		AddChild(_wipeLayer);
```

- [ ] **Step 3: Start the wipe on a new floor**

At the end of `OnNewFloor(int floor)` (after `_floorBannerTimer = BannerTotal;`), add:

```csharp
		_wipeTimer = WipeDuration;
		_wipeRect.Visible = true;
```

- [ ] **Step 4: Drive the wipe in `_PhysicsProcess`**

In `_PhysicsProcess`, right after the floor-banner update block (before the `// Treasure-floor escape-open announcement.` comment), add:

```csharp
		if (_wipeTimer > 0f)
		{
			_wipeTimer -= (float)delta;
			// progress 0 (full black) → 1 (clear); black's left edge sweeps right, revealing left→right.
			float progress = 1f - Mathf.Clamp(_wipeTimer / WipeDuration, 0f, 1f);
			_wipeRect.AnchorLeft = progress;
			_wipeRect.OffsetLeft = 0f;
			if (_wipeTimer <= 0f) _wipeRect.Visible = false;
		}
```

- [ ] **Step 5: Build**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — advance a floor (Expedition, or reach the ladder) and confirm the new floor wipes in as black recedes left→right, with the floor banner still readable over it.

- [ ] **Step 7: Commit**

```bash
git add game/Main.cs
git commit -m "feat(fx): directional floor-transition wipe

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Screen vignette

**Files:**
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: existing `_Ready`.
- Produces: `static ImageTexture Main.BuildVignetteTexture(int size, Color edge)`.

- [ ] **Step 1: Add the vignette layer in `_Ready`**

In `_Ready`, right after the wipe layer is added (`AddChild(_wipeLayer);` from Task 3), add:

```csharp
		// Subtle cave-mood vignette (static, under the HUD).
		var vignetteLayer = new CanvasLayer { Layer = 8, Name = "VignetteLayer" };
		var vignette = new TextureRect
		{
			Texture     = BuildVignetteTexture(256, new Color(0f, 0f, 0f, 0.5f)),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
		};
		vignette.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		vignetteLayer.AddChild(vignette);
		AddChild(vignetteLayer);
```

- [ ] **Step 2: Add the radial-texture builder**

Add this static method to the `Main` class (near the other helpers):

```csharp
	// Radial vignette: transparent centre, `edge` colour ramping in over the outer ~45%.
	private static ImageTexture BuildVignetteTexture(int size, Color edge)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float c = (size - 1) / 2f;
		float maxD = Mathf.Sqrt(2f) * c;
		for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
			{
				float d = new Vector2(x - c, y - c).Length() / maxD; // 0 centre .. 1 corner
				float a = Mathf.SmoothStep(0.55f, 1f, d) * edge.A;
				img.SetPixel(x, y, new Color(edge.R, edge.G, edge.B, a));
			}
		return ImageTexture.CreateFromImage(img);
	}
```

- [ ] **Step 3: Build**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — confirm the screen has a subtle dark-edged vignette (corners a touch darker, centre unaffected), and the HUD/minimap still read clearly over it.

- [ ] **Step 5: Commit**

```bash
git add game/Main.cs
git commit -m "feat(fx): subtle screen vignette

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Completion

After all four tasks: floors wipe in, pickups sparkle, kills spark, dying miners poof, fuses crackle, water splashes, and the screen has a mood vignette. Tuning: `WipeDuration`, `VignetteColor`/peak alpha (the `new Color(0,0,0,0.5f)` arg), `FuseSparkRate`, and the burst counts/colours/velocities inside each `EmitX` method + the water-splash branch.

**Merge:** use superpowers:finishing-a-development-branch.
