shader_type canvas_item;

uniform float wave_speed     : hint_range(0.1, 3.0) = 1.2;
uniform float wave_amplitude : hint_range(0.0, 0.05) = 0.012;
uniform float pulse_strength : hint_range(0.0, 0.3)  = 0.12;

void fragment() {
	vec2 uv = UV;
	uv.y += sin(uv.x * 20.0 + TIME * wave_speed)        * wave_amplitude;
	uv.x += sin(uv.y * 18.0 + TIME * wave_speed * 0.7)  * wave_amplitude;
	uv.y += sin(uv.x * 9.0  + TIME * wave_speed * 0.5)  * wave_amplitude * 0.5;
	COLOR = texture(TEXTURE, uv);
	float pulse = 1.0 + pulse_strength * sin(TIME * 1.5 + UV.x * 10.0 + UV.y * 10.0);
	COLOR.rgb *= pulse;
}
