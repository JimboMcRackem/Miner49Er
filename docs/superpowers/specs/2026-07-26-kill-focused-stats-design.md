# Kill-Focused Stats in Combat Modes — Design

**Date:** 2026-07-26

## Goal

In the three combat modes — Last Man Standing, Demolition Derby, Grudge Match —
the two stat surfaces that still lead with gold should instead lead with kills,
and drop gold. Kills already decide the winner in these modes; gold is
irrelevant there.

## Background

Per-player stats surface in four places. Two already lead with kills in combat
modes; two do not:

| Surface | Combat modes today | Change |
|---|---|---|
| HUD objective line (`Main.cs`) | ✅ leads with `Kills: N` | none |
| Tab scoreboard (`ScoreboardOverlay.cs`) | ✅ ranks by kills, "N kills" | reuse shared helper only |
| F3 stats overlay (`StatsOverlay.cs`) | ❌ shows only `{gold}g` | **lead with kills** |
| End-of-match results (`ResultsOverlay` via `Main.cs`) | ❌ no stat line at all | **add kill summary** |

All required data already exists: `MinerSnapshot.Kills` is captured and sent to
every client. No networking or wire-format changes.

## Scope

- **In:** LMS, Demolition Derby, Grudge Match — the existing "kill mode" set
  already used by `ScoreboardOverlay.cs:73`.
- **Out:** Gold Rush, Reach Center, Treasure Hunt, Treasure Heist, Expedition —
  their scoring is unchanged. HUD and Tab scoreboard — already lead with kills.

## Components

### 1. Shared helper (Core)

Add an extension so all surfaces agree on one definition of a kill-scored mode:

```csharp
public static class GameModeExtensions
{
    /// <summary>Modes where the winner is decided by rival kills, so kills —
    /// not gold — are the headline per-player stat.</summary>
    public static bool IsKillScored(this GameMode mode) =>
        mode is GameMode.LastManStanding
             or GameMode.DemolitionDerby
             or GameMode.GrudgeMatch;
}
```

Replace the inline `killMode` expression in `ScoreboardOverlay.cs:73` with
`mode.IsKillScored()` so there is a single source of truth. No behavior change
to the scoreboard — same three modes.

### 2. F3 stats overlay (`StatsOverlay.cs`)

In `BuildText()`, for a kill-scored mode:

- Sort the player rows by `Kills` descending (the list is currently unsorted).
- Render `{m.Kills} kills` in place of `{m.Gold}g`. Gold is dropped for these
  modes.

All other modes keep the current unsorted, `{gold}g` rendering.

### 3. End-of-match results (`Main.cs` `OnMatchEnded`)

The competitive (`else`) branch currently leaves `scoreText` empty, so the
results screen shows only "You Win" / "Winner: X". For a kill-scored mode,
build `scoreText` as a ranked kill summary:

```
Kills — Alice 4 · You 2 · Bob 1
```

- Ranked by kills descending.
- Local player labeled `You`; others by their lobby name via the existing
  `NameForMiner(int minerId)` helper (`Main.cs:992`), which already maps a
  miner snapshot's `Id` to its lobby name.
- Non-kill competitive modes keep the empty `scoreText` — no regression.

`ResultsOverlay.Show` already renders `scoreText`; no change to that file.

## Data flow

Both overlays read `_client.Miners` (a list of `MinerSnapshot`), which already
carries `Kills`, `Gold`, `Alive`, and `Id`. Mode comes from
`NetworkManager.Instance.MatchMode`. No new data crosses the wire.

## Testing

- **Core unit tests:** `IsKillScored` returns true for LMS/Derby/Grudge and
  false for every other `GameMode` value.
- **Overlays:** Godot `CanvasLayer` UI, not headless-testable in this project.
  Verified by a clean build of both projects and a play-test, consistent with
  how the rest of the UI is validated here.

## Out of scope / non-goals

- No change to how kills are counted or attributed (`DetonateAt` seam unchanged).
- No change to the HUD line or Tab scoreboard content (already kill-led).
- No change to gold-scored or treasure-scored modes.
