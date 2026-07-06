shader_type canvas_item;
// Passthrough — water appearance is handled by WorldRenderer draw calls.
void fragment() {
	COLOR = texture(TEXTURE, UV);
}
