# Dungeon Floors Implementation Design

## Goal

Add a 21-floor dungeon progression to Expedition mode. Floors 1–20 are procedurally generated with escalating difficulty; floor 21 is a hand-crafted boss floor with a stationary octopus whose arms sweep timed arcs. Grabbing the chest on the boss floor wins the run.

## Architecture

### In-run floor progression (single session)

The entire 21-floor run is one Expedition session — no scene changes, no lobby between floors. Floor number lives in `NetworkManager.MatchFloor` (int, starts at 1).

`RoundResolver` gains a third outcome alongside `Win` and `Loss`:

```csharp
public enum RoundOutcome { Ongoing, Win, Loss, FloorCleared }
```

`FloorCleared` is returned when the miner steps on the exit tile while `GoldCollected / StartingGoldCount >= 0.5` (regular floors) or when the miner picks up the chest (boss floor).

**On `FloorCleared` the host:**
1. Increments `MatchFloor`
2. Generates a new map from `FloorConfig(floor, seed)` (regular) or `GenerateBossFloor(seed)` (floor 21)
3. Resets the `Simulation` with the new grid, re-seeds items, re-spawns monsters
4. Broadcasts a `NewFloor` RPC with the new seed + floor number
5. Clients call `MatchClient.ResetFloor(newGrid, escapeTile, startingGoldCount)` — tears down old `TerrainMap`/`WorldRenderer`/`FogRenderer` nodes and re-inits them in place

The miner re-spawns empty-handed. The `Simulation` is fully replaced (no state leaks from prior floor).

---

## Difficulty Scaling — Floors 1–20

`FloorConfig(int floor, int seed) → MapConfig` is a pure function. No randomness in the difficulty curve — only map content varies by seed.

| Floors | Map size | mapScale | Hazards |
|--------|----------|----------|---------|
| 1–5 | 24×24 | 1 (Small) | None |
| 6–10 | 32×32 | 2 (Medium) | Pits |
| 11–15 | 40×40 | 3 (Large) | Pits + Cave-ins |
| 16–20 | 48×48 | 4 (Huge) | Pits + Cave-ins + Lava |

**Monster count:** `MonsterRoster.CountFor(width, height)` (area-based 3–5) plus a floor bonus:
- Floor ≥ 8: +1
- Floor ≥ 14: +1
- Hard cap: 7

Monster speed stays constant. Hazard and map-size escalation provide the difficulty ramp.

**Gold threshold:** 50% of starting gold count on every floor. Exit is always visible but locked (grey ladder glyph) until threshold is met.

---

## Boss Floor — Floor 21

### Map layout

`GenerateBossFloor(seed)` produces a fixed-structure map:
- **Deep water** fills the outer 60% of a 40×40 grid
- **Shallow water stepping-stone ring** forms a navigable path to the center
- **Small central island** (≈5×5 Floor tiles) surrounds the octopus
- **Octopus** placed at exact center
- **Chest** placed one tile south of center

No gold, no normal monsters. Win condition: step on chest tile.

### Octopus entity (Core)

New file: `src/Miner49er.Core/Sim/Octopus.cs`

```csharp
public class Octopus
{
    public GridPos Pos { get; }          // center, immovable
    public OctopusArm[] Arms { get; }   // 4 arms, 90° apart

    public void Advance(double dt) { /* tick each arm's angle */ }
    public IEnumerable<GridPos> DangerTiles() { /* Bresenham ray per arm */ }
}

public class OctopusArm
{
    public double RestAngle;       // degrees: 0=N, 90=E, 180=S, 270=W
    public double CurrentAngle;    // current sweep position
    public double ArcHalfWidth = 45.0;   // ±45° around rest
    public double AngularSpeed   = 30.0; // degrees per second
    public int    Length         = 5;    // tiles from center
    public int    SwingDir       = 1;    // +1 or -1
    public double PauseRemaining = 0.0;  // seconds left pausing at arc end
    public const double PauseSeconds = 1.0;
}
```

Each arm sweeps ±45° around its rest angle at 30°/sec, pauses 1 second at each end, then reverses — a full cycle takes ~4 seconds per arm. 4 arms are offset 90° so their sweeps interleave.

**Danger tile computation:** Bresenham line from `Octopus.Pos` in the direction of `CurrentAngle`, taking the first 5 in-bounds tiles. Any miner on a danger tile that tick dies (`DeathCause.Crushed`).

**Chest win:** `TryPickupItem` on a `Chest` tile (new `ItemKind.Chest`) triggers `RoundResult { IsOver=true, WinnerId=minerId }` instead of carrying an item.

### Snapshot plumbing

`WorldSnapshot` gains:
```csharp
public OctopusSnapshot? Octopus { get; init; }
// OctopusSnapshot: Pos, ArmAngles[], DangerTiles[]
```

`SnapshotFactory.Capture` and `SnapshotCodec` updated accordingly. `MatchClient` carries `OctopusSnapshot?`.

---

## HUD Changes

`Main._PhysicsProcess` formats the objective line differently per floor:

| Situation | HUD text |
|-----------|----------|
| Regular floor, exit locked | `"Floor 3/20    Gold: 42% (need 50%)    Ready"` |
| Regular floor, exit open | `"Floor 3/20    Gold: 51% ✓ — ESCAPE!"    Ready"` |
| Boss floor | `"BOSS FLOOR    Reach the chest!"` |

**Exit locked visual:** `WorldRenderer` draws the ladder glyph in grey (desaturated) when `!_client.EscapeOpen`. Once unlocked it pulses gold as before.

**Floor transition banner:** on `NewFloor` receipt, `Main` shows a full-width `Label` ("FLOOR 3" / "BOSS FLOOR") that fades in over 0.3s, holds for 1.5s, fades out over 0.3s. Drawn at ZIndex 20.

**Win screen:** `ResultsOverlay` gets a `"You conquered the dungeon!"` path for the boss-floor chest grab, distinct from the normal Expedition escape message.

---

## New Death Cause

`DeathCause.Crushed` — for octopus arm contact. Death message: `"YOU WERE CRUSHED BY THE OCTOPUS!"` in `DeathFeed`.

---

## Files Changed / Created

| File | Change |
|------|--------|
| `src/Miner49er.Core/Sim/Octopus.cs` | New — octopus + arm entity |
| `src/Miner49er.Core/Sim/OctopusArm.cs` | New — arm state |
| `src/Miner49er.Core/Sim/Simulation.cs` | Add `_octopus`, `AdvanceOctopus`, `Crushed` kill pass, chest pickup |
| `src/Miner49er.Core/Sim/DeathCause.cs` | Add `Crushed` |
| `src/Miner49er.Core/Sim/RoundResolver.cs` | Add `FloorCleared` outcome |
| `src/Miner49er.Core/Net/WorldSnapshot.cs` | Add `OctopusSnapshot?`, `StartingGoldCount` |
| `src/Miner49er.Core/Net/SnapshotFactory.cs` | Capture octopus state |
| `src/Miner49er.Core/Net/SnapshotCodec.cs` | Encode/decode octopus |
| `src/Miner49er.Core/Map/MapConfig.cs` | `FloorConfig()` static method |
| `src/Miner49er.Core/Map/MapGenerator.cs` | `GenerateBossFloor()` |
| `src/Miner49er.Core/Map/MonsterRoster.cs` | Floor-bonus overload |
| `src/Miner49er.Core/Map/Item.cs` | Add `ItemKind.Chest` |
| `game/net/NetworkManager.cs` | `MatchFloor`, `NewFloor` RPC |
| `game/net/MatchClient.cs` | `ResetFloor()`, carry `OctopusSnapshot?`, `StartingGoldCount` |
| `game/net/MatchHost.cs` | Handle `FloorCleared` — regenerate + broadcast |
| `game/Main.cs` | Floor banner, updated HUD, `ResultsOverlay` dungeon-win path |
| `game/WorldRenderer.cs` | Octopus body + arm sweep overlay + locked ladder tint + chest glyph |
| `game/ui/DeathFeed.cs` | `Crushed` message |
| `game/ui/ResultsOverlay.cs` | Dungeon win message |

---

## Testing

- `FloorConfig` returns valid `MapConfig` for all floors 1–20 (parameterised test)
- Octopus arm danger tiles never exceed arm length; always in-bounds
- `FloorCleared` fires when gold ≥ 50% and miner on exit; does not fire below threshold
- Boss floor chest pickup triggers `Win` not `FloorCleared`
- Monster count respects floor bonus and hard cap of 7
- `Simulation` reset on floor transition leaves no miner/monster/item state from prior floor
