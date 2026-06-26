# Smooth Fog Gradient Design

**Goal:** Replace the blocky per-tile fog edge around the player's vision circle with a pixel-smooth radial gradient that fades seamlessly from clear (centre) to the dim "remembered" state (edge).

**Architecture:** Add one pre-built gradient texture to `FogRenderer`; draw it as a single `DrawTextureRect` centred on the local miner each frame, replacing the existing per-tile edge-veil pass. All LOS / fog-state logic stays in Core unchanged.

**Tech Stack:** Godot 4 C#, `Node2D._Draw()`, `Image`/`ImageTexture` (same as the lantern glow in `WorldRenderer`).

---

## Global Constraints

- No Core changes — `fog.IsVisible()` / `fog.IsExplored()` remain the authoritative source of truth for occlusion.
- No new files — change is entirely inside `game/FogRenderer.cs`.
- Visual occlusion is preserved: wall-blocked tiles stay dim; the gradient only softens the rim of the *visible* set.
- Multiplayer: gradient follows the *local* miner's position and radius (already sourced from `LocalMinerView()`).

---

## How the Current Renderer Works

`FogRenderer._Draw()` iterates every grid position:

| Tile state | Current action |
|---|---|
| `fog.IsVisible` — inner 70 % of radius | `continue` (clear) |
| `fog.IsVisible` — outer 30 % rim | `DrawRect` with alpha up to `EdgeVeil = 0.35` (linear step) |
| Not visible, explored | `DrawRect` `Dim = (0,0,0,0.6)` |
| Not visible, unexplored | `DrawRect` `Unexplored = (0,0,0,1.0)` |

The "outer rim" veil is the blocky part: every tile in that annulus gets a flat colour for its whole 32 × 32 square, so the transition staircase-steps with the tile grid.

---

## What Changes

### 1 — Gradient texture

`BuildFogGradientTex(int size = 256)` builds a 256 × 256 `ImageTexture` (built once in `Init()`):

- Each pixel's alpha = `t < 1f ? DimAlpha * Mathf.Pow(t, 2f) : 0f`
  where `t = Vector2(x − half, y − half).Length() / half`
- Colour is always `(0, 0, 0)` (pure black).
- Result: a circular gradient — opaque at the radius boundary (matching `Dim`), transparent at the centre and fully transparent outside the circle.

`Pow(t, 2f)` keeps the central play area clear for most of the vision distance and concentrates the fade in the outer third, matching the feel of the existing `ClearUntil = 0.7` constant.

### 2 — Simplified draw loop

The per-tile loop drops the visible-rim veil case entirely:

```
foreach (var p in grid.Positions())
{
    if (fog.IsVisible(p)) continue;           // clear — handled by gradient below
    DrawRect(..., fog.IsExplored(p) ? Dim : Unexplored);
}
```

### 3 — Gradient draw (after the loop)

```
var (origin, radius) = LocalMinerView();
if (radius > 0)
{
    int gradPx = radius * 2 * MatchClient.TileSize;
    var centre = new Vector2(origin.X * ts + ts / 2f, origin.Y * ts + ts / 2f);
    DrawTextureRect(_fogGradientTex,
        new Rect2(centre.X - gradPx / 2f, centre.Y - gradPx / 2f, gradPx, gradPx),
        false);
}
```

`gradPx = radius * 2 * ts` scales the fixed 256 × 256 texture so `t = 1` lands exactly at the vision-radius boundary, where the gradient's alpha (0.6) matches `Dim` precisely — a seamless join.

Since `radius` comes from `m.VisionRadius` each frame, the gradient automatically resizes when the player picks up a lantern (radius 5 → 7).

---

## Occlusion Preservation

The gradient is a *black overlay* that adds darkness. It cannot make a hidden tile appear lighter than its background:

- Non-visible tiles within the gradient circle still have `Dim` (0.6) or `Unexplored` (1.0) drawn first. The gradient's alpha near the centre is close to zero, so these tiles remain visually unchanged.
- At the rim, gradient alpha ≈ 0.6, which slightly deepens `Dim` tiles that are wall-blocked at that distance — they look no brighter than `Dim`, never clearer.
- The irregular wall-carved boundary of the visible set remains visible; the gradient only smooths the *fade* across it, not the shape of it.

---

## Fields Added / Removed in FogRenderer

Added:
```csharp
private ImageTexture _fogGradientTex = null!;
private const float DimAlpha = 0.6f;  // kept in sync with Dim colour
```

Removed (dead code once per-tile veil is gone):
```csharp
private const float EdgeVeil = 0.35f;
private const float ClearUntil = 0.7f;
```

---

## No Tests Required

This is a pure visual change with no Core or networking impact. Verify by running the game and observing the fog edge.
