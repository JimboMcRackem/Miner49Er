# Bots Contest Prizes + Derby Dynamite — Design

**Date:** 2026-07-24
**Status:** Approved (design phase)

## Problem

Two gaps left over from the "livelier multiplayer" work:

1. **Prize events** — bots have zero awareness of the global prize-event subsystem.
   When the host enables events, a prize spawns (telegraphed banner + on-map marker),
   and every bot simply keeps mining / hunting past it. Humans race for it; bots ignore
   it. Jarring, and it makes events feel single-player.
2. **Derby dynamite** — the throw-dynamite bot intent (`BotAction.ThrowDynamite` +
   `RivalInThrowLine`) is gated to Last Man Standing only. Demolition Derby is the other
   pure-combat mode and should get the same Dan bombardment.

Both live entirely in `Miner49er.Core.AI.BotBrain`. Reading prize state is free —
`Think(sim, mode)` already receives the full `Simulation`, and the host is
authoritative, so no networking or plumbing is required. Derby dynamite is a one-line
gate widening.

## Design

### 1. Prize-seeking goal (competitive modes, `PrizeState == Active`)

A new goal source, evaluated in `Think` **after** every safety/hazard flee block
(explosives, monsters, rockfall, trip mines) — bots never walk into a live fuse to grab
a coin — but **overriding** the mode's normal goal (gold / hunt / wander) once chosen.

Gated on `PrizeState == PrizeState.Active` and the mode being competitive
(`mode != Expedition`). Skill-scaled commitment range (Chebyshev from the bot to the
prize's effective target tile):

| Tier | Commit range |
|------|--------------|
| Greenhorn | ≤ 8 tiles |
| Miner | ≤ 14 tiles |
| Foreman / Dan | map-wide (`int.MaxValue`) |

Below range, the bot keeps its normal goal (it hasn't "noticed" the prize yet).

Per claim type, the goal tile:

| `PrizeType` | Goal tile | Notes |
|-------------|-----------|-------|
| GrabAndGo | `PrizePos` | Stepping onto it claims. |
| MineOut | `PrizePos` | Stand on it un-stunned to channel. |
| HoldPoint | `PrizePos` | `PrizePos` is inside the radius-3 ring. |
| CarryRelic | If `PrizeHolderId == MinerId` → `miner.SpawnPos`; else `PrizePos` | Bank at home, or go grab. |

The bot pathfinds to the goal tile with its normal `passRock` / `avoidHazards`
settings (Foreman+ tunnel through rock; Miner+ route around hazards). Standing still on
the tile once arrived (MineOut / HoldPoint) falls out naturally: the pathfinder returns
`dir == -1` at the goal, and the existing "arrived, idle" path holds position, which is
exactly what channeling / holding requires.

### 2. Contest aggression

A bot **adjacent to a rival who is actively claiming/holding the prize** swings its
pickaxe (0.8 s stun), which resets MineOut / HoldPoint progress and drops a CarryRelic.
The rival is "claiming" when `PrizeHolderId` is that rival's id, **or** (for GrabAndGo /
an ungrabbed contested tile) when a rival stands within the ring / on the tile.

- LMS and Derby already swing at *any* adjacent rival — unchanged, already covers this.
- The normally non-combat modes (GoldRush / ReachCenter / TreasureHunt) gain a *narrow*
  aggression: swing **only** at a rival that is the current `PrizeHolderId`, and **only**
  for Miner+ (Greenhorn stays oblivious). This mirrors the existing Treasure Heist
  carrier-chase aggression (`TreasureHolderId` swing). It does not make these modes
  generally combative — the bot only contests the specific miner banking the prize.

### 3. Derby dynamite

Widen the Dan bombard block gate from `lms` to `lms || derby`:

```csharp
if ((lms || derby) && Skill == BotSkill.DynamiteDan && sim.Config.DynamiteEnabled
    && miner.DynamiteThrowCooldown <= 0 && RivalInThrowLine(sim, miner))
    return new BotAction(-1, throwDynamite: true);
```

Dan-only, identical to LMS — consistent with the approved throw-dynamite scoping.

## Files

- **Modify:** `src/Miner49er.Core/AI/BotBrain.cs`
  - New helper `PrizeGoal(sim, miner)` → `GridPos?` returning the goal tile (or `null`
    when no active prize / out of commit range).
  - New helper `RivalIsPrizeClaimant(sim, pos, selfId)` → `bool` for the narrow contest
    swing.
  - Prize-goal override block after the hazard blocks, before the Treasure blocks (or
    folded next to them — same tier of "mode goal override").
  - Widen the Dan dynamite gate to `lms || derby`.
  - Extend the aggressive-swing predicate to include a Miner+ prize claimant in
    non-combat modes.
- **Modify:** `src/Miner49er.Core.Tests/AI/BotBrainTests.cs` — new tests.

No SimConfig, networking, Godot, or snapshot changes.

## Testing

Test seams already present: `ForcePrizeForTest(type, pos)`, `SetMinerPositionForTest`,
`SetFacingForTest`, `AddMiner`, `AddStones`.

New tests (GoldRush unless noted):
1. `Foreman_seeks_a_GrabAndGo_prize_across_the_map` — steps toward `PrizePos`.
2. `Miner_seeks_a_nearby_MineOut_prize` — within 14 tiles → routes to prize.
3. `Miner_ignores_a_distant_prize` — prize > 14 tiles away → keeps gold goal.
4. `Greenhorn_seeks_a_close_prize_only` — prize ≤ 8 → moves toward it.
5. `CarryRelic_holder_heads_home` — `PrizeHolderId == bot`, goal becomes `SpawnPos`.
6. `Miner_swings_at_a_rival_banking_the_prize_in_GoldRush` — adjacent claimant → `Mine`.
7. `Bot_does_not_swing_at_a_non_claimant_rival_in_GoldRush` — control: no `Mine`.
8. `DynamiteDan_throws_dynamite_at_a_rival_in_line_in_Derby` — Derby gate fires.

## Non-goals

- No pre-positioning during the 5 s Telegraph phase — bots react on Active.
- No new tunables, networking, or snapshot fields.
- No prize-marker rendering changes.
- Greenhorn never contest-swings in non-combat modes (stays a wanderer).
- Bots don't coordinate (no "you grab, I block") — each seeks independently.
