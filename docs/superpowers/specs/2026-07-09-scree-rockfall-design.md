# Scree / Rockfall Hazard — Design Spec

**Date:** 2026-07-09  
**Status:** Approved

---

## Overview

Add three tiers of unstable rock to the game that, when mined or blasted, may trigger a rockslide. Rockslides fill nearby floor tiles with Rock and crush any miners caught in the collapse zone. The hazard is detectable only in Listen mode, where each tier shows a distinct colour. Frequency is sporadic on early floors and grows (but never becomes "a lot") on later floors.

---

## 1. Tile Types

Three new `TileType` enum values, appended at the end to preserve network serialisation:

| Value | Name | Listen colour | Trigger chance | Collapse radius |
|-------|------|--------------|----------------|-----------------|
| 13 | `ScreeRock` | Yellow / amber (`#ffaa00`) | 50% | 1 (3×3 area) |
| 14 | `UnstableRock` | Light red (`#ff4400`) | 100% | 1 (3×3 area) |
| 15 | `VolatileRock` | Bright red (`#ff0000`) | 100% | 2 (5×5 area) |

All three behave identically to `Rock` in every respect except Listen rendering and the collapse mechanic:
- `IsMinable = true`
- `IsBlastable = true`
- `BlocksSight = true`
- Visually indistinguishable from Rock without Listen active

---

## 2. Collapse Mechanic

Triggered when a miner mines or an explosion destroys a scree tile.

**Probability check:**
- `ScreeRock`: 50% chance (roll `Random.NextDouble() < 0.5`)
- `UnstableRock`: 100% (always triggers)
- `VolatileRock`: 100% (always triggers)

**If triggered:**
1. The mined tile converts to `Floor` normally (the miner gets their rock)
2. Every `Floor` tile within Chebyshev radius R (1 or 2) of the mined tile converts to `Rock`
3. Any living miner occupying one of those newly-filled tiles receives `MinerCrushed`
4. Emit `ScreeCollapsed(GridPos Pos, int Radius)` for audio/visual feedback

**If not triggered (ScreeRock only):**
- Tile converts to `Floor` as normal — no collapse, no event emitted

---

## 3. New SimEvent

```csharp
public sealed record ScreeCollapsed(GridPos Pos, int Radius) : SimEvent;
```

`Radius` is 1 for ScreeRock/UnstableRock, 2 for VolatileRock. The client uses this to scale the visual effect.

`MinerCrushed` is already defined and is reused as-is for crush deaths.

---

## 4. Listen Rendering

In `WorldRenderer.cs`, the existing Listen shimmer loop (gated on `_client.Listening && _client.ListenTime >= 2.0f`) is extended to cover scree tiles within `ListenItemRevealRadius` (6 tiles of the local miner).

Shimmer colours:
```csharp
private static readonly Color ScreeColor    = new(1.0f, 0.67f, 0.0f, 1f); // amber
private static readonly Color UnstableColor = new(1.0f, 0.27f, 0.07f, 1f); // light red
private static readonly Color VolatileColor = new(1.0f, 0.0f,  0.0f, 1f);  // bright red
```

The shimmer animation (wave alpha) is identical to the buried-item shimmer — only the colour differs. No new infrastructure needed.

---

## 5. Map Generation

New fields on `MapConfig`:
```csharp
public int ScreePatchCount    { get; init; } = 0;
public int UnstableRockCount  { get; init; } = 0;
public int VolatileRockCount  { get; init; } = 0;
```

**FloorConfig density curve** (patches per floor):

| Floor range | ScreePatchCount | UnstableRockCount | VolatileRockCount |
|-------------|----------------|-------------------|-------------------|
| 1–2         | 0              | 0                 | 0                 |
| 3–7         | 1              | 0                 | 0                 |
| 8–14        | 2              | 1                 | 0                 |
| 15–19       | 3              | 1                 | 1                 |
| 20+         | 3              | 2                 | 1                 |

Each "patch" is a BFS cluster of 2–4 tiles that must border an existing Floor tile (same algorithm as `PlaceCrystalPatches`). Placement uses the 4×4 region grid to ensure spatial distribution. Placement runs after item/spawn placement so hazards do not overlap idol spawns or chest positions.

New method: `MapGenerator.PlaceScreePatches()` called from `GenerateFloor()` after `PlaceCrystalPatches()`.

---

## 6. Audio & Visual Feedback

**Screen shake:** Reuse existing shake system (same as explosions). Small shake for radius-1; larger for radius-2.

**Dust/debris burst:** `WorldRenderer` listens for `ScreeCollapsed` and draws a particle burst centred on `Pos`. Extend or reuse the existing explosion debris draw code. Burst size scales with `Radius`.

**Audio:** New sound `rockfall.ogg` (falling rock rumble, distinct from explosion). One play call in `MatchHost` on `ScreeCollapsed`. Louder/longer variant is handled by volume scaling based on `Radius` — same file, different volume (e.g. 0.7 for radius 1, 1.0 for radius 2).

---

## 7. Death Cause Messaging

The existing death-cause system already handles `MinerCrushed`. Confirm the displayed message ("crushed by a rockfall" or similar) is appropriate — no new message type needed unless the text needs a scree-specific variant.

---

## 8. Out of Scope

- No lobby toggle (always-on map feature like crystals)
- No sprites for scree tiles — they look identical to Rock; Listen colour is the only visual signal
- No chain reactions (one scree tile collapsing does not trigger adjacent scree tiles)
- No blast-protection gear or player ability to defuse scree tiles
