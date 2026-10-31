# Treasure Heist — Game Mode Design

**Status:** Approved design, ready for implementation planning.
**Date:** 2026-07-13
**Mode:** competitive multiplayer (2–8 players + bots), host-authoritative.

## 1. Summary

A new PvP game mode where a single **treasure** (a random urn/idol) is buried somewhere
near the centre of the mine. Every player's **compass** points to it. Players dig it out,
then **carry and hold** it. The winner is decided entirely by who controls the treasure —
either who is holding it when the clock runs out, or who banks the most cumulative hold
time, per a lobby toggle.

Combat is non-lethal by default: **throwing a stone** or **pickaxing an adjacent rival**
stuns them for 0.8s (both already implemented). A stunned or killed miner **drops
everything** — their held item and the treasure — so the treasure changes hands through
positioning and stuns, not just footspeed. Dynamite (already lethal) is the source of
actual deaths, gated by the respawn toggle.

## 2. Lobby options

The mode is selected from the existing mode picker as **"Treasure Heist"**
(`GameMode.TreasureHeist`). When selected, two mode-specific controls appear (alongside the
map-size / time controls, in the lobby's Advanced box):

- **Win by** — `Buzzer` (default) or `Most time`.
- **Respawns** — `Unlimited` (default) or `Death match`.

The match timer comes from the existing time-limit picker (a Treasure Heist match is always
timed; if "No Time Limit" is selected we default it to 5 minutes, mirroring how flooded
matches force a clock).

Both toggles are persisted in `SettingsStore` (lobby section) like the other options and
threaded to the sim as host-only config (the host runs the sim, so these need not travel in
the `BeginMatch` RPC — same pattern as `MatchPrizeEvents` / `MatchPrizeFrequency`).

## 3. Win conditions

The winner is **always** decided by the treasure. Kills never win on their own.

### 3.1 Win by = Buzzer
- When the match clock reaches 0:00, whoever is **carrying** the treasure wins.
- If the treasure is **loose** (on the ground) at 0:00, enter **sudden-death**: the clock
  holds at 0, and the first player to pick it up and hold it **uncontested for
  `SuddenDeathHoldSeconds` (3.0s)** wins. "Contested" = a rival is within
  `TreasureSneakRadius` tiles and alive; contest resets the sudden-death hold timer.

### 3.2 Win by = Most time
- Each player banks `HoldSeconds` while carrying (accrued in `Tick`).
- At 0:00 the highest `HoldSeconds` wins.
- Tie (or nobody has held it) → current holder wins; if loose, fall through to the same
  sudden-death rule as 3.1.

### 3.3 Respawns interaction
- **Unlimited respawns:** deaths never end the game or remove a contender; the win is purely
  3.1 / 3.2.
- **Death match:** dead players are eliminated (spectate). The treasure still decides the
  winner exactly as in 3.1 / 3.2. Being the last one alive is **not** an instant win. Edge
  case — if **all** players are eliminated (e.g. a chain of dynamite deaths), the winner is
  the highest cumulative `HoldSeconds`; if that is all zero, the match is a draw
  (`Winner = -1`).

The existing universal "last man standing" auto-win in `RoundResolver` is **suppressed** for
this mode — Treasure Heist never resolves on survivor count.

## 4. The treasure

- **Identity:** one shared treasure per match, a single idol/urn `ItemKind` chosen
  deterministically from the match seed (reusing the existing idol kinds; default pool can be
  the "urn"-like kinds). All peers derive the same kind.
- **Placement:** buried in a rock wall near map centre, reusing Treasure Hunt's buried-item
  placement (`MapConfig.BuriedIdolKinds` → `PlaceBuriedIdols`, but a single central kind).
  The buried tile is exposed via the compass from match start.
- **Find phase:** mining the treasure's tile unearths it (existing `ItemUnburied` reveal path)
  → it becomes a **loose** item on the floor. First unearth/pickup fires **"found it"**.
- **Carry:** stepping onto the loose treasure picks it up (modelled on Prize `CarryRelic`);
  the carrier becomes `_treasureHolderId`, and the treasure's rendered position tracks the
  carrier.
- **Drop:** on **stun** or **death**, the holder drops the treasure at their tile as a loose
  item, and `_treasureHolderId = -1`. Any subsequent pickup fires **"recovered it"**.

## 5. Combat, stun & drops

Existing (no change): `TryThrowStone` stuns a hit rival 0.8s; `TryStartMining` stuns an
adjacent rival 0.8s (2.0s `StunCooldown`); `StunRemaining` blocks all actions and decays in
`Tick`.

New behaviour, **gated to Treasure Heist only** (guarded by `SimConfig.TreasureHeistMode` so
other modes' stun/death behaviour is unchanged):

- **Drop-all on stun/death:** when a miner becomes stunned or dies, they drop:
  - their **held item** (the `Held` slot, if any) as a loose pickup on their tile, and
  - the **treasure**, if they were carrying it.
  Dropped items land on the miner's current tile (or nearest free floor tile if occupied).
- **Dizzy visual:** a stunned miner shows spinning "stars" (small circling glyphs) above
  their head for the stun duration — client-only render keyed off `StunRemaining`.

### 5.1 Stone economy
- Every miner starts the match with **9 stones** (`StoneCount` is already capped at 9;
  set the starting count for this mode at match start).
- **Refill from the ground:** thrown stones and stones dropped on stun/death leave a loose
  **Stone** pickup where they land, and the map generator scatters a light number of loose
  stones. Walking over a Stone pickup restocks toward the cap of 9. (Stone items and pickup
  already exist.)

## 6. Carrying the treasure

While `_treasureHolderId == a miner`:

- **Minimap ping:** the carrier's position blips on every player's minimap (reusing the
  prize-ping rendering in `MinimapOverlay`).
- **Slow:** the carrier's move cadence is multiplied by `TreasureCarrySlowFactor` (1.35),
  clamped by the existing `MinMoveSeconds`/`MaxMoveSeconds`, so a lone runner can be caught.
- **Compass lock:** the compass needle points to the treasure's **current** location — the
  loose tile when uncarried, or the carrier's tile when carried.

## 7. Announcements (toasts)

Fired from `SimEvent`s, surfaced via the existing announcement banner + death-feed
(`Main.cs` `SetAnnouncement` / `DeathFeed.PushMessage`):

| Toast | Trigger |
|-------|---------|
| **"X has found it!"** | First unearth/pickup of the match. |
| **"X recovered it!"** | Any pickup of a loose treasure after the first (i.e. after a drop). |
| **"X is sneaking away with it!"** | Carrier has held **continuously ≥ `TreasureSneakSeconds` (8.0s)** with **no rival within `TreasureSneakRadius` (6) tiles**; cooldowned (`TreasureSneakCooldown`, 10s) so it can't spam. |
| **"X dropped the treasure!"** | Holder is stunned or dies while carrying. |

Names resolve via the existing `NameForMiner` helper in `Main.cs`.

## 8. HUD

- **Compass:** an always-on needle in this mode pointing at the treasure (reusing the
  `Compass` `ArcCanvas` rose; extend it to render when the mode is Treasure Heist rather than
  only while listening).
- **Banner:**
  - Win-by Buzzer → "Held by: X" + the match clock (existing timer HUD).
  - Win-by Most time → a compact hold-time leaderboard (top holders + seconds), plus the
    local player's own banked seconds.

## 9. Bots (v1 — basic seeking)

`BotBrain` gains a Treasure-Heist intent so a bot-filled lobby actually contests the
objective:

- If the treasure is **loose or buried**, path toward it (dig it out if buried, on the tile).
- If a **rival is carrying** it, path toward the carrier and, when within stone range and
  facing, **throw a stone** at them (reusing the existing bot stone-throw capability where
  present, else pickaxe-stun when adjacent).
- If the **bot is carrying** it, path **away** from the nearest rival (toward open space /
  its spawn), leaning on the compass/holder position.

Bot skill tiers can scale reaction (e.g. only Miner+ bots throw at the holder), consistent
with existing bot-skill gating. This is intentionally simple greedy behaviour — no A*.

## 10. Architecture & reuse

Everything lives behind `GameMode.TreasureHeist`; other modes are unaffected.

### 10.1 Core (`Miner49er.Core`, unit-tested)
- **`Sim/GameMode.cs`:** add `TreasureHeist` (= 6).
- **`Sim/SimConfig.cs`:** `TreasureHeistMode` (bool), `TreasureWinByCumulative` (bool),
  `TreasureRespawnEnabled` (bool), `TreasureCarrySlowFactor` (1.35),
  `TreasureSneakSeconds` (8.0), `TreasureSneakRadius` (6), `TreasureSneakCooldown` (10.0),
  `SuddenDeathHoldSeconds` (3.0), `RespawnSeconds` (5.0), `StartingStones` (9).
- **`Sim/Simulation.cs`:** state — `_treasurePos` (GridPos), `_treasureHolderId` (int, -1),
  `_treasureFound` (bool), `_treasureUnearthed` (bool), `_holdSeconds`
  (`Dictionary<int,double>`), `_sneakTimer`, `_sneakCooldown`, `_suddenDeathHold`,
  per-miner `_respawnTimer`. New `AdvanceTreasureHeist(dt)` in `Tick` (after cooldowns):
  accrue holder seconds, pickup-on-move (via `TryMove`, like `CarryRelic`), sneak detection,
  sudden-death progress, respawns. Extend the stun/death paths to drop held item + treasure.
  Public accessors mirroring the Prize accessors (`TreasurePos`, `TreasureHolderId`,
  `TreasureFound`, `HoldSecondsOf(id)`). Test seams (`internal`) to force treasure
  state/positions.
- **`Map/MapConfig.cs` + generator:** bury the single treasure kind near centre.
- **`Sim/SimEvent.cs`:** `TreasureFound(int MinerId)`, `TreasureRecovered(int MinerId)`,
  `TreasureSneaking(int MinerId)`, `TreasureDropped(int MinerId)`. (`ItemUnburied` /
  `ItemPickedUp` already exist for the mechanical reveal/pickup.)
- **`Sim/RoundResolver.cs`:** Treasure Heist branch implementing §3; suppress the universal
  LMS auto-win for this mode.

### 10.2 Net
- **`Net/Snapshots.cs`:** `TreasureSnapshot(byte State, int X, int Y, int HolderId,
  float SuddenDeathProgress)` + a per-player `HoldTimeSnapshot(int MinerId, float Seconds)`
  list, both added as trailing optional `WorldSnapshot` params.
- **`Net/SnapshotCodec.cs`:** encode/decode symmetrically (guarded `HasValue` + list),
  following the Prize snapshot pattern.
- **`Net/SnapshotFactory.cs`:** populate from sim when mode is Treasure Heist.
- **`game/net/MatchHost.cs`:** map the new `SimEvent`s to one-tick transient snapshot fields
  (like `PrizeClaim`) so clients can fire toasts.

### 10.3 Godot presentation
- **`game/net/MatchClient.cs`:** expose treasure state + events (`TreasureFound`,
  `TreasureRecovered`, `TreasureSneaking`, `TreasureDropped`).
- **`game/WorldRenderer.cs`:** draw the loose/carried treasure (reuse item/idol textures +
  the prize carried-lift), and the **dizzy stars** over stunned miners.
- **`game/ui/MinimapOverlay.cs`:** ping the carrier (reuse prize-ping).
- **`game/ui/Compass.cs`:** point the needle at the treasure in this mode (always-on).
- **`game/Main.cs`:** build `SimConfig` for Treasure Heist (mode + win-by + respawn +
  starting stones); wire toasts + HUD (holder banner / hold-time leaderboard); set starting
  stones.
- **`game/net/NetworkManager.cs`:** host-only `MatchTreasureWinByCumulative` +
  `MatchTreasureRespawn`, set in `StartMatch`.
- **`game/ui/Lobby.cs`:** add the mode + the two mode-specific toggles (visible only when
  Treasure Heist is selected), persisted via `SettingsStore`.
- **`game/audio/SettingsStore.cs`:** persist `heist_winby` + `heist_respawn`.

### 10.4 Bots
- **`src/Miner49er.Core/AI/BotBrain.cs`:** Treasure-Heist intent per §9.

## 11. Testing

Core unit tests (xUnit), mirroring existing mode coverage:
- Treasure buries/unearths; first pickup fires `TreasureFound`; later pickup fires
  `TreasureRecovered`.
- Carry accrues `HoldSeconds`; stun drops treasure + held item; death drops likewise.
- Buzzer win: holder at expiry wins; loose-at-expiry → sudden-death; uncontested 3s wins;
  contest resets.
- Most-time win: highest cumulative wins; tie → holder; all-zero → draw.
- Death match: last-survivor is **not** an instant win; treasure still decides; all-eliminated
  → most cumulative time (or draw).
- Unlimited respawns: dead miner respawns after `RespawnSeconds` at spawn with no items.
- Sneak toast: fires only after ≥8s continuous hold with no rival within 6 tiles; respects
  cooldown.
- Stone refill: thrown/dropped stones become pickups; walking over restocks to ≤9.
- Snapshot codec round-trips `TreasureSnapshot` + hold-times.
- Bot intent: greedy seek toward treasure / carrier (deterministic, seed-driven).

## 12. Out of scope (v1)

- A* pathfinding for bots (greedy/line movement is sufficient, per existing convention).
- New bespoke art for the treasure (reuse existing idol/urn sprites); dizzy-star and any
  treasure-marker polish can get PixelLab passes later.
- Non-idol treasure types / multiple simultaneous treasures.
