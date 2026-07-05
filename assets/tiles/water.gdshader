shader_type canvas_item;

uniform float glint_speed    : hint_range(0.5, 4.0)  = 2.0;
uniform float glint_strength : hint_range(0.0, 1.0)  = 0.55;

void fragment() {
	COLOR = texture(TEXTURE, UV);

	// Three multiplied sines produce irregular constructive interference.
	// Squaring sharpens the result into brief bright pinpoints rather than
	// a broad wash, giving the impression of sunlight catching the surface.
	float g = sin(UV.x * 7.3 + UV.y * 5.1 + TIME * glint_speed)
	        * sin(UV.x * 3.7 - UV.y * 9.2 + TIME * glint_speed * 0.6)
	        * sin(UV.y * 6.1 + TIME * glint_speed * 1.3);
	float glint = pow(max(0.0, g), 2.0);

	// Second layer — smaller, faster glints for variety
	float g2 = sin(UV.x * 11.1 - UV.y * 4.3 + TIME * glint_speed * 1.7)
	         * sin(UV.x * 5.9 + UV.y * 8.7 + TIME * glint_speed * 0.9)
	         * sin(UV.y * 3.2 - TIME * glint_speed * 1.1);
	glint += pow(max(0.0, g2), 2.0) * 0.5;

	COLOR.rgb += glint * glint_strength;
}
