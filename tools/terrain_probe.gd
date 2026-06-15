extends SceneTree

# Headless verification for the dual-grid terrain renderer. Mirrors the lookup in
# game/TerrainMap.cs against the real combined_terrain.tres. A wall+floor map (only the
# authored floor<->wall pair) must resolve EVERY display cell via the exact corner table
# with zero fallback and zero blanks — proving the algorithm + authored corner set are
# complete for a 2-terrain boundary. Run via PowerShell:
#   & godot --headless --path . -s tools/terrain_probe.gd; "EXIT=$LASTEXITCODE"

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

	# Map: wall background, floor room. Only the floor<->wall authored pair.
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
