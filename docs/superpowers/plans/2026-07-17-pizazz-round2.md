# Pizazz Round 2 Pack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three client-side visual-juice features — particles, drifting water caustics, and floating pickup numbers — in that order.

**Architecture:** A pure, tested `Miner49er.Core.Fx.ParticleSystem` owns particle simulation; `WorldRenderer` owns emission + drawing and the water/floating-text draws; `MatchClient` fires the explosion-debris burst and detects local pickup deltas. All presentation — no sim/snapshot/netcode/fog changes.

**Tech Stack:** C# / .NET 10, Godot 4.6 (Mono), xUnit. Engine-free `Miner49er.Core` + Godot `game/` presentation.

## Global Constraints

- Client-side only. NO changes to the sim, `MinerSnapshot`/codec, netcode, `Fog`/visibility, or culling.
- Wall-clock time via `Time.GetTicksMsec()`; per-client, cosmetic (out-of-phase between clients is fine).
- Everything is viewport/fog-culled and the particle pool is capped, so lava-heavy and flooded floors stay cheap (do not regress the culling fix).
- Pure math lives in `Miner49er.Core` with xUnit tests; Godot `game/` presentation is verified by running the game.
- Floating numbers are **local-player only**.
- Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Stage only the explicit paths in each Commit step — never `git add -A`.

---

## File Structure

- **Create:** `src/Miner49er.Core/Fx/ParticleSystem.cs` — `Particle` struct + capped pool (`Emit`/`Update`/`Live`).
- **Create:** `src/Miner49er.Core.Tests/ParticleSystemTests.cs` — pool/integration tests.
- **Modify:** `game/WorldRenderer.cs` — own the `ParticleSystem`; emitters + particle draw (Task 2); water caustics (Task 3); floating-text list/draw (Task 4).
- **Modify:** `game/net/MatchClient.cs` — explosion-debris emit (Task 2); local gold/idol/prize delta detection (Task 4).

---

### Task 1: Core `ParticleSystem`

**Files:**
- Create: `src/Miner49er.Core/Fx/ParticleSystem.cs`
- Test: `src/Miner49er.Core.Tests/ParticleSystemTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `struct Miner49er.Core.Fx.Particle` with public float fields `X, Y, Vx, Vy, Gravity, Life, MaxLife, Size, R, G, B, A`.
  - `class ParticleSystem`: `ParticleSystem(int capacity)`, `int Count`, `int Capacity`, `void Emit(in Particle p)`, `void Update(float dt)`, `IReadOnlyList<Particle> Live`.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/ParticleSystemTests.cs`:

```csharp
using Miner49er.Core.Fx;
using Xunit;

public class ParticleSystemTests
{
    private static Particle P(float life, float vx = 0, float vy = 0, float gravity = 0) =>
        new() { X = 0, Y = 0, Vx = vx, Vy = vy, Gravity = gravity, Life = life, MaxLife = life, Size = 1, A = 1 };

    [Fact]
    public void Position_integrates_by_velocity()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 1f, vx: 10f));
        sys.Update(0.5f);
        Assert.Equal(5f, sys.Live[0].X, 3);
    }

    [Fact]
    public void Gravity_accelerates_vertical_velocity()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 5f, gravity: 10f));
        sys.Update(1f);
        Assert.Equal(10f, sys.Live[0].Vy, 3);
    }

    [Fact]
    public void Life_decrements_and_dead_particles_are_removed()
    {
        var sys = new ParticleSystem(8);
        sys.Emit(P(life: 1f));
        sys.Update(0.5f);
        Assert.Equal(1, sys.Count);
        Assert.Equal(0.5f, sys.Live[0].Life, 3);
        sys.Update(0.6f);
        Assert.Equal(0, sys.Count);
    }

    [Fact]
    public void Capacity_cap_replaces_the_particle_nearest_death()
    {
        var sys = new ParticleSystem(3);
        foreach (var life in new[] { 1f, 2f, 3f, 4f, 5f })
            sys.Emit(P(life));
        Assert.Equal(3, sys.Count);
        for (int i = 0; i < sys.Count; i++)
            Assert.True(sys.Live[i].Life >= 3f, $"unexpectedly small life {sys.Live[i].Life}");
    }

    [Fact]
    public void Empty_update_is_a_noop()
    {
        var sys = new ParticleSystem(4);
        sys.Update(1f);
        Assert.Equal(0, sys.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~ParticleSystemTests`
Expected: FAIL to compile — `ParticleSystem` / `Particle` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Miner49er.Core/Fx/ParticleSystem.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Miner49er.Core.Fx;

/// <summary>One lightweight particle. Colours are plain floats so the pure Core has no
/// Godot dependency; the renderer maps R/G/B/A to a Color and fades by Life/MaxLife.</summary>
public struct Particle
{
    public float X, Y;        // position (world px)
    public float Vx, Vy;      // velocity (px/sec)
    public float Gravity;     // added to Vy each second (px/sec^2)
    public float Life;        // remaining seconds; dead at <= 0
    public float MaxLife;     // initial life, for the fade ratio
    public float Size;        // half-extent in px
    public float R, G, B, A;  // base colour; A is the peak alpha
}

/// <summary>Fixed-capacity particle pool. Never allocates after construction: Emit past
/// capacity replaces the particle closest to expiring; Update integrates + swap-removes
/// the dead. Pure C#; client-side cosmetic only.</summary>
public sealed class ParticleSystem
{
    private readonly Particle[] _pool;
    private int _count;

    public ParticleSystem(int capacity) => _pool = new Particle[capacity];

    public int Count => _count;
    public int Capacity => _pool.Length;

    public void Emit(in Particle p)
    {
        if (_count < _pool.Length)
        {
            _pool[_count++] = p;
            return;
        }
        // At capacity: overwrite whichever particle is nearest death.
        int min = 0;
        for (int i = 1; i < _count; i++)
            if (_pool[i].Life < _pool[min].Life) min = i;
        _pool[min] = p;
    }

    public void Update(float dt)
    {
        for (int i = _count - 1; i >= 0; i--)
        {
            ref var p = ref _pool[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += p.Gravity * dt;
            p.Life -= dt;
            if (p.Life <= 0f)
            {
                _pool[i] = _pool[_count - 1];
                _count--;
            }
        }
    }

    /// <summary>Live particles, valid indices [0, Count). Backed by the internal array.</summary>
    public IReadOnlyList<Particle> Live => new ArraySegment<Particle>(_pool, 0, _count);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~ParticleSystemTests`
Expected: PASS — 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Fx/ParticleSystem.cs src/Miner49er.Core.Tests/ParticleSystemTests.cs
git commit -m "feat(fx): pure capped particle-system pool

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Particles in `WorldRenderer` + explosion debris

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/net/MatchClient.cs`

**Interfaces:**
- Consumes: `Miner49er.Core.Fx.ParticleSystem`, `Particle` (Task 1).
- Produces: `WorldRenderer.EmitExplosionDebris(Vector2 center)`; a `float dt` local at the top of the particle block in `_Process` that Task 4 also uses.

**Testing:** presentation — gate is a clean build, the full Core suite still green, and a visual run.

- [ ] **Step 1: Add the using + fields to `WorldRenderer`**

At the top of `game/WorldRenderer.cs`, add after the existing `using` lines:

```csharp
using Miner49er.Core.Fx;
```

Add these fields near the other `WorldRenderer` private fields (e.g. just below the `_floodAdvances` declaration around line 97):

```csharp
    // Particle pool + emitters (client-side cosmetic).
    private readonly ParticleSystem _particles = new(400);
    private readonly System.Random _fxRng = new();
    private readonly System.Collections.Generic.Dictionary<int, GridPos> _lastFootTile = new();
    private float _miningDustTimer;
    private float _moteTimer;
```

- [ ] **Step 2: Drive the particle system from `_Process`**

In `game/WorldRenderer.cs` `_Process`, replace the flood-advance block + `QueueRedraw();` (the tail of the method) with the same block plus the particle drive:

```csharp
			for (int i = _floodAdvances.Count - 1; i >= 0; i--)
			{
				var fa = _floodAdvances[i];
				fa.life += (float)delta;
				if (fa.life >= FloodAnimSeconds) _floodAdvances.RemoveAt(i);
				else _floodAdvances[i] = fa;
			}
			float dt = (float)delta;
			_particles.Update(dt);
			EmitParticleEffects(dt);
			QueueRedraw();
```

- [ ] **Step 3: Add the emitter + draw helpers**

Add these methods to `WorldRenderer` (near the other transient-effect helpers, e.g. after `AddThrownDynamite`):

```csharp
    // ── Particles ─────────────────────────────────────────────────────────────
    public void EmitExplosionDebris(Vector2 center)
    {
        for (int i = 0; i < 14; i++)
        {
            float ang = _fxRng.NextSingle() * Mathf.Tau;
            float spd = 40f + _fxRng.NextSingle() * 90f;
            Spawn(center.X, center.Y,
                  Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd - 30f,
                  160f, 0.5f + _fxRng.NextSingle() * 0.4f, 1.6f,
                  new Color(0.92f, 0.6f, 0.3f, 1f));
        }
    }

    private void Spawn(float x, float y, float vx, float vy, float gravity, float life, float size, Color c)
        => _particles.Emit(new Particle
        {
            X = x, Y = y, Vx = vx, Vy = vy, Gravity = gravity,
            Life = life, MaxLife = life, Size = size,
            R = c.R, G = c.G, B = c.B, A = c.A,
        });

    private void EmitParticleEffects(float dt)
    {
        var grid = _client.Grid;
        int ts = MatchClient.TileSize;
        bool spectating = _client.FogRenderer?.SpectatorMode == true;

        _miningDustTimer += dt;
        bool emitDust = _miningDustTimer >= 0.06f;
        if (emitDust) _miningDustTimer = 0f;

        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            var mp = new GridPos(m.X, m.Y);
            if (!spectating && !_client.Fog.IsVisible(mp)) continue;

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
            _lastFootTile[m.Id] = mp;

            // Mining dust while actively mining (facing the wall being dug).
            if (emitDust && m.Activity == 1 && m.ActivityRemaining > 0)
            {
                int fdx = m.Facing == 1 ? 1 : m.Facing == 3 ? -1 : 0;
                int fdy = m.Facing == 0 ? -1 : m.Facing == 2 ? 1 : 0;
                float cx = (m.X + fdx) * ts + ts / 2f, cy = (m.Y + fdy) * ts + ts / 2f;
                for (int i = 0; i < 2; i++)
                    Spawn(cx + (_fxRng.NextSingle() - 0.5f) * 10f, cy + (_fxRng.NextSingle() - 0.5f) * 10f,
                          (_fxRng.NextSingle() - 0.5f) * 24f, -20f - _fxRng.NextSingle() * 20f,
                          90f, 0.4f + _fxRng.NextSingle() * 0.3f, 1.5f,
                          new Color(0.55f, 0.45f, 0.34f, 0.9f));
            }
        }

        // Lava embers on visible lava/vent tiles.
        var (vx0, vx1, vy0, vy1) = VisibleTileRange(grid, ts);
        for (int y = vy0; y <= vy1; y++)
            for (int x = vx0; x <= vx1; x++)
            {
                var tt = grid.Get(new GridPos(x, y));
                if (tt != TileType.Lava && tt != TileType.LavaVent) continue;
                if (_fxRng.NextSingle() >= dt * 1.6f) continue;   // ~1.6 embers/sec/tile
                float ex = x * ts + ts * 0.2f + _fxRng.NextSingle() * ts * 0.6f;
                float ey = y * ts + ts * 0.6f;
                Spawn(ex, ey, (_fxRng.NextSingle() - 0.5f) * 10f, -18f - _fxRng.NextSingle() * 22f,
                      -8f, 0.6f + _fxRng.NextSingle() * 0.5f, 1.2f,
                      new Color(1f, 0.55f, 0.16f, 1f));
            }

        // Ambient dust motes: keep a faint drifting population inside the viewport.
        _moteTimer += dt;
        if (_moteTimer >= 0.15f && _particles.Count < 140)
        {
            _moteTimer = 0f;
            float wx0 = vx0 * ts, wy0 = vy0 * ts;
            float ww = (vx1 - vx0 + 1) * ts, wh = (vy1 - vy0 + 1) * ts;
            Spawn(wx0 + _fxRng.NextSingle() * ww, wy0 + _fxRng.NextSingle() * wh,
                  (_fxRng.NextSingle() - 0.5f) * 6f, -3f - _fxRng.NextSingle() * 4f,
                  0f, 3.5f + _fxRng.NextSingle() * 2.5f, 1.0f,
                  new Color(0.82f, 0.85f, 0.9f, 0.35f));
        }
    }

    private void DrawParticles()
    {
        foreach (var p in _particles.Live)
        {
            float fade = p.MaxLife > 0f ? p.Life / p.MaxLife : 0f;
            if (fade <= 0f) continue;
            DrawRect(new Rect2(p.X - p.Size, p.Y - p.Size, p.Size * 2f, p.Size * 2f),
                new Color(p.R, p.G, p.B, p.A * fade));
        }
    }
```

- [ ] **Step 4: Draw the particles at the end of `_Draw`**

In `game/WorldRenderer.cs` `_Draw()`, immediately before the method's closing brace, add:

```csharp
			DrawParticles();
```

- [ ] **Step 5: Fire the explosion-debris burst from `MatchClient`**

In `game/net/MatchClient.cs`, inside the `if (blastCount > 0)` block, add the debris emit right after the shake (after `ShakeFromWorld(c, 0.7f);`):

```csharp
				ShakeFromWorld(c, 0.7f);
				_world?.EmitExplosionDebris(c);
```

- [ ] **Step 6: Build + run the Core suite**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all tests green.

- [ ] **Step 7: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — start a match and confirm mining kicks brown dust, walking puffs at the feet, lava throws rising embers, dynamite sprays debris, and faint motes drift in the air. Frame rate holds on a lava/flooded floor.

- [ ] **Step 8: Commit**

```bash
git add game/WorldRenderer.cs game/net/MatchClient.cs
git commit -m "feat(fx): mining dust, footsteps, lava embers, debris, ambient motes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Water caustic glints

**Files:**
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: nothing new (replaces the water sparkle inner loop).

**Testing:** presentation — clean build + visual run.

- [ ] **Step 1: Add the glint tuning constants**

In `game/WorldRenderer.cs`, add near the other water constants (e.g. below `_deepWaterCol` around line 91):

```csharp
    private const float GlintDriftSpeed = 0.8f;  // rad/sec centre drift
    private const float GlintDriftAmp   = 3.0f;  // px drift radius
    private const float GlintPulseSpeed = 1.4f;  // rad/sec brightness pulse
    private const float GlintAlpha      = 0.35f; // peak shallow-water glint alpha
    private const float GlintRadius     = 3.2f;  // px, soft blob radius
```

- [ ] **Step 2: Replace the sparkle loop with drifting caustics**

In `_Draw()`, replace the existing per-tile sparkle block (keep `wx0`/`wy0`/`wh` — the shoreline code below still uses them):

Replace:

```csharp
			int sparkCount = 4 + (int)(wh & 1u);
			for (int wv = 0; wv < sparkCount; wv++)
			{
				uint wh2 = wh ^ (uint)(wv * 1013904223u);
				float px = wx0 + 2f + (wh2 & 0x1Fu) * (ts - 4) / 31f;
				float py = wy0 + 2f + ((wh2 >> 8) & 0x1Fu) * (ts - 4) / 31f;
				float phase = ((wh2 >> 16) & 0xFFu) * (Mathf.Tau / 255f);
				float t = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(wTime * 2.5f + phase)), 3f);
				if (t < 0.02f) continue;
				float alpha = t * 0.90f;
				DrawCircle(new Vector2(px, py), 1.2f,
					deep ? new Color(0.55f, 0.75f, 1f, alpha)
					     : new Color(0.70f, 0.88f, 1f, alpha));
			}
```

With:

```csharp
			for (int g = 0; g < 2; g++)
			{
				uint gh = wh ^ (uint)(g * 2654435761u);
				float bx = wx0 + 4f + (gh & 0xFu) * (ts - 8) / 15f;
				float by = wy0 + 4f + ((gh >> 8) & 0xFu) * (ts - 8) / 15f;
				float ph = ((gh >> 16) & 0xFFu) * (Mathf.Tau / 255f);
				float gx = bx + Mathf.Sin(wTime * GlintDriftSpeed + ph) * GlintDriftAmp;
				float gy = by + Mathf.Cos(wTime * GlintDriftSpeed * 0.8f + ph) * GlintDriftAmp;
				float pulse = 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(wTime * GlintPulseSpeed + ph));
				float alpha = GlintAlpha * pulse;
				DrawCircle(new Vector2(gx, gy), GlintRadius,
					deep ? new Color(0.5f, 0.72f, 1f, alpha * 0.7f)
					     : new Color(0.68f, 0.86f, 1f, alpha));
			}
```

- [ ] **Step 3: Build**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — on a water/flooded floor, confirm the water shows soft, slowly drifting light glints (caustics) rather than in-place twinkles; shoreline foam still animates.

- [ ] **Step 5: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(fx): drifting caustic glints on water (replaces sparkle twinkle)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Floating pickup numbers

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/net/MatchClient.cs`

**Interfaces:**
- Consumes: the `float dt` local in `_Process` from Task 2; `MatchClient.MinerVisualPos(int,int,int)` (existing).
- Produces: `WorldRenderer.AddFloatingText(Vector2 worldPos, string text, Color color)`.

**Testing:** presentation + client-only delta tracking — clean build + visual run.

- [ ] **Step 1: Add the floating-text list, constants, and API to `WorldRenderer`**

Add fields near the other transient lists (e.g. below the particle fields from Task 2):

```csharp
    // Floating pickup numbers (local player only; fed by MatchClient).
    private readonly System.Collections.Generic.List<(string text, Vector2 pos, Color color, float life, float maxLife)> _floatTexts = new();
    private const float FloatRiseSpeed = 26f; // px/sec upward
    private const float FloatLife      = 1.1f;

    public void AddFloatingText(Vector2 worldPos, string text, Color color)
        => _floatTexts.Add((text, worldPos, color, FloatLife, FloatLife));
```

- [ ] **Step 2: Update + draw the floating texts**

Add these helpers to `WorldRenderer`:

```csharp
    private void UpdateFloatingTexts(float dt)
    {
        for (int i = _floatTexts.Count - 1; i >= 0; i--)
        {
            var f = _floatTexts[i];
            f.life -= dt;
            f.pos.Y -= FloatRiseSpeed * dt;
            if (f.life <= 0f) _floatTexts.RemoveAt(i);
            else _floatTexts[i] = f;
        }
    }

    private void DrawFloatingTexts()
    {
        var font = ThemeDB.FallbackFont;
        const float w = 48f;
        foreach (var f in _floatTexts)
        {
            var col = f.color;
            col.A = f.maxLife > 0f ? f.life / f.maxLife : 0f;
            DrawString(font, new Vector2(f.pos.X - w / 2f, f.pos.Y), f.text,
                HorizontalAlignment.Center, w, 14, col);
        }
    }
```

In `_Process`, add the update call right after `EmitParticleEffects(dt);` (added in Task 2):

```csharp
			EmitParticleEffects(dt);
			UpdateFloatingTexts(dt);
```

In `_Draw()`, add the draw right after `DrawParticles();` (added in Task 2):

```csharp
			DrawParticles();
			DrawFloatingTexts();
```

- [ ] **Step 3: Add local-pickup delta detection to `MatchClient`**

Add fields near the other `MatchClient` presentation fields (e.g. beside `_lastMinerPos`):

```csharp
	private int _lastLocalGold  = -1;
	private int _lastLocalIdols = -1;
```

In `ResetFloor` (around line 359), reset them (near `_lastMinerPos.Clear();` at line 399):

```csharp
		_lastMinerPos.Clear();
		_lastLocalGold  = -1;
		_lastLocalIdols = -1;
```

In `ApplyUpdate`, immediately after `TreasureProgress = update.Snapshot.TreasureProgress;` (around line 323), add:

```csharp
			// Floating pickup numbers over the LOCAL miner only (client-side cosmetic).
			foreach (var lm in _miners)
			{
				if (lm.Id != LocalMinerId) continue;
				var basePos = MinerVisualPos(lm.Id, lm.X, lm.Y);

				if (_lastLocalGold >= 0 && lm.Gold > _lastLocalGold)
					_world?.AddFloatingText(basePos + new Vector2(0f, -20f),
						$"+{lm.Gold - _lastLocalGold}g", new Color(1f, 0.85f, 0.3f));
				_lastLocalGold = lm.Gold;

				int idols = 0;
				if (TreasureProgress is { } tp)
					foreach (var e in tp) if (e.MinerId == LocalMinerId) { idols = e.Found; break; }
				if (_lastLocalIdols >= 0 && idols > _lastLocalIdols)
					_world?.AddFloatingText(basePos + new Vector2(0f, -28f),
						$"+{idols - _lastLocalIdols} idol", new Color(1f, 0.8f, 0.2f));
				_lastLocalIdols = idols;

				if (update.Snapshot.PrizeClaim is { } pc && pc.MinerId == LocalMinerId)
				{
					Color pcol = (PrizeType)pc.Type switch
					{
						PrizeType.MineOut    => new Color(0.35f, 0.85f, 1f),
						PrizeType.HoldPoint  => new Color(1f, 0.55f, 0.15f),
						PrizeType.CarryRelic => new Color(0.85f, 0.4f, 1f),
						_                    => new Color(1f, 0.85f, 0.3f),
					};
					_world?.AddFloatingText(basePos + new Vector2(0f, -36f), "PRIZE!", pcol);
				}
				break;
			}
```

- [ ] **Step 4: Build + run the Core suite**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all tests green.

- [ ] **Step 5: Visual verification (manual)**

Launch via PowerShell: `godot --path .` — confirm picking up gold floats a rising amber `+Ng` over YOUR miner (not rivals); an idol pickup (Treasure Hunt) floats `+1 idol`; claiming a prize floats a coloured `PRIZE!`. First snapshot / floor entry must NOT spew a number (the `-1` guard). If the numbers read dim over the miner, note it — they draw at `WorldRenderer`'s layer under the darkness overlay, and can be lifted to `MatchClient._Draw` later if needed.

- [ ] **Step 6: Commit**

```bash
git add game/WorldRenderer.cs game/net/MatchClient.cs
git commit -m "feat(fx): floating gold/idol/prize numbers over the local miner

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Completion

After all four tasks: mining/footsteps/lava/explosions/atmosphere emit particles, water shows drifting caustics, and local pickups float numbers. Tuning knobs: the `Spawn` call constants + rates in `EmitParticleEffects`, the `Glint*` constants, and `FloatRiseSpeed`/`FloatLife`; pool size on `new ParticleSystem(400)`.

**Merge:** use superpowers:finishing-a-development-branch.
