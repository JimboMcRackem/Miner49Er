# Pit Props — Design

**Date:** 2026-07-18
**Status:** Approved (design), pending implementation plan
**Area:** Client-side presentation. NO simulation / snapshot / netcode / fog / Core changes.

Decorative mine support timbers ("pit props") on rock walls, plus a wood-splinter
debris reaction when a propped wall is destroyed by an explosion. Reuses the existing
seeded wall-decoration pattern (embedded skeletal remains) and the
`Miner49er.Core.Fx.ParticleSystem` debris pool. No new Core logic ⇒ no new unit tests;
verified by running the game.

## Global constraints

- Client-side only. No sim / `MinerSnapshot` / `TileChange` / codec / netcode / `Fog` changes.
- The prop pattern is a pure deterministic function of tile `(x, y)`, so every client draws
  the identical props with no networking. Same seeding style as the skeleton overlay.
- Props sit **only on plain `Rock` tiles**. `GoldRock` (gold-vein overlay), `CrystalRock`,
  `LavaVent`, `Cracked`, `Crumbling`, `Plank`, and all non-wall tiles are skipped.
- All new tuning values are named constants.

---

## 1. The prop sprite (PixelLab)

A single timber pit-prop sprite: two vertical wooden posts joined by a horizontal cap beam
(the classic mine-shaft frame), transparent background, sized to overlay one wall tile.

- Generated via PixelLab and saved to `res://assets/objects/pit_prop.png`.
- Loaded in `WorldRenderer` with the standard guard:
  `_pitPropTex = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;`
- **Procedural fallback** when the texture is absent: two brown vertical `DrawRect` posts
  near the tile's left/right edges plus a horizontal cap beam near the top, in a dark-timber
  colour. The feature works (if plainer) before the sprite lands.

## 2. Props on walls

A new seeded overlay pass in `WorldRenderer._Draw`, placed beside the existing skeletal-remains
pass and iterating the same `VisibleTiles(vx0, vx1, vy0, vy1)` range:

- For each visible tile whose type is `Rock`, hash `(x, y)` into a `uint` (same mixing constants
  as the skeleton pass but a distinct salt, e.g. `0xB0A5u`, so props and skeletons don't
  correlate). A prop is present when `(h & 0xFFu) < PitPropThreshold`, where
  `PitPropThreshold = 38` gives ≈15% (`38/256 ≈ 0.148`).
- When present, draw `_pitPropTex` over the tile rect `r`; if the texture is null, run the
  procedural fallback. Apply a seeded horizontal flip (from another bit of `h`) for variety —
  for a texture, draw into a rect with negative width via `DrawTextureRect(tex, flippedRect, false)`
  or the sprite's flip flag; for the fallback, mirror the post offsets.
- The pass relies on the FogRenderer overlay (ZIndex −5) to hide props on explored-but-unseen
  tiles, exactly as the skeleton pass does — no explicit `Fog.IsVisible` check needed.

**Shared seed helper.** Expose the presence test as
`public static bool HasPitProp(int x, int y)` on `WorldRenderer`, and have the overlay pass call
it. The blast-debris code (below) calls the same method, guaranteeing a wall that *looks*
propped is exactly the wall that splinters.

## 3. Wood splinters on blast

The blast handling in `MatchClient.ApplyUpdate` already iterates every `TileChange` and flags
`t.FromBlast`. Two small additions:

- **Capture the pre-blast tile type.** At the top of the per-tile loop, before any grid mutation,
  read `var oldType = Grid.Get(p);` (the loop already reads `Grid.Get(p)` for the flood check, so
  the old value is available at that point).
- **Emit splinters per blasted propped wall.** For each change with
  `t.FromBlast && oldType == TileType.Rock && WorldRenderer.HasPitProp(t.X, t.Y)`, call a new
  `_world?.EmitWoodSplinters(tileCenter)` where
  `tileCenter = new Vector2(t.X * TileSize + TileSize/2f, t.Y * TileSize + TileSize/2f)`.
  This fires **in addition to** the single `EmitExplosionDebris(c)` rock-debris burst at the
  blast centroid — splinters layer on top of the rock dust; they do not replace it.

`WorldRenderer.EmitWoodSplinters(Vector2 pos)`: ~8 tan/brown **elongated** particles sprayed
outward with an upward bias and gravity (mirroring `EmitExplosionDebris`'s velocity model but
with a wood palette, e.g. `new Color(0.62f, 0.42f, 0.22f)` varied slightly per particle and a
larger `Size` so they read as splinters, not dust). Fog/viewport culling is inherited from the
`ParticleSystem` draw path.

Hand-mining a propped wall (a non-`FromBlast` `Rock → Floor` change) triggers nothing extra —
splinters are explosion-only by design.

---

## Files

- **Modify:** `game/WorldRenderer.cs` — load `pit_prop.png` (+ fallback); `HasPitProp(int,int)`
  seed helper; the prop overlay pass; `EmitWoodSplinters(Vector2)`.
- **Modify:** `game/net/MatchClient.cs` — capture pre-blast `oldType`; per-blasted-tile
  `EmitWoodSplinters` call gated on `oldType == Rock && WorldRenderer.HasPitProp(...)`.
- **New asset:** `assets/objects/pit_prop.png` (PixelLab), with its `.import` sidecar.

No sim / snapshot / codec / netcode / fog files touched. No Core changes.

## Testing

- No new pure logic ⇒ no new unit tests (the reused `ParticleSystem` is already covered).
- Regression: existing Core suite stays green (currently 825/825).
- Verified by running: timber pit props appear on ≈15% of visible rock walls (seeded, stable
  per tile, identical across clients); dynamiting a propped wall throws wood splinters over the
  usual rock debris; hand-mining a propped wall shows only the normal mining dust.

## Tuning knobs

`PitPropThreshold` (density, default 38 ≈ 15%); prop seed salt (`0xB0A5u`); splinter count,
palette, size, and velocity inside `EmitWoodSplinters`; procedural-fallback timber colour and
post geometry.
