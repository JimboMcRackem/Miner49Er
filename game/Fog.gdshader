// Fog over the world, drawn by FogRenderer. COLOR.a is the fog opacity from FogMask. Remembered
// areas lose their colour, and darkness fades toward the floor's shadow tint, reaching pure black
// only at full opacity, so unexplored tiles stay completely hidden. Slow wisps of haze drift
// through remembered areas so the dark has air in it.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform vec3 shadow_tint;       // linear: the viewport renders in linear HDR
uniform float dim_alpha;          // opacity of explored-but-not-visible tiles
uniform float memory_from;        // opacity where greying starts: the in-view vignette's peak
uniform float memory_grey = 0.6;  // how much colour remembered areas lose; some hue stays so water never reads as lava
uniform float tile_px = 32.0;     // world px per tile (set from MatchClient.TileSize); sets the haze's scale
uniform float haze_amount = 0.8;  // how much brighter the thickest wisps make a remembered area
uniform float haze_gap = 0.25;    // how much darker the gaps between wisps are, as a share of haze_amount

varying vec2 world_px;

void vertex() {
	world_px = VERTEX;   // FogRenderer draws in world space at the origin
}

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
	           mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

// Two layers drifting different ways, a few tiles across, so wisps slowly change shape as they move.
float haze(vec2 tiles, float t) {
	float n = value_noise(tiles / 3.0 + t * vec2(0.08, 0.03)) * 0.65
	        + value_noise(tiles / 1.3 - t * vec2(0.05, 0.1)) * 0.35;
	return smoothstep(0.45, 0.8, n);
}

void fragment() {
	float a = COLOR.a;
	vec3 scene = texture(screen_tex, SCREEN_UV).rgb;
	float grey = dot(scene, vec3(0.2126, 0.7152, 0.0722));
	float memory = smoothstep(memory_from, dim_alpha, a);
	scene = mix(scene, vec3(grey), memory * memory_grey);
	float fade = clamp((1.0 - a) / (1.0 - dim_alpha), 0.0, 1.0);   // 1 down to dim, 0 at unexplored
	vec3 col = mix(scene, shadow_tint * fade, a);
	// Relative, not a colour wash: a fixed haze colour only shows against near-black and would
	// outline every explored edge. The gaps between wisps sit slightly darker so the light moves.
	float wisp = haze(world_px / tile_px, TIME) - haze_gap;
	col *= 1.0 + wisp * haze_amount * memory * fade;
	COLOR = vec4(col, 1.0);
}
