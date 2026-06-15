# Phase 6a — Dual-Grid Terrain Renderer + Water/Floor Art Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Godot's seam-prone terrain-solver autotiling with a deterministic dual-grid corner renderer, then add a distinct water terrain and a calmer floor via PixelLab.

**Architecture:** A display `TileMapLayer` is offset by half a cell; each display cell sits over a world vertex and reads the 4 surrounding cells as its corner terrains, looked up against a corner-bit table built from the tileset. No Godot terrain solver. Terrain ids come from a fixed registry in the converter so the tileset and renderer never drift.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C# (`game/`, TAB-indented); GDScript tools; PixelLab MCP for art. `Miner49er.Core` is untouched (312 tests stay green).

**Branch:** `phase6a-dualgrid-terrain` (already created; foundational "terrain renders" fix already committed at `334dccb`).

**Note on testing:** Terrain rendering is Godot-typed and not C#-unit-testable. Per the spec, the safety net is: Core tests untouched + a headless GDScript coverage probe (run via PowerShell) + headless boot + manual visual review. The probe is the closest thing to a unit test and is written before the renderer (Task A1's probe asserts the *tileset + algorithm* are sound independent of the C# wiring).

**Sequencing note (refines spec §7):** the converter's fixed-id registry moves into Phase A because the renderer's terrain-id constants depend on it. Phase B is purely art + a `TileToTerrain` flip.

---

## File Structure

| File | Responsibility | Change |
|---|---|---|
| `tools/pixellab_tileset_converter.gd` | PixelLab pairs → `combined_terrain.tres` | Fixed terrain-id registry; always define all 5 terrains |
| `assets/tiles/combined_terrain.tres` | The tileset (atlas + corner bits) | Regenerated |
| `tools/terrain_probe.gd` | Headless coverage/correctness probe | Created (permanent verification tool) |
| `game/TerrainMap.cs` | Dual-grid terrain renderer | Full rewrite |
| `assets/tiles/water_floor_*`, `water_wall_*`, `floor_wall_*` | PixelLab art | Generated (Phase B) |

`game/net/MatchClient.cs` is **unchanged**: it already calls `_terrainMap.Init(this)` and `_terrainMap.UpdateTiles(update.TileChanges)`, and the rewrite preserves both signatures.

---

## Phase A — Dual-Grid Renderer (ships against current art)

### Task A1: Converter fixed-id registry + regenerate tileset

**Files:**
- Modify: `tools/pixellab_tileset_converter.gd`
- Regenerate: `assets/tiles/combined_terrain.tres`

- [ ] **Step 1: Replace `get_terrain_id` and `canonical_terrain_name` with a fixed registry**

In `tools/pixellab_tileset_converter.gd`, replace the existing `get_terrain_id` function and the `canonical_terrain_name` function with:

```gdscript
# Canonical terrain ids — MUST match the constants in game/TerrainMap.cs.
const TERRAIN_REGISTRY := {
	"cave wall": 0,
	"cave floor": 1,
	"lava": 2,
	"water": 3,
	"pit": 4,
}

func get_terrain_id(name: String) -> int:
	var cname = canonical_terrain_name(name)
	var id = TERRAIN_REGISTRY.get(cname, -1)
	if id == -1:
		push_warning("Unknown terrain name '%s' (canonical '%s'); appending" % [name, cname])
		id = TERRAIN_REGISTRY.size() + terrains.size()
	terrains[id] = cname
	return id

# Map PixelLab's verbose prompt names onto canonical registry keys. Order matters:
# "river, lava pit, lava flow" must read as lava (not water/pit), so check lava early.
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

- [ ] **Step 2: Always define all five terrains in the resource**

The renderer reads each tile's corner peering bits, and Godot resets a peering bit to `-1` on load if its terrain id isn't defined in the set. So every registry terrain must be defined even when it has no tiles yet (e.g. water before its art exists).

In `create_tileset()`, replace the terrain-definition loop (currently `for id in terrains: ... terrain_set_0/terrain_%d/...`) with:

```gdscript
	var default_colors := {
		0: Color(0.149020, 0.117647, 0.262745),  # wall
		1: Color(0.168627, 0.168627, 0.200000),  # floor
		2: Color(0.823529, 0.321569, 0.101961),  # lava
		3: Color(0.180392, 0.435294, 0.560784),  # water
		4: Color(0.035294, 0.047059, 0.113725),  # pit
	}
	var names := {0: "cave wall", 1: "cave floor", 2: "lava", 3: "water", 4: "pit"}
	for id in [0, 1, 2, 3, 4]:
		var color = terrain_colors.get(id, default_colors[id])
		terrain_defs.append('terrain_set_0/terrain_%d/name = "%s"' % [id, names[id]])
		terrain_defs.append('terrain_set_0/terrain_%d/color = Color(%f, %f, %f, 1)' % [id, color.r, color.g, color.b])
```

- [ ] **Step 3: Regenerate the tileset from the three existing pairs**

Run (PowerShell only — never the Bash tool for `godot`):

```
& godot --headless --path . -s tools/pixellab_tileset_converter.gd assets/tiles/lava_wall_metadata.json assets/tiles/lava_wall_image.png assets/tiles/floor_wall_metadata.json assets/tiles/floor_wall_image.png assets/tiles/pit_wall_metadata.json assets/tiles/pit_wall_image.png
```

Expected tail output:

```
Terrain indices:
   0: cave wall
   1: cave floor
   2: lava
   4: pit
```

(Water id 3 is defined but has no tiles yet — correct.)

- [ ] **Step 4: Commit**

```
git add tools/pixellab_tileset_converter.gd assets/tiles/combined_terrain.tres
git commit -m "feat(tools): fixed terrain-id registry in tileset converter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A2: Coverage probe (written before the renderer)

**Files:**
- Create: `tools/terrain_probe.gd`

This probe mirrors the renderer's lookup algorithm in GDScript and asserts it against the real tileset. A wall+floor map must resolve **every** display cell via the exact table (zero fallback) — proving the dual-grid algorithm and the authored corner set are complete for a 2-terrain boundary.

- [ ] **Step 1: Write the probe**

Create `tools/terrain_probe.gd`:

```gdscript
extends SceneTree

# Terrain ids — mirror game/TerrainMap.cs.
const WALL := 0
const FLOOR := 1

var lookup := {}      # "tl,tr,bl,br" -> atlas Vector2i
var solid := {}       # terrain -> atlas Vector2i

func build_lookup(ts: TileSet) -> void:
	var src = ts.get_source(0)
	for i in range(src.get_tiles_count()):
		var coord = src.get_tile_id(i)
		var td: TileData = src.get_tile_data(coord, 0)
		var tl = td.get_terrain_peering_bit(TileSet.CELL_NEIGHBOR_TOP_LEFT_CORNER)
		var tr = td.get_terrain_peering_bit(TileSet.CELL_NEIGHBOR_TOP_RIGHT_CORNER)
		var bl = td.get_terrain_peering_bit(TileSet.CELL_NEIGHBOR_BOTTOM_LEFT_CORNER)
		var br = td.get_terrain_peering_bit(TileSet.CELL_NEIGHBOR_BOTTOM_RIGHT_CORNER)
		if tl < 0:
			continue
		var key = "%d,%d,%d,%d" % [tl, tr, bl, br]
		if not lookup.has(key):
			lookup[key] = coord
		if tl == tr and tr == bl and bl == br and not solid.has(tl):
			solid[tl] = coord

# Returns [coord, is_fallback].
func resolve(tl, tr, bl, br):
	var key = "%d,%d,%d,%d" % [tl, tr, bl, br]
	if lookup.has(key):
		return [lookup[key], false]
	var counts := {}
	for v in [tl, tr, bl, br]:
		counts[v] = counts.get(v, 0) + 1
	var best = tl
	var best_n = 0
	for k in counts:
		if counts[k] > best_n:
			best_n = counts[k]
			best = k
	return [solid.get(best, solid.get(WALL)), true]

func terrain_at(grid, x, y) -> int:
	if y < 0 or x < 0 or y >= grid.size() or x >= grid[0].size():
		return WALL
	return grid[y][x]

func paint_and_count(grid) -> Array:  # [fallbacks, blanks]
	var h = grid.size()
	var w = grid[0].size()
	var fb := 0
	var blanks := 0
	for j in range(h + 1):
		for i in range(w + 1):
			var r = resolve(
				terrain_at(grid, i - 1, j - 1),
				terrain_at(grid, i, j - 1),
				terrain_at(grid, i - 1, j),
				terrain_at(grid, i, j))
			if r[1]:
				fb += 1
			if r[0] == null:
				blanks += 1
	return [fb, blanks]

func _init():
	var ts = load("res://assets/tiles/combined_terrain.tres")
	build_lookup(ts)
	print("lookup combos=", lookup.size(), " solids=", solid.keys())

	# Map 1: wall background, floor room. Only the floor<->wall authored pair.
	var g1 := []
	for y in range(16):
		var row := []
		for x in range(16):
			row.append(FLOOR if (x >= 4 and x <= 11 and y >= 4 and y <= 11) else WALL)
		g1.append(row)
	var r1 = paint_and_count(g1)
	print("wall/floor map: fallbacks=", r1[0], " blanks=", r1[1])

	var ok = (r1[0] == 0 and r1[1] == 0)
	print("RESULT: ", "PASS" if ok else "FAIL")
	quit(0 if ok else 1)
```

- [ ] **Step 2: Run the probe and verify it passes**

Run (PowerShell):

```
& godot --headless --path . -s tools/terrain_probe.gd; "EXIT=$LASTEXITCODE"
```

Expected: `wall/floor map: fallbacks=0 blanks=0` and `RESULT: PASS`, `EXIT=0`.

If `fallbacks > 0`, the floor↔wall pair is missing a corner combo — stop and inspect the tileset before writing the renderer.

- [ ] **Step 3: Commit**

```
git add tools/terrain_probe.gd
git commit -m "test(tools): dual-grid terrain coverage probe

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task A3: Rewrite `TerrainMap` to dual-grid

**Files:**
- Modify (full rewrite): `game/TerrainMap.cs`

- [ ] **Step 1: Replace the file contents**

Replace all of `game/TerrainMap.cs` with (TAB-indented, matching `game/` convention):

```csharp
using System;
using Godot;
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;

namespace Miner49er;

/// <summary>Dual-grid terrain renderer. A display TileMapLayer is offset by half a
/// cell; each display cell sits over a world vertex and reads the four cells around it
/// as its corner terrains, looked up against the tileset's corner-bit table. No Godot
/// terrain solver — authored boundaries resolve to exact edge tiles, and rare
/// unauthored junctions fall back to the majority terrain's solid tile.</summary>
public partial class TerrainMap : Node2D
{
	private TileMapLayer _layer = null!;
	private MatchClient _client = null!;
	private bool _ready;
	private int _sourceId;

	// Terrain ids — MUST match the converter's TERRAIN_REGISTRY.
	private const int Wall  = 0;
	private const int Floor = 1;
	private const int Lava  = 2;
	private const int Water = 3;
	private const int Pit   = 4;

	private readonly Dictionary<(int, int, int, int), Vector2I> _lookup = new();
	private readonly Dictionary<int, Vector2I> _solid = new();
	private static readonly int[] FallbackPriority = { Wall, Lava, Water, Pit, Floor };

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

	// Corner-signature -> atlas coord, plus one solid tile per terrain for fallback.
	private void BuildLookup(TileSet ts)
	{
		if (ts.GetSource(_sourceId) is not TileSetAtlasSource src) return;
		for (int i = 0; i < src.GetTilesCount(); i++)
		{
			var coord = src.GetTileId(i);
			var td = src.GetTileData(coord, 0);
			int tl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopLeftCorner);
			int tr = td.GetTerrainPeeringBit(TileSet.CellNeighbor.TopRightCorner);
			int bl = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomLeftCorner);
			int br = td.GetTerrainPeeringBit(TileSet.CellNeighbor.BottomRightCorner);
			if (tl < 0) continue;
			_lookup.TryAdd((tl, tr, bl, br), coord);
			if (tl == tr && tr == bl && bl == br)
				_solid.TryAdd(tl, coord);
		}
	}

	private void PaintFullGrid()
	{
		var grid = _client.Grid;
		for (int j = 0; j <= grid.Height; j++)
			for (int i = 0; i <= grid.Width; i++)
				PaintDisplayCell(i, j);
	}

	// Each changed world cell touches the four display cells around its vertices.
	public void UpdateTiles(IReadOnlyList<TileChange> changes)
	{
		if (!_ready) return;
		foreach (var t in changes)
		{
			PaintDisplayCell(t.X, t.Y);
			PaintDisplayCell(t.X + 1, t.Y);
			PaintDisplayCell(t.X, t.Y + 1);
			PaintDisplayCell(t.X + 1, t.Y + 1);
		}
	}

	private void PaintDisplayCell(int i, int j)
	{
		int tl = TerrainAt(i - 1, j - 1);
		int tr = TerrainAt(i,     j - 1);
		int bl = TerrainAt(i - 1, j);
		int br = TerrainAt(i,     j);
		_layer.SetCell(new Vector2I(i, j), _sourceId, Resolve(tl, tr, bl, br));
	}

	private Vector2I Resolve(int tl, int tr, int bl, int br)
	{
		if (_lookup.TryGetValue((tl, tr, bl, br), out var c)) return c;
		int m = Majority(tl, tr, bl, br);
		if (_solid.TryGetValue(m, out var s)) return s;
		return _solid.TryGetValue(Wall, out var w) ? w : new Vector2I(0, 0);
	}

	private static int Majority(int a, int b, int c, int d)
	{
		Span<int> v = stackalloc int[] { a, b, c, d };
		int best = a, bestN = 0;
		foreach (int cand in v)
		{
			int n = 0;
			foreach (int x in v) if (x == cand) n++;
			if (n > bestN || (n == bestN && Pri(cand) < Pri(best))) { best = cand; bestN = n; }
		}
		return best;
	}

	private static int Pri(int terrain)
	{
		for (int k = 0; k < FallbackPriority.Length; k++)
			if (FallbackPriority[k] == terrain) return k;
		return int.MaxValue;
	}

	private int TerrainAt(int x, int y)
	{
		var p = new GridPos(x, y);
		return _client.Grid.InBounds(p) ? TileToTerrain(_client.Grid.Get(p)) : Wall;
	}

	// PHASE A: water renders as lava until its art lands (see plan Task B2, which flips
	// ShallowWater/DeepWater to Water).
	private static int TileToTerrain(TileType t) => t switch
	{
		TileType.Rock or TileType.GoldRock or TileType.ImpermeableRock => Wall,
		TileType.Floor or TileType.Cracked or TileType.Crumbling or TileType.Plank => Floor,
		TileType.Lava or TileType.ShallowWater or TileType.DeepWater => Lava,
		TileType.Pit => Pit,
		_ => Wall, // LavaVent — wall underneath; WorldRenderer overlays the vent glow
	};
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Run the coverage probe again (guards the data the renderer relies on)**

Run (PowerShell): `& godot --headless --path . -s tools/terrain_probe.gd; "EXIT=$LASTEXITCODE"`
Expected: `RESULT: PASS`, `EXIT=0`.

- [ ] **Step 4: Headless boot smoke test**

Run (PowerShell): `$o = & godot --headless --quit-after 90 2>&1; "EXIT=$LASTEXITCODE"; $o | Select-Object -Last 5`
Expected: `EXIT=0`, no terrain/TileSet load errors in the output.

- [ ] **Step 5: Run Core tests (must stay green)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 312`.

- [ ] **Step 6: Commit**

```
git add game/TerrainMap.cs
git commit -m "feat(game): dual-grid terrain renderer replaces terrain solver

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 7: Manual visual checkpoint (human)**

Launch the game and start a match. Confirm: terrain boundaries (floor/wall/lava/pit) render with crisp corner pieces and **no seams**; mining/flooding/blasts update terrain immediately; fog reads correctly. Water still appears as lava (expected until Phase B). This is the gate before Phase B spends PixelLab credits.

---

## Phase B — Water + calmer floor art

### Task B1: Generate PixelLab art

**Files (generated):**
- `assets/tiles/floor_wall_metadata.json` + `floor_wall_image.png` (regenerated, calmer)
- `assets/tiles/water_floor_metadata.json` + `water_floor_image.png` (new)
- `assets/tiles/water_wall_metadata.json` + `water_wall_image.png` (new)

- [ ] **Step 1: Generate the three top-down tileset pairs via the PixelLab MCP**

Use the PixelLab MCP `create_topdown_tileset` tool (the tool that produced the existing pairs). Generate, in the project's PixelLab project, three transition tilesets with these lower/upper prompts (keep wording so the converter's `canonical_terrain_name` classifies them correctly — water prompts must avoid the words "lava", "wall", "floor", "pit"):

| Pair | lower prompt | upper prompt |
|---|---|---|
| floor_wall (regen) | `plain smooth cave floor, minimal detail` | `cave wall` |
| water_floor | `shallow water pool` | `cave floor` |
| water_wall | `shallow water, river` | `cave wall` |

These are async jobs — poll each to completion, then download each tileset's metadata JSON and sprite-sheet PNG into `assets/tiles/` using the `<name>_metadata.json` / `<name>_image.png` naming the converter expects (`floor_wall_*`, `water_floor_*`, `water_wall_*`).

- [ ] **Step 2: Human visual review of generated art**

Read each `*_image.png` and present them. The user approves or requests regeneration (adjust prompts and repeat Step 1) before proceeding. Do not continue until approved.

- [ ] **Step 3: Commit the approved art**

```
git add assets/tiles/floor_wall_metadata.json assets/tiles/floor_wall_image.png assets/tiles/water_floor_metadata.json assets/tiles/water_floor_image.png assets/tiles/water_wall_metadata.json assets/tiles/water_wall_image.png
git commit -m "feat(art): calmer floor + water tileset pairs (PixelLab)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

### Task B2: Regenerate tileset with water and switch water terrain on

**Files:**
- Regenerate: `assets/tiles/combined_terrain.tres`
- Modify: `game/TerrainMap.cs` (the `TileToTerrain` water mapping)

- [ ] **Step 1: Regenerate the tileset over all five pairs**

Run (PowerShell):

```
& godot --headless --path . -s tools/pixellab_tileset_converter.gd assets/tiles/lava_wall_metadata.json assets/tiles/lava_wall_image.png assets/tiles/floor_wall_metadata.json assets/tiles/floor_wall_image.png assets/tiles/pit_wall_metadata.json assets/tiles/pit_wall_image.png assets/tiles/water_floor_metadata.json assets/tiles/water_floor_image.png assets/tiles/water_wall_metadata.json assets/tiles/water_wall_image.png
```

Expected `Terrain indices:` to now include `3: water`.

- [ ] **Step 2: Point water tiles at the water terrain**

In `game/TerrainMap.cs`, change the `TileToTerrain` water line from the Phase-A temporary mapping to the real one:

```csharp
		TileType.Lava => Lava,
		TileType.ShallowWater or TileType.DeepWater => Water,
```

(Remove `ShallowWater`/`DeepWater` from the `Lava` case; lava maps to `Lava` alone.)

- [ ] **Step 3: Build**

Run: `dotnet build Miner49er.sln`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Probe + boot + Core tests**

Run (PowerShell): `& godot --headless --path . -s tools/terrain_probe.gd; "EXIT=$LASTEXITCODE"` → `RESULT: PASS`.
Run (PowerShell): `$o = & godot --headless --quit-after 90 2>&1; "EXIT=$LASTEXITCODE"` → `EXIT=0`.
Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` → `Passed! ... 312`.

- [ ] **Step 5: Commit**

```
git add assets/tiles/combined_terrain.tres game/TerrainMap.cs
git commit -m "feat(game): distinct water terrain with PixelLab water tiles

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 6: Manual visual checkpoint (human)**

Launch a match; flood a tile. Confirm ponds/rivers read as water (not lava), the calmer floor looks right, and edges remain seam-free.

---

## Completion

After all tasks: use **superpowers:finishing-a-development-branch** to verify tests and present merge/PR options. Do not merge without explicit user authorization and a passed manual play-test.
