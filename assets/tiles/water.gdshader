shader_type canvas_item;

// Outputs solid colour using the tile's own alpha as a mask.
// The Modulate on the TileMapLayer provides the actual water colour,
// so rounded Wang edge shapes are preserved while the stone texture is replaced.
void fragment() {
	float a = texture(TEXTURE, UV).a;
	COLOR = vec4(1.0, 1.0, 1.0, a);
}
