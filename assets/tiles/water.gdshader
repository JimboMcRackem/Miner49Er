shader_type canvas_item;

uniform float wave_speed     : hint_range(0.3, 3.0)  = 1.2;
uniform float wave_amplitude : hint_range(0.0, 0.05) = 0.005;
uniform float glint_speed    : hint_range(0.5, 4.0)  = 2.0;
uniform float glint_strength : hint_range(0.0, 0.5)  = 0.20;

void fragment() {
	// Sub-pixel UV ripple — makes the tile appear to move like real water
	vec2 wuv = UV;
	wuv.x += sin(UV.y * 8.0 + TIME * wave_speed) * wave_amplitude;
	wuv.y += sin(UV.x * 8.0 + TIME * wave_speed * 0.8) * wave_amplitude;
	COLOR = texture(TEXTURE, wuv);

	// Three multiplied sines produce irregular constructive interference (glints)
	float g = sin(UV.x * 7.3 + UV.y * 5.1 + TIME * glint_speed)
	        * sin(UV.x * 3.7 - UV.y * 9.2 + TIME * glint_speed * 0.6)
	        * sin(UV.y * 6.1 + TIME * glint_speed * 1.3);
	COLOR.rgb += max(0.0, g) * glint_strength;
}
