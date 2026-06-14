# Phase 4d-3: Lava — Design

**Status:** Approved 2026-06-13
**Phase:** 4d-3 (third hazard in the 4d family, after bottomless-pit 4d-1 and cave-ins 4d-2)
**Toggle:** Host lobby option, OFF by default (same pattern as Pits / Cave-ins / Flooding)

## 1. Summary

Lava is a heat hazard with two flavors that share one lethal tile family:

- **Static lava** — inert pools/lines placed at map-gen. Lethal to step on, never move. Reshape the map like pits.
- **Vents** — dormant lava sources buried in rock. When a miner *breaches* them (mines or blasts the rock orthogonally touching the vent), the vent wakes and **creeps outward one ring at a time**, up to a bounded tile budget. This makes careless digging a punished-greed moment: blast into a vent and you release a slow, visible, lethal flow.

The signature interaction is **quench**: when an advancing lava front reaches a floor tile adjacent to water, it solidifies into **Cracked** "cave-in" crust instead of lava — walkable but fragile. Water is therefore lava's dedicated counter (lead water toward an advancing vent to freeze it into crossable-but-fragile ground). Planks do **not** bridge lava (they burn); water is the answer.

## 2. Locked decisions (from brainstorming)

1. **Two forms:** static pools/lines AND breachable vents. Both gated by the single `Lava` toggle.
2. **Vent trigger = adjacent rock removed.** A vent is dormant until any orthogonally-adjacent rock is mined or blasted away; then it begins flowing into newly-opened floor.
3. **Spread = one ring per interval, fixed tile budget.** Once active, a vent advances one BFS ring into eligible floor every `LavaSpreadIntervalSeconds`, halting permanently once it has spent its `LavaVentBudget`.
4. **Quench rule = lava front solidifies to Cracked.** When lava tries to spread into a floor tile that touches water, that tile becomes `Cracked` crust instead of lava and stops spreading. The water is unchanged.
5. **Planks burn — water is the answer.** Lava is NOT bridgeable. A plank cannot be laid on lava.
6. **No warning ring.** Lava renders as lava once in vision; the ~1.5s-per-ring crawl is itself the warning. No extra "hot" tile state.

## 3. Tile model (Core: `Grid/TileType.cs`)

Add two tile types:

- **`Lava`** — flowing or static molten rock.
- **`LavaVent`** — a dormant/active spread source, buried in rock at gen. Shares Lava's lethal flags; it is the BFS origin.

Tile-flag table for the new types:

| Flag | `Lava` | `LavaVent` | Rationale |
|---|---|---|---|
| `IsWalkable` | no | no | both lethal |
| `IsEnterable` | yes | yes | enterable + lethal, like Pit / DeepWater |
| `IsLethal` | yes | yes | step on = death |
| `IsMinable` | no | no | you mine the rock *next to* a vent, never the vent |
| `IsBlastable` | no | no | a charge cannot destroy lava |
| `BlocksSight` | no | no | ground-level glow, like water / pit |
| `IsBridgeable` | no | no | planks burn |
| `MoveCostMultiplier` | 1.0 | 1.0 | irrelevant (lethal) |
| `IsWater` | no | no | — |

`IsTraversable` in `MapGenerator` (Floor/ShallowWater only) already excludes both, so lava is correctly omitted from the traversable region exactly like pits.

## 4. Death cause & events (Core: `Sim/DeathCause.cs`, `Sim/SimEvent.cs`)

- `DeathCause` gains **`Burned`**, appended at the end: `{ None, Drowned, Exploded, Left, Fell, Crushed, Burned }` → byte value 6. Appending keeps every existing codec index stable.
- New events:
  - `MinerBurned(int MinerId)` — death event.
  - `LavaSpread(GridPos Pos)` — a tile became lava (host → `TileChange(Lava)`).
  - `LavaQuenched(GridPos Pos)` — a lava front solidified to crust (host → `TileChange(Cracked)`).

`KillByTile` gains a lava branch:

```
var t = Grid.Get(m.Pos);
if (t == TileType.Pit)                              -> Fell
else if (t is TileType.Lava or TileType.LavaVent)  -> Burned
else                                               -> Drowned
```

## 5. Vent state & spread (Core: `Sim/Simulation.cs`)

### State
The `Simulation` owns a private `List<LavaVent>` (host-only). On construction it scans the grid once for `TileType.LavaVent` and seeds a dormant entry per vent:

```csharp
internal sealed class LavaVent
{
    public GridPos Pos;
    public bool Active;
    public int Budget;              // remaining tiles it may still convert
    public double Timer;            // accumulates dt toward the next ring
    public readonly List<GridPos> Frontier = new();  // current spread edge
}
```

`Budget` is seeded from `Config.LavaVentBudget`. If the grid has no vents (toggle off), the list is empty and `AdvanceLava` is a no-op.

### Activation
A helper `ActivateVentsAround(GridPos pos)` is called wherever a rock tile becomes Floor:
- in `CompleteActivity` after a mine resolves to Floor,
- in `Detonate` for each blasted rock tile.

It activates any dormant vent orthogonally adjacent to `pos`, setting `Active = true` and seeding `Frontier = { vent.Pos }`.

### Spread (`AdvanceLava(dt)`, called in `Tick` after `AdvanceCracks`)
For each active vent with `Budget > 0`:
1. `Timer += dt`; if `Timer < Config.LavaSpreadIntervalSeconds`, continue.
2. `Timer -= Config.LavaSpreadIntervalSeconds`.
3. Gather the deduplicated set of orthogonal **Floor** neighbors of every frontier tile (Card order, deterministic).
4. For each candidate while `Budget > 0`:
   - if the candidate is orthogonally adjacent to any water tile → `Set(candidate, Cracked)`, emit `LavaQuenched`, `Budget--`, do **not** add to the new frontier;
   - else → `Set(candidate, Lava)`, emit `LavaSpread`, `Budget--`, add to the new frontier.
5. Replace the vent's frontier with the newly-laid lava tiles (next ring expands from these). Quenched crust is excluded, so lava halts at water.

After all vents advance, run the occupant sweep (below) so a miner the flow reached this tick dies.

### Occupant kill
Generalize the existing `DrownOccupants` → **`KillOccupantsOnLethalTiles`** (kills any living miner on an `IsLethal` tile via `KillByTile`, which now assigns `Burned` for lava). Call it from both `AdvanceFlood` (unchanged behavior — flood only makes deep water) and after `AdvanceLava`.

### Reused cave-in crust (no new code)
The quench produces `TileType.Cracked`, which the existing cave-in logic already handles: walk-once-safe, collapse on dwell/re-cross (`AdvanceCracks` / `TryMove`), plank-bridgeable. That logic keys on the tile type, **not** on the cave-ins toggle, so a lava-made crust is fragile even when the cave-ins lobby option is off.

## 6. Map generation (Core: `Map/MapGenerator.cs`, `Map/MapConfig.cs`)

Gated by `MapConfig.Lava` (default `false`). Two passes at two points in `Generate`:

- **`PlaceLava` — static pools/lines.** Runs *after* `PlacePits` and *before* the `LargestTraversableRegion` recompute (line 23), so lava (impassable) reshapes the map like pits and reachability is preserved structurally. Carves `Lava` clusters over Floor, biased to small blobs, **skipping any tile adjacent to water** (so gen-time lava never sits on a quench boundary — quench is a runtime concept). Cluster grower mirrors `GrowPit`.
- **`PlaceLavaVents` — buried sources.** Runs *after* the objective passes (alongside `PlaceCracks`), placing `LavaVent` in **rim rock** (`g.Get(p) == Rock && HasRegionNeighbor(p, region)`), so vents are breachable by mining the play-area edge and never affect the traversable region (they're in rock).

New `MapConfig` knobs:

```csharp
public bool Lava { get; set; } = false;            // gates both lava passes
public int LavaPoolCount { get; set; } = 3;         // static pools/lines
public int LavaPoolMax { get; set; } = 6;           // max tiles in a grown pool
public double LavaPoolGrowChance { get; set; } = 0.6;
public int LavaVentCount { get; set; } = 3;         // buried vents (light per-player scaling)
```

`For(...)` signature gains `bool lava = false`, assigning `Lava = lava`. Per-player scaling: `LavaVentCount + (PlayerCount - 1)`, matching the pits/cracks pattern.

New `SimConfig` knobs:

```csharp
public double LavaSpreadIntervalSeconds { get; set; } = 1.5;  // seconds per spread ring
public int LavaVentBudget { get; set; } = 8;                   // max tiles a single vent converts
```

## 7. Sync (host-authoritative)

Identical model to cave-ins / flood. Vent state lives only on the host; spread reaches clients purely as per-tick tile deltas. `MatchHost` (game/) translates:

- `LavaSpread cs → TileChange(cs.Pos.X, cs.Pos.Y, false, TileType.Lava)`
- `LavaQuenched cq → TileChange(cq.Pos.X, cq.Pos.Y, false, TileType.Cracked)`

Static lava and vents regenerate deterministically on every peer from `(seed, MapConfig.Lava)`. `Burned` rides the existing `MinerSnapshot.Cause` byte; the codec needs no schema change beyond the appended enum value. Clients never run vent state — they render `LavaVent` tiles from regen and overwrite them with `TileChange`s as the host's flow advances.

## 8. Adapter (game/ — TAB indent)

- **`WorldRenderer.cs`:** `LavaColor` (molten orange, e.g. `"d2521a"`), `LavaVentColor` (brighter, e.g. `"ff7a2a"`); add `TileType.Lava` and `TileType.LavaVent` arms to `TargetColor`.
- **`SfxLibrary.cs`:** `Sizzle` SFX for a burn death (noise burst).
- **`MatchAudio.cs`:** `DeathCause.Burned => SfxLibrary.Sizzle` arm.
- **`DeathFeed.cs`:** `DeathCause.Burned => "BURNED ALIVE!"` (banner) and `DeathCause.Burned => $"{name} was incinerated"` (toast).
- **`MatchHost.cs`:** the two `TileChange` cases above.
- **`NetworkManager.cs`:** `MatchLava` property; `StartMatch` / `BeginMatch` gain `bool lava`, threaded through the Rpc and the local call; `MatchLava = lava;` in `BeginMatch`.
- **`Lobby.cs`:** host-only `_lavaCheck` CheckBox "Lava" after `_caveInCheck`; pass `_lavaCheck.ButtonPressed` to `StartMatch`.
- **`Main.cs`:** both `MapConfig.For(...)` calls gain `, nm.MatchLava`.

## 9. Testing

**Core (TDD):**
- `TileType` flags for `Lava` / `LavaVent` (lethal, enterable, not walkable, not bridgeable, not minable, not blastable).
- Sim:
  - vent activates when adjacent rock is mined,
  - vent activates when a blast clears adjacent rock,
  - active vent spreads exactly one ring per `LavaSpreadIntervalSeconds`,
  - spread halts after `LavaVentBudget` tiles,
  - quench: a front adjacent to water becomes `Cracked`, not `Lava`, and lava stops,
  - a miner standing where lava spreads dies `Burned`,
  - a quenched crust still collapses on dwell **with `CaveIns` off** (tile-driven).
- Map-gen (theory):
  - `Lava=false` → no `Lava`/`LavaVent` tiles,
  - `Lava=true` → both static lava and vents present,
  - determinism (same seed ⇒ identical grids),
  - static lava never water-adjacent,
  - vents only in rock,
  - traversable region stays connected.

**Codec:** extend the death-cause round-trip with a `Burned` miner.

## 10. Out of scope / non-goals

- No proximity/heat-aura damage (lava kills only on contact; the dwell danger lives in the quenched crust).
- No host-side ambient lava SFX (death SFX only, to keep host/client audio symmetric like cave-ins).
- No plank-burns-over-time or lava-cooling-back-to-rock mechanics.
- Lava does not consume or interact with items, charges, or molds beyond blocking spread on non-Floor tiles.
