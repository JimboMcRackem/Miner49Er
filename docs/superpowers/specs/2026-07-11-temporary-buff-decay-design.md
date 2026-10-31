# Temporary (Decaying) Buff Pickups — Design

**Source:** ToDo/Tasks5..txt #3 — "Buffs are too frequent. Rather than keep them permanently, the value should be lowered between levels or they should weaken over time (e.g. sight opals wear out, explosive powder becomes wet). Maybe add a time decay."

**Goal:** The three floor buff pickups (`SpeedPotion`, `LongerVision`, `BiggerBlast`) become **temporary, time-decaying** boosts instead of permanent level-ups, killing the permanent snowball that made them feel "too frequent." Paid shop upgrades stay permanent.

## Decisions (locked with the user)

1. **Model:** time-decay temporary buffs for floor pickups.
2. **Scope split:** floor pickups → temporary; gold-bought **shop upgrades stay permanent** and still carry across Expedition floors.
3. **Feedback:** a pickup toast (reworded to signal it's temporary) + the implicit effect. No new persistent HUD.

## Architecture

The `StatusEffect` timed-effect system already exists and is engine-free/unit-tested. It already carries the exact channels needed (`MoveSpeed`, `VisionRadius`, `BlastRadius`) and the exact `EffectKind`s (`SpeedPotion`, `LongerVision`, `BiggerBlast`). `ApplyEffect` refreshes an existing effect of the same kind (never compounds); `AdvanceEffects` decrements `RemainingSeconds` and removes it at zero. The `Effective{MoveSeconds,VisionRadius,BlastBonus}` methods already **sum timed effects on top of permanent levels**. So the change is a rewire, not new machinery.

- **Floor pickup application** moves from "increment `PermXLevel`" to "apply a timed effect."
- **Shop upgrades** are untouched (`MatchHost.ReceiveBuy` → `SetPermLevels`), so permanent progression survives via the paid path. A temp buff stacks additively on top of a permanent shop level (vision/blast) or multiplicatively (speed), exactly as the `Effective*` methods already combine them.

## Components / changes

### Core

- **`ApplyBuff(minerId, kind)`** (`Simulation.cs`): the three cases call
  `ApplyEffect(minerId, kind, channel, magnitude, durationSeconds)` instead of bumping `PermXLevel`:
  - `LongerVision` → `ApplyEffect(id, EffectKind.LongerVision, EffectChannel.VisionRadius, BuffTuning.VisionMagnitude, BuffTuning.VisionSeconds)`
  - `BiggerBlast`  → `ApplyEffect(id, EffectKind.BiggerBlast, EffectChannel.BlastRadius, BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds)`
  - `SpeedPotion`  → `ApplyEffect(id, EffectKind.SpeedPotion, EffectChannel.MoveSpeed, BuffTuning.SpeedMagnitude, BuffTuning.SpeedSeconds)`
  - Chest loot rolling one of these three inherits the temporary behavior automatically (it calls `ApplyBuff`).

- **`IsPermBuffMaxed(m, kind)`** (`Simulation.cs`): drop the three buff cases so it returns `false` for them. A temporary buff is never "wasted" (a pickup always refreshes the timer), so the anti-waste skip must no longer block collecting one.

- **New `BuffTuning` constants** (small static class in `Miner49er.Core`, so the game/client can reference the same numbers for the toast text without duplicating literals). Durations are authored in **minutes**; second-valued accessors feed `ApplyEffect` (which takes seconds):
  - `VisionMagnitude = 3` (tiles), `VisionMinutes = 2.0` → `VisionSeconds => VisionMinutes * 60`
  - `BlastMagnitude = 1` (radius), `BlastMinutes = 2.0` → `BlastSeconds => BlastMinutes * 60`
  - `SpeedMagnitude = 0.80` (move-cadence multiplier; <1 = faster), `SpeedMinutes = 2.0` → `SpeedSeconds => SpeedMinutes * 60`

- **Stacking = refresh-only:** guaranteed by the existing `ApplyEffect` (updates the existing effect's magnitude + resets `RemainingSeconds`; never adds a second effect of the same kind). This is the anti-snowball mechanism.

### Game / client

- **`PickupMessage(kind, isBlocked)`** (`Main.cs`): reword the three buff messages to signal impermanence, reading the duration (in minutes) from `BuffTuning`, e.g. `"Sight opal — fades in 2 min"`, `"Explosive powder — 2 min"`, `"Speed tonic — 2 min"`.
- **No new networking.** Vision already flows to clients via `MinerSnapshot.VisionRadius` (host computes `EffectiveVisionRadius`, which includes the timed bonus). Blast and speed are host-side. The pickup toast is driven by the existing `ProcessPickupAnnouncements` (detects the item disappearing at the miner's tile).

## What explicitly does NOT change

- Shop upgrades remain permanent and persist across floors (`_permLevels`, `SavePermLevels`, `ExpeditionResume`).
- Spawn placement/counts in `PlaceItems` — decay addresses "too frequent"; spawns can be trimmed later if playtesting shows toast spam.
- Temp buffs are not carried across floors (a new floor builds a fresh `Simulation`; active effects are not restored). This is intended.
- The `SlowMold` debuff (the other `ApplyEffect` user) is untouched.

## Testing (Core, TDD)

1. **Vision pickup is temporary:** collecting `LongerVision` raises `EffectiveVisionRadius` by `VisionMagnitude`; after `VisionSeconds` (2 min) it returns to base.
2. **Refresh, not compound:** two `LongerVision` pickups yield the single magnitude (not doubled) and the timer is reset to full.
3. **Blast temporary:** `BiggerBlast` raises `EffectiveBlastBonus` by `BlastMagnitude`, expires after `BlastSeconds`.
4. **Speed temporary:** `SpeedPotion` lowers `EffectiveMoveSeconds` (faster) while active, reverts after `SpeedSeconds`.
5. **Stacks with shop perm level:** a permanent vision level (via `SetPermLevels`) plus a temp `LongerVision` sum additively.
6. **Never wasted:** `LongerVision` is collectible (not skipped by `IsPermBuffMaxed`) even when the permanent vision level is at max.

## Non-goals

- No persistent buff-timer HUD component.
- No change to shop pricing or the permanent-buff caps.
- No per-match configurability of buff durations beyond the `BuffTuning` defaults (values live in one place and are easily edited).
