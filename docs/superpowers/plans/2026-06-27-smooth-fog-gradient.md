# Smooth Fog Gradient Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the blocky per-tile fog edge in `FogRenderer` with a single smooth radial gradient texture centred on the local player.

**Architecture:** Build a 256×256 black gradient texture once in `Init()` (alpha 0 at centre → 0.6 at radius, 0 outside circle); draw it each frame via one `DrawTextureRect` scaled to `vision_radius * 2 * tile_size`. The per-tile visible-rim veil pass is deleted entirely. All LOS logic remains in Core unchanged.

**Tech Stack:** Godot 4.6.3 C#, `Node2D._Draw()`, `Image` / `ImageTexture` (same pattern as `BuildRadialGlowTex` in `WorldRenderer.cs`).

## Global Constraints

- Modify `game/FogRenderer.cs` only — no Core, no other game files.
- `godot` must be launched via **PowerShell only** (not Bash — the Bash shim produces a false "assemblies not found" error).
- Never `git add -A`; never stage `.superpowers/`, `*.uid`, or preview files.

---

### Task 1: Replace per-tile veil with smooth gradient texture

**Files:**
- Modify: `game/FogRenderer.cs`

**Interfaces:**
- Consumes: `MatchClient.TileSize` (int, = 32), `LocalMinerView()` → `(GridPos origin, int radius)`, `fog.IsVisible(GridPos)`, `fog.IsExplored(GridPos)`.
- Produces: smooth circular fog fade visible in-game.

---

- [ ] **Step 1: Open `game/FogRenderer.cs` and replace the entire file contents**

The complete new file is shown below. Read the current file first to confirm the structure matches, then apply all changes at once.

**Current file structure to confirm** (lines 1–61 of `game/FogRenderer.cs`):
- Line 15: `private static readonly Color Unexplored = new(0, 0, 0, 1f);`
- Line 16: `private static readonly Color Dim = new(0, 0, 0, 0.6f);`
- Line 17: `private const float EdgeVeil = 0.35f;`
- Line 18: `private const float ClearUntil = 0.7f;`
- Line 20: `public void Init(MatchClient client) => _client = client;`
- Lines 24–51: `_Draw()` iterates all tiles with per-tile visible-rim veil logic.

**New complete file:**

```csharp
using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness as a smooth radial gradient centred on the local miner.
/// Unexplored = opaque black, explored-but-not-visible = flat dim, currently visible =
/// a pixel-smooth circular falloff from clear at the centre to dim at the radius edge.
/// Wall occlusion is preserved: tiles excluded from fog.IsVisible() keep their dim/unexplored
/// background; the gradient adds darkness, never brightness.</summary>
public partial class FogRenderer : Node2D
{
	private MatchClient _client = null!;
	private ImageTexture _fogGradientTex = null!;

	private static readonly Color Unexplored = new(0, 0, 0, 1f);
	private const float DimAlpha = 0.6f;
	private static readonly Color Dim = new(0, 0, 0, DimAlpha);

	public void Init(MatchClient client)
	{
		_client = client;
		_fogGradientTex = BuildFogGradientTex();
	}

	// Black circular gradient: alpha 0 at centre → DimAlpha at radius, 0 outside circle.
	// Drawn once at Init; scaled each frame to match the miner's vision radius.
	private static ImageTexture BuildFogGradientTex(int size = 256)
	{
		var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		float half = size / 2f;
		for (int y = 0; y < size; y++)
		for (int x = 0; x < size; x++)
		{
			float t = new Vector2(x - half, y - half).Length() / half;
			float alpha = t < 1f ? DimAlpha * Mathf.Pow(t, 2f) : 0f;
			img.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
		}
		return ImageTexture.CreateFromImage(img);
	}

	public override void _Process(double delta) => QueueRedraw();

	public override void _Draw()
	{
		if (_client == null) return;
		var grid = _client.Grid;
		var fog = _client.Fog;
		int ts = MatchClient.TileSize;

		// Background pass: dim explored tiles and black out unexplored ones.
		// Visible tiles are skipped here — the gradient handles them below.
		foreach (var p in grid.Positions())
		{
			if (fog.IsVisible(p)) continue;
			DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts),
					 fog.IsExplored(p) ? Dim : Unexplored);
		}

		// Smooth radial gradient centred on the local miner.
		// Sized so t=1 in texture space lands exactly at vision_radius tiles,
		// where gradient alpha (DimAlpha) matches the Dim background seamlessly.
		var (origin, radius) = LocalMinerView();
		if (radius > 0)
		{
			int gradPx = radius * 2 * ts;
			var centre = new Vector2(origin.X * ts + ts / 2f, origin.Y * ts + ts / 2f);
			DrawTextureRect(_fogGradientTex,
				new Rect2(centre.X - gradPx / 2f, centre.Y - gradPx / 2f, gradPx, gradPx),
				false);
		}
	}

	// Local miner's grid position and vision radius, for the radial gradient.
	private (GridPos origin, int radius) LocalMinerView()
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId)
				return (new GridPos(m.X, m.Y), m.VisionRadius);
		return (new GridPos(0, 0), 0);
	}
}
```

- [ ] **Step 2: Build and confirm zero errors**

```powershell
cd D:\Projects\Miner49er; dotnet build
```

Expected: `Build succeeded. 0 Error(s)` (warnings about unused variables are fine as long as the count matches the pre-change baseline — there should be none introduced by this change).

- [ ] **Step 3: Visual verification in-game**

Launch the game:
```powershell
cd D:\Projects\Miner49er; godot --path . project.godot
```

Start a local match and observe the fog edge around your miner. Confirm:
1. The fog boundary is a smooth circular gradient — no tile-grid staircase.
2. The centre of vision is fully clear (bright).
3. The edge of the vision circle fades smoothly into the dim grey remembered tiles.
4. Moving around a corner (wall occlusion): tiles behind walls remain dim/unexplored; the gradient does not "punch through" walls to reveal hidden areas.
5. Picking up a lantern: the gradient circle expands from radius 5 → 7 seamlessly.

- [ ] **Step 4: Commit**

```bash
git add game/FogRenderer.cs
git commit -m "feat(fog): smooth radial gradient replaces blocky per-tile veil"
```
