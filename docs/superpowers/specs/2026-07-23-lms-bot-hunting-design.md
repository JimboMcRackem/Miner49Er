# LMS Bot Hunting — Design

**Date:** 2026-07-23
**Goal:** In Last Man Standing, AI miners should actively hunt down and try to
eliminate rival miners (bot or human) instead of mining gold and ignoring each
other.

## Problem

LMS is won by being the last miner alive (`RoundResolver`: round ends when
`alive.Count <= 1`). Gold is irrelevant to the LMS win condition. Yet the
current bot AI (`BotBrain.cs`) makes bots mine gold and only rarely engage:

- Only **Foreman** and **Dynamite Dan** target rivals, and only when a rival is
  already within **6 tiles** (Chebyshev). Outside that leash they drift back to
  gold.
- **Miner** bots mine the nearest gold; **Greenhorn** bots wander randomly.
  Neither ever hunts.
- The only contact attack is a pickaxe **stun** (0.8 s), not a kill. Kills come
  from planting charges next to a rival (Foreman+), thrown stones (Dan only), or
  luring rivals into hazards. Bots never throw dynamite.

Net effect: in an LMS round the bots mostly dig and ignore each other.

## Design

All changes are gated on `mode == GameMode.LastManStanding`; every other mode
(Derby, Gold Rush, Treasure Hunt/Heist, Reach Center, Expedition) is untouched.

### 1. Targeting — tier-scaled pursuit (`PickGoal`, LMS branch)

Replace the "Foreman+ within 6 tiles" rule with pursuit of the nearest **living
rival**, scaled by skill:

| Tier      | Pursuit range         | Path through rock | Reval cadence |
|-----------|-----------------------|-------------------|---------------|
| Greenhorn | ≤ 8 tiles, else wander | no (routes around) | slow (30) — clumsy |
| Miner     | ≤ 12 tiles, else gold  | no                | 15 |
| Foreman   | whole map              | yes (digs toward)  | 7 |
| Dan       | whole map              | yes                | 3 |

Range is measured Chebyshev from the bot to the nearest living rival. When no
rival is in range, Greenhorn falls back to `RandomFloor` and Miner to its normal
gold goal, so lower tiers are not omniscient.

### 2. Don't flee what you're hunting (defensive flee block)

The "flee an adjacent rival" block currently exempts only `lms && Skill >=
Foreman`. Extend the exemption to **all tiers in LMS**: a hunter presses the
attack rather than backing off.

### 3. Engagement — tier-scaled weapons (final action, LMS)

As a hunter closes on a rival:

- **Pickaxe stun** — all tiers. When the bot steps into an adjacent rival it
  swings (stun) instead of moving. Extend the existing `aggressiveTowardRivals`
  flag from `Foreman+` to all LMS tiers.
- **Throw stone** (stun + close the gap) — **Miner+**, when a rival is within 2
  tiles and the bot has stones.
- **Plant charge** in an adjacent wall to catch a rival within 2 tiles —
  **Foreman+** (existing LMS plant path, unchanged).
- **Throw dynamite** at a rival within throw range in the facing direction —
  **Dan** (new). `TryThrowDynamite` needs no held item — it self-guards on
  `DynamiteEnabled` and the throw cooldown, so it is a no-op when the host chose
  a detonator-only explosive mode.

### 4. Plumbing for dynamite throws

`BotAction` has no dynamite-throw field, so bots can never fill the host's
existing `_pendingThrowDynamite` set. Add:

- `BotAction.ThrowDynamite` (bool, default false).
- One line in `MatchHost` bot loop: `if (action.ThrowDynamite)
  _pendingThrowDynamite.Add(minerId);`.

## Files

- `src/Miner49er.Core/AI/BotBrain.cs` — targeting + engagement.
- `src/Miner49er.Core/AI/BotAction.cs` — new `ThrowDynamite` field.
- `game/net/MatchHost.cs` — one wiring line in the bot action loop.
- `src/Miner49er.Core.Tests/AI/BotBrainTests.cs` — new LMS cases.

## Testing

Deterministic `Simulation` + `BotBrain.Think` tests (no Godot):

- Greenhorn in LMS with a rival ≤ 8 tiles heads toward that rival (goal /
  step direction reduces distance), not random wander.
- Greenhorn with the nearest rival beyond 8 tiles does **not** lock on (falls
  back to wander).
- Miner in LMS throws a stone when a rival is within 2 tiles and it holds stones.
- Miner in LMS with a rival ≤ 12 tiles pursues; beyond 12 it seeks gold.
- Foreman/Dan pursue a rival on the far side of the map (whole-map targeting).
- Dan in LMS facing a rival within throw range emits `ThrowDynamite`.
- A hunter does **not** flee an adjacent rival in LMS (contrast with a
  non-LMS mode where Miner+ still flees).

## Non-goals

- No changes to human controls or to any non-LMS mode.
- No new hazard-luring pathfinding (bots exploit hazards only incidentally).
- No change to the LMS win condition or scoring.
