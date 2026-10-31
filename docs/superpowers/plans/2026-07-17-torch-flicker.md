# Torch Flicker (Guttering) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make environmental cave lights (lanterns, lava/vents, crystals) occasionally *gutter* — a smooth dip-and-recover in brightness — while the player torch stays steady, so lit pools feel alive.

**Architecture:** A pure, tested `Miner49er.Core.Light.Flicker` helper returns a brightness multiplier in `[floor…1.0]` that is mostly 1.0 with sparse smooth dips, deterministic per `(seed, time)`. `LightMap.AddLight` gains an intensity `scale`. `LightRenderer` scales each gutter source's light-map contribution by `Flicker.Multiplier(sourceSeed, now, profile)`; the player torch passes no scale. Because the darkness overlay (ZIndex -8) is composited above the colored halos (ZIndex -9), guttering the light-map dims the whole pool — halos included — with no WorldRenderer change.

**Tech Stack:** C# / .NET 10, Godot 4.6 (Mono), xUnit tests. Engine-free `Miner49er.Core` + Godot `game/` presentation.

## Global Constraints

- Client-side presentation only. NO changes to the simulation, snapshots, `SnapshotCodec`, netcode, `Fog`/visibility, or culling.
- Radius of every light is **unchanged** — flicker only scales *intensity* within the existing radius, so the set of lit tiles never changes (no tile popping, no extra shadowcasting, no gameplay/fog impact).
- The **player torch stays steady** — `AddPlayerTorches` passes no scale (default 1.0). Only lanterns, lava/vents, and crystals gutter.
- Two dip profiles, exact values: **Fire** = `Window 1.6, Chance 0.5, Depth 0.5, Width 0.25, Floor 0.5`; **Crystal** = `Window 2.6, Chance 0.4, Depth 0.25, Width 0.5, Floor 0.75`. Fire is used for lanterns and lava/vents; Crystal for CrystalRock and CrystalShard.
- `Flicker.Multiplier` is pure and deterministic in `(seed, t, profile)` — no RNG object, no mutable state, no wall-clock read inside it (the caller supplies `t`).
- Time source in the renderer is wall-clock, per-client: `Time.GetTicksMsec() / 1000.0`. Out-of-phase flicker between clients is acceptable (cosmetic).
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. Stage only the explicit paths listed in each Commit step — never `git add -A`.

---

## File Structure

- **Create:** `src/Miner49er.Core/Light/Flicker.cs` — the pure guttering function + two profile presets.
- **Create:** `src/Miner49er.Core.Tests/FlickerTests.cs` — unit tests for `Flicker` (flat, beside `LightFieldTests.cs`).
- **Modify:** `src/Miner49er.Core/Light/LightMap.cs` — add optional `scale` to `AddLight`.
- **Modify:** `src/Miner49er.Core.Tests/LightMapTests.cs` — add a `scale` test.
- **Modify:** `game/LightRenderer.cs` — seed helpers, capture `now`, pass flicker scale for gutter sources.

---

### Task 1: Core `Flicker` guttering function

**Files:**
- Create: `src/Miner49er.Core/Light/Flicker.cs`
- Test: `src/Miner49er.Core.Tests/FlickerTests.cs`

**Interfaces:**
- Consumes: nothing (pure, standalone).
- Produces:
  - `Miner49er.Core.Flicker.Profile` — `readonly record struct Profile(double WindowSeconds, double GutterChance, double DipDepth, double DipWidthSeconds, double FloorLevel)`
  - `static readonly Flicker.Profile Flicker.Fire` and `Flicker.Crystal`
  - `static float Flicker.Multiplier(int seed, double t, Flicker.Profile p)` → value in `[p.FloorLevel, 1.0]`

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/FlickerTests.cs`:

```csharp
using System;
using Miner49er.Core;
using Xunit;

public class FlickerTests
{
    [Fact]
    public void Result_stays_within_floor_and_one_for_fire()
    {
        for (int seed = 0; seed < 20; seed++)
            for (double t = 0; t < 40; t += 0.013)
            {
                float m = Flicker.Multiplier(seed, t, Flicker.Fire);
                Assert.InRange((double)m, Flicker.Fire.FloorLevel, 1.0);
            }
    }

    [Fact]
    public void Result_stays_within_floor_and_one_for_crystal()
    {
        for (int seed = 0; seed < 20; seed++)
            for (double t = 0; t < 40; t += 0.013)
            {
                float m = Flicker.Multiplier(seed, t, Flicker.Crystal);
                Assert.InRange((double)m, Flicker.Crystal.FloorLevel, 1.0);
            }
    }

    [Fact]
    public void Deterministic_for_same_inputs()
    {
        Assert.Equal(Flicker.Multiplier(7, 12.34, Flicker.Fire),
                     Flicker.Multiplier(7, 12.34, Flicker.Fire));
    }

    [Fact]
    public void Different_seeds_are_not_all_identical()
    {
        bool anyDifference = false;
        for (double t = 0; t < 40 && !anyDifference; t += 0.01)
            if (Flicker.Multiplier(1, t, Flicker.Fire) != Flicker.Multiplier(2, t, Flicker.Fire))
                anyDifference = true;
        Assert.True(anyDifference);
    }

    [Fact]
    public void Mostly_full_brightness_between_dips()
    {
        int atFull = 0, total = 0;
        for (double t = 0; t < 200; t += 0.01, total++)
            if (Flicker.Multiplier(3, t, Flicker.Fire) > 0.999f) atFull++;
        // Dips are sparse (~8% duty); the clear majority of time is full brightness.
        Assert.True(atFull > total * 0.6, $"only {atFull}/{total} samples at full");
    }

    [Fact]
    public void Dips_actually_happen()
    {
        bool dipped = false;
        for (double t = 0; t < 200 && !dipped; t += 0.01)
            if (Flicker.Multiplier(3, t, Flicker.Fire) < 0.9f) dipped = true;
        Assert.True(dipped);
    }

    [Fact]
    public void No_sudden_jumps_between_adjacent_frames()
    {
        const double step = 1.0 / 120.0;
        for (int seed = 0; seed < 10; seed++)
            for (double t = 0; t < 60; t += step)
            {
                float a = Flicker.Multiplier(seed, t, Flicker.Fire);
                float b = Flicker.Multiplier(seed, t + step, Flicker.Fire);
                Assert.True(Math.Abs(a - b) < 0.1f, $"jump {Math.Abs(a - b)} at seed {seed} t {t}");
            }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~FlickerTests`
Expected: FAIL to compile — `Flicker` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/Miner49er.Core/Light/Flicker.cs`:

```csharp
using System;

namespace Miner49er.Core;

/// <summary>Occasional-guttering brightness multiplier for a light source. Pure and
/// deterministic in (seed, t, profile): mostly 1.0, with sparse raised-cosine dips.
/// The caller supplies the time, so there is no wall-clock read or mutable state here.
/// Client-side cosmetic only — never used by the simulation.</summary>
public static class Flicker
{
    /// <param name="WindowSeconds">Length of each independent event window.</param>
    /// <param name="GutterChance">0..1 probability a given window contains a dip.</param>
    /// <param name="DipDepth">0..1 fraction subtracted at the dip's deepest point.</param>
    /// <param name="DipWidthSeconds">Full width of the raised-cosine dip.</param>
    /// <param name="FloorLevel">The multiplier never drops below this.</param>
    public readonly record struct Profile(
        double WindowSeconds,
        double GutterChance,
        double DipDepth,
        double DipWidthSeconds,
        double FloorLevel);

    // Fire: lanterns + lava/vents — deeper, snappier dips.
    public static readonly Profile Fire = new(1.6, 0.5, 0.5, 0.25, 0.5);
    // Crystal: CrystalRock + CrystalShard — gentler, slower (magical, not flame).
    public static readonly Profile Crystal = new(2.6, 0.4, 0.25, 0.5, 0.75);

    /// <summary>Brightness multiplier at time <paramref name="t"/> for a source
    /// identified by <paramref name="seed"/>. In [p.FloorLevel, 1.0].</summary>
    public static float Multiplier(int seed, double t, Profile p)
    {
        if (p.WindowSeconds <= 0.0 || p.DipWidthSeconds <= 0.0) return 1f;

        long k = (long)Math.Floor(t / p.WindowSeconds);
        double half = p.DipWidthSeconds * 0.5;
        double maxDip = 0.0;

        // Check this window and its neighbours so a dip straddling a boundary isn't clipped.
        for (long w = k - 1; w <= k + 1; w++)
        {
            if (Hash01(seed, w, 0x9E3779B1u) >= p.GutterChance) continue;   // no event this window
            double centre = w * p.WindowSeconds + Hash01(seed, w, 0x85EBCA77u) * p.WindowSeconds;
            double d = t - centre;
            if (d <= -half || d >= half) continue;                          // outside this dip

            // Raised cosine: 1 at the centre, smoothly 0 at ±half.
            double shape = 0.5 * (1.0 + Math.Cos(Math.PI * d / half));
            double dip = shape * p.DipDepth;
            if (dip > maxDip) maxDip = dip;
        }

        double m = 1.0 - maxDip;
        if (m < p.FloorLevel) m = p.FloorLevel;
        if (m > 1.0) m = 1.0;
        return (float)m;
    }

    // Deterministic (seed, window) -> [0,1). `salt` yields independent draws from one window.
    private static double Hash01(int seed, long window, uint salt)
    {
        ulong x = (ulong)(uint)seed;
        x ^= (ulong)window * 0x9E3779B97F4A7C15UL;
        x ^= salt + 0x165667B19E3779F9UL;
        // splitmix64 finalizer
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / 9007199254740992.0); // top 53 bits → [0,1)
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~FlickerTests`
Expected: PASS — 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Light/Flicker.cs src/Miner49er.Core.Tests/FlickerTests.cs
git commit -m "feat(light): pure guttering flicker function (Fire/Crystal profiles)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `LightMap.AddLight` intensity scale

**Files:**
- Modify: `src/Miner49er.Core/Light/LightMap.cs`
- Test: `src/Miner49er.Core.Tests/LightMapTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `void LightMap.AddLight(TileGrid grid, GridPos origin, int radius, float scale = 1f)` — accumulated intensity per tile is multiplied by `scale`. Default `1f` keeps every existing caller identical.

- [ ] **Step 1: Write the failing tests**

Add these two tests to `src/Miner49er.Core.Tests/LightMapTests.cs` (inside the class, keep existing tests):

```csharp
    [Fact]
    public void Default_scale_matches_unscaled_call()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var tile = new GridPos(5, 5);
        var a = new LightMap(); a.AddLight(grid, tile, radius: 3);
        var b = new LightMap(); b.AddLight(grid, tile, radius: 3, scale: 1f);
        Assert.Equal(a.SampleClamped(tile), b.SampleClamped(tile));
    }

    [Fact]
    public void Scale_multiplies_added_intensity()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var tile = new GridPos(5, 5);

        var full = new LightMap(); full.AddLight(grid, tile, radius: 4);
        var half = new LightMap(); half.AddLight(grid, tile, radius: 4, scale: 0.5f);

        // A tile partway out is lit below 1.0, so halving is visible before the clamp.
        var probe = new GridPos(5, 7);
        Assert.True(full.SampleClamped(probe) > 0f, "probe should be lit");
        Assert.True(System.Math.Abs(full.SampleClamped(probe) * 0.5f - half.SampleClamped(probe)) < 1e-4f);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~LightMapTests`
Expected: FAIL to compile — `AddLight` has no `scale` parameter.

- [ ] **Step 3: Write the implementation**

In `src/Miner49er.Core/Light/LightMap.cs`, replace the `AddLight` method:

```csharp
    public void AddLight(TileGrid grid, GridPos origin, int radius, float scale = 1f)
    {
        foreach (var (p, intensity) in LightField.Compute(grid, origin, radius))
            _acc[p] = (_acc.TryGetValue(p, out var cur) ? cur : 0f) + intensity * scale;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter FullyQualifiedName~LightMapTests`
Expected: PASS — the existing 4 tests plus the 2 new ones.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Light/LightMap.cs src/Miner49er.Core.Tests/LightMapTests.cs
git commit -m "feat(light): optional intensity scale on LightMap.AddLight

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Wire flicker into `LightRenderer`

**Files:**
- Modify: `game/LightRenderer.cs`

**Interfaces:**
- Consumes: `Flicker.Multiplier(int, double, Flicker.Profile)`, `Flicker.Fire`, `Flicker.Crystal` (Task 1); `LightMap.AddLight(grid, origin, radius, scale)` (Task 2).
- Produces: no new public surface — internal wiring only.

**Note on testing:** `LightRenderer` is a Godot `Node2D` (presentation), not unit-tested — consistent with the rest of `game/`. Its gate is a clean build plus the full Core suite staying green; visual correctness is verified by running the game.

- [ ] **Step 1: Replace `BuildLights` to capture wall-clock time**

In `game/LightRenderer.cs`, replace the `BuildLights` method:

```csharp
    private void BuildLights(TileGrid grid)
    {
        _lights.Clear();
        double now = Time.GetTicksMsec() / 1000.0;
        AddPlayerTorches(grid);        // steady — no flicker scale
        AddSourceLights(grid, now);
    }
```

- [ ] **Step 2: Replace `AddSourceLights` to pass flicker scales**

In `game/LightRenderer.cs`, replace the entire `AddSourceLights` method with the version below (adds a `double now` parameter and a flicker `scale` on every gutter source; the viewport-cull block is unchanged):

```csharp
    private void AddSourceLights(TileGrid grid, double now)
    {
        // Held lanterns / crystal shards — keyed by miner id so the flicker phase
        // doesn't jump as the miner walks tile to tile.
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            if (m.Held == (int)ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), LanternRadius,
                    Flicker.Multiplier(HeldSeed(m.Id), now, Flicker.Fire));
            else if (m.Held == (int)ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), CrystalRadius,
                    Flicker.Multiplier(HeldSeed(m.Id), now, Flicker.Crystal));
        }

        // Dropped lanterns / shards on the ground — stationary, keyed by tile.
        foreach (var it in _client.Items)
        {
            if (it.Placement != ItemPlacement.Loose) continue;
            if (it.Kind == ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), LanternRadius,
                    Flicker.Multiplier(TileSeed(it.X, it.Y), now, Flicker.Fire));
            else if (it.Kind == ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), CrystalRadius,
                    Flicker.Multiplier(TileSeed(it.X, it.Y), now, Flicker.Crystal));
        }

        // Lava, vents, and crystal-rock walls glow. Scan only the on-screen window so this
        // stays bounded to viewport size (same order as the darkness draw).
        int ts = MatchClient.TileSize;
        Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
        int vx0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.X / ts) - 1);
        int vy0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.Y / ts) - 1);
        int vx1 = Mathf.Min(grid.Width  - 1, (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + 1);
        int vy1 = Mathf.Min(grid.Height - 1, (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + 1);
        for (int y = vy0; y <= vy1; y++)
            for (int x = vx0; x <= vx1; x++)
            {
                var p = new GridPos(x, y);
                var t = grid.Get(p);
                if (t == TileType.Lava || t == TileType.LavaVent)
                    _lights.AddLight(grid, p, LavaRadius,
                        Flicker.Multiplier(TileSeed(x, y), now, Flicker.Fire));
                else if (t == TileType.CrystalRock)
                    _lights.AddLight(grid, p, CrystalRadius,
                        Flicker.Multiplier(TileSeed(x, y), now, Flicker.Crystal));
            }
    }

    // Stable per-source flicker seeds. Tile-based for stationary sources; miner-based
    // for held sources so their phase stays continuous while walking.
    private static int TileSeed(int x, int y) => (x * 73856093) ^ (y * 19349663);
    private static int HeldSeed(int minerId) => (minerId * 83492791) ^ unchecked((int)0x9E3779B1);
```

- [ ] **Step 3: Build and run the Core suite**

Run: `dotnet build Miner49er.sln -v quiet -clp:ErrorsOnly`
Expected: `Build succeeded. 0 Error(s)`.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all tests green (baseline + Task 1's 7 + Task 2's 2).

- [ ] **Step 4: Visual verification (manual)**

Launch via PowerShell (headless Bash shim is broken for this project): `godot --path .`
Start a match with lanterns / lava / crystals in view and confirm:
- Lanterns, lava/vents, and crystals occasionally gutter (smooth dip-and-recover), each on its own rhythm.
- The player's own torch pool stays steady while walking.
- No tiles pop in/out at pool edges; darkness and colored halo dim together.

- [ ] **Step 5: Commit**

```bash
git add game/LightRenderer.cs
git commit -m "feat(light): gutter lanterns, lava, and crystals on the light-map

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Completion

After all three tasks: the environmental lights gutter while the player torch is steady. Tune the feel by editing the two `Flicker.Profile` presets in `Flicker.cs` (`WindowSeconds`, `GutterChance`, `DipDepth`, `DipWidthSeconds`, `FloorLevel`) — no structural change needed.

**Merge:** use superpowers:finishing-a-development-branch.
