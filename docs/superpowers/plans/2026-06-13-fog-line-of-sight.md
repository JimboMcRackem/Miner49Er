# Fog Line-of-Sight & Smooth Flood Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the fog true line-of-sight (rock blocks vision), render it as a soft round lantern light with hard wall shadows, and soften the flood so rising water seeps in instead of popping ring-by-ring.

**Architecture:** Three focused changes. (1) Core `Visibility.Compute` becomes recursive shadowcasting (same signature, engine-free, unit-tested) gated by a new `BlocksSight` tile predicate. (2) `FogRenderer._Draw` paints a radial-gradient darkness overlay carved by the crisp LOS set. (3) `WorldRenderer` crossfades each tile's displayed color toward its target with a deterministic per-tile stagger. Visibility is already derived client-side, so there are no net/snapshot changes.

**Tech Stack:** C# / .NET 8, Godot 4.6.3 (.NET/Mono), xUnit. Pure-C# `Miner49er.Core` (4-space indent) + Godot adapter in `game/` (TAB indent).

**Conventions for every task:**
- Build the whole solution with: `dotnet build Miner49er.sln` (expected: `Build succeeded. 0 Warning(s) 0 Error(s)`).
- Run Core tests with: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.
- `git add` only the exact files listed in each commit step — never `git add -A` (the working tree has pre-existing untracked `assets/Splash.png*` and CRLF-only changes that must NOT be staged).
- Every commit message MUST end with the trailer line:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Core files use 4-space indentation; `game/` files use TAB indentation.

---

### Task 1: `BlocksSight` sight-blocker predicate (Core)

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Test: `src/Miner49er.Core.Tests/BlocksSightTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/BlocksSightTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class BlocksSightTests
{
    [Theory]
    [InlineData(TileType.Rock)]
    [InlineData(TileType.GoldRock)]
    [InlineData(TileType.ImpermeableRock)]
    public void Rock_family_blocks_sight(TileType t)
    {
        Assert.True(t.BlocksSight());
    }

    [Theory]
    [InlineData(TileType.Floor)]
    [InlineData(TileType.ShallowWater)]
    [InlineData(TileType.DeepWater)]
    [InlineData(TileType.Plank)]
    public void Open_tiles_are_transparent(TileType t)
    {
        Assert.False(t.BlocksSight());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~BlocksSightTests"`
Expected: FAIL — compile error, `TileType` has no method `BlocksSight`.

- [ ] **Step 3: Add the predicate**

In `src/Miner49er.Core/Grid/TileType.cs`, inside `TileTypeExtensions`, add (place it after `IsBlastable`, before `IsWater`):

```csharp
    /// <summary>Blocks line-of-sight (the rock family). Floor, water, and planks are transparent.</summary>
    public static bool BlocksSight(this TileType t) =>
        t is TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~BlocksSightTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core.Tests/BlocksSightTests.cs
git commit -m "feat(core): add BlocksSight tile predicate for line-of-sight

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Recursive shadowcasting in `Visibility.Compute` (Core)

**Files:**
- Modify: `src/Miner49er.Core/Fog/Visibility.cs` (full rewrite, same signature)
- Test: `src/Miner49er.Core.Tests/VisibilityTests.cs` (append new tests; keep the 3 existing ones)

Note: the 3 existing tests (`Visible_set_is_a_radius_disc_clipped_to_bounds`, `Visible_set_clips_at_grid_edges`, `FogState_accumulates_explored_across_updates`) stay unchanged and must keep passing — on an all-`Floor` grid, shadowcasting reproduces the round disc, so they verify no regression.

- [ ] **Step 1: Write the failing tests**

Append these to `src/Miner49er.Core.Tests/VisibilityTests.cs` (inside the existing `VisibilityTests` class, before the closing brace). Add `using System.Linq;` is already present at the top of the file.

```csharp
    [Fact]
    public void Origin_is_always_visible()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(3, 3), radius: 4);
        Assert.Contains(new GridPos(3, 3), visible);
    }

    [Fact]
    public void Rock_wall_blocks_tiles_behind_it()
    {
        // Solid horizontal rock wall across row y=3; miner stands south at (5,5).
        var grid = new TileGrid(11, 11, TileType.Floor);
        for (int x = 0; x < 11; x++) grid.Set(new GridPos(x, 3), TileType.Rock);

        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.Contains(new GridPos(5, 4), visible);     // near-side floor: seen
        Assert.Contains(new GridPos(5, 3), visible);     // the wall face itself: seen
        Assert.DoesNotContain(new GridPos(5, 2), visible); // directly behind the wall: hidden
        Assert.DoesNotContain(new GridPos(5, 1), visible); // farther behind: hidden
    }

    [Fact]
    public void Single_pillar_casts_a_shadow_directly_behind_it()
    {
        // One rock pillar two tiles north of the miner.
        var grid = new TileGrid(11, 11, TileType.Floor);
        grid.Set(new GridPos(5, 3), TileType.Rock);

        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.Contains(new GridPos(5, 3), visible);       // pillar face: seen
        Assert.DoesNotContain(new GridPos(5, 2), visible); // umbra directly behind: hidden
        Assert.Contains(new GridPos(2, 3), visible);       // well to the side: seen
        Assert.Contains(new GridPos(8, 3), visible);       // well to the side: seen
    }

    [Fact]
    public void Visibility_is_symmetric_on_open_ground()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var a = new GridPos(4, 4);
        var b = new GridPos(6, 5);

        var fromA = Visibility.Compute(grid, a, radius: 5);
        var fromB = Visibility.Compute(grid, b, radius: 5);

        Assert.Equal(fromA.Contains(b), fromB.Contains(a));
    }
```

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~VisibilityTests"`
Expected: FAIL — `Rock_wall_blocks_tiles_behind_it` and `Single_pillar_casts_a_shadow_directly_behind_it` fail because the current disc impl has no occlusion (`(5,2)` is currently visible through the wall/pillar). The disc/origin/symmetry tests pass.

- [ ] **Step 3: Rewrite `Visibility.cs` with recursive shadowcasting**

Replace the entire contents of `src/Miner49er.Core/Fog/Visibility.cs` with:

```csharp
namespace Miner49er.Core;

/// <summary>Field-of-view via recursive shadowcasting over 8 octants. Tiles whose
/// <see cref="TileTypeExtensions.BlocksSight"/> is true cast shadows; the blocker
/// tile itself is visible (you see the rock face) but nothing behind it in that
/// cone is. Integer/rational slope math only, so the result is deterministic and
/// identical on host and every client.</summary>
public static class Visibility
{
    // Octant transforms: (xx, xy, yx, yy) for the 8 octants.
    private static readonly int[] Xx = { 1, 0, 0, -1, -1, 0, 0, 1 };
    private static readonly int[] Xy = { 0, 1, -1, 0, 0, -1, 1, 0 };
    private static readonly int[] Yx = { 0, 1, 1, 0, 0, -1, -1, 0 };
    private static readonly int[] Yy = { 1, 0, 0, 1, -1, 0, 0, -1 };

    public static HashSet<GridPos> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var visible = new HashSet<GridPos>();
        if (grid.InBounds(origin)) visible.Add(origin);
        for (int oct = 0; oct < 8; oct++)
            CastLight(grid, origin, radius, visible, 1, 1.0, 0.0,
                      Xx[oct], Xy[oct], Yx[oct], Yy[oct]);
        return visible;
    }

    private static void CastLight(TileGrid grid, GridPos origin, int radius,
        HashSet<GridPos> visible, int row, double startSlope, double endSlope,
        int xx, int xy, int yx, int yy)
    {
        if (startSlope < endSlope) return;
        int r2 = radius * radius;
        double nextStartSlope = startSlope;

        for (int i = row; i <= radius; i++)
        {
            bool blocked = false;
            for (int dx = -i, dy = -i; dx <= 0; dx++)
            {
                double lSlope = (dx - 0.5) / (dy + 0.5);
                double rSlope = (dx + 0.5) / (dy - 0.5);
                if (startSlope < rSlope) continue;
                if (endSlope > lSlope) break;

                int mapX = origin.X + dx * xx + dy * xy;
                int mapY = origin.Y + dx * yx + dy * yy;
                var p = new GridPos(mapX, mapY);

                if (dx * dx + dy * dy <= r2 && grid.InBounds(p))
                    visible.Add(p);

                bool wall = !grid.InBounds(p) || grid.Get(p).BlocksSight();

                if (blocked)
                {
                    if (wall)
                    {
                        nextStartSlope = rSlope;
                        continue;
                    }
                    blocked = false;
                    startSlope = nextStartSlope;
                }
                else if (wall && i < radius)
                {
                    blocked = true;
                    CastLight(grid, origin, radius, visible, i + 1, startSlope, lSlope,
                              xx, xy, yx, yy);
                    nextStartSlope = rSlope;
                }
            }

            if (blocked) break;
        }
    }
}
```

- [ ] **Step 4: Run the full Visibility suite to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~VisibilityTests"`
Expected: PASS (all 7: 3 original + 4 new).

- [ ] **Step 5: Run the whole Core suite to confirm no regressions**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all existing tests still green — visibility is the only Core change and its public signature is unchanged).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Fog/Visibility.cs src/Miner49er.Core.Tests/VisibilityTests.cs
git commit -m "feat(core): true line-of-sight via recursive shadowcasting

Rock blocks vision; blocker tiles are themselves visible. Same Compute
signature, deterministic integer slope math, no net surface.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Soft round light + wall shadows (FogRenderer)

**Files:**
- Modify: `game/FogRenderer.cs` (rewrite `_Draw`; add a small helper; keep `Init`/`_Process`)

This is a Godot adapter (no unit tests). Verification is a clean solution build; visual confirmation happens at the play-test gate (Task 5). Uses TAB indentation.

- [ ] **Step 1: Rewrite `game/FogRenderer.cs`**

Replace the entire contents with:

```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness as a soft round lantern light. Unexplored = opaque
/// black, explored-but-not-visible = flat dim, currently visible = a radial falloff
/// that stays fully clear through the play area and feathers into fog only at the
/// rim. Wall shadows are carved for free: occluded tiles are simply absent from the
/// visible set, so they read as full dark and the gradient never bleeds into
/// them.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private static readonly Color Dim = new(0, 0, 0, 0.6f);
	private const float EdgeVeil = 0.35f;   // max darkness alpha at the lit rim
	private const float ClearUntil = 0.7f;  // fraction of radius that stays fully clear

	public void Init(MatchClient client) => _client = client;

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;
		var (origin, radius) = LocalMinerView();

		foreach (var p in grid.Positions())
		{
			Color color;
			if (fog.IsVisible(p))
			{
				if (radius <= 0) continue; // no falloff info: leave clear
				int ddx = p.X - origin.X, ddy = p.Y - origin.Y;
				float t = Mathf.Sqrt(ddx * ddx + ddy * ddy) / radius; // 0 at miner, 1 at rim
				if (t <= ClearUntil) continue;                         // clear core
				float k = (t - ClearUntil) / (1f - ClearUntil);
				float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k, 0f, 1f)) * EdgeVeil;
				color = new Color(0, 0, 0, alpha);
			}
			else
			{
				color = fog.IsExplored(p) ? Dim : Unexplored;
			}
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
		}
	}

	// Local miner's grid position and vision radius, for the radial falloff.
	private (GridPos origin, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (new GridPos(m.X, m.Y), m.VisionRadius);
		return (new GridPos(0, 0), 0);
	}
}
```

- [ ] **Step 2: Build the solution to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add game/FogRenderer.cs
git commit -m "feat(game): soft round fog light with carved wall shadows

Radial alpha falloff over the LOS set: clear play-area core, feathered rim;
occluded tiles stay full dark.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Smooth flood crossfade + seep (WorldRenderer)

**Files:**
- Modify: `game/WorldRenderer.cs` (add per-tile displayed-color state; ease in `_Process`; draw eased color; extract a `TargetColor` helper)

Godot adapter (no unit tests). Verify with a clean build; visual confirmation at the play-test gate. TAB indentation.

- [ ] **Step 1: Add per-tile crossfade state fields**

In `game/WorldRenderer.cs`, after the existing `_flashes` field (line ~12, `private readonly System.Collections.Generic.List<(GridPos pos, float life)> _flashes = new();`), add:

```csharp
	// Smooth flood: each tile's currently-shown color eases toward its grid target.
	private readonly System.Collections.Generic.Dictionary<GridPos, Color> _displayed = new();
	private readonly System.Collections.Generic.Dictionary<GridPos, Color> _target = new();
	private readonly System.Collections.Generic.Dictionary<GridPos, float> _delay = new();
	private const float FadeRate = 6f; // exponential approach; ~0.45s to settle
```

- [ ] **Step 2: Extract a `TargetColor` helper**

Add this method to the class (e.g. just above `_Draw`). It mirrors the existing tile-color switch exactly:

```csharp
	private Color TargetColor(TileType t) => t switch
	{
		TileType.Floor => FloorColor,
		TileType.Rock => RockColor,
		TileType.GoldRock => GoldColor,
		TileType.ImpermeableRock => ImpermeableColor,
		TileType.ShallowWater => ShallowWaterColor,
		TileType.DeepWater => DeepWaterColor,
		TileType.Plank => PlankColor,
		_ => FloorColor,
	};

	// Deterministic per-tile stagger so a flooded ring seeps in unevenly (0..0.25s).
	private static float SeepDelay(GridPos p)
	{
		int h = (p.X * 73856093) ^ (p.Y * 19349663);
		h &= 0x7fffffff;
		return (h % 1000) / 1000f * 0.25f;
	}
```

- [ ] **Step 3: Drive the easing in `_Process`**

In `game/WorldRenderer.cs`, inside `_Process(double delta)`, immediately before the existing `QueueRedraw();` call (line ~46), insert the easing loop:

```csharp
		var grid = _client?.Grid;
		if (grid != null)
		{
			foreach (var p in grid.Positions())
			{
				var tgt = TargetColor(grid.Get(p));
				if (!_displayed.ContainsKey(p))
				{
					_displayed[p] = tgt; // snap on first sight: no fade for pre-existing water
					_target[p] = tgt;
					continue;
				}
				if (_target[p] != tgt)
				{
					_target[p] = tgt;
					_delay[p] = SeepDelay(p); // arm the seep stagger on a new transition
				}
				if (_delay.TryGetValue(p, out float d) && d > 0f)
				{
					_delay[p] = d - (float)delta;
					continue; // still waiting to start easing
				}
				float w = Mathf.Min(1f, FadeRate * (float)delta);
				_displayed[p] = _displayed[p].Lerp(tgt, w);
			}
		}
```

- [ ] **Step 4: Draw the eased color**

In `game/WorldRenderer.cs`, replace the tile-drawing loop in `_Draw` (the `foreach (var p in grid.Positions())` block that builds `color` from a `switch` and calls `DrawRect`, lines ~55-69) with:

```csharp
			foreach (var p in grid.Positions())
			{
				var color = _displayed.TryGetValue(p, out var c) ? c : TargetColor(grid.Get(p));
				DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
			}
```

(The `var grid = _client.Grid;` and `int ts = MatchClient.TileSize;` lines at the top of `_Draw` stay as they are.)

- [ ] **Step 5: Build the solution to verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add game/WorldRenderer.cs
git commit -m "feat(game): smooth flood crossfade with per-tile seep stagger

Each tile eases its shown color toward the grid target; new floods are
delayed by a deterministic per-position offset so a ring seeps in unevenly.
Pre-existing water snaps on first sight (no fade storm on join).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Full verification gate

**Files:** none (verification only).

- [ ] **Step 1: Clean build of the whole solution**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Full Core test suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all tests green (the prior suite plus the 7 `BlocksSightTests` and 4 new `VisibilityTests`).

- [ ] **Step 3: Hand off to the play-test gate**

Report build/test results, then stop for the human play-test (per project policy: play-test before merge). Confirm by play-testing:
- Rock now blocks vision — corridors reveal only what's in line of sight; no seeing through walls.
- Fog reads as a soft round light: clear, readable play area with a feathered rim, hard shadows behind rock.
- Rising water seeps in smoothly and unevenly rather than popping a whole ring at once.
- Listen still reveals buried-item shimmer through rock exactly as before.

Do NOT merge until the human approves the play-test. Completion is handled by superpowers:finishing-a-development-branch.
```
