# Cave Lighting & Darkness — Design Spec

**Date:** 2026-07-16
**Status:** Approved (brainstorm)
**Feature:** GFX "pizazz" item 2 — real 2D lighting + darkness (follows bloom / camera-shake / drop-shadows, `gfx-pop-status`).

## Goal

Give the caves genuine dynamic lighting: a darker ambient baseline, warm/cool
colored light pools that spill from sources onto nearby tiles, and walls that
block light so shadows fall behind them. The local player always carries a
small cool torch. Ghosts are exempt from the whole system.

All of this is **client-side presentation only** — no `Miner49er.Core`, sim, or
netcode changes. Same footprint as the existing fog-of-war and bloom passes.

## Approach (chosen: B — custom light-map in immediate mode)

The game renders everything through immediate-mode `_Draw` (`DrawRect` /
`DrawTextureRect`), not Godot sprites + `Light2D`. So lighting is a custom
light-map that fits that architecture, rather than `CanvasModulate` +
`PointLight2D` + `LightOccluder2D` (rejected: maintaining wall-occluder
geometry synced to a constantly-mutating grid is the risky, perf-sensitive
part, and it fights the existing fog overlay).

## Architecture & compositing

A new **`LightRenderer`** node (sibling to `FogRenderer`), initialized from
`MatchClient` like the fog renderer.

Each frame it builds a **light-map**: a dark ambient "floor" with additive
colored light pools painted into it; shadowed tiles are left at the ambient
floor. The light-map is composited over the world with **multiply blend** —
unlit tiles darken toward the ambient floor, lit tiles stay bright and take on
the source's color.

Layer order (bottom → top):

1. World terrain + actors (`WorldRenderer`)
2. **Light-map (multiply)** — `LightRenderer`
3. **Fog overlay (LOS)** — `FogRenderer`
4. Bloom (`WorldEnvironment` glow, already present)
5. UI (`CanvasLayer`s — unaffected, they are on separate canvases)

**Ghosts** are drawn **above** the light-map so they render full-bright — they
emit no light and cast/receive no shadow. This matches their existing
drop-shadow exemption in `WorldRenderer`.

**Compositing mechanism:** build the additive light pools into an offscreen
light-map (SubViewport or equivalent accumulation), then draw it over the world
via a `CanvasItemMaterial` with multiply blend. The ambient floor is a dark
fill so unlit visible tiles multiply down to the floor level rather than to
black.

## Light sources & colors

All values are starting points, tuned in play-test. RGB in 0–1.

| Source | Color | Radius | Notes |
|---|---|---|---|
| Ambient floor | `0.30, 0.32, 0.40` | — | Multiply level of a visible-but-unlit tile. Slightly cool. The "darker baseline." |
| Player torch | `0.55, 0.70, 1.0` | ~4 tiles | Every **living miner** (incl. bots). Cool blue-white, dimmer/smaller than a lantern. You are always centered in your own lit pool. |
| Lantern (held or dropped) | `1.0, 0.80, 0.45` | ~6 tiles | Warm amber, strong. |
| Crystal (held shard + crystal-rock walls) | `0.60, 0.55, 1.0` | ~4 tiles | Cyan/violet. |
| Lava / vents | deep orange | ~3–4 tiles | Gentle flicker. |
| Explosion | bright warm | large | Brief flash, fast decay; rides the existing shake/flash timing. |

The current lantern / crystal / lava **glow halos** (additive textures in
`WorldRenderer`) fold into this light system — they become real lights. Keep a
small emissive core per source so the bloom pass still catches them.

## Shadows

Per-light **grid shadowcasting** (recursive, symmetric) within each light's
radius. Wall tiles block light, so a lantern lights a corridor and stops at its
bends, throwing dark pockets behind walls. Reuses the same tile-grid
"what blocks sight" logic the fog already relies on.

**Performance:**
- Only lights whose radius intersects the viewport are processed.
- Each light is bounded to its `(2r+1)²` tile window (~80 tiles at r=4).
- Results are **cached** and recomputed only when a light moves or terrain
  changes (mining / cave-in / flood already raise a dirty signal).
- Worst case: a handful of lights = a few hundred tile-ops/frame, cached when
  static.

## Fog reconciliation (layer-vs-replace)

Fog keeps its **LOS job**: unexplored = black, explored = dim, plus the 1-tile
boundary overhang. **Soften fog's in-view radial darkening** (the current
`DimAlpha = 0.78` center falloff), because the light-map now owns *how lit* the
visible area is.

Net split: **fog decides what you can see; the light-map decides how it's lit.**
The player's blue torch keeps the view center lit even with fog's gradient
flattened.

## Testing

Compositing is visual — validated by play-test. The **pure math** extracts into
an engine-free helper and is unit-tested:

- A wall blocks a light beyond it (shadowcast).
- Two overlapping lights sum on a tile.
- Radius falloff (intensity decreases with distance, zero past radius).

Keeps parity with the project's tested-sim discipline. The existing ~800 tests
stay green (no Core changes).

## Out of scope (YAGNI)

- Normal-mapped / bump lighting (flat-shaded pixel cave doesn't need it).
- Godot native `Light2D` / occluder nodes.
- Networked/authoritative lighting — it is pure client presentation, derived
  from the world snapshot each client already has.
- Colored light on the minimap / fog-of-war explored tiles.
