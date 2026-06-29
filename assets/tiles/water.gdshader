shader_type canvas_item;

uniform float glint_speed    : hint_range(0.5, 4.0) = 2.0;
uniform float glint_strength : hint_range(0.0, 0.3) = 0.10;

void fragment() {
	COLOR = texture(TEXTURE, UV);
	// Three multiplied sines produce irregular constructive interference (glints)
	float g = sin(UV.x * 7.3 + UV.y * 5.1 + TIME * glint_speed)
	        * sin(UV.x * 3.7 - UV.y * 9.2 + TIME * glint_speed * 0.6)
	        * sin(UV.y * 6.1 + TIME * glint_speed * 1.3);
	COLOR.rgb += max(0.0, g) * glint_strength;
}
