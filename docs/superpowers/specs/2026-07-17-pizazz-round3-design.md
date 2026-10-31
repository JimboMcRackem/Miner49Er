# Pizazz Round 3 Pack — Design

**Date:** 2026-07-17
**Status:** Approved (design), pending implementation plan
**Area:** Client-side presentation. NO simulation / snapshot / netcode / fog changes.

Seven independent visual-juice effects, reusing the `Miner49er.Core.Fx.ParticleSystem`
(already built + tested) and the `WorldRenderer` transient-effect pattern. No new Core
logic, so no new unit tests — each effect is verified by running the game.

## Global constraints

- Client-side only. No sim / `MinerSnapshot` / codec / netcode / `Fog` changes.
- Wall-clock, per-client (`Time.GetTicksMsec()` / frame `delta`); cosmetic.
- Particle effects go through the existing capped `ParticleSystem` (pool of 400) and are
  fog/viewport-culled; the pool cap keeps lava/flood floors cheap.
- All new tuning values are named constants.

---

## 1. Floor-transition wipe (directional)

`Main` gains a screen-fixed `CanvasLayer` (layer 25) holding a black `ColorRect`. A
`float _wipeTimer` (seconds remaining) drives it in `_PhysicsProcess`:

- `OnNewFloor(floor)` sets `_wipeTimer = WipeDuration` (~0.45 s).
- While `_wipeTimer > 0`: progress `= 1 - _wipeTimer / WipeDuration` (0→1); the black rect
  covers `x ∈ [progress * viewportWidth, viewportWidth]` — i.e. black recedes to the
  right, revealing the new floor left→right. Rect is full height.
- At `_wipeTimer <= 0`: rect hidden (zero width / `Visible = false`).

Reuses the existing `OnNewFloor` signal; the floor banner (layer 20) is unaffected — the
wipe sits above the world, and the banner still reads over it. Constant: `WipeDuration`.

## 2. Pickup sparkle burst

New `WorldRenderer.EmitPickupBurst(Vector2 pos, Color color)`: ~8 sparkles sprayed
upward-and-out from `pos`, short life, gravity, the given colour. Fired from the existing
**local** gold/idol pickup detection block in `MatchClient.ApplyUpdate` (the same place that
raises the floating number), so a pickup pops a matching-coloured sparkle over the local
miner. Constant: burst count/spread inside the method.

## 3. Monster death spark

In `MatchClient.ApplyUpdate`, **before** reassigning `_monsters` from the new snapshot,
diff old vs new: build a set of the new snapshot's alive monster ids, then for each monster
in the current `_monsters` that was `Alive` but is now missing-or-dead **and** visible
(`Fog.IsVisible` or spectating), call a new `WorldRenderer.EmitMonsterDeath(Vector2 pos,
MonsterKind kind)` — a white/kind-tinted spark burst at the monster's last tile. (Monsters
carry no per-hit HP, so this fires on death, which also makes kills read.)

## 4. Miner death poof

Same diff pattern for miners. `MatchClient` tracks previous alive state per miner id
(`Dictionary<int,bool> _prevMinerAlive`, cleared in `ResetFloor`). In `ApplyUpdate`, after
`_miners` is refreshed, for each miner that was alive last frame and is now dead **and**
visible, call `WorldRenderer.EmitMinerDeath(Vector2 pos)` — a grey dust/smoke poof. Fires
for local and rivals (deaths are dramatic). Update the map to the current alive states.

## 5. Dynamite fuse sparks

In `WorldRenderer.EmitParticleEffects` (the existing per-frame emitter), scan
`_client.Charges` (each is `ChargeSnapshot(OwnerId, X, Y, FuseRemaining)`). For each charge
with `FuseRemaining > 0` that is visible, with a small per-frame chance (`~dt * FuseSparkRate`)
emit one tiny orange spark just above the stick (slight upward velocity, very short life).
Constant: `FuseSparkRate`.

## 6. Water entry splash

In the existing footstep branch of `WorldRenderer.EmitParticleEffects` (fires on a miner's
tile change), check the miner's **new** tile type. If it is water (`ShallowWater`,
`DeepWater`, or a flooded tile), emit a blue-white droplet splash (a few particles with
upward spread + gravity) instead of the brown dust puff. Non-water tiles keep the dust puff.

## 7. Screen vignette

`Main` gains a static screen-fixed `CanvasLayer` (layer 8, below the HUD) with a full-screen
`TextureRect` (`LayoutPreset.FullRect`, `StretchMode.Scale`, `MouseFilter.Ignore`) whose
texture is a generated radial gradient: transparent at centre, dark (`VignetteColor`, low
alpha) at the corners. Built once in `_Ready` via a small radial-image helper. Constant:
`VignetteColor` / peak alpha. Subtle cave-mood edge darkening; no per-frame cost.

---

## Files

- **Modify:** `game/Main.cs` — floor-wipe `CanvasLayer` + `_wipeTimer` drive (1); vignette `CanvasLayer` + radial-texture builder (7).
- **Modify:** `game/WorldRenderer.cs` — `EmitPickupBurst` (2), `EmitMonsterDeath` (3), `EmitMinerDeath` (4), fuse-spark scan in `EmitParticleEffects` (5), water-splash branch in the footstep emitter (6).
- **Modify:** `game/net/MatchClient.cs` — pickup-burst call in the local pickup block (2); monster-death diff (3); miner-death diff + `_prevMinerAlive` tracking, reset in `ResetFloor` (4).

No sim / snapshot / codec / netcode / fog files touched. No Core changes.

## Testing

- No new pure logic ⇒ no new unit tests (the reused `ParticleSystem` is already covered).
- Regression: existing Core suite stays green.
- Verified by running: floors wipe in left→right; gold/idol pickups pop a coloured sparkle;
  killing a monster sparks; a dying miner poofs; lit fuses crackle; stepping into water
  splashes; the screen has a subtle dark-edged vignette.

## Tuning knobs

`WipeDuration` (1); pickup burst count/spread (2); monster spark count/colour (3); miner
poof count/colour (4); `FuseSparkRate` (5); splash count/colour (6); `VignetteColor` + peak
alpha (7).
