# Lantern Overhaul Design

**Date:** 2026-06-25

## Goal

Replace the placeholder lantern visuals (circles + flat yellow tint) with a proper lantern sprite and a smooth radial amber glow, and make holding a lantern extend the carrier's fog-of-war vision radius.

## Current State

- **Item visual:** Two `DrawCircle` calls in `WorldRenderer` — a filled amber circle with a dark outline. No sprite.
- **Glow overlay:** A flat uniform `DrawRect` per visible tile within Chebyshev 3 of any held/dropped lantern, using `LanternGlowColor = new Color(1f, 0.9f, 0.3f, 0.18f)`. No radial falloff.
- **Vision:** Holding a lantern does NOT change `EffectiveVisionRadius`. It only kills/repels ghosts (Chebyshev radius 3) and draws the flat tint.
- **Ghost logic:** Binary — any ghost within Chebyshev 3 of a held or dropped lantern is killed each tick; ghosts won't step into that zone. This is unchanged.

## What Changes

### 1 — Lantern sprite (`assets/objects/item_lantern.png`)

Generate a new sprite from PixelLab (`create_1_direction_object` or `create_map_object`):
- **Description:** `"small hanging mine lantern, top-down view, warm amber glow, dark metal frame, glass panels, candle inside, pixel art"`
- **View:** `"high top-down"`
- **Outline:** `"lineless"`
- **Detail:** `"highly detailed"`
- **Size:** 16×16

`WorldRenderer`:
- Add `LoadItemTex(ItemKind.Lantern, "res://assets/objects/item_lantern.png")` alongside the other `LoadItemTex` calls in `_Ready()`.
- Remove both `DrawCircle` blocks that currently handle `it.Kind == ItemKind.Lantern` (loose and toolbox cases). The item texture fallthrough already handles `_itemTex` keys.

### 2 — Radial glow overlay

**Generate a gradient texture at startup.**

`WorldRenderer` gains a new field `private ImageTexture _lanternGlowTex = null!;` and a helper called from `_Ready()`:

```csharp
private static ImageTexture BuildRadialGlowTex(int size = 128)
{
    var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
    float half = size / 2f;
    for (int y = 0; y < size; y++)
    for (int x = 0; x < size; x++)
    {
        float t = Mathf.Clamp(new Vector2(x - half, y - half).Length() / half, 0f, 1f);
        float a = Mathf.Pow(1f - t, 2.5f);   // cubic-ish falloff, bright center
        img.SetPixel(x, y, new Color(1f, 0.85f, 0.4f, a * 0.55f));
    }
    return ImageTexture.CreateFromImage(img);
}
```

Color: warm amber `(1, 0.85, 0.4)` at peak opacity ~0.55, fading to transparent.

**Replace the per-tile glow loop.**

Remove:
```csharp
// Lantern light: dim glow over fog-visible tiles within AOE of held or placed lanterns
foreach (var p in grid.Positions())
{
    if (!_client.Fog.IsVisible(p)) continue;
    if (IsInLanternLight(p))
        DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), LanternGlowColor);
}
```

And remove the `IsInLanternLight()` helper method and the `LanternGlowColor` and `LanternRadius` constants.

Replace with a single draw call per active lantern source:

```csharp
const int LanternGlowTiles = 5;   // visual radius in tiles (wider than the ghost kill radius of 3)
int glowPx = LanternGlowTiles * ts * 2;
foreach (var m in _client.Miners)
{
    if (!m.Alive || m.Held != (int)ItemKind.Lantern) continue;
    var center = new Vector2(m.X * ts + ts / 2f, m.Y * ts + ts / 2f);
    DrawTextureRect(_lanternGlowTex,
        new Rect2(center.X - glowPx / 2f, center.Y - glowPx / 2f, glowPx, glowPx),
        false);
}
foreach (var it in _client.Items)
{
    if (it.Kind != ItemKind.Lantern || it.Placement != ItemPlacement.Loose) continue;
    var center = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
    DrawTextureRect(_lanternGlowTex,
        new Rect2(center.X - glowPx / 2f, center.Y - glowPx / 2f, glowPx, glowPx),
        false);
}
```

The glow is drawn before monsters/miners so it sits under character sprites, on top of terrain.

### 3 — Vision extension (Core)

**`SimConfig.cs`** — add after `LanternRadius`:
```csharp
public int LanternVisionBonus { get; set; } = 2;
```

**`Simulation.cs`** — in `EffectiveVisionRadius(Miner m)`, after the existing status effect loop, add:
```csharp
if (m.Held == ItemKind.Lantern) bonus += Config.LanternVisionBonus;
```

Effect: holding a lantern raises fog-of-war reveal radius from the default 5 to 7. A dropped lantern gives no vision bonus. The snapshot already carries `VisionRadius` per miner, so `FogRenderer` and fog culling update automatically — no snapshot or network changes needed.

### 4 — Tests

One new test in `SimulationLanternTests.cs`:

```csharp
[Fact]
public void HeldLantern_increases_effective_vision_radius()
{
    var cfg = new SimConfig { VisionRadius = 5, LanternVisionBonus = 2 };
    var grid = new TileGrid(15, 3, TileType.Floor);
    var sim = new Simulation(grid, cfg);
    sim.AddMiner(1, new GridPos(7, 1));
    Assert.Equal(5, sim.EffectiveVisionRadius(1));

    sim.AddItem(new Item(new GridPos(7, 1), ItemKind.Lantern, ItemPlacement.Loose));
    sim.TryUseItem(1);   // pick up
    Assert.Equal(7, sim.EffectiveVisionRadius(1));
}
```

## Scope

- No changes to ghost kill/repel radius (stays Chebyshev 3 — the outer glow is ambient only).
- No network protocol changes.
- No changes to `FogRenderer` — `VisionRadius` in the snapshot is the only coupling.
- One PixelLab job for the sprite.

## Files

| File | Change |
|------|--------|
| `assets/objects/item_lantern.png` | New — PixelLab sprite |
| `assets/objects/item_lantern.png.import` | New — auto-generated by Godot |
| `game/WorldRenderer.cs` | Add gradient texture, replace glow loop, load lantern sprite, remove old constants |
| `src/Miner49er.Core/Sim/SimConfig.cs` | Add `LanternVisionBonus = 2` |
| `src/Miner49er.Core/Sim/Simulation.cs` | `EffectiveVisionRadius` checks `m.Held == ItemKind.Lantern` |
| `src/Miner49er.Core.Tests/SimulationLanternTests.cs` | Add `HeldLantern_increases_effective_vision_radius` test |
