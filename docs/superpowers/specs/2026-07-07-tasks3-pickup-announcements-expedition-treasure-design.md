# Design: Pickup Cap + Announcements + Expedition Treasure Gate

Date: 2026-07-07

## Task 3 — Block pickup when already maxed

### Behaviour
When a miner walks over a perm-buff item (SpeedPotion, BiggerBlast, LongerVision) and is already at the configured max for that stat, the item **stays on the floor**. A brief status-line message appears (shared with the Task 2 announcement system): "Maximum gain reached — Speed Tonic wasted!" or similar.

### Implementation
- **`Simulation.PickUpItems()`** — before `_items.RemoveAt(i)`, add a guard:
  - `SpeedPotion` + `m.PermSpeedLevel >= Config.MaxPermSpeedLevel` → skip, queue a `PickupBlocked` event
  - `BiggerBlast` + `m.PermBlastLevel >= Config.MaxPermBlastLevel` → same
  - `LongerVision` + `m.PermVisionLevel >= Config.MaxPermVisionLevel` → same
- Add `PickupBlocked(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent` to `SimEvent.cs`
- `Main.cs` consumes `PickupBlocked` for the local miner and sets the announcement text (see Task 2)

### Out of scope
- No change to the shop max-cap guard in `MatchHost.cs` (already correct)
- LifePotion is not capped (no max lives concept on pickup)

---

## Task 2 — Pickup announcements in the status line

### Behaviour
When the local miner picks up a consumable or buff item, or is blocked from picking one up, a short message flashes in the existing HUD status line for ~2.5 seconds, then reverts to normal status. Tutorial hints appear on first pickup of certain item kinds (plank, lantern, stones, etc.) using the same mechanism.

### Items and messages

| Event | Message |
|---|---|
| `ItemPickedUp` SpeedPotion | "Speed Tonic! Move faster." |
| `ItemPickedUp` BiggerBlast | "Bigger Blast! Larger explosion radius." |
| `ItemPickedUp` LongerVision | "Keen Eyes! See further." |
| `ItemPickedUp` LifePotion | "Life Restored!" |
| `PickupBlocked` any | "Already maxed out — \{item name\}!" |
| `ItemPickedUp` WaterPlank (first 3) | "Water Plank — place across deep water." |
| `ItemPickedUp` Lantern (first) | "Lantern — drop it to light the area." |
| First stone pickup | "Rocks — throw them to distract monsters." |

Tutorial hint tracking (first-pickup counters) lives in `Main.cs` as simple `HashSet<ItemKind>` or counters — not persisted across floors.

### Implementation
- **`Main.cs`** — add `string? _announcement` and `double _announcementExpiry`
- On each `DrainEvents()` pass, check for `ItemPickedUp` / `PickupBlocked` for local miner, set `_announcement` + expiry
- In the per-frame status string assembly: if `Time.GetTicksMsec() < _announcementExpiry`, prepend/replace with `_announcement`
- No new nodes or components needed

---

## Task 1 — Expedition treasure gate (every 4th floor)

### Behaviour
On every 4th expedition floor (4, 8, 12, 16…), a single mythical treasure (a random Idol) is hidden — either **buried in a wall** or **inside a sealed chest** (chosen by RNG 50/50). The escape gate does **not** open on gold percentage for these floors; it opens when the idol is picked up. On all other floors, the existing 50%-gold mechanic is unchanged.

**Compass behaviour on treasure floors:**
- During listen mode, the green exit compass needle points to the **treasure position** instead of the escape tile — until the treasure is found
- After pickup, the needle pivots to the escape tile as normal (escape is now open)

### SimConfig
- Add `bool HasExpeditionTreasure` — set to `true` by the floor generator on every 4th floor
- Add `GridPos? ExpeditionTreasurePos` — where the idol is placed (wall tile or chest tile)
- Add `ItemKind? ExpeditionTreasureKind` — which idol was chosen

### Simulation changes
- In the constructor: if `Config.HasExpeditionTreasure`, skip the gold-based `EscapeOpen` initialisation
- In `UpdateGoldCount()`: skip the `EscapeOpen = true` block if `Config.HasExpeditionTreasure`
- In `PickUpItems()` / `ApplyBuff()`: when an idol matching `Config.ExpeditionTreasureKind` is picked up, fire `EscapeOpened` (same event as today) and set `EscapeOpen = true`

### Map generation
- Every 4th floor: pick a random Idol kind from `TreasureAssignment.AllIdols`
- 50/50: place as a `BuriedItem` (wall tile, unburied when mined) or as a `SealedChest` containing the idol
- Pass the chosen kind + position into `SimConfig`

### Compass changes (`Compass.cs` / `ComputeExitAngle()`)
- `MatchClient` exposes `GridPos? ExpeditionTreasurePos` (null if floor has no treasure or treasure already found)
- `ComputeExitAngle()` is renamed / extended: if `_client.ExpeditionTreasurePos` is set, return angle toward treasure; else return angle toward `EscapeTile`
- When `EscapeOpened` event is received on a treasure floor, clear `ExpeditionTreasurePos` on the client — needle snaps to exit

### Out of scope
- No multi-treasure floors
- No compass needle colour change (same green needle, different target)
- The "every 4th" counter resets each Expedition run (floor 4, 8, 12…)

---

## Shared notes

- Suggested order: 3 → 2 → 1 (ascending complexity, each builds confidence for the next)
- Task 3 is purely sim-side: block pickup + emit `PickupBlocked` event. No display logic yet.
- Task 2 adds the display layer that consumes both `ItemPickedUp` and `PickupBlocked` — this is when the "max reached" message becomes visible.
- Task 1 is self-contained but benefits from the announcement plumbing being in place for the "treasure found" moment.
