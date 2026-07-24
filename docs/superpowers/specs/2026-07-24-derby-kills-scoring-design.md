# Demolition Derby "Most Kills" + On-Screen Kill Counter — Design

**Date:** 2026-07-24
**Status:** Approved (design phase)

## Problem

Demolition Derby is scored purely by last-man-standing (it isn't in the `RoundResolver`
switch, so it falls through to the universal rule). A **timed** Derby that reaches the
buzzer with 2+ miners still alive is therefore a flat draw — there's no way to crown the
most effective fighter. And the player has no on-screen feedback for how many rivals
they've eliminated.

## Decisions

- **What counts as a kill:** explosion kills only. You are credited when a blast you own
  — planted charge, thrown dynamite, trip mine, reel charge, or a chained detonation —
  kills a *rival* miner. Suicides don't count (`victim != owner`). Environmental deaths
  (pit / lava / rockfall / drowning) have no killer and don't count. Pickaxe and thrown
  stones only stun, so explosions are the entire miner-vs-miner kill set.
- **HUD scope:** show the local player's kill count in Demolition Derby **and** Last Man
  Standing (both PvP combat modes). Kills are tracked globally, so this is free once the
  field is networked.

## Design

### 1. Kill counter (Core)

Add `public int Kills { get; internal set; }` to `Miner`.

Credit at the single attribution seam in `Simulation.DetonateAt(wallPos, blastBonus,
ownerId)` — the miner-kill loop (`Simulation.cs:2101-2110`). When a miner `m` is killed
by the blast:

```csharp
if (m.Id != ownerId && _miners.ContainsKey(ownerId))
    _miners[ownerId].Kills++;
```

Every kill vector routes through `DetonateAt` with the correct `ownerId`:
- planted `Charge` → `charge.OwnerId`
- thrown dynamite → thrower id (lands as a `Charge`)
- `TripCharge` → `tc.OwnerId`
- `ReelCharge` → `rc.OwnerId`
- chained charges → `chained.OwnerId`

Credit is unconditional on the owner's alive state (a thrower who dies the same tick
still earns the kill; they're ineligible to *win* anyway if dead).

### 2. Resolver (Core)

`RoundResolver.Resolve`: last-man-standing is unchanged — the instant `alive.Count == 1`
that miner wins, before any mode-specific branch. Add one branch for timed Derby:

```csharp
GameMode.DemolitionDerby when sim.TimeExpired
    => MostKillsResult(alive),
```

placed in the mode switch (which is only reached with 2+ alive). `MostKillsResult`
mirrors `GoldRushResult`: the single alive miner with the most kills wins; a tie for the
lead is a draw (`RoundEndReason.Tie`). Only *alive* miners are eligible (a dead miner
lost by dying, regardless of their kill tally).

```csharp
private static RoundResult MostKillsResult(List<Miner> alive)
{
    int max = alive.Max(m => m.Kills);
    var leaders = alive.Where(m => m.Kills == max).ToList();
    return leaders.Count == 1 ? RoundResult.Win(leaders[0].Id)
                              : RoundResult.Loss(RoundEndReason.Tie);
}
```

Note: `max` can be 0 (nobody got a kill) — then every alive miner ties → draw, which is
the right outcome for a bloodless timed Derby.

### 3. Networking (Core)

Add `int Kills = 0` to `MinerSnapshot` (appended, default 0 for wire-compat with the
codec's positional layout). Populate from `m.Kills` in `SnapshotFactory`; encode/decode
in `SnapshotCodec` alongside the existing per-miner ints.

### 4. HUD (Godot)

`Main.cs` builds the Derby HUD at `_hud.SetHud(0, "Demolition Derby", ...)` (line ~445)
and the LMS HUD in the general branch. Show the local miner's kills:

- Derby: objective string → `$"Demolition Derby — Kills: {m.Kills}"`.
- LMS: append `— Kills: {m.Kills}` to that mode's objective string.

`m` is the local `MinerSnapshot`, so `m.Kills` is available once the field is networked.
No new MatchClient work beyond the codec decode.

## Files

- **Modify:** `src/Miner49er.Core/Sim/Miner.cs` — `Kills` field.
- **Modify:** `src/Miner49er.Core/Sim/Simulation.cs` — increment in `DetonateAt`.
- **Modify:** `src/Miner49er.Core/Sim/RoundResolver.cs` — Derby branch + `MostKillsResult`.
- **Modify:** `src/Miner49er.Core/Net/Snapshots.cs`, `SnapshotCodec.cs`, `SnapshotFactory.cs` — `Kills` field.
- **Modify:** `game/Main.cs` — Derby + LMS HUD strings.
- **Test:** `src/Miner49er.Core.Tests/` — new kill-attribution, resolver, and codec tests.

## Testing

New tests:
1. `Explosion_credits_a_kill_to_the_charge_owner` — plant/detonate near a rival → owner `Kills == 1`.
2. `A_self_kill_does_not_count` — owner caught in own blast → owner `Kills == 0`.
3. `An_environmental_death_credits_no_one` — rockfall/pit death → all miners `Kills == 0`.
4. `Timed_Derby_awards_the_win_to_the_most_kills` — 2 alive, distinct kill counts, `TimeExpired` → richer-in-kills wins.
5. `Timed_Derby_tied_on_kills_is_a_draw` — 2 alive, equal kills, `TimeExpired` → `Loss(Tie)`.
6. `Last_alive_still_wins_Derby_before_the_buzzer` — one alive → `Win`, kills irrelevant.
7. Codec round-trip carries `Kills`.

## Non-goals

- No kill feed / assist tracking / most-kills scoreboard column (results screen unchanged
  beyond the existing winner line).
- No hazard-shove or cart-crush attribution.
- No kill counter in non-combat modes (GoldRush/ReachCenter/Treasure/Expedition).
