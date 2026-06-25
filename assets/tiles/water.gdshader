shader_type canvas_item;

uniform float wave_speed     : hint_range(0.1, 3.0) = 0.8;
uniform float wave_amplitude : hint_range(0.0, 0.02) = 0.003;
uniform float pulse_strength : hint_range(0.0, 0.1)  = 0.04;

void fragment() {
	vec2 uv = UV;
	uv.y += sin(uv.x * 20.0 + TIME * wave_speed)        * wave_amplitude;
	uv.x += sin(uv.y * 18.0 + TIME * wave_speed * 0.7)  * wave_amplitude;
	COLOR = texture(TEXTURE, uv);
	float pulse = 1.0 + pulse_strength * sin(TIME * 1.2 + UV.x * 12.0 + UV.y * 12.0);
	COLOR.rgb *= pulse;
}
