# Cave Lighting & Darkness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the caves a dark ambient baseline with light pools that spill from sources (player torch, lantern, crystal, lava) and are blocked by walls, so shadows fall behind rock — ghosts exempt.

**Architecture:** A per-tile *light-map* (custom, immediate-mode — matching how fog and the rest of the game render). Two new pure-C# `Miner49er.Core` types compute per-tile light with wall occlusion + falloff (reusing the existing `Visibility` shadowcaster). A new Godot `LightRenderer` node draws a darkness overlay over the visible area, thinned wherever light reaches. Colored pools come from the existing additive glow halos (lantern/crystal/lava) plus a new cool-blue torch glow, which show through the thinned darkness. Ghosts are lifted to their own overlay above the darkness so they stay full-bright.

**Tech Stack:** C# / .NET 10, Godot 4.6 (immediate-mode `_Draw`), xUnit (`Miner49er.Core.Tests`).

## Global Constraints

- **Client-side presentation only.** No sim behavior, no netcode, no snapshot changes. The two new `Miner49er.Core` files are *pure deterministic helpers* (like the existing `Visibility`), not simulation state.
- **Existing test suite stays green.** Baseline is ~800 passing tests; this plan adds new Core tests and must not break any.
- **Ghosts are exempt** from darkening and from casting/receiving shadow (they already float above drop-shadows).
- **Build:** `dotnet build -c Debug` (Bash or PowerShell). **Tests:** `dotnet test`. If launching Godot headless to smoke-check, run `godot` **via PowerShell only** (the Bash shim breaks headless with a false "assemblies not found").
- **Staging discipline:** stage only the explicit files each task names. Never `git add -A`; never stage `.import` / `.uid` / `Temp/` / `Done/` / `ToDo/` / `.superpowers/` / `docs/` specs & plans.
- **Commits:** branch off `main` before the first commit; end every commit message with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

## File Structure

- `src/Miner49er.Core/Light/LightField.cs` — **new.** Per-tile intensity from one source (occlusion + radial falloff).
- `src/Miner49er.Core/Light/LightMap.cs` — **new.** Accumulates many sources into one clamped per-tile field.
- `src/Miner49er.Core.Tests/LightFieldTests.cs` — **new.** Unit tests for `LightField`.
- `src/Miner49er.Core.Tests/LightMapTests.cs` — **new.** Unit tests for `LightMap`.
- `game/LightRenderer.cs` — **new.** Godot node: builds the light-map from live sources, draws the darkness overlay.
- `game/GhostOverlay.cs` — **new.** Godot node: draws ghosts above the darkness so they stay bright.
- `game/net/MatchClient.cs` — **modify.** Wire in `LightRenderer` (ZIndex -8) and `GhostOverlay` (ZIndex -7) in both the ctor scene-build and `ResetFloor`; toggle their spectator flag alongside fog.
- `game/WorldRenderer.cs` — **modify.** Add a blue torch glow; skip ghosts in the main monster loop and expose `DrawGhosts(CanvasItem)`.
- `game/FogRenderer.cs` — **modify.** Soften the in-view radial gradient so it no longer double-darkens now that the light-map owns in-view brightness.

## Layer order (ZIndex, bottom → top)

| ZIndex | Node | Lit by darkness? |
|---|---|---|
| -10 | `TerrainMap` | yes |
| -9  | `WorldRenderer` (terrain, items, non-ghost monsters, glow halos) | yes (glows show through where darkness is thinned) |
| **-8** | **`LightRenderer`** (darkness overlay) | — |
| **-7** | **`GhostOverlay`** (ghosts) | no |
| -5  | `FogRenderer` (LOS mask) | — |
| 0   | `MatchClient._Draw` (miners) | no (already draws above fog, culled by visibility) |

Key insight: light sources do **not** need to be above the darkness — the darkness overlay's per-tile alpha is `AmbientDarkAlpha * (1 - brightness)`, so around a light it thins to ~0 and the underlying glow halo shows through undimmed. Only ghosts need lifting.

---

### Task 1: `LightField` — per-tile intensity with occlusion (Core)

**Files:**
- Create: `src/Miner49er.Core/Light/LightField.cs`
- Test: `src/Miner49er.Core.Tests/LightFieldTests.cs`

**Interfaces:**
- Consumes: `Visibility.Compute(TileGrid, GridPos, int) -> HashSet<GridPos>` (existing), `GridPos(int X, int Y)`, `TileGrid(int w, int h, TileType fill)` / `.Set(GridPos, TileType)`.
- Produces: `LightField.Compute(TileGrid grid, GridPos origin, int radius) -> Dictionary<GridPos, float>` — intensity in `(0,1]`, `1` at origin, linear falloff to `0` at the radius edge; tiles a wall occludes are absent; tiles beyond radius are absent.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/LightFieldTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class LightFieldTests
{
    [Fact]
    public void Intensity_is_full_at_origin_and_falls_with_distance()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var origin = new GridPos(5, 5);
        var field = LightField.Compute(grid, origin, radius: 4);

        Assert.Equal(1f, field[origin]);
        float near = field[new GridPos(6, 5)]; // distance 1
        float far  = field[new GridPos(8, 5)]; // distance 3
        Assert.True(near > far);
        Assert.True(far > 0f);
    }

    [Fact]
    public void Tiles_beyond_the_radius_receive_no_light()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var field = LightField.Compute(grid, new GridPos(5, 5), radius: 2);
        Assert.False(field.ContainsKey(new GridPos(8, 5))); // distance 3 > radius 2
    }

    [Fact]
    public void A_wall_blocks_light_from_reaching_tiles_behind_it()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        grid.Set(new GridPos(5, 3), TileType.Rock);       // pillar two tiles north of origin
        var field = LightField.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.False(field.ContainsKey(new GridPos(5, 2))); // umbra directly behind the pillar
        Assert.True(field.ContainsKey(new GridPos(2, 3)));  // well to the side: lit
    }

    [Fact]
    public void Non_positive_radius_yields_an_empty_field()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        Assert.Empty(LightField.Compute(grid, new GridPos(5, 5), radius: 0));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter LightFieldTests`
Expected: FAIL — `LightField` does not exist (compile error).

- [ ] **Step 3: Implement `LightField`**

Create `src/Miner49er.Core/Light/LightField.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Per-tile light intensity from a single source, respecting wall occlusion.
/// Reuses <see cref="Visibility.Compute"/> for the shadowcast — walls that
/// <see cref="TileTypeExtensions.BlocksSight"/> block light exactly as they block sight —
/// then applies a linear radial falloff: 1.0 at the origin down to 0.0 at the radius edge.
/// Pure and deterministic, like <see cref="Visibility"/>; used by the client light-map.</summary>
public static class LightField
{
    public static Dictionary<GridPos, float> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var field = new Dictionary<GridPos, float>();
        if (radius <= 0) return field;

        foreach (var p in Visibility.Compute(grid, origin, radius))
        {
            int dx = p.X - origin.X, dy = p.Y - origin.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float intensity = 1f - dist / radius;   // 1 at centre, 0 at the edge
            if (intensity > 0f) field[p] = intensity;
        }
        return field;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter LightFieldTests`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Light/LightField.cs src/Miner49er.Core.Tests/LightFieldTests.cs
git commit -m "feat(light): per-tile light field with wall occlusion and falloff"
```

---

### Task 2: `LightMap` — accumulate many sources (Core)

**Files:**
- Create: `src/Miner49er.Core/Light/LightMap.cs`
- Test: `src/Miner49er.Core.Tests/LightMapTests.cs`

**Interfaces:**
- Consumes: `LightField.Compute` (Task 1).
- Produces: `LightMap` with `Clear()`, `AddLight(TileGrid grid, GridPos origin, int radius)`, `float SampleClamped(GridPos p)` (sum of all sources at that tile, clamped to `[0,1]`; `0` if unlit).

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/LightMapTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class LightMapTests
{
    [Fact]
    public void Unlit_tile_samples_zero()
    {
        var map = new LightMap();
        Assert.Equal(0f, map.SampleClamped(new GridPos(0, 0)));
    }

    [Fact]
    public void Overlapping_lights_sum_and_clamp_to_one()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var tile = new GridPos(5, 5);
        // Two lights both centred on the tile contribute 1.0 each → 2.0, clamped to 1.0.
        map.AddLight(grid, tile, radius: 3);
        map.AddLight(grid, tile, radius: 3);
        Assert.Equal(1f, map.SampleClamped(tile));
    }

    [Fact]
    public void Two_offset_lights_sum_on_a_shared_tile()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var shared = new GridPos(5, 5);

        map.AddLight(grid, new GridPos(4, 5), radius: 3); // distance 1 from shared
        float oneLight = map.SampleClamped(shared);
        map.AddLight(grid, new GridPos(6, 5), radius: 3); // symmetric, adds more
        float twoLights = map.SampleClamped(shared);

        Assert.True(twoLights > oneLight);
    }

    [Fact]
    public void Clear_resets_the_accumulator()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var tile = new GridPos(5, 5);
        map.AddLight(grid, tile, radius: 3);
        map.Clear();
        Assert.Equal(0f, map.SampleClamped(tile));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter LightMapTests`
Expected: FAIL — `LightMap` does not exist (compile error).

- [ ] **Step 3: Implement `LightMap`**

Create `src/Miner49er.Core/Light/LightMap.cs`:

```csharp
using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Accumulates light intensity from many sources into one per-tile field.
/// Overlapping lights sum; <see cref="SampleClamped"/> caps the total at 1.0. The client
/// renderer rebuilds it each frame: <see cref="Clear"/> then one <see cref="AddLight"/>
/// per source. Pure C#; no Godot dependency.</summary>
public sealed class LightMap
{
    private readonly Dictionary<GridPos, float> _acc = new();

    public void Clear() => _acc.Clear();

    public void AddLight(TileGrid grid, GridPos origin, int radius)
    {
        foreach (var (p, intensity) in LightField.Compute(grid, origin, radius))
            _acc[p] = (_acc.TryGetValue(p, out var cur) ? cur : 0f) + intensity;
    }

    /// <summary>Total light at a tile, clamped to [0,1]. Unlit tiles return 0.</summary>
    public float SampleClamped(GridPos p) =>
        _acc.TryGetValue(p, out var v) ? (v > 1f ? 1f : v) : 0f;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter LightMapTests`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Light/LightMap.cs src/Miner49er.Core.Tests/LightMapTests.cs
git commit -m "feat(light): accumulate multiple light sources into a clamped light-map"
```

---

### Task 3: `LightRenderer` darkness overlay + player torches (Godot)

No unit test — this is Godot immediate-mode rendering. Verified by a clean build and play-test; the accumulation/occlusion math is covered by Tasks 1–2.

**Files:**
- Create: `game/LightRenderer.cs`
- Modify: `game/net/MatchClient.cs` (scene-build ~line 139, `ResetFloor` ~line 404, teardown ~line 353, spectator toggles ~lines 517 & 524)

**Interfaces:**
- Consumes: `LightMap` (Task 2); `MatchClient.TileSize`, `MatchClient.Grid` (`TileGrid`), `MatchClient.Fog` (`FogState.IsVisible`), `MatchClient.Miners` (each `m.Alive`, `m.X`, `m.Y`).
- Produces: `LightRenderer` node with `Init(MatchClient)` and `bool SpectatorMode`.

- [ ] **Step 1: Create `game/LightRenderer.cs`**

```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Darkens the visible area toward a dim ambient floor, then thins that darkness
/// wherever light reaches — a per-tile light-map composited over the world at ZIndex -8
/// (above the world, below ghosts and fog). Every living miner carries a torch; lanterns,
/// crystals, and lava are added in <see cref="AddSourceLights"/>. Colour comes from the
/// world's additive glow halos, which show through where the darkness is thinned.
/// Client-side presentation only.</summary>
public partial class LightRenderer : Node2D
{
    private MatchClient _client = null!;
    private readonly LightMap _lights = new();

    /// <summary>Set true when the local miner is dead — suppresses darkness so the full map reads clearly.</summary>
    public bool SpectatorMode { get; set; }

    // Alpha of full darkness on a visible-but-unlit tile. Lit tiles thin toward 0.
    private const float AmbientDarkAlpha = 0.55f;
    private const int   PlayerTorchRadius = 4;

    public void Init(MatchClient client) => _client = client;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_client == null || SpectatorMode) return;
        var grid = _client.Grid;
        var fog  = _client.Fog;
        int ts   = MatchClient.TileSize;

        BuildLights(grid);

        // Only darken on-screen tiles (same viewport-cull the fog renderer uses).
        Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
        int vx0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.X / ts) - 1);
        int vy0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.Y / ts) - 1);
        int vx1 = Mathf.Min(grid.Width  - 1, (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + 1);
        int vy1 = Mathf.Min(grid.Height - 1, (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + 1);

        for (int y = vy0; y <= vy1; y++)
            for (int x = vx0; x <= vx1; x++)
            {
                var p = new GridPos(x, y);
                if (!fog.IsVisible(p)) continue;            // fog owns non-visible darkness
                float bright = _lights.SampleClamped(p);
                float alpha  = AmbientDarkAlpha * (1f - bright);
                if (alpha <= 0.01f) continue;               // fully lit → nothing to draw
                DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), new Color(0f, 0f, 0f, alpha));
            }
    }

    private void BuildLights(TileGrid grid)
    {
        _lights.Clear();
        AddPlayerTorches(grid);
        AddSourceLights(grid);      // extended in Task 4
    }

    private void AddPlayerTorches(TileGrid grid)
    {
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            _lights.AddLight(grid, new GridPos(m.X, m.Y), PlayerTorchRadius);
        }
    }

    // Lantern / crystal / lava light sources — filled in Task 4.
    private void AddSourceLights(TileGrid grid) { }
}
```

- [ ] **Step 2: Wire `LightRenderer` into the ctor scene-build**

In `game/net/MatchClient.cs`, add the field near the fog field (`private FogRenderer _fogRenderer = null!;`, ~line 85):

```csharp
	private LightRenderer _lightRenderer = null!;
```

Then in the constructor, immediately **after** the fog-renderer block (the three lines ending `FogRenderer = _fogRenderer;`, ~line 142), insert:

```csharp
		_lightRenderer = new LightRenderer { Name = "LightRenderer", ZIndex = -8 };
		sceneRoot.AddChild(_lightRenderer);
		_lightRenderer.Init(this);
```

- [ ] **Step 3: Wire `LightRenderer` into `ResetFloor` and teardown**

In `ResetFloor` (~line 404), after the fog-renderer block that ends `FogRenderer = _fogRenderer;`, insert the same three lines using `_sceneRoot`:

```csharp
		_lightRenderer = new LightRenderer { Name = "LightRenderer", ZIndex = -8 };
		_sceneRoot.AddChild(_lightRenderer);
		_lightRenderer.Init(this);
```

In the teardown near `_fogRenderer?.QueueFree(); _fogRenderer = null!;` (~line 353), add:

```csharp
		_lightRenderer?.QueueFree(); _lightRenderer = null!;
```

- [ ] **Step 4: Toggle spectator alongside fog**

At the two spots that set `_fogRenderer.SpectatorMode` (~lines 517 & 524), add the matching line right after each:

```csharp
			if (_lightRenderer != null) _lightRenderer.SpectatorMode = false;   // beside the fog =false line
```
```csharp
			if (_lightRenderer != null) _lightRenderer.SpectatorMode = true;    // beside the fog =true line
```

- [ ] **Step 5: Build**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Play-test verification**

Launch the game (PowerShell: `godot`), start any match. Expect: caves are noticeably darker; your miner sits in a soft lit pool ~4 tiles across that moves with you; the lit pool is cut off by walls (darkness fills corners behind rock). Dying (spectator) shows the full map with no darkness.

- [ ] **Step 7: Commit**

```bash
git add game/LightRenderer.cs game/net/MatchClient.cs
git commit -m "feat(light): darkness overlay with per-miner torch light-map"
```

---

### Task 4: Add lantern, crystal, and lava light sources (Godot)

No unit test (Godot rendering); verified by build + play-test.

**Files:**
- Modify: `game/LightRenderer.cs` (`AddSourceLights` + new radius constants)

**Interfaces:**
- Consumes: `MatchClient.Miners` (`m.Held` — int, compare to `(int)ItemKind.*`), `MatchClient.Items` (each `it.Kind`, `it.Placement`, `it.X`, `it.Y`), `TileGrid.Get(GridPos)`, `ItemKind.Lantern`, `ItemKind.CrystalShard`, `ItemPlacement.Loose`, `TileType.Lava`, `TileType.LavaVent`, `TileType.CrystalRock`.

- [ ] **Step 1: Replace the stub `AddSourceLights` and add radius constants**

In `game/LightRenderer.cs`, add these constants beside `PlayerTorchRadius`:

```csharp
    private const int LanternRadius = 6;
    private const int CrystalRadius = 4;
    private const int LavaRadius    = 3;
```

Replace the stub method:

```csharp
    // Lantern / crystal / lava light sources — filled in Task 4.
    private void AddSourceLights(TileGrid grid) { }
```

with:

```csharp
    private void AddSourceLights(TileGrid grid)
    {
        // Held lanterns / crystal shards.
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            if (m.Held == (int)ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), LanternRadius);
            else if (m.Held == (int)ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), CrystalRadius);
        }

        // Dropped lanterns / shards on the ground.
        foreach (var it in _client.Items)
        {
            if (it.Placement != ItemPlacement.Loose) continue;
            if (it.Kind == ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), LanternRadius);
            else if (it.Kind == ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), CrystalRadius);
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
                    _lights.AddLight(grid, p, LavaRadius);
                else if (t == TileType.CrystalRock)
                    _lights.AddLight(grid, p, CrystalRadius);
            }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Play-test verification**

In a match: a carried or dropped lantern lights a wider amber area (the existing halo now reads as real light against the dark); crystal clusters and lava pools glow and light their surroundings; light from all of them stops at walls. On a floor with lava, confirm no visible frame-rate drop while lava is on screen (the source scan is viewport-bounded).

- [ ] **Step 4: Commit**

```bash
git add game/LightRenderer.cs
git commit -m "feat(light): lanterns, crystals, and lava feed the light-map"
```

---

### Task 5: Cool-blue player torch glow (Godot)

Gives the player torch its cool colour (the darkness-thinning alone is colourless). No unit test; build + play-test.

**Files:**
- Modify: `game/WorldRenderer.cs` (glow-texture fields ~line 116, `_Ready` texture build ~line 213, glow-boost constants ~line 62, the light-pass miner loop ~line 1201)

**Interfaces:**
- Consumes: existing `BuildRadialGlowTex(Color inner, Color outer)`, `_client.Miners`, `_client.Fog.IsVisible`, `_client.MinerVisualPos(id, x, y)`, the local `bool spectating` already in scope in `_Draw`, `MatchClient.TileSize` (`ts`).

- [ ] **Step 1: Add the torch glow texture field and boost colour**

In `game/WorldRenderer.cs`, beside `private ImageTexture _lanternGlowTex = null!;` (~line 116) add:

```csharp
	private ImageTexture _torchGlowTex   = null!;
```

Beside `LanternGlowBoost` (~line 62) add:

```csharp
	// Cool blue-white — only the bright core blooms; dimmer/cooler than the lantern.
	private static readonly Color TorchGlowBoost = new(0.70f, 0.85f, 1.15f);
	private const int TorchGlowTiles = 4;
```

- [ ] **Step 2: Build the torch glow texture in `_Ready`**

Beside `_lanternGlowTex = BuildRadialGlowTex();` (~line 213) add:

```csharp
		_torchGlowTex  = BuildRadialGlowTex(new Color(0.45f, 0.62f, 1.0f, 1f), new Color(0.28f, 0.42f, 0.90f, 1f));
```

- [ ] **Step 3: Draw the torch glow in the light pass**

In `_Draw`, immediately **before** the `// Lantern light:` block (~line 1201), insert:

```csharp
		// Player torch: a soft cool-blue pool under every living miner. Its own light-map
		// entry (LightRenderer) thins the darkness here so this glow reads through.
		int torchPx = TorchGlowTiles * ts * 2;
		foreach (var m in _client.Miners)
		{
			if (!m.Alive) continue;
			if (!_client.Fog.IsVisible(new GridPos(m.X, m.Y)) && !spectating) continue;
			var tc = _client.MinerVisualPos(m.Id, m.X, m.Y);
			DrawTextureRect(_torchGlowTex,
				new Rect2(tc.X - torchPx / 2f, tc.Y - torchPx / 2f, torchPx, torchPx),
				false, TorchGlowBoost);
		}
```

- [ ] **Step 4: Build**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Play-test verification**

Each miner now carries a faint cool-blue halo, clearly cooler and smaller than the amber lantern. Holding a lantern, the warm amber dominates the blue.

- [ ] **Step 6: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(light): cool-blue player torch glow"
```

---

### Task 6: Ghost exemption overlay (Godot)

Ghosts must stay full-bright even in unlit areas, so they draw above the darkness. No unit test; build + play-test.

**Files:**
- Create: `game/GhostOverlay.cs`
- Modify: `game/WorldRenderer.cs` (monster loop ~line 1230 — skip ghosts; add `DrawGhosts`), `game/net/MatchClient.cs` (create the overlay at ZIndex -7 in both scene-build and `ResetFloor`; free it in teardown)

**Interfaces:**
- Consumes: `_client.Monsters` (each `mo.Alive`, `mo.X`, `mo.Y`, `mo.Kind`, `mo.Facing`, `mo.Id`), `_client.Fog.IsVisible`, `_client.MonsterVisualPos(id, x, y)`, `MonsterKind.Ghost`, the ghost textures `_ghostWalkTex[dir, frame]` / `_ghostTex[dir]`, `GhostColor`, `MatchClient.TileSize`.
- Produces: `WorldRenderer.DrawGhosts(CanvasItem target)`; `GhostOverlay` node with `Init(WorldRenderer)` (the overlay reaches `MatchClient` through the world renderer).

- [ ] **Step 1: Skip ghosts in the main monster loop and extract `DrawGhosts`**

In `game/WorldRenderer.cs`, in the monster loop, right after the visibility check `if (!_client.Fog.IsVisible(mp)) continue;` (~line 1234) add:

```csharp
			if (mo.Kind == MonsterKind.Ghost) continue;   // ghosts drawn by GhostOverlay, above the darkness
```

Then delete the whole `case MonsterKind.Ghost:` block (~lines 1258–1275) from the switch (it is now unreachable).

Add this method to `WorldRenderer` (place it just after the `_Draw` method):

```csharp
	/// <summary>Draws every visible ghost onto <paramref name="target"/>. Called by GhostOverlay,
	/// which sits above the darkness overlay so ghosts stay full-bright (they emit no light and
	/// cast no shadow). Ghosts are skipped by the in-world monster pass in <see cref="_Draw"/>.</summary>
	public void DrawGhosts(CanvasItem target)
	{
		if (_client == null) return;
		int ts = MatchClient.TileSize;
		foreach (var mo in _client.Monsters)
		{
			if (!mo.Alive || mo.Kind != MonsterKind.Ghost) continue;
			var mp = new GridPos(mo.X, mo.Y);
			if (!_client.Fog.IsVisible(mp)) continue;
			var c = _client.MonsterVisualPos(mo.Id, mo.X, mo.Y);
			int ghostFrame = (int)(Time.GetTicksMsec() / 150u) % 9;
			var tex = _ghostWalkTex[mo.Facing, ghostFrame] ?? _ghostTex[mo.Facing];
			if (tex != null)
			{
				float gs = ts * 1.3f;
				target.DrawTextureRect(tex, new Rect2(c.X - gs / 2f, c.Y - gs / 2f, gs, gs), false);
			}
			else
			{
				var ghostCol = GhostColor with { A = 0.6f };
				var headOff  = new Vector2(0, -ts * 0.10f);
				target.DrawCircle(c + headOff, ts * 0.28f, ghostCol);
				target.DrawRect(new Rect2(c.X - ts * 0.28f, c.Y - ts * 0.10f, ts * 0.56f, ts * 0.28f), ghostCol);
			}
		}
	}
```

- [ ] **Step 2: Create `game/GhostOverlay.cs`**

```csharp
using Godot;

namespace Miner49er;

/// <summary>Draws ghosts above the darkness overlay (ZIndex -7) so they render full-bright,
/// unaffected by the light-map. All ghost drawing lives in <see cref="WorldRenderer.DrawGhosts"/>;
/// this node just provides a canvas at the right layer. Client-side presentation only.</summary>
public partial class GhostOverlay : Node2D
{
    private WorldRenderer _world = null!;

    public void Init(WorldRenderer world) => _world = world;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world != null) _world.DrawGhosts(this);
    }
}
```

- [ ] **Step 3: Wire `GhostOverlay` into `MatchClient`**

Add a field beside `_lightRenderer`:

```csharp
	private GhostOverlay _ghostOverlay = null!;
```

In the constructor, right after the `_lightRenderer` block (Task 3, Step 2), insert:

```csharp
		_ghostOverlay = new GhostOverlay { Name = "GhostOverlay", ZIndex = -7 };
		sceneRoot.AddChild(_ghostOverlay);
		_ghostOverlay.Init(_world);
```

In `ResetFloor`, right after the `_lightRenderer` block (Task 3, Step 3), insert the same with `_sceneRoot`:

```csharp
		_ghostOverlay = new GhostOverlay { Name = "GhostOverlay", ZIndex = -7 };
		_sceneRoot.AddChild(_ghostOverlay);
		_ghostOverlay.Init(_world);
```

In teardown, beside `_lightRenderer?.QueueFree(); _lightRenderer = null!;`, add:

```csharp
		_ghostOverlay?.QueueFree(); _ghostOverlay = null!;
```

- [ ] **Step 4: Build**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 5: Play-test verification**

On a floor with ghosts (`MonsterKind.Ghost` spawns from floor 1): a ghost stays bright and clearly visible even when it drifts into an unlit corner, while a slime/zombie in the same corner is dimmed by the darkness. Ghosts still vanish correctly when outside your line of sight.

- [ ] **Step 6: Commit**

```bash
git add game/GhostOverlay.cs game/WorldRenderer.cs game/net/MatchClient.cs
git commit -m "feat(light): ghosts render above the darkness, unaffected by lighting"
```

---

### Task 7: Soften fog's in-view gradient + full-suite verification (Godot)

The fog's radial in-view dim now double-darkens with the light-map. Narrow fog's job to the LOS mask (unexplored/explored/boundary) and only a gentle edge vignette. No unit test; build + play-test + full suite.

**Files:**
- Modify: `game/FogRenderer.cs` (`BuildFogGradientTex` ~lines 30–45; add an in-view alpha constant near `DimAlpha` ~line 21)

**Interfaces:**
- Consumes: existing `DimAlpha`, `Dim`, `Unexplored`, `BuildFogGradientTex(int radius, int size)`.

- [ ] **Step 1: Add a softer in-view gradient alpha**

In `game/FogRenderer.cs`, beside `private const float DimAlpha = 0.78f;` (~line 21) add:

```csharp
	// The visible disc is now lit by the light-map, so fog only lightly vignettes it —
	// far below the flat DimAlpha used for explored-but-not-visible tiles.
	private const float InViewDimAlpha = 0.32f;
```

- [ ] **Step 2: Use it in `BuildFogGradientTex`**

In `BuildFogGradientTex`, change the alpha computation (~lines 39–41) from:

```csharp
				float alpha = t < edgeFraction ? DimAlpha * Mathf.Pow(t / edgeFraction, 3.0f)
				            : t < 1f           ? DimAlpha
				            :                    0f;
```

to:

```csharp
				float alpha = t < edgeFraction ? InViewDimAlpha * Mathf.Pow(t / edgeFraction, 3.0f)
				            : t < 1f           ? InViewDimAlpha
				            :                    0f;
```

(The explored/unexplored flat fills in `_Draw` keep using `Dim`/`Unexplored` — only the in-view radial texture softens.)

- [ ] **Step 3: Build**

Run: `dotnet build -c Debug`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: all pass — the prior baseline (~800) plus the 8 new Core tests from Tasks 1–2, with zero failures.

- [ ] **Step 5: Play-test verification**

The center of your view is no longer doubly-darkened: the light-map now owns how lit the visible area is, while fog still hard-cuts unexplored (black) and explored-but-unseen (dim) tiles, with only a gentle vignette at the LOS edge. Overall the cave reads dark-and-moody with clear lit pools, not muddy.

- [ ] **Step 6: Commit**

```bash
git add game/FogRenderer.cs
git commit -m "feat(light): soften fog's in-view gradient so the light-map owns brightness"
```

---

## Completion

After Task 7, all lighting behavior is in place. Hand off to **superpowers:finishing-a-development-branch** to run the suite, merge to `main`, and push. (Per user workflow: fast-forward merge to `main`, push, delete the branch, then update the `gfx-pop-status` memory to mark item 2 done.)

Tuning knobs to revisit in play-test (all in one place each): `AmbientDarkAlpha`, `PlayerTorchRadius`, `LanternRadius`, `CrystalRadius`, `LavaRadius` (`LightRenderer`); `TorchGlowBoost`, `TorchGlowTiles`, `LanternGlowBoost` (`WorldRenderer`); `InViewDimAlpha` (`FogRenderer`).
