# Grudge Match Mode — Design

**Date:** 2026-07-24
**Status:** Approved (design phase)

## Problem

Add a free-for-all deathmatch mode: an open arena, infinite respawns, a fixed clock, and
the player with the most kills at the buzzer wins. Builds on the shared kill counter from
[2026-07-24-derby-kills-scoring-design.md](2026-07-24-derby-kills-scoring-design.md).

## Decisions (confirmed)

- **Free-for-all**, everyone-against-everyone. Individual scoring.
- **Arena:** open with scattered rock cover (walls to plant charges in, cover to dodge) —
  not a barren plain.
- **Hazards/monsters:** stay on. Environmental deaths credit no one.
- **Buzzer tie:** draw (`RoundEndReason.Tie`), consistent with Gold Rush / timed Derby.
- **Derby:** becomes optionally timed so "most kills" is reachable there too (default stays
  untimed = last-man-standing).

## Design

### 1. Mode enum (Core)

`GameMode` gains `GrudgeMatch` (appended: `..., DemolitionDerby, TreasureHeist,
GrudgeMatch`). Appending keeps existing serialized ordinals stable.

### 2. Kill counter (Core) — shared

Per the derby-kills spec: `Miner.Kills`, incremented in `DetonateAt` when a rival dies to
your blast; carried in `MinerSnapshot`. Kills persist across respawns (the respawn reset
in `AdvanceRespawns` does not touch `Kills`).

### 3. Respawns (Core)

`Simulation.RespawnEnabled` currently returns `Config.TreasureRespawnEnabled`. Change to:

```csharp
public bool RespawnEnabled => Config.TreasureRespawnEnabled || Config.Mode == GameMode.GrudgeMatch;
```

Gate the respawn pump on it: `AdvanceRespawns` is currently called under
`if (Config.TreasureRespawnEnabled)` — change to `if (RespawnEnabled)`. `AdvanceRespawns`
already revives at `SafeSpawn(m.SpawnPos)` after `Config.RespawnSeconds` (5s), clearing
death cause / stun / held / effects and restoring starting stones. No change needed there.

### 4. Arena map (Core)

`MapConfig.For` gains a `GrudgeMatch` branch mirroring Derby's item stripping (no gold,
no items/planks/molds/lanterns/chests/decoys) plus an openness bump:

```csharp
if (mode == GameMode.GrudgeMatch)
{
    cfg.GoldVeinCount = 0; cfg.BaseItemCount = 0; cfg.VisibleItemCount = 0;
    cfg.WaterPlankCount = 0; cfg.SlowMoldCount = 0; cfg.LanternCount = 0;
    cfg.ChestCount = 0; cfg.DecoyCount = 0;
    cfg.InitialFloorChance = 0.60f;   // open arena with scattered rock cover
    cfg.StonePileCount = 10;          // throwables so non-Dan players can contest
}
```

`InitialFloorChance = 0.60` (vs base 0.45) with the existing 4 smoothing steps yields
large open floor broken by rock clumps. Stone piles kept generous so every tier has a
kill/stun tool, not just Dan's dynamite.

### 5. Resolver (Core)

In `RoundResolver.Resolve`, handle Grudge **before** the universal last-man-standing block
(respawns make "all dead" a transient state), mirroring the Heist early return:

```csharp
if (mode == GameMode.GrudgeMatch)
    return sim.TimeExpired ? MostKillsResult(sim.Miners.ToList())
                           : RoundResult.Ongoing();
```

`MostKillsResult` (shared with timed Derby) compares `Kills` across the given miners; sole
leader wins, tie → `Loss(Tie)`. For Grudge it is passed *all* miners (dead-at-buzzer is
irrelevant — the counter is cumulative). `max == 0` (nobody killed) → everyone ties →
draw.

Timed Derby branch (from the derby spec) uses `MostKillsResult(alive)` — alive-only,
because Derby has no respawns and a dead miner truly lost.

### 6. Bots (Core)

In `BotBrain.Think`, wherever `derby` currently drives aggression, include Grudge. Add a
local `bool grudge = mode == GameMode.GrudgeMatch;` and treat `derby || grudge`
identically for: aggressive pickaxe swing, stone throwing (Miner+), and Dan's dynamite
bombardment gate (currently `lms || derby`). Also route the goal: `PickGoal` should send
Grudge bots to the nearest rival (reuse `DerbyGoal`).

### 7. Lobby (Godot)

- `_modePicker.AddItem("Grudge Match", (int)GameMode.GrudgeMatch)` inserted alphabetically
  between "Gold Rush" and "Last Man Standing".
- `ModeName` / `ModeDescription` entries for Grudge.
- Always-timed enforcement: extend the Heist rule to Grudge —
  `bool timed = heist || grudge; _timePicker.SetItemDisabled(0, timed); if (timed && sel == 0) bump`.
- `normalMode` (which gates the time + explosive pickers visible) must include Grudge so
  the time picker shows; but Grudge forces Dynamite explosive like Derby. Simplest: add
  `bool grudge = ...;` treat Grudge as time-visible but explosive-forced.
  - Time picker visible: `isHost && (normalMode || grudge)`.
  - `timeLimit` at start: remove the forced-0 for Grudge (it must carry the clock); also
    **remove `derby`** from the forced-0 so Derby can be timed.
  - `explosive` forced to Dynamite (0) for `derby || grudge` (unchanged for derby; add grudge).
- Prize events already show for all `!expedition` — Grudge included automatically.

### 8. HUD (Godot)

`Main.cs`: show `Kills: {m.Kills}` for the local miner in Derby, LMS, and Grudge. Grudge
gets its own HUD branch (objective `"Grudge Match — Kills: N"` + timer + respawn hint when
the local miner is dead/awaiting respawn).

## Files

- Core: `Sim/GameMode.cs`, `Sim/Miner.cs`, `Sim/Simulation.cs` (RespawnEnabled, gate,
  DetonateAt increment), `Sim/RoundResolver.cs`, `Map/MapConfig.cs`, `AI/BotBrain.cs`,
  `Net/Snapshots.cs`, `Net/SnapshotCodec.cs`, `Net/SnapshotFactory.cs`.
- Godot: `game/ui/Lobby.cs`, `game/Main.cs`.
- Tests: resolver (Grudge + timed Derby), kill attribution, codec round-trip, map openness,
  bot aggression in Grudge.

## Testing

1. Kill attribution: blast credits owner; self-kill and environmental death credit no one.
2. `Timed_Grudge_awards_the_win_to_the_most_kills`; `..._tie_is_a_draw`;
   `Grudge_is_not_over_before_the_buzzer` (even with all miners momentarily dead).
3. `Timed_Derby_awards_win_to_most_kills` / `_tie_draws`; untimed Derby still LMS.
4. `RespawnEnabled_is_true_in_Grudge`; a killed Grudge miner revives after RespawnSeconds.
5. `Grudge_map_has_no_gold` and is open (floor fraction above a threshold).
6. Bots: `Grudge_bot_hunts_and_swings_at_a_rival`; `Grudge_Dan_throws_dynamite_in_line`.
7. Codec round-trip carries `Kills`.

## Non-goals

- No teams, no per-kill scoreboard column beyond the winner line, no kill feed.
- No hazard-shove/cart-crush kill attribution.
- No dedicated Grudge respawn/time lobby toggles beyond always-timed (respawns implied by
  mode).
