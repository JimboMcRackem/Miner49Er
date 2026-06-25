# Animated Water Design

**Date:** 2026-06-25

## Goal

Replace the static flat-color deep-water overlay with proper Wang tile boundaries between shallow and deep water, and add a gently lapping wave animation to interior water tiles via a canvas-item shader.

## Current State

- `TerrainMap` has 5 terrain IDs: Wall(0), Floor(1), Lava(2), Water(3), Pit(4).
- Both `TileType.ShallowWater` and `TileType.DeepWater` map to terrain 3 — they share the same Wang tiles with no visual distinction.
- `WorldRenderer` draws a `deepOverlay` `DrawRect` (dark-blue semi-transparent rect) over each `DeepWater` tile as a crude workaround.
- No shader exists anywhere in the project.
- The combined atlas is rebuilt from `tools/pixellab_tileset_converter.gd` given 4 JSON+PNG pairs.

## What Changes

### 1 — PixelLab: new deep water Wang tileset

Generate a sixth terrain: **deep water**.

PixelLab job parameters (`create_topdown_tileset`):
- `lower_description`: `"dark deep cavern pool, murky near-black water, deep underground water"`
- `upper_description`: `"shallow blue water pool, calm cave pond surface"`
- `outline`: `"lineless"`
- `view`: `"high top-down"`
- `detail`: `"highly detailed"`
- `shading`: `"detailed shading"`
- `tile_strength`: `0.4`
- `text_guidance_scale`: `15`
- `tileset_adherence_freedom`: `800`
- `lower_base_tile_id`: (none — new terrain, no chaining required)
- `upper_base_tile_id`: shallow water base tile ID from existing `water_floor_metadata.json` = `"ed7c23a1-73cf-4f47-8568-379459c73981"` — ensures deep↔shallow transition tiles use the same shallow water base tile as floor↔shallow transition tiles

Output saved as:
- `assets/tiles/deep_water_shallow_metadata.json`
- `assets/tiles/deep_water_shallow_image.png`

### 2 — Tileset converter

`tools/pixellab_tileset_converter.gd`, `TERRAIN_REGISTRY`:

```gdscript
const TERRAIN_REGISTRY := {
    "cave wall":   0,
    "cave floor":  1,
    "lava":        2,
    "water":       3,
    "pit":         4,
    "deep water":  5,
}
```

The canonical name for the new terrain must match the `canonical_terrain_name()` function in the converter. The current function maps any description containing `"water"` → `"water"`, which would misclassify the deep-water lower terrain. A new rule must be added **before** the generic water check:

```gdscript
if "deep" in n and ("water" in n or "pool" in n):
    return "deep water"
```

Rebuild command (adds the new pair as a 5th argument):

```powershell
godot --headless -s tools/pixellab_tileset_converter.gd `
    assets/tiles/floor_wall_metadata.json assets/tiles/floor_wall_image.png `
    assets/tiles/water_floor_metadata.json assets/tiles/water_floor_image.png `
    assets/tiles/lava_wall_metadata.json assets/tiles/lava_wall_image.png `
    assets/tiles/pit_wall_metadata.json assets/tiles/pit_wall_image.png `
    assets/tiles/deep_water_shallow_metadata.json assets/tiles/deep_water_shallow_image.png
```

Output: `assets/tiles/combined_terrain.tres` + `assets/tiles/combined_terrain_atlas.png` (6 terrains).

### 3 — `TerrainMap.cs`

Add `DeepWater = 5` constant and update all terrain mappings:

```csharp
private const int Wall      = 0;
private const int Floor     = 1;
private const int Lava      = 2;
private const int Water     = 3;
private const int Pit       = 4;
private const int DeepWater = 5;
```

`FallbackPriority` (priority order for fallback resolution):
```csharp
private static readonly int[] FallbackPriority = { Wall, Lava, DeepWater, Water, Pit, Floor };
```
Deep water beats shallow water in fallback so a mixed-depth corner resolves to deep.

`TileToTerrain`:
```csharp
TileType.ShallowWater => Water,
TileType.DeepWater    => DeepWater,
```

**Animated water layer:**

`TerrainMap` gains a second `TileMapLayer` field:

```csharp
private TileMapLayer _waterLayer = null!;
```

In `Init()`, after creating `_layer`, create `_waterLayer` with the water shader material:

```csharp
var waterMat = new ShaderMaterial
{
    Shader = GD.Load<Shader>("res://assets/tiles/water.gdshader"),
};
_waterLayer = new TileMapLayer
{
    Name     = "WaterAnimLayer",
    TileSet  = ts,
    Position = new Vector2(-half, -half),
    ZIndex   = 1,
    Material = waterMat,
};
AddChild(_waterLayer);
```

`PaintDisplayCell` paints `_waterLayer` after painting `_layer`. Only purely-interior water cells (all four corners the same water terrain) receive an animated tile; all others are erased:

```csharp
private void PaintDisplayCell(int i, int j)
{
    int tl = TerrainAt(i - 1, j - 1);
    int tr = TerrainAt(i,     j - 1);
    int bl = TerrainAt(i - 1, j);
    int br = TerrainAt(i,     j);
    var cell = new Vector2I(i, j);
    _layer.SetCell(cell, _sourceId, Resolve(tl, tr, bl, br));

    if (tl == tr && tr == bl && bl == br
        && (tl == Water || tl == DeepWater)
        && _solid.TryGetValue(tl, out var wc))
        _waterLayer.SetCell(cell, _sourceId, wc);
    else
        _waterLayer.EraseCell(cell);
}
```

`UpdateTiles` already calls `PaintDisplayCell` for each changed cell's four neighbours — no additional change needed there.

### 4 — `WorldRenderer.cs`

Remove the `deepOverlay` block entirely:

```csharp
// DELETE this case:
case TileType.DeepWater:
    DrawRect(r, deepOverlay);
    break;
```

The `deepOverlay` Color declaration can be removed too since it is no longer referenced.

### 5 — Water shader: `assets/tiles/water.gdshader`

```glsl
shader_type canvas_item;

uniform float wave_speed     : hint_range(0.1, 3.0) = 0.8;
uniform float wave_amplitude : hint_range(0.0, 0.02) = 0.003;
uniform float pulse_strength : hint_range(0.0, 0.1)  = 0.04;

void fragment() {
    vec2 uv = UV;
    uv.y += sin(uv.x * 20.0 + TIME * wave_speed)        * wave_amplitude;
    uv.x += sin(uv.y * 18.0 + TIME * wave_speed * 0.7)  * wave_amplitude;
    COLOR = texture(TEXTURE, uv);
    float pulse = 1.0 + pulse_strength * sin(TIME * 1.2 + UV.x * 12.0 + UV.y * 12.0);
    COLOR.rgb *= pulse;
}
```

The shader samples the tile's atlas UV with a gentle sinusoidal offset on each axis, then applies a brightness pulse. `UV` here is the tile's local UV (0,0)–(1,1) within its atlas region, so the wave is relative to each tile independently — no seam artifacts between adjacent animated tiles.

## Scope

- No Core changes (ShallowWater/DeepWater distinction already exists in TileType).
- No test changes needed (TerrainMap is Godot-side only, no Core tests affected).
- No new PixelLab credits for the shader; only one PixelLab job (the deep water tileset).
- The existing converter handles any number of pairs — no structural change to the converter, only the TERRAIN_REGISTRY and one new canonical-name mapping rule if needed.

## Files

| File | Change |
|------|--------|
| `assets/tiles/deep_water_shallow_metadata.json` | New — from PixelLab |
| `assets/tiles/deep_water_shallow_image.png` | New — from PixelLab |
| `assets/tiles/water.gdshader` | New — water animation shader |
| `assets/tiles/combined_terrain.tres` | Rebuilt — 6 terrains |
| `assets/tiles/combined_terrain_atlas.png` | Rebuilt — includes deep water tiles |
| `tools/pixellab_tileset_converter.gd` | Add `"deep water": 5` to TERRAIN_REGISTRY + canonical mapping |
| `game/TerrainMap.cs` | Add `DeepWater = 5`, update TileToTerrain + FallbackPriority, add `_waterLayer` |
| `game/WorldRenderer.cs` | Remove `deepOverlay` DrawRect + Color declaration |
