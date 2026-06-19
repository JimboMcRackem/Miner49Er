# Shop & Throwing Stones Design

**Date:** 2026-06-19  
**Feature:** Expedition-mode shopkeeper every 4 floors; stackable throwing stones to distract monsters

---

## Overview

Two interlocking features for Expedition mode:

1. **Shop** — a shopkeeper appears on floors 4, 8, 12, 16, 20 (the same "clean" floors that have no modifier). The player spends mined gold on permanent upgrades, life potions, or throwing stones.
2. **Throwing stones** — a stackable consumable (separate from the 1-slot carried inventory). Thrown in the miner's facing direction; lands at the first wall; the landing tile becomes a timed noise source that distracts all monster types for 4 seconds.

---

## 1. Data Model

### 1.1 `Miner.StoneCount` (Core)

Add `public int StoneCount { get; private set; } = 0;` to `Miner`. Two new methods:

- `AddStones(int n)` — increments `StoneCount` (called by shop purchase and by `TryThrowStone` no-op path is just a guard)
- Stones are independent of the `ItemKind? Held` slot; a miner can carry a plank and still throw stones.

Cap: `StoneCount` max 9. `AddStones` clamps at 9.

### 1.2 `NoiseSource` (Core)

New internal class (or record) in `Simulation`:

```csharp
internal sealed class NoiseSource
{
    public GridPos Pos;
    public double LifetimeRemaining; // seconds, default 4.0
}
```

`Simulation` holds `private readonly List<NoiseSource> _noiseSources = new()`.  
Each `Tick(double dt)` call decrements lifetime and removes expired sources.

### 1.3 `ShopItemKind` enum (Core)

```csharp
public enum ShopItemKind { SpeedUp, VisionUp, BlastUp, LifePotion, Stones3 }
```

Prices (hardcoded in Core, not in `SimConfig`):

| Kind | Price | Cap condition |
|---|---|---|
| SpeedUp | 15 gold | `PermSpeedLevel >= 5` |
| VisionUp | 15 gold | `PermVisionLevel >= 5` |
| BlastUp | 20 gold | `PermBlastLevel >= 3` |
| LifePotion | 25 gold | `_livesRemaining >= _livesMax` (host-side) |
| Stones3 | 10 gold | `StoneCount >= 9` |

### 1.4 `MinerSnapshot.StoneCount` (Net)

Add `public int StoneCount` to `MinerSnapshot`. Populated by `SnapshotFactory.Capture`. Used by the HUD to show `"  Stones: N"` when `N > 0`.

### 1.5 `GeneratedMap.ShopPos` (Core)

```csharp
public GridPos? ShopPos { get; init; }
```

Set by `MapGenerator` (or a `ShopPlacer` helper) only for floors where `floor % 4 == 0 && floor != 0` (floors 4, 8, 12, 16, 20). On other floors it is `null`.

The position is chosen deterministically from the floor seed: a Floor tile adjacent to the spawn area, not the escape tile, not the spawn tile itself. Both host and client regenerate the map from the same seed, so `ShopPos` is always in agreement with no extra network messages.

---

## 2. Shopkeeper Placement

`MapGenerator.Generate` (or a post-processing step) selects `ShopPos` when the floor qualifies:

1. Start from `map.Spawns[0]` (the player's spawn).
2. Walk up to 5 tiles in cardinal directions (Manhattan search) to find a Floor tile that is neither the spawn itself nor the escape tile.
3. If no suitable tile is found within 5 tiles, expand the search radius to 10. If still none, leave `ShopPos = null`.

`WorldRenderer` draws a colored rect + "§" label at `ShopPos` (fog-gated like other objects).

---

## 3. Shop Interaction — Client Side

### 3.1 Detection

`Main._PhysicsProcess` checks each frame:

```csharp
bool atShop = _client.ShopPos is GridPos sp && localPos == sp;
```

Where `localPos` is the current miner's `GridPos` from the latest snapshot.

### 3.2 `ShopPanel` (Godot `Control`)

A `ShopPanel` node is added in `Main._Ready`. It is hidden by default.

- **Opens** when `atShop == true` and the panel is not already open.
- **Closes** when `atShop == false` (player walked away) or ESC is pressed.
- Stays open while the player stands on the shop tile — player remains vulnerable to monsters.
- Buying an item keeps the panel open (can buy multiple items per visit).

### 3.3 Panel Contents

Five rows, one per `ShopItemKind`, in order: SpeedUp, VisionUp, BlastUp, LifePotion, Stones3.

Each row shows:
- Item name and brief effect description
- Price in gold
- "BUY" (selectable), "MAX" (at cap), or "Can't afford" (not enough gold)

Navigation: Up/Down arrow keys. Buy: Use key (`InputBindings.Use`). Dismiss: ESC.

The panel reads current gold/levels/lives/stoneCount from the latest `MinerSnapshot` to determine availability.

---

## 4. Shop Purchase — Network Protocol

### 4.1 Client → Host RPC

`NetworkManager` gains:

```csharp
public void BuyShopItem(ShopItemKind kind) { /* RPC to host */ }
```

The client calls this when the player confirms a purchase. No optimistic update — the client waits for the next snapshot tick to see the effect.

### 4.2 Host Validation (`MatchHost`)

`MatchHost` receives the buy RPC and validates:

1. Miner is on (or adjacent to) the shop tile for the current floor (prevents out-of-shop exploits).
2. `m.GoldCollected >= price` for the item.
3. Item is not at cap.

If valid:
- Deduct gold: `sim.DeductGold(minerId, price)` (new sim method — reduces `GoldCollected`)
- Apply effect:
  - SpeedUp / VisionUp / BlastUp: `sim.SetPermLevels(minerId, speed+1, vision, blast)` (increment the relevant level)
  - LifePotion: `_livesRemaining = Math.Min(_livesRemaining + 1, _livesMax)` in `MatchHost`
  - Stones3: `sim.AddStones(minerId, 3)`

Effect surfaces in the next `TickUpdate` snapshot — gold drops, levels/lives/stones update on the client automatically.

### 4.3 Gold Deduction and Score

`Miner.GoldCollected` is reduced. This affects the **score** (cumulative gold) but does **not** affect the escape threshold (which is based on `GoldRemaining` on the map, not on the miner's total). The escape condition `EscapeOpened` watches tiles, not player gold.

---

## 5. Throwing Stones

### 5.1 Input

New action `Throw` added to `InputBindings` with default key T. `InputSender` sends a throw pulse (same pattern as mine/plant: a `bool pendingThrow` flag set on press, cleared each step in `StepOnce`).

### 5.2 `Simulation.TryThrowStone(int minerId)` (Core)

1. Look up miner; if `StoneCount == 0`, return (no-op).
2. Determine trajectory from miner's current `Facing` direction.
3. Walk tiles from `miner.Pos + 1 * facing` until hitting a non-Floor/non-Plank/non-ShallowWater tile or map boundary.
4. Land position = last walkable tile visited (or miner's own tile if blocked immediately).
5. Add `new NoiseSource { Pos = landPos, LifetimeRemaining = 4.0 }` to `_noiseSources`.
6. Decrement `miner.StoneCount`.
7. Emit `StoneThrown(minerId, landPos)` sim event (for audio — a thud sound).

### 5.3 Monster AI — Noise Distraction

In each monster's move evaluation (Slime, Ghost, Goat), before the normal player-chase logic:

1. Filter `_noiseSources` to those within `MonsterSenseRadius` of the monster.
2. If any exist, target the nearest one's `Pos` instead of the nearest player.

Behavior per kind:
- **Slime**: enters a "noise-chase" state identical to player-chase, pathfinds toward `NoiseSource.Pos`.
- **Ghost**: phases toward `NoiseSource.Pos` (same wall-phasing logic, different target).
- **Goat**: re-aims its charge direction toward `NoiseSource.Pos`.

When the noise source expires (lifetime ≤ 0), the monster reverts to its normal behavior on the next move tick.

---

## 6. Display

### 6.1 HUD

Stone count appended to the Expedition HUD when `StoneCount > 0`:

```
♥♥♥  Floor 5/20  Gold: 42%  [HASTE]    Ready    Stones: 2
```

### 6.2 Shopkeeper rendering

`WorldRenderer` draws the shopkeeper at `ShopPos` (if non-null and visible through fog):
- 28×28 colored rect (distinct color, e.g. warm yellow `#C8A020`)
- "§" label centered in the tile

### 6.3 Shop-approach prompt

When `atShop` is true and `ShopPanel` is not yet open, the HUD appends `"  [Step here to shop]"` for one tick, then the panel opens automatically.

---

## 7. Scope / Out of Scope

**In scope:**
- Shop on floors 4, 8, 12, 16, 20 (Expedition mode only)
- Five shop items as specified
- Throwing stones distracting Slime, Ghost, Goat
- Stone count shown in HUD and MinerSnapshot

**Out of scope:**
- Shop in multiplayer (Classic/Competitive modes)
- Stone throwing in non-Expedition floors (boss floor 21 has no shopkeeper; stones still work if the player has them)
- Monster-specific distraction durations (all use 4s)
- Visual stone-in-flight animation (throw is instant; only the noise source persists)
- Shopkeeper blocking movement (player can walk through the tile)
