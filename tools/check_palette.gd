extends SceneTree

# Verifies what the ENGINE actually hands back for a converted sprite, not what the
# source PNG holds. Miner tinting preserves skin and hard hat by exact palette
# membership, so any colour drift introduced by the importer (compression, sRGB
# conversion, alpha-border fixing) would silently break preservation and render the
# whole miner in one flat team colour.
#
# Run: godot --headless -s tools/check_palette.gd

const PALETTE := {
	"000000": "black", "FFFFFF": "white", "813338": "red", "75CEC8": "cyan",
	"8E3C97": "purple", "56AC4D": "green", "2E2C9B": "blue", "EDF171": "yellow",
	"8E5029": "orange", "553800": "brown", "C46C71": "lightred", "4A4A4A": "darkgrey",
	"7B7B7B": "grey", "A9FF9F": "lightgreen", "706DEB": "lightblue", "B2B2B2": "lightgrey",
}

const TARGETS := [
	"res://assets/miners/idle/idle0.png",
	"res://assets/miners/veteran/walk/e0.png",
	"res://assets/objects/chest.png",
	"res://assets/monsters/slime/walk/east_0.png",
]

func _init() -> void:
	var bad_total := 0
	for path in TARGETS:
		var tex = load(path)
		if tex == null:
			print("MISSING: ", path)
			bad_total += 1
			continue
		var img: Image = tex.get_image()
		var seen := {}
		for y in img.get_height():
			for x in img.get_width():
				var c := img.get_pixel(x, y)
				if c.a < 0.05:
					continue
				var key := "%02X%02X%02X" % [round(c.r * 255.0), round(c.g * 255.0), round(c.b * 255.0)]
				seen[key] = seen.get(key, 0) + 1
		var offpal := []
		for k in seen:
			if not PALETTE.has(k):
				offpal.append(k)
		print("\n", path, "  format=", img.get_format())
		for k in seen:
			var label: String = PALETTE.get(k, "*** OFF-PALETTE ***")
			print("   #", k, "  ", label, "  (", seen[k], " px)")
		if offpal.size() > 0:
			print("   FAIL: ", offpal.size(), " off-palette colour(s)")
			bad_total += offpal.size()
	print("\n==== off-palette colours seen by the engine: ", bad_total, " ====")
	quit(1 if bad_total > 0 else 0)
