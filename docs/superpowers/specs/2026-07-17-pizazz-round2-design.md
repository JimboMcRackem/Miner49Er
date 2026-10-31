# Pizazz Round 2 Pack — Design

**Date:** 2026-07-17
**Status:** Approved (design), pending implementation plan
**Area:** Client-side presentation. NO simulation / snapshot / netcode / fog / culling changes.

Three independent visual-juice features, built in order as a pack:
1. **Particles** (mining dust, footstep puffs, lava embers, explosion debris, ambient dust motes)
2. **Water shimmer** (caustic glints)
3. **Floating pickup numbers** (gold / idols / prizes, local player only)

Each is independently shippable; they share only the theme and the `WorldRenderer`
"add a transient effect" pattern already used by explosion flashes, rings, and
flood advances.

## Global constraints

- Client-side only. No changes to the sim, `MinerSnapshot`/codec, netcode,
  `Fog`/visibility, or culling.
- Wall-clock, per-client time via `Time.GetTicksMsec() / 1000.0`. Out-of-phase
  between clients is fine (cosmetic).
- Everything is **viewport / fog culled** and **capped** so lava-heavy and
  flooded floors stay cheap (those floors were previously lag-prone and fixed by
  culling — do not regress that).
- Pure, engine-free math lives in `Miner49er.Core` with xUnit tests; Godot `game/`
  presentation is verified by running the game.

---

## Feature 1 — Particles

### Core: `Miner49er.Core.Fx.ParticleSystem` (pure, tested)

A fixed-capacity pool of lightweight particles. No Godot types — colours are plain
floats so the renderer maps them to `Color`.

```csharp
namespace Miner49er.Core.Fx;

public struct Particle
{
    public float X, Y;        // position (world px)
    public float Vx, Vy;      // velocity (px/sec)
    public float Gravity;     // added to Vy each second (px/sec^2)
    public float Life;        // remaining seconds; dead at <= 0
    public float MaxLife;     // initial life, for fade ratio
    public float Size;        // half-extent in px
    public float R, G, B, A;  // base colour; A is the peak alpha
}

public sealed class ParticleSystem
{
    public ParticleSystem(int capacity);      // fixed-size backing array; never allocates after ctor
    public int Count { get; }                 // live particle count
    public int Capacity { get; }

    // Adds one particle. At capacity, replaces the particle closest to expiring
    // (smallest Life), so bursts never allocate and never grow past the cap.
    public void Emit(in Particle p);

    // Advances all particles by dt: X += Vx*dt; Y += Vy*dt; Vy += Gravity*dt;
    // Life -= dt; particles with Life <= 0 are removed (swap-remove, order not preserved).
    public void Update(float dt);

    // Live particles for drawing (renderer computes fade = Life/MaxLife). Backed by the
    // internal array; valid indices are [0, Count). No allocation per read.
    public IReadOnlyList<Particle> Live { get; }
}
```

**Tests (`src/Miner49er.Core.Tests/ParticleSystemTests.cs`):**
- Position integrates by velocity × dt.
- Gravity accelerates Vy over successive updates.
- Life decrements by dt; a particle with `Life <= 0` after update is removed (`Count` drops).
- Capacity cap: emitting past capacity keeps `Count == Capacity` (the particle with the smallest `Life` is replaced).
- Empty system `Update` is a no-op and `Count == 0`.

### Game: `WorldRenderer` emission + draw

`WorldRenderer` owns one `ParticleSystem` (capacity ~400), calls `Update(dt)` once
per frame (alongside the existing flood-advance update), and draws each live
particle as a small `DrawRect` whose alpha = `A * (Life / MaxLife)`. Drawing is
inside the existing viewport-culled `_Draw`; particles off-screen are skipped.

Emitters (each uses a small `System.Random` for spread — cosmetic, not the sim RNG):

| Emitter | Trigger | Look |
|---|---|---|
| **Mining dust** | visible miner with `Activity == 1`, throttled (a few per ~0.1 s) | small brown puffs at the mined tile (miner tile + facing offset), slight upward Vy + gravity |
| **Footstep puffs** | a visible miner's tile changed (reuse the walk/last-pos detection) | one faint grey puff at the feet, brief |
| **Lava embers** | on-screen `Lava`/`LavaVent` tiles (reuse the light/terrain viewport scan), low per-tile chance/frame | glowing orange embers, rising (negative Vy), short life |
| **Explosion debris** | on the existing detonation path (`AddExplosionFlash` / ring), one burst per blast | a radial spray of warm debris chunks, gravity pulls them down |
| **Ambient dust motes** | maintain a small population inside the current viewport; respawn as they age or drift out | faint, near-white, very slow drift; atmospheric |

All emitters gate on `Fog.IsVisible` (or the existing viewport window) so nothing
spawns in unseen areas, and total live particles are bounded by the pool cap.

---

## Feature 2 — Water shimmer (caustic glints)

Pure `WorldRenderer` draw, no Core, no shader. Water is drawn as flat shallow/deep
polygons in a viewport-culled loop that **already** has a per-tile *sparkle*
(twinkling specular dots — the look NOT chosen). Feature 2 **replaces that inner
sparkle loop** with drifting caustic glints; the base polygon and the shoreline
foam edges are left untouched.

For each visible water tile, draw a couple of soft light-blue highlight blobs
(larger, softer `DrawCircle`s than the old point-sparkles, low alpha, lighter than
the base water). Their **centres drift with time**: offset = sine/cosine of
`now * speed + hash(tileX, tileY)` so each tile's glints move on their own phase
and the field reads as slow caustics rather than in-place twinkling. Deep water
uses a slightly dimmer glint than shallow. Amplitude/speed/alpha/radius are named
constants for tuning. Cost stays bounded by the existing water viewport cull.

---

## Feature 3 — Floating pickup numbers

### Detection: `MatchClient`

Track the **local miner's** `Gold` between snapshots (a single `int _lastLocalGold`,
reset on floor change / respawn). On an increase, raise `+{delta}g`. Idols: track
the local miner's Treasure-Hunt idol count (`TreasureProgress[...].Found`) delta →
`+1 idol`. Prizes: the existing `PrizeWon` signal already fires with a `MinerId`
and prize type — when it is the local miner, raise the prize reward text. All three
are **local-player only** (per the brainstorm), so rivals never clutter the view.

### Draw: `WorldRenderer`

A small list of transient labels `(string text, Vector2 worldPos, Color color,
float life, float maxLife)` with an `AddFloatingText(worldPos, text, color)` entry
point (same shape as `AddExplosionFlash`). Each frame: `worldPos.Y -= RiseSpeed*dt`,
`life -= dt`, cull at `life <= 0`; draw with `DrawString` (fallback font), alpha =
`life / maxLife`. Spawn position is just above the miner sprite. Colours: gold amber,
idols in the idol colour, prizes in the prize colour. Bounded (few concurrent).

---

## File structure

- **Create:** `src/Miner49er.Core/Fx/ParticleSystem.cs` — pool + `Particle` struct + `Update`/`Emit`.
- **Create:** `src/Miner49er.Core.Tests/ParticleSystemTests.cs` — pool/integration tests.
- **Modify:** `game/WorldRenderer.cs` — own the `ParticleSystem`; add emitters + particle draw (Feature 1); water glints (Feature 2); floating-text list + `AddFloatingText` + draw (Feature 3).
- **Modify:** `game/net/MatchClient.cs` — detonation burst emit (Feature 1); local gold/idol/prize delta detection calling `AddFloatingText` (Feature 3).

No sim, snapshot, codec, netcode, or fog files are touched.

## Testing strategy

- **Core:** `ParticleSystem` unit tests (per Feature 1). Target: current suite +
  the new particle tests, all green.
- **Renderers/detection:** presentation and client-only delta tracking, verified by
  running the game — mining kicks dust, footsteps puff, lava throws embers,
  explosions spray debris, motes drift, water glints drift, and picking up gold /
  idols / a prize floats a rising number over the local miner only.

## Tuning knobs

Named constants in `WorldRenderer` per emitter (rate, count, colour, velocity,
gravity, life) and for water (glint count, amplitude, speed, alpha); pool capacity
on the `ParticleSystem`; rise speed / life for floating numbers.
