# Phase 4b-1 Refinement — Lobby Time Limit & Reach-Center Map

**Date:** 2026-06-07
**Status:** Design approved, ready for planning
**Context:** Play-test feedback on the just-built 4b-1 (mode framework, win modes, match timer). Continues on the **existing `phase4b1-modes-timer` branch** (not yet merged) so the whole of 4b-1 + this refinement merges together after the next play-test.

## 1. Goal

Two play-test-driven refinements:
1. Make the **match time limit a host-selectable lobby option** (decoupled from the mode), instead of a per-mode hardcoded constant.
2. Make **Reach Center an actual challenge** by giving it a **larger, less-open map** (mode-specific), instead of the small open cavern shared by all modes.

## 2. Match timer becomes a lobby option

### 2.1 Lobby control
Add a second host-only `OptionButton` — **"Time limit"** — alongside the existing mode picker, with entries **None / 1 min / 2 min / 3 min / 5 min**, default **2 min**. Like the mode, the selection is the host's and rides the match-start RPC to all peers.

### 2.2 Duration is decoupled; timeout *meaning* stays per-mode
The selected duration applies to whatever mode is chosen. What happens *at* timeout remains mode-specific, which keeps the three modes distinct:

| Mode | Round ends when… | Winner |
|---|---|---|
| `LastManStanding` | ≤1 alive, **or** timer expires | survivor; **timeout → draw** |
| `ReachCenter` | a miner reaches center, **or** ≤1 alive, **or** timer expires | first-to-center / survivor; **timeout → draw** |
| `GoldRush` | timer expires, **or** ≤1 alive | survivor; **timeout → richest living miner (draw on exact tie)** |

Only **Gold Rush** scores on timeout. For the other modes the clock is a "no-infinite-game" deadline that ends in a draw if no mode-specific win occurs first. `None` = untimed (no clock; the round ends only by the mode's non-timer condition).

### 2.3 Resolver change (small)
`RoundResolver.Resolve(sim, mode)` keeps the universal last-man-standing check first, then the mode switch gains **one universal timeout arm** after the existing mode-specific arms:

```csharp
return mode switch
{
    GameMode.ReachCenter when sim.FirstToReachCenter >= 0
        => new RoundResult(true, sim.FirstToReachCenter),
    GameMode.GoldRush when sim.TimeExpired
        => new RoundResult(true, MostGoldWinner(alive)),
    _ when sim.TimeExpired
        => new RoundResult(true, -1),   // any timed mode whose clock ran out → draw
    _ => new RoundResult(false, -1),
};
```

Switch arms evaluate top-to-bottom, so Gold Rush's timeout→gold and Reach Center's reach→winner are decided before the universal timeout→draw fallback. This correctly yields: Gold Rush timeout → gold; Reach Center reached-in-time → winner; Reach Center timeout (no reach) → draw; LMS timeout → draw.

### 2.4 Duration now comes from the lobby, not the mode
`GameModeExtensions.TimeLimitSeconds()` (the per-mode 120s/null mapping) becomes **dead and is removed**, along with its unit test (`GoldRush_is_timed_others_are_not`). The `Simulation` still takes `timeLimitSeconds` exactly as today — only the *source* of that value changes (lobby selection rather than `mode.TimeLimitSeconds()`).

### 2.5 Netcode threading
- `NetworkManager` gains `public int MatchTimeLimitSeconds { get; private set; }` (seconds; `0` = none).
- `StartMatch(GameMode mode, int timeLimitSeconds)` and `BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, long[] peerOrder)` carry it (one extra `int`, placed after `mode`).
- The lobby's Start handler passes both dropdown values: `StartMatch((GameMode)_modePicker.GetSelectedId(), _timePicker.GetSelectedId())` — the time picker's item ids ARE the seconds (None=0, 1min=60, …).
- `Main.cs` host sim construction uses the synced value: `timeLimitSeconds: nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null`.

## 3. Reach Center gets a bigger, denser map (mode-aware)

### 3.1 A small Core factory centralizes the mode→map mapping
Add **`MapConfig.For(GameMode mode, int seed, int playerCount)`** — a static factory returning a `MapConfig` with mode-specific overrides:

```csharp
public static MapConfig For(GameMode mode, int seed, int playerCount)
{
    var cfg = new MapConfig { Seed = seed, PlayerCount = playerCount };
    if (mode == GameMode.ReachCenter)
    {
        cfg.BaseWidth = 40;
        cfg.BaseHeight = 40;
        cfg.InitialFloorChance = 0.42f;
    }
    return cfg;
}
```

- **Default (LMS, Gold Rush):** unchanged — `Base 24×24`, `InitialFloorChance 0.45`.
- **Reach Center (starting values, tunable in play-test):** `Base 40×40` (~2.8× area) and `InitialFloorChance 0.42` (less open floor).

`MapGenerator.Generate` stays mode-agnostic — it just consumes the knobs. The factory is the single place the mode affects the map.

### 3.2 Both grids use the factory (determinism)
`Main.cs` generates the map twice — once for the client render grid, once for the host's authoritative sim grid (intentionally separate `TileGrid` instances; see the carry-forward note in [[phase4-status]]). **Both** call sites switch to `MapConfig.For(nm.MatchMode, seed, playerCount)`, so the two grids remain byte-identical (same mode + seed + playerCount → same map).

### 3.3 Honest behavior note
The center and all spawns are always placed in the same connected walkable region, so a route to the center always **exists on foot**. Lower floor density makes that route **longer and mazier**, not un-walkable. Mining becomes an optional **shortcut** (dig toward center at the pickaxe rate vs. walking the long way) — an emergent choice, not a forced one. Density lengthens the journey; it does not wall the center off.

## 4. Decomposition (one cycle; ≈3 tasks)

1. **Core** — resolver universal timeout→draw arm; remove dead `GameModeExtensions.TimeLimitSeconds()` + its test; add `MapConfig.For`; xUnit tests for the new resolver arm (LMS timeout → draw, Reach Center timeout → draw, Gold Rush timeout → gold still works) and for `MapConfig.For` (Reach Center overrides vs. default passthrough).
2. **Netcode + lobby** — `MatchTimeLimitSeconds` threading through `StartMatch`/`BeginMatch`; the host-only "Time limit" `OptionButton` (None/1/2/3/5 min, default 2 min).
3. **Main wiring** — both map generations use `MapConfig.For(mode, …)`; host sim time limit reads `nm.MatchTimeLimitSeconds`.

## 5. Verification

- `Miner49er.Core` xUnit suite green (108 prior − 1 removed timing test + new resolver/`MapConfig.For` tests).
- `dotnet build Miner49er.csproj` 0 errors; `godot --headless --quit-after 180` exits 0, no error lines.
- Play-test (user): lobby shows mode + time-limit dropdowns; Gold Rush honors the chosen duration and scores most-gold at 0; LMS/Reach Center with a clock end in a draw at 0; None = untimed; Reach Center map is noticeably larger and the run to center is a real journey (tune 40/0.42 if needed).
- Final opus review before merging the whole `phase4b1-modes-timer` branch to main.

## 6. Untouched / still deferred to 4b-2

The Reach-Center alive-recheck trap and the host/client separate-`TileGrid` invariant (both recorded in [[phase4-status]]), plus the `TileChange`→`TileType` seam, `DrownOccupants`, and `IsWater()` dedup — all remain 4b-2 work.
