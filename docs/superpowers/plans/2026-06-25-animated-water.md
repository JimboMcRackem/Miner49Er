# Animated Water Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deep-water Wang terrain with proper shallow↔deep boundary tiles and a gentle lapping wave animation on interior water cells via a canvas-item shader.

**Architecture:** A new PixelLab Wang tileset (deep water ↔ shallow water) adds terrain ID 5 to the combined atlas. A second `TileMapLayer` in `TerrainMap` renders only flat-interior water cells with a `ShaderMaterial` (`water.gdshader`) that applies sinusoidal UV displacement and a brightness pulse. Shore/boundary tiles remain on the static layer, untouched by the shader.

**Tech Stack:** GDScript (tileset converter), C#/Godot 4.6.3 (TerrainMap, WorldRenderer), GLSL canvas-item shader, PixelLab MCP (`create_topdown_tileset` / `get_topdown_tileset`).

## Global Constraints

- `game/` files use TAB indentation; `tools/` GDScript files use TAB indentation.
- Never `git add -A`; never stage `.superpowers/`, `*.png.import`, `*.uid`.
- Run Godot via PowerShell ONLY — never Bash (Bash shim breaks headless assembly loading).
- All 504 existing Core tests must stay green after every task.
- `TERRAIN_REGISTRY` IDs in `pixellab_tileset_converter.gd` MUST match the `private const int` values in `game/TerrainMap.cs`.

---

## File Map

| File | Role |
|------|------|
| `assets/tiles/deep_water_shallow_metadata.json` | New — PixelLab metadata for deep↔shallow Wang tileset |
| `assets/tiles/deep_water_shallow_image.png` | New — PixelLab spritesheet for deep↔shallow tiles |
| `assets/tiles/water.gdshader` | New — canvas-item shader for water animation |
| `assets/tiles/combined_terrain.tres` | Rebuilt — now 6 terrains |
| `assets/tiles/combined_terrain_atlas.png` | Rebuilt — includes deep water tiles |
| `tools/pixellab_tileset_converter.gd` | Add `"deep water": 5` + canonical name rule |
| `game/TerrainMap.cs` | Add `DeepWater = 5`, update terrain mappings, add `_waterLayer` |
| `game/WorldRenderer.cs` | Remove `deepOverlay` DrawRect and Color declaration |

---

### Task 1: PixelLab — generate and download deep water Wang tileset

**Files:**
- Create: `assets/tiles/deep_water_shallow_metadata.json`
- Create: `assets/tiles/deep_water_shallow_image.png`

**Interfaces:**
- Produces: JSON metadata file and PNG spritesheet consumable by the tileset converter in Task 2.

No automated tests. Verify visually: open the downloaded PNG and confirm it shows dark murky water tiles blending into lighter shallow water tiles.

- [ ] **Step 1: Fix corrupted water_floor_metadata.json if needed**

The file `assets/tiles/water_floor_metadata.json` may have a stray `When ` prefix (a known artifact from a previous session) that causes Godot's JSON parser to fail silently. Check and strip it:

Read the first 10 characters of the file:
```powershell
(Get-Content "assets\tiles\water_floor_metadata.json" -Raw).Substring(0, 10)
```

If output starts with `When ` rather than `{`:
```powershell
$raw = Get-Content "assets\tiles\water_floor_metadata.json" -Raw
$fixed = $raw -replace '^When ', ''
Set-Content "assets\tiles\water_floor_metadata.json" $fixed -Encoding utf8 -NoNewline
```

Verify:
```powershell
(Get-Content "assets\tiles\water_floor_metadata.json" -Raw).Substring(0, 1)
```
Expected: `{`

- [ ] **Step 2: Submit PixelLab job**

Use the `mcp__pixellab__create_topdown_tileset` tool with these exact parameters:

```
lower_description:  "dark deep cavern pool, murky near-black water, deep underground water"
upper_description:  "shallow blue water pool, calm cave pond surface"
outline:            "lineless"
view:               "high top-down"
detail:             "highly detailed"
shading:            "detailed shading"
tile_strength:      0.4
text_guidance_scale: 15
tileset_adherence_freedom: 800
upper_base_tile_id: "ed7c23a1-73cf-4f47-8568-379459c73981"
```

The `upper_base_tile_id` is the shallow-water solid tile from `water_floor_metadata.json` — it ensures the shallow-water tiles at the deep↔shallow boundary match the tiles at the water↔floor boundary.

Note the returned `id` (job ID).

- [ ] **Step 3: Poll until complete**

Call `mcp__pixellab__get_topdown_tileset` with the job `id` from Step 2. Repeat every 30–60 seconds until `status == "done"`. The job typically takes 2–5 minutes.

- [ ] **Step 4: Save metadata JSON**

From the completed job result, write the entire JSON response object to:
```
assets/tiles/deep_water_shallow_metadata.json
```

The file must start with `{` (no prefix). The converter reads `metadata.tileset_data.tiles`, `metadata.metadata.terrain_prompts.lower`, and `metadata.metadata.terrain_prompts.upper`.

- [ ] **Step 5: Download spritesheet PNG**

From the completed job result, find `tileset_data.spritesheet_url` and download the PNG to:
```
assets/tiles/deep_water_shallow_image.png
```

Use `WebFetch` or `curl` to download. Verify the file is a valid PNG (non-zero size, starts with PNG magic bytes).

- [ ] **Step 6: Commit**

```
git add assets/tiles/deep_water_shallow_metadata.json assets/tiles/deep_water_shallow_image.png assets/tiles/water_floor_metadata.json
git commit -m "art: deep water Wang tileset (PixelLab) + fix water metadata prefix"
```

---

### Task 2: Tileset converter update + atlas rebuild

**Files:**
- Modify: `tools/pixellab_tileset_converter.gd` (lines ~162–194)
- Rebuild: `assets/tiles/combined_terrain.tres`, `assets/tiles/combined_terrain_atlas.png`

**Interfaces:**
- Consumes: `deep_water_shallow_metadata.json` + `deep_water_shallow_image.png` from Task 1.
- Produces: Updated `combined_terrain.tres` with 6 terrains; terrain index 5 = deep water. Used by Task 3.

No automated tests. Verify by checking the converter's printed output — it should report `5: deep water` in the terrain index list.

- [ ] **Step 1: Add `"deep water"` to `TERRAIN_REGISTRY`**

In `tools/pixellab_tileset_converter.gd`, the current `TERRAIN_REGISTRY` (around line 162):

```gdscript
const TERRAIN_REGISTRY := {
    "cave wall": 0,
    "cave floor": 1,
    "lava": 2,
    "water": 3,
    "pit": 4,
}
```

Change to:

```gdscript
const TERRAIN_REGISTRY := {
    "cave wall":  0,
    "cave floor": 1,
    "lava":       2,
    "water":      3,
    "pit":        4,
    "deep water": 5,
}
```

- [ ] **Step 2: Add deep-water canonical name rule**

In `tools/pixellab_tileset_converter.gd`, the `canonical_terrain_name` function currently reads (around line 182):

```gdscript
func canonical_terrain_name(name: String) -> String:
    var n = name.to_lower()
    if "wall" in n and not ("floor" in n):
        return "cave wall"
    if "lava" in n:
        return "lava"
    if "floor" in n:
        return "cave floor"
    if "water" in n or "pond" in n:
        return "water"
    if "pit" in n or "abyss" in n or "void" in n:
        return "pit"
    return name
```

Change to (add the deep-water check **before** the generic water check):

```gdscript
func canonical_terrain_name(name: String) -> String:
    var n = name.to_lower()
    if "wall" in n and not ("floor" in n):
        return "cave wall"
    if "lava" in n:
        return "lava"
    if "floor" in n:
        return "cave floor"
    if "deep" in n and ("water" in n or "pool" in n):
        return "deep water"
    if "water" in n or "pond" in n:
        return "water"
    if "pit" in n or "abyss" in n or "void" in n:
        return "pit"
    return name
```

- [ ] **Step 3: Run the tileset converter (PowerShell)**

```powershell
godot --headless -s tools/pixellab_tileset_converter.gd `
    assets/tiles/floor_wall_metadata.json assets/tiles/floor_wall_image.png `
    assets/tiles/water_floor_metadata.json assets/tiles/water_floor_image.png `
    assets/tiles/lava_wall_metadata.json assets/tiles/lava_wall_image.png `
    assets/tiles/pit_wall_metadata.json assets/tiles/pit_wall_image.png `
    assets/tiles/deep_water_shallow_metadata.json assets/tiles/deep_water_shallow_image.png
```

Expected output includes:
```
Terrain indices:
   0: cave wall
   1: cave floor
   2: lava
   3: water
   4: pit
   5: deep water
✅ Created: assets/tiles/combined_terrain.tres
```

If the water pair prints `❌ Invalid JSON`, go back and fix `water_floor_metadata.json` (Task 1 Step 1).

- [ ] **Step 4: Verify atlas rebuilt**

```powershell
(Get-Item assets\tiles\combined_terrain_atlas.png).LastWriteTime
```

Timestamp should be within the last few minutes.

- [ ] **Step 5: Commit**

```
git add tools/pixellab_tileset_converter.gd assets/tiles/combined_terrain.tres assets/tiles/combined_terrain_atlas.png
git commit -m "feat(tileset): add deep water terrain 5, rebuild combined atlas (6 terrains)"
```

---

### Task 3: Water shader + TerrainMap + WorldRenderer

**Files:**
- Create: `assets/tiles/water.gdshader`
- Modify: `game/TerrainMap.cs`
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: `combined_terrain.tres` with terrain ID 5 = deep water (Task 2).
- Consumes: `assets/tiles/water.gdshader` (created in this task, loaded by TerrainMap).

No automated tests (Godot-side only, no Core changes). Verify by building the project and launching a match with water on the map — shallow water should animate gently; deep water should animate with darker tiles; shore tiles should be crisp and static.

- [ ] **Step 1: Write the water shader**

Create `assets/tiles/water.gdshader`:

```glsl
shader_type canvas_item;

uniform float wave_speed     : hint_range(0.1, 3.0) = 0.8;
uniform float wave_amplitude : hint_range(0.0, 0.02) = 0.003;
uniform float pulse_strength : hint_range(0.0, 0.1)  = 0.04;

void fragment() {
	vec2 uv = UV;
	uv.y += sin(uv.x * 20.0 + TIME * wave_speed)         * wave_amplitude;
	uv.x += sin(uv.y * 18.0 + TIME * wave_speed * 0.7)   * wave_amplitude;
	COLOR = texture(TEXTURE, uv);
	float pulse = 1.0 + pulse_strength * sin(TIME * 1.2 + UV.x * 12.0 + UV.y * 12.0);
	COLOR.rgb *= pulse;
}
```

Note: `UV` is the tile-local 0–1 UV within the tile's atlas region (Godot canvas-item shader behaviour). `TEXTURE` is the atlas texture. Using the unperturbed `UV` for the pulse `sin()` keeps the brightness wave independent of the position distortion.

- [ ] **Step 2: Add `DeepWater = 5` and update constants in `TerrainMap.cs`**

In `game/TerrainMap.cs`, the constants block (lines 22–30) currently reads:

```csharp
	// Terrain ids — MUST match the converter's TERRAIN_REGISTRY.
	private const int Wall  = 0;
	private const int Floor = 1;
	private const int Lava  = 2;
	private const int Water = 3;
	private const int Pit   = 4;

	private readonly Dictionary<(int, int, int, int), Vector2I> _lookup = new();
	private readonly Dictionary<int, Vector2I> _solid = new();
	private static readonly int[] FallbackPriority = { Wall, Lava, Water, Pit, Floor };
```

Change to:

```csharp
	// Terrain ids — MUST match the converter's TERRAIN_REGISTRY.
	private const int Wall      = 0;
	private const int Floor     = 1;
	private const int Lava      = 2;
	private const int Water     = 3;
	private const int Pit       = 4;
	private const int DeepWater = 5;

	private readonly Dictionary<(int, int, int, int), Vector2I> _lookup = new();
	private readonly Dictionary<int, Vector2I> _solid = new();
	private static readonly int[] FallbackPriority = { Wall, Lava, DeepWater, Water, Pit, Floor };
```

`DeepWater` is placed before `Water` in `FallbackPriority` so that a corner shared between deep and shallow resolves to deep (darker wins).

- [ ] **Step 3: Add `_waterLayer` field to `TerrainMap.cs`**

In `game/TerrainMap.cs`, the existing field `_layer` is declared at line 16:

```csharp
	private TileMapLayer _layer = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;
```

Add `_waterLayer` after `_layer`:

```csharp
	private TileMapLayer _layer = null!;
	private TileMapLayer _waterLayer = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;
```

- [ ] **Step 4: Create `_waterLayer` in `Init()`**

In `game/TerrainMap.cs`, the current `Init()` body (lines 32–45):

```csharp
	public void Init(MatchClient client)
	{
		_client = client;
		var ts = GD.Load<TileSet>("res://assets/tiles/combined_terrain.tres");
		if (ts == null) return;
		_sourceId = ts.GetSourceId(0);
		BuildLookup(ts);

		float half = MatchClient.TileSize / 2f;
		_layer = new TileMapLayer { Name = "TileLayer", TileSet = ts, Position = new Vector2(-half, -half) };
		AddChild(_layer);
		_ready = true;
		PaintFullGrid();
	}
```

Change to:

```csharp
	public void Init(MatchClient client)
	{
		_client = client;
		var ts = GD.Load<TileSet>("res://assets/tiles/combined_terrain.tres");
		if (ts == null) return;
		_sourceId = ts.GetSourceId(0);
		BuildLookup(ts);

		float half = MatchClient.TileSize / 2f;
		_layer = new TileMapLayer { Name = "TileLayer", TileSet = ts, Position = new Vector2(-half, -half) };
		AddChild(_layer);

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

		_ready = true;
		PaintFullGrid();
	}
```

- [ ] **Step 5: Update `TileToTerrain` for `DeepWater`**

In `game/TerrainMap.cs`, `TileToTerrain` (line 130–138) currently reads:

```csharp
	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock => Wall,
		TileType.Floor or TileType.Cracked or TileType.Crumbling or TileType.Plank => Floor,
		TileType.Lava => Lava,
		TileType.ShallowWater or TileType.DeepWater => Water,
		TileType.Pit => Pit,
		_ => Wall, // LavaVent — wall underneath; WorldRenderer overlays the vent glow
	};
```

Change the water line:

```csharp
	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock => Wall,
		TileType.Floor or TileType.Cracked or TileType.Crumbling or TileType.Plank => Floor,
		TileType.Lava => Lava,
		TileType.ShallowWater => Water,
		TileType.DeepWater    => DeepWater,
		TileType.Pit => Pit,
		_ => Wall, // LavaVent — wall underneath; WorldRenderer overlays the vent glow
	};
```

- [ ] **Step 6: Update `PaintDisplayCell` to drive `_waterLayer`**

In `game/TerrainMap.cs`, `PaintDisplayCell` (lines 87–94) currently reads:

```csharp
	private void PaintDisplayCell(int i, int j)
	{
		int tl = TerrainAt(i - 1, j - 1);
		int tr = TerrainAt(i,     j - 1);
		int bl = TerrainAt(i - 1, j);
		int br = TerrainAt(i,     j);
		_layer.SetCell(new Vector2I(i, j), _sourceId, Resolve(tl, tr, bl, br));
	}
```

Change to:

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

Only cells where all four dual-grid corners are the same water terrain get an animated tile. Shore cells (mixed corners) are cleared from `_waterLayer` — their static boundary tile from `_layer` shows through.

- [ ] **Step 7: Remove `deepOverlay` from `WorldRenderer.cs`**

In `game/WorldRenderer.cs`, in the `_Draw()` method around line 215–234, remove both the Color declaration and the `DeepWater` case:

Current code:

```csharp
		// Single-pass tile overlays on top of TerrainMap (FogRenderer at ZIndex -5 covers these naturally).
		var deepOverlay = new Color(0.0f, 0.05f, 0.35f, 0.55f);
		foreach (var p in grid.Positions())
		{
			var r = new Rect2(p.X * ts, p.Y * ts, ts, ts);
			switch (grid.Get(p))
			{
				case TileType.GoldRock:
					if (_goldRockTex != null) DrawTextureRect(_goldRockTex, r, false);
					break;
				case TileType.Plank:
					if (_plankTex != null) DrawTextureRect(_plankTex, r, false);
					break;
				case TileType.LavaVent:
					if (_lavaVentTex != null) DrawTextureRect(_lavaVentTex, r, false);
					else DrawRect(r, LavaVentColor);
					break;
				case TileType.DeepWater:
					DrawRect(r, deepOverlay);
					break;
				case TileType.Cracked:
```

Change to (remove the `deepOverlay` declaration and the `DeepWater` case entirely):

```csharp
		// Single-pass tile overlays on top of TerrainMap (FogRenderer at ZIndex -5 covers these naturally).
		foreach (var p in grid.Positions())
		{
			var r = new Rect2(p.X * ts, p.Y * ts, ts, ts);
			switch (grid.Get(p))
			{
				case TileType.GoldRock:
					if (_goldRockTex != null) DrawTextureRect(_goldRockTex, r, false);
					break;
				case TileType.Plank:
					if (_plankTex != null) DrawTextureRect(_plankTex, r, false);
					break;
				case TileType.LavaVent:
					if (_lavaVentTex != null) DrawTextureRect(_lavaVentTex, r, false);
					else DrawRect(r, LavaVentColor);
					break;
				case TileType.Cracked:
```

- [ ] **Step 8: Verify Core tests still pass**

```
dotnet test src/Miner49er.Core.Tests -q
```

Expected: `Passed: 504, Failed: 0`

- [ ] **Step 9: Build Godot project (PowerShell)**

```powershell
godot --headless --build-solutions --quit-after 2
```

Expected: `[ DONE ] dotnet_build_project` with no `CS` errors.

- [ ] **Step 10: Commit**

```
git add assets/tiles/water.gdshader game/TerrainMap.cs game/WorldRenderer.cs
git commit -m "feat: animated water layer (shader) + DeepWater terrain 5 in TerrainMap"
```

---

## Self-Review

**Spec coverage:**
- ✅ PixelLab deep water tileset — Task 1
- ✅ `upper_base_tile_id` set for shallow-water continuity — Task 1 Step 2
- ✅ `water_floor_metadata.json` prefix fix — Task 1 Step 1
- ✅ `canonical_terrain_name` deep-water rule before generic water — Task 2 Step 2
- ✅ `TERRAIN_REGISTRY` gains `"deep water": 5` — Task 2 Step 1
- ✅ Converter rebuild with 5 pairs — Task 2 Step 3
- ✅ `water.gdshader` with UV displacement + brightness pulse — Task 3 Step 1
- ✅ `DeepWater = 5` constant — Task 3 Step 2
- ✅ `FallbackPriority` includes DeepWater before Water — Task 3 Step 2
- ✅ `TileToTerrain` maps `TileType.DeepWater → DeepWater` — Task 3 Step 5
- ✅ `_waterLayer` field + Init creation with ShaderMaterial — Task 3 Steps 3–4
- ✅ `PaintDisplayCell` paints/erases `_waterLayer` for pure-water interior cells — Task 3 Step 6
- ✅ `deepOverlay` DrawRect removed from WorldRenderer — Task 3 Step 7

**Placeholder scan:** None.

**Type consistency:** `DeepWater = 5` defined in Step 2, referenced in Steps 5 and 6 of the same task. `_waterLayer` declared in Step 3, created in Step 4, used in Step 6 — all within Task 3. Converter registry key `"deep water"` defined in Step 1, mapped by `canonical_terrain_name` in Step 2 — same task.
