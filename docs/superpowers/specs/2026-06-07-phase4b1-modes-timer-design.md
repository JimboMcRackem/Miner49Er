# Phase 4b-1 — Mode Framework, Win Modes & Match Timer

**Date:** 2026-06-07
**Status:** Design approved, ready for planning
**Sub-phase of:** Phase 4 (hazards & modes). 4b was split into **4b-1** (this doc — framework, win modes, timer) and **4b-2** (flood mode, designed later).

## 1. Goal

Introduce a **game-mode framework** so a match can run one of several win conditions, plus a **per-mode match timer** for time pressure. Ship two secondary modes (Gold Rush, Reach Center) on top of the existing universal last-man-standing base. This establishes the framework, timer primitive, and the `BeginMatch` mode seam that **4b-2's flood mode** will plug into as just another mode.

Non-goals (explicitly deferred to 4b-2): the flood driver, the `TileChange`→`TileType` netcode change, the under-occupant `DrownOccupants` kill path, and the `IsWater` predicate dedup.

## 2. Core concept — modes layer over a universal base

Last-man-standing is **universal**: in every mode, the round ends the instant ≤1 miner is alive, and the sole survivor wins (draw if zero alive). A mode adds an *additional* terminal condition layered on top.

| Mode | Round ends when… | Winner | Timed? |
|---|---|---|---|
| `LastManStanding` | ≤1 alive | sole survivor (else draw) | no |
| `ReachCenter` | a miner steps on the map center **or** ≤1 alive | first to reach center (else survivor) | no |
| `GoldRush` | the match timer expires **or** ≤1 alive | living miner with the most gold (draw on exact tie among the leaders) | **yes**, default 120 s |

`RoundResolver` is the **single mode-aware decision point**. The `Simulation` itself never branches on the mode — it only records neutral facts that the resolver interprets.

## 3. Core changes (`Miner49er.Core`, unit-tested)

### 3.1 `Sim/GameMode.cs` (new)
```csharp
namespace Miner49er.Core;

public enum GameMode { LastManStanding, GoldRush, ReachCenter }

public static class GameModeExtensions
{
    public const double GoldRushTimeLimitSeconds = 120.0;

    /// <summary>Per-mode time budget in seconds; null = untimed.</summary>
    public static double? TimeLimitSeconds(this GameMode mode) => mode switch
    {
        GameMode.GoldRush => GoldRushTimeLimitSeconds,
        _ => null,
    };
}
```
The mode→time-limit mapping lives here so both the host (to seed the timer) and tests have one source of truth.

### 3.2 `Simulation` — neutral facts, no mode branching
Add (constructed from match config / map, not from the mode enum):
- `GridPos? Center` — the map center tile (from `GeneratedMap.Center`); null when no center-based mode is in play (it's harmless to always set it).
- `int FirstToReachCenter` — default `-1`; set to the miner id the first time any miner's move lands exactly on `Center`.
- A time budget: `double? TimeLimitSeconds` (set at construction) and an accumulating `double Elapsed` advanced inside `Tick(double dt)`. Expose:
  - `double SecondsRemaining` → `TimeLimitSeconds is { } lim ? Math.Max(0, lim - Elapsed) : -1` (−1 sentinel = untimed).
  - `bool TimeExpired` → `TimeLimitSeconds is { } lim && Elapsed >= lim`.

`TryMove` gains, after a successful move and the existing lethal-tile drown check:
```csharp
if (Center is { } c && target == c && FirstToReachCenter < 0)
{
    FirstToReachCenter = id;
    _events.Add(new MinerReachedCenter(id));
}
```
(Recording the fact is unconditional — cheap, mode-agnostic. Only the resolver cares whether it matters.)

### 3.3 `Sim/SimEvent.cs` — new event
Add `public sealed record MinerReachedCenter(int MinerId) : SimEvent;` alongside the existing events. Lets audio/UI react later; harmless if unused in 4b-1.

### 3.4 `Sim/RoundResolver.cs` — mode-aware
```csharp
public static RoundResult Resolve(Simulation sim, GameMode mode)
{
    var alive = sim.Miners.Where(m => m.Alive).ToList();

    // Universal last-man-standing: applies in every mode.
    if (alive.Count <= 1)
        return new RoundResult(true, alive.Count == 1 ? alive[0].Id : -1);

    return mode switch
    {
        GameMode.ReachCenter when sim.FirstToReachCenter >= 0
            => new RoundResult(true, sim.FirstToReachCenter),
        GameMode.GoldRush when sim.TimeExpired
            => new RoundResult(true, MostGoldWinner(alive)),
        _ => new RoundResult(false, -1),
    };
}
```
`MostGoldWinner(alive)` returns the id of the unique living miner with the strictly-highest `GoldCollected`; if the top gold value is tied between two or more living miners, returns `-1` (draw). `GoldCollected` already exists on `Miner` and is already serialized in `MinerSnapshot` — no new gold plumbing.

> Note: for `ReachCenter`, the reached-center miner is, by construction, alive at the moment they step on center (they just moved there); resolution happens the same tick.

### 3.5 Folded-in 4a hardening
`MapGenerator.NearestFloorToCenter` currently calls `.First()` unguarded. Now that `Center` actually drives a win condition, guard it (e.g. fall back to the raw geometric center or throw a clear error if no floor exists) so a degenerate map can't crash mid-match. *(The `IsWater` predicate dedup stays deferred to 4b-2, where the flood needs it.)*

## 4. Netcode changes (minimal)

### 4.1 Mode threads through `BeginMatch`
- `NetworkManager.StartMatch()` reads the host's chosen mode and broadcasts it: `Rpc(nameof(BeginMatch), seed, order.Length, (int)mode, order)`.
- `BeginMatch(int seed, int playerCount, int mode, long[] peerOrder)` caches `MatchMode = (GameMode)mode` alongside the existing seed/playerCount.
- Clients receive the mode but don't resolve with it (resolution is host-only); they keep it only for any future mode-specific UI.

### 4.2 Timer rides the per-tick snapshot (+4 bytes)
- `WorldSnapshot` gains `float SecondsRemaining` (−1 = untimed).
- `SnapshotCodec.Write`/`Read` add one `float` after `Tick`.
- `SnapshotFactory.Capture` reads `sim.SecondsRemaining` into the snapshot.
- `SnapshotCodecTests` updated so the round-trip covers the new field.

`TileChange` is **untouched** in 4b-1 — extending it to carry a `TileType` is 4b-2's flood seam.

## 5. Host & UI

### 5.1 `MatchHost`
- Constructs the `Simulation` with the map `Center` and `mode.TimeLimitSeconds()` (read from `NetworkManager.Instance.MatchMode`).
- In `StepOnce()`, the resolver call becomes `RoundResolver.Resolve(_sim, NetworkManager.Instance.MatchMode)`. The timer is advanced inside `_sim.Tick()` (already called each step), so no separate countdown loop is needed in the host.
- Clients run **no** `Simulation`; they render the synced `SecondsRemaining` only.

### 5.2 `Lobby`
- Add a host-only mode `OptionButton` (entries: Last Man Standing / Gold Rush / Reach Center). The selected `GameMode` is read in `StartMatch()`. Non-host peers don't see/select it; the host's choice governs via the RPC.

### 5.3 `Hud`
- When `SecondsRemaining >= 0`, append `Time: {SecondsRemaining:0}s` to the existing HUD status line. When untimed (−1), show nothing extra.
- Results overlay and `BroadcastResult` are unchanged — the winner peer flows exactly as today.

## 6. Decomposition (≈5 tasks; the two Core tasks fan in parallel like 4a)

1. **Core: GameMode + resolver + sim facts + tests** — `GameMode` enum/extensions, `MinerReachedCenter` event, `Simulation` center/timer facts + `TryMove` hook, mode-aware `RoundResolver`, the `.First()` guard, and xUnit tests for every mode branch (incl. gold tie → draw, center-reach win, timeout win, universal LMS).
2. **Core/Net: timer sync** — `WorldSnapshot.SecondsRemaining`, codec read/write, `SnapshotFactory.Capture`, updated `SnapshotCodecTests`.
   *(Tasks 1 and 2 are independent → fan in parallel worktrees, then merge.)*
3. **Host wiring** — `MatchHost` constructs the sim with center + time limit and calls the mode-aware resolver.
4. **Lobby mode picker** — host-only `OptionButton`; `StartMatch`/`BeginMatch` RPC carries `int mode`; `NetworkManager.MatchMode`.
5. **HUD timer readout** — append `Time: {s}s` when timed.

## 7. Verification

- `Miner49er.Core` xUnit suite green (94 existing + new mode/timer tests).
- Build 0 errors; `godot --headless --quit-after N` exits 0.
- Play-test: pick each mode in the lobby; Gold Rush counts down and awards most-gold at timeout; Reach Center ends instantly when a miner reaches the center; Last Man Standing unchanged; timer HUD reads correctly on host and a client.
- Final opus code review before merge.

## 8. Carried forward to 4b-2 (flood)

Architected here, built next: flood driver (ring-by-ring inward, Floor→Shallow→Deep, preserving the deep-always-shallow-ringed invariant), the `TileChange`→`TileType` netcode change (fixes the hardcoded `Floor` at `MatchClient.cs:59`), the under-occupant `DrownOccupants` kill path (water rising under a standing miner — drowning currently only fires on move), and the public `TileTypeExtensions.IsWater()` dedup. Flood plugs in as a fourth `GameMode` reusing this timer.
