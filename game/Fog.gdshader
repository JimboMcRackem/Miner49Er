// Fog over the world, drawn by FogRenderer. COLOR.a is the fog opacity from FogMask. Remembered
// areas lose their colour, and darkness fades toward the floor's shadow tint, reaching pure black
// only at full opacity, so unexplored tiles stay completely hidden.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec3 shadow_tint;       // linear: the viewport renders in linear HDR
uniform float dim_alpha;          // opacity of explored-but-not-visible tiles
uniform float memory_from;        // opacity where greying starts: the in-view vignette's peak
uniform float memory_grey = 0.6;  // how much colour remembered areas lose; some hue stays so water never reads as lava

void fragment() {
	float a = COLOR.a;
	vec3 scene = texture(screen_tex, SCREEN_UV).rgb;
	float grey = dot(scene, vec3(0.2126, 0.7152, 0.0722));
	scene = mix(scene, vec3(grey), smoothstep(memory_from, dim_alpha, a) * memory_grey);
	vec3 shade = shadow_tint * clamp((1.0 - a) / (1.0 - dim_alpha), 0.0, 1.0);
	COLOR = vec4(mix(scene, shade, a), 1.0);
}
