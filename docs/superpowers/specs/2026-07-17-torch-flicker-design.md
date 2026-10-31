# Torch Flicker (Guttering) — Design

**Date:** 2026-07-17
**Status:** Approved (design), pending implementation plan
**Area:** Client-side presentation (cave lighting). No sim / netcode / fog changes.

## Goal

Make cave light **feel alive**: environmental light sources occasionally *gutter*
— a smooth dip-and-recover in brightness, like a flame catching — instead of
glowing at a dead-constant level. Steady the player torch as a visual anchor;
gutter the world sources around it.

## Scope

**Gutters:** lanterns (held + dropped), lava & lava vents, crystals (CrystalRock
walls + held CrystalShard).

**Does NOT gutter:** the player torch (the blue-white pool under each living
miner) — it stays rock-steady so moving around never feels nauseating.

**Explicitly out of scope:** any change to the simulation, snapshots, netcode,
fog/line-of-sight, culling, or which tiles are lit. Flicker only scales the
*brightness* of already-lit tiles within a fixed radius. Purely cosmetic,
per-client, wall-clock driven.

## Character: guttering, not continuous shimmer

Chosen profile (from brainstorm): **mostly steady ≈ full brightness, with sparse,
smooth dips.** A dip eases down and back over a fraction of a second (a
raised-cosine shape, ~0.25 s wide) rather than a single-frame flash, so it reads
as a flame catching — never a strobe or a rendering bug. Each source gutters on
its own independent rhythm.

Two dip profiles:
- **Fire** (lantern, lava/vent): dips to ~0.5× brightness.
- **Crystal** (CrystalRock, CrystalShard): shallower (~0.75×) and slower (longer
  window), since crystals are magical rather than fire. Still gutters, just gentler.

## Architecture

Approach **A — a pure, stateless flicker function in Core**, called independently
by both renderers so they agree with no shared state.

### Component 1 — `Miner49er.Core.Light.Flicker` (new, pure, tested)

Engine-free static helper. Public surface:

```csharp
public static class Flicker
{
    // Profiles bundle the tuning knobs; two presets provided.
    public readonly record struct Profile(
        double WindowSeconds,   // length of each event window
        double GutterChance,    // 0..1 probability a given window contains a dip
        double DipDepth,        // 0..1 fraction subtracted at the dip's deepest point
        double DipWidthSeconds, // full width of the raised-cosine dip
        double FloorLevel);     // clamp: multiplier never goes below this

    public static readonly Profile Fire;    // Window 1.6, Chance 0.5, Depth 0.5, Width 0.25, Floor 0.5
    public static readonly Profile Crystal; // Window 2.6, Chance 0.4, Depth 0.25, Width 0.5,  Floor 0.75

    // Returns a brightness multiplier in [profile.FloorLevel .. 1.0].
    public static float Multiplier(int seed, double t, Profile p);
}
```

**Algorithm (stateless, deterministic in `(seed, t, p)`):**
1. Consider the current window `k = floor(t / p.WindowSeconds)` and its two
   neighbours `k-1`, `k+1` (so a dip near a window boundary is not clipped).
2. For each of those windows `w`, deterministically hash `(seed, w)` to:
   - decide whether that window has a gutter event (`hash01 < p.GutterChance`),
   - and if so, an event centre time `tc` = `w * WindowSeconds + offset`, where
     `offset` is a second hash mapped into `[0, WindowSeconds)`.
3. For each active event, compute a raised-cosine dip centred at `tc` with full
   width `p.DipWidthSeconds`: `dip = 0.5*(1+cos(pi * (t-tc)/(halfWidth)))` for
   `|t-tc| < halfWidth`, else 0. Scale by `p.DipDepth`.
4. Take the **maximum** dip across the considered events, subtract from 1.0,
   clamp to `[p.FloorLevel, 1.0]`, return as float.

Hashing uses a small integer mix (e.g. splitmix-style on `seed` combined with the
window index) producing a stable double in `[0,1)`. No RNG object, no state.

**Unit tests (`src/Miner49er.Core.Tests/FlickerTests.cs`, alongside the existing
`LightFieldTests.cs` / `LightMapTests.cs`):**
- Range: for many `(seed, t)` samples, result is within `[FloorLevel, 1.0]`.
- Determinism: same `(seed, t, profile)` returns the same value on repeat calls,
  and different seeds are not all identical.
- Mostly bright: over a dense sweep of `t`, the value equals ~1.0 for a clear
  majority of samples (steady between dips).
- Dips occur: over a long sweep, at least some samples fall meaningfully below 1.0
  (a gutter actually happens).
- Continuity: adjacent samples (`t`, `t + 1/120`) never jump by more than a small
  epsilon — no discontinuities that would strobe.

### Component 2 — seeds (stable identity per source)

A source's seed must be **stable over time** so its flicker phase does not jump:
- Lava / LavaVent / CrystalRock **tile** → `hash(x, y)`.
- **Held** lantern / shard → `hash("held" marker, minerId)` (stable while the
  miner walks — keyed by miner, not by moving position).
- **Dropped** lantern / shard on the ground → `hash(x, y)` (stationary).

A tiny local helper computes these ints (e.g. `x * 73856093 ^ y * 19349663`, with
a distinct constant folded in for the held-by-miner namespace). Independent seeds
→ sources gutter on unrelated rhythms.

### Component 3 — `LightRenderer` (darkness light-map) integration

`LightMap.AddLight` gains an optional intensity `scale` (default `1.0`), which
multiplies the per-tile intensity it accumulates:

```csharp
public void AddLight(TileGrid grid, GridPos origin, int radius, float scale = 1f);
```

In `LightRenderer.AddSourceLights`, each **gutter** source passes
`Flicker.Multiplier(seed, now, profile)` as `scale` (Fire for lantern/lava,
Crystal for crystal). `AddPlayerTorches` passes **no scale** (stays 1.0, steady).
`now = Time.GetTicksMsec() / 1000.0`, captured once per `_Draw`. Radius is
unchanged, so the set of lit tiles is identical — only their brightness dips.

### Component 4 — WorldRenderer: NOT needed (compositing does it for free)

The darkness overlay (`LightRenderer`, ZIndex **-8**) is drawn *on top of* the
additive colored halos (`WorldRenderer`, ZIndex **-9**). So guttering the
light-map thickens the black overlay over a tile, which dims **both** the terrain
and the halo behind it — the whole lit pool gutters coherently from the light-map
alone. No WorldRenderer changes, no per-halo seed matching, and no desync risk
(the same darkening drives darkness and halo together).

One accepted caveat: lava's bloom-boosted vent core (`LavaGlowBoost`, which feeds
the viewport-wide bloom post-process) does **not** gutter, because bloom reads the
final framebuffer independently of the 2D compositing order. A lava vent's bright
centre stays steady while its surrounding pool darkness gutters. Negligible.

## Data flow & safety

```
Time.GetTicksMsec ─┐
tile/miner seed ───┴─► Flicker.Multiplier ─► scale ─► LightRenderer light-map (darkness)
                                                        └► darkness composited above the
                                                           halos ⇒ pool + halo gutter together
```

- Wall-clock, per-client. Different clients may gutter out of phase — acceptable,
  it is cosmetic.
- No Core sim / snapshot / codec / netcode touched.
- Radius fixed ⇒ `Fog`, visibility, culling, and all gameplay are provably
  unaffected; the only added per-frame cost is a couple of integer hashes per
  visible gutter source.

## Tuning knobs

All live as named constants on the two `Flicker.Profile` presets: window length,
gutter chance, dip depth, dip width, floor level. After a play-test we dial "how
alive" without touching structure.

## Testing strategy

- **Core:** `Flicker` unit tests as listed in Component 1. Target: existing suite
  count + the new Flicker tests, all green.
- **Renderers:** presentation wiring in `game/` is verified by running the game
  (consistent with the rest of `game/`, which is not unit-tested), watching that
  lanterns/lava/crystals gutter, the player torch stays steady, and darkness +
  halo dip together with no popping.

## File structure

- **Create:** `src/Miner49er.Core/Light/Flicker.cs`
- **Create:** `src/Miner49er.Core.Tests/FlickerTests.cs` (flat, beside
  `LightFieldTests.cs` / `LightMapTests.cs`)
- **Modify:** `src/Miner49er.Core/Light/LightMap.cs` — add `scale` param to `AddLight`
- **Modify:** `game/LightRenderer.cs` — pass flicker scale for gutter sources; add
  the seed helper; capture `now`

(No `game/WorldRenderer.cs` change — the darkness overlay is composited above the
halos, so guttering the light-map dims the halos for free. See Component 4.)
