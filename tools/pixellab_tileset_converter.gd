extends SceneTree

# PixelLab to Godot Tileset Converter (Split Format)
# Converts PixelLab metadata JSON + PNG sprite sheets to Godot terrain system
# Usage: godot --headless -s tools/pixellab_tileset_converter.gd metadata1.json image1.png metadata2.json image2.png ...

var output_path = "assets/tiles/combined_terrain.tres"
var tile_size = 0
var terrains = {}
var tiles = []

var corner_layout = [
	# Row 0
	"ss/sw", "ss/ww", "ss/ws", "ww/ws", "ww/sw",
	# Row 1
	"sw/sw", "ww/ww", "ws/ws", "ws/ww", "sw/ww",
	# Row 2
	"sw/ss", "ww/ss", "ws/ss", "ws/sw", "sw/ws",
	# Row 3
	"ww/ww", "ss/ss", "", "", ""
]

func _init():
	print("\n🎨 PixelLab to Godot Converter (Split Format)")
	print("==========================================")

	var tileset_pairs = []
	var args = OS.get_cmdline_args()

	for i in range(args.size()):
		if args[i].ends_with("_metadata.json"):
			var json_path = args[i]
			var expected_png = json_path.replace("_metadata.json", "_image.png")

			var png_path = ""
			for j in range(args.size()):
				if args[j] == expected_png:
					png_path = args[j]
					break

			if png_path != "":
				tileset_pairs.append({"json": json_path, "png": png_path})
			else:
				print("⚠️  PNG not found for %s (expected: %s)" % [json_path, expected_png])

	if tileset_pairs.is_empty():
		print("❌ No valid JSON/PNG pairs found!")
		print("Usage: godot --headless -s tools/pixellab_tileset_converter.gd metadata1.json image1.png ...")
		quit()
		return

	print("📦 Found %d tileset pairs:" % tileset_pairs.size())
	for pair in tileset_pairs:
		print("   %s + %s" % [pair.json, pair.png])

	for pair in tileset_pairs:
		load_tileset_pair(pair.json, pair.png)

	if tiles.is_empty():
		print("❌ No tiles loaded")
		quit()
		return

	create_tileset()
	print("\n✅ Created: %s" % output_path)
	print("   Terrains: %s" % ", ".join(terrains.values()))
	print("\nTerrain indices:")
	for id in terrains:
		print("   %d: %s" % [id, terrains[id]])
	quit()

func load_tileset_pair(json_path: String, png_path: String):
	print("\n📁 Loading %s + %s..." % [json_path, png_path])

	if not FileAccess.file_exists(json_path):
		print("  ❌ Metadata file not found")
		return

	var file = FileAccess.open(json_path, FileAccess.READ)
	var json = JSON.new()
	if json.parse(file.get_as_text()) != OK:
		print("  ❌ Invalid JSON")
		return
	file.close()

	var metadata = json.data

	if not FileAccess.file_exists(png_path):
		print("  ❌ PNG file not found")
		return

	var png_file = FileAccess.open(png_path, FileAccess.READ)
	if png_file == null:
		print("  ❌ Cannot open PNG file")
		return
	var png_bytes = png_file.get_buffer(png_file.get_length())
	png_file.close()
	var sprite_sheet = Image.new()
	if sprite_sheet.load_png_from_buffer(png_bytes) != OK:
		print("  ❌ Failed to decode PNG")
		return

	if tile_size == 0:
		var size = metadata.tileset_data.tile_size
		tile_size = size.width
		print("  Tile size: %dx%d" % [tile_size, tile_size])

	var lower_name = metadata.metadata.terrain_prompts.lower
	var upper_name = metadata.metadata.terrain_prompts.upper

	var lower_id = get_terrain_id(lower_name)
	var upper_id = get_terrain_id(upper_name)

	var wang_tiles = {}
	for tile in metadata.tileset_data.tiles:
		var corners = tile.corners
		var bbox = tile.bounding_box

		var tile_image = Image.create(bbox.width, bbox.height, false, Image.FORMAT_RGBA8)
		tile_image.blit_rect(sprite_sheet, Rect2i(bbox.x, bbox.y, bbox.width, bbox.height), Vector2i.ZERO)

		var nw = 1 if corners.NW == "upper" else 0
		var ne = 1 if corners.NE == "upper" else 0
		var sw = 1 if corners.SW == "upper" else 0
		var se = 1 if corners.SE == "upper" else 0
		var wang_idx = nw * 8 + ne * 4 + sw * 2 + se

		wang_tiles[wang_idx] = {
			"image": tile_image,
			"corners": [
				upper_id if nw == 1 else lower_id,
				upper_id if ne == 1 else lower_id,
				upper_id if sw == 1 else lower_id,
				upper_id if se == 1 else lower_id
			]
		}

	var added = 0
	for pattern in corner_layout:
		if pattern == "":
			tiles.append(null)
		else:
			var parts = pattern.split("/")
			var top = parts[0]
			var bottom = parts[1]

			var nw = 1 if top[0] == "s" else 0
			var ne = 1 if top[1] == "s" else 0
			var sw = 1 if bottom[0] == "s" else 0
			var se = 1 if bottom[1] == "s" else 0
			var wang_idx = nw * 8 + ne * 4 + sw * 2 + se

			if wang_tiles.has(wang_idx):
				tiles.append(wang_tiles[wang_idx])
				added += 1
			else:
				tiles.append(null)

	print("  ✅ Added %d tiles (lower=%d '%s', upper=%d '%s')" % [added, lower_id, lower_name, upper_id, upper_name])

# Canonical terrain ids — MUST match the constants in game/TerrainMap.cs.
const TERRAIN_REGISTRY := {
	"cave wall":  0,
	"cave floor": 1,
	"lava":       2,
	"water":      3,
	"pit":        4,
	"deep water": 5,
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
# Also collapses "cave wall" vs "cave wall, cave wall with crack" into one wall terrain.
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

# Center terrain for a Wang corner tile = the terrain occupying most of its corners.
# Godot's terrain solver ignores tiles whose center terrain is unset (-1).
func majority_corner(corners) -> int:
	var counts = {}
	for c in corners:
		counts[c] = counts.get(c, 0) + 1
	var best = corners[0]
	var best_n = counts[best]
	for k in counts:
		if counts[k] > best_n or (counts[k] == best_n and k < best):
			best = k
			best_n = counts[k]
	return best

func create_tileset():
	print("\n🔨 Creating tileset...")

	var cols = 5
	var rows = (tiles.size() + cols - 1) / cols
	var atlas = Image.create(cols * tile_size, rows * tile_size, false, Image.FORMAT_RGBA8)

	for i in range(tiles.size()):
		if tiles[i] == null:
			continue
		var img = tiles[i].image
		if img.get_width() != tile_size or img.get_height() != tile_size:
			img = img.duplicate()
			img.resize(tile_size, tile_size, Image.INTERPOLATE_NEAREST)
		var x = (i % cols) * tile_size
		var y = (i / cols) * tile_size
		atlas.blit_rect(img, Rect2i(0, 0, tile_size, tile_size), Vector2i(x, y))

	atlas.save_png(output_path.replace(".tres", "_atlas.png"))
	print("  Preview: %s" % output_path.replace(".tres", "_atlas.png"))

	var tile_defs = []
	for i in range(tiles.size()):
		if tiles[i] == null:
			continue
		var x = i % cols
		var y = i / cols
		var corners = tiles[i].corners

		tile_defs.append("%d:%d/0 = 0" % [x, y])
		tile_defs.append("%d:%d/0/terrain_set = 0" % [x, y])
		tile_defs.append("%d:%d/0/terrain = %d" % [x, y, majority_corner(corners)])
		tile_defs.append("%d:%d/0/terrains_peering_bit/top_left_corner = %d" % [x, y, corners[0]])
		tile_defs.append("%d:%d/0/terrains_peering_bit/top_right_corner = %d" % [x, y, corners[1]])
		tile_defs.append("%d:%d/0/terrains_peering_bit/bottom_left_corner = %d" % [x, y, corners[2]])
		tile_defs.append("%d:%d/0/terrains_peering_bit/bottom_right_corner = %d" % [x, y, corners[3]])

	var terrain_defs = []
	var terrain_colors = {}

	for i in range(tiles.size()):
		if tiles[i] == null:
			continue
		var corners = tiles[i].corners
		if corners[0] == corners[1] and corners[1] == corners[2] and corners[2] == corners[3]:
			var terrain_id = corners[0]
			if not terrain_colors.has(terrain_id):
				var img = tiles[i].image
				terrain_colors[terrain_id] = img.get_pixel(img.get_width() / 2, img.get_height() / 2)

	# Always define all five registry terrains, even tile-less ones (e.g. water before
	# its art exists): Godot resets a peering bit to -1 on load if its terrain id isn't
	# defined in the set, which would break the renderer's corner lookup.
	var default_colors := {
		0: Color(0.149020, 0.117647, 0.262745),  # wall
		1: Color(0.168627, 0.168627, 0.200000),  # floor
		2: Color(0.823529, 0.321569, 0.101961),  # lava
		3: Color(0.180392, 0.435294, 0.560784),  # water
		4: Color(0.035294, 0.047059, 0.113725),  # pit
		5: Color(0.050000, 0.120000, 0.380000),  # deep water
	}
	var names := {0: "cave wall", 1: "cave floor", 2: "lava", 3: "water", 4: "pit", 5: "deep water"}
	for id in [0, 1, 2, 3, 4, 5]:
		var color = terrain_colors.get(id, default_colors[id])
		terrain_defs.append('terrain_set_0/terrain_%d/name = "%s"' % [id, names[id]])
		terrain_defs.append('terrain_set_0/terrain_%d/color = Color(%f, %f, %f, 1)' % [id, color.r, color.g, color.b])

	var bytes = []
	for b in atlas.get_data():
		bytes.append(str(b))

	var tres = '[gd_resource type="TileSet" load_steps=4 format=3]\n\n'
	tres += '[sub_resource type="Image" id="Image_1"]\n'
	tres += 'data = {\n'
	tres += '"data": PackedByteArray(%s),\n' % ", ".join(bytes)
	tres += '"format": "RGBA8",\n'
	tres += '"height": %d,\n' % atlas.get_height()
	tres += '"mipmaps": false,\n'
	tres += '"width": %d\n' % atlas.get_width()
	tres += '}\n\n'
	tres += '[sub_resource type="ImageTexture" id="ImageTexture_1"]\n'
	tres += 'image = SubResource("Image_1")\n\n'
	tres += '[sub_resource type="TileSetAtlasSource" id="TileSetAtlasSource_1"]\n'
	tres += 'texture = SubResource("ImageTexture_1")\n'
	tres += 'texture_region_size = Vector2i(%d, %d)\n' % [tile_size, tile_size]
	tres += "\n".join(tile_defs) + '\n\n'
	tres += '[resource]\n'
	tres += 'tile_size = Vector2i(%d, %d)\n' % [tile_size, tile_size]
	tres += 'terrain_set_0/mode = 1\n'
	tres += "\n".join(terrain_defs) + '\n'
	tres += 'sources/0 = SubResource("TileSetAtlasSource_1")\n'

	var out = FileAccess.open(output_path, FileAccess.WRITE)
	out.store_string(tres)
	out.close()
