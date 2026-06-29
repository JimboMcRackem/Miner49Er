# Treasure Hunt Mode Design

## Overview

Treasure Hunt is a multiplayer-only game mode (2–8 players) in which each player races to locate and deposit two privately assigned idol treasures before their opponents. It replaces gold-mining as the core loop with exploration, mining, and a strategic chest-placement decision.

---

## Rules

1. Each player is assigned 2 idol types, unique across all players, drawn from a pool of 17.
2. All assigned idols are buried inside rock walls and must be mined out (no toolbox placement).
3. Each player starts holding a `TreasureChest` in their 1-slot inventory.
4. The player places their chest somewhere on the map (Use verb, like SlowMold drop). The chest can be re-picked-up at any time by walking over it.
5. The player finds and mines out one of their idol types. The idol becomes a loose item on the floor.
6. Any player can pick up any idol. Only the assigned player can deposit a given idol into their chest.
7. Walking onto your own chest tile while holding one of your two assigned idols auto-deposits it.
8. The first player to deposit both assigned idols wins. All other game-over conditions (last man standing) still apply.

---

## The 17-Idol Pool

Idols are drawn in a seeded shuffle; a match uses exactly `playerCount × 2` from the shuffled order, so some idol types sit out in smaller games.

| # | ItemKind | Category |
|---|----------|----------|
| 1 | IdolVishnu | Deity |
| 2 | IdolZeus | Deity |
| 3 | IdolAnubis | Deity |
| 4 | IdolOdin | Deity |
| 5 | IdolShiva | Deity |
| 6 | IdolBuddha | Deity |
| 7 | IdolRa | Deity |
| 8 | IdolQuetzalcoatl | Deity |
| 9 | IdolUrn | Artifact |
| 10 | IdolLamp | Artifact |
| 11 | IdolMace | Artifact |
| 12 | IdolSceptre | Artifact |
| 13 | IdolGlobe | Artifact |
| 14 | IdolTrophyCup | Trophy |
| 15 | IdolChalice | Trophy |
| 16 | IdolCrown | Trophy |
| 17 | IdolSkull | Trophy |

---

## Assignment Algorithm

A new static class `TreasureAssignment` in `src/Miner49er.Core/Sim/`:

```csharp
public static class TreasureAssignment
{
    // Returns the two idol ItemKinds assigned to the given minerId (1-based).
    // Deterministic from seed — no network message needed.
    public static (ItemKind A, ItemKind B) For(int seed, int minerId)
    {
        var pool = AllIdols(); // 17 elements, fixed order
        Shuffle(pool, seed);
        int i = (minerId - 1) * 2;
        return (pool[i], pool[i + 1]);
    }

    public static ItemKind[] AllIdols() => new[]
    {
        ItemKind.IdolVishnu, ItemKind.IdolZeus, ItemKind.IdolAnubis,
        ItemKind.IdolOdin,   ItemKind.IdolShiva, ItemKind.IdolBuddha,
        ItemKind.IdolRa,     ItemKind.IdolQuetzalcoatl,
        ItemKind.IdolUrn,    ItemKind.IdolLamp,  ItemKind.IdolMace,
        ItemKind.IdolSceptre,ItemKind.IdolGlobe,
        ItemKind.IdolTrophyCup, ItemKind.IdolChalice,
        ItemKind.IdolCrown,  ItemKind.IdolSkull,
    };

    private static void Shuffle(ItemKind[] arr, int seed)
    {
        var rng = new Random(seed);
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
    }
}
```

Called identically on host (to seed the map and sim) and on each client (to know local player's two idols for HUD rendering). No private RPC needed.

---

## Item Scheme

### New ItemKind values

`TreasureChest` (carryable, placed by Use verb) + 17 `Idol*` kinds.

`IsCarried()` returns true for `TreasureChest` and all `Idol*` kinds so they occupy the 1-slot inventory.

`IsPlaceable()` returns false for all new kinds (not randomly distributed as toolboxes).

### Map placement

`MapConfig` gets `TreasureHuntMode` flag. When set:

- `ChestCount = 0` (no loot chests — the Treasure Hunt chest is a separate kind)
- `BuriedIdolCount = playerCount * 2` — MapGenerator buries exactly the assigned idol types in rock walls using the same seeded placement logic as existing buried items.
- Normal items (potions, planks, etc.) still appear as usual.
- The map generator calls `TreasureAssignment.For(seed, minerId)` for each miner to know which idol types to place.

Each assigned idol is placed as `Item(pos, kind, ItemPlacement.Buried)`.

### Miner starting inventory

In `MatchHost.Begin()`, for TreasureHunt mode each miner is given `Held = ItemKind.TreasureChest` immediately after `AddMiner`.

---

## Simulation Layer

### New state on `Simulation`

```csharp
// TreasureHunt: minerId → placed chest position (null = chest still held / not placed)
private readonly Dictionary<int, GridPos?> _chestPos = new();

// TreasureHunt: minerId → number of idols deposited (0, 1, or 2)
private readonly Dictionary<int, int> _idolsFound = new();

// TreasureHunt: minerId → (idolA, idolB) assignment; populated at construction
private readonly Dictionary<int, (ItemKind A, ItemKind B)> _idolAssignments = new();
```

Populated in `AddMiner()` when mode is TreasureHunt.

### Chest placement (Use verb)

When a miner Uses the held `TreasureChest`:
- Set grid item: drop a `TreasureChest` item at the miner's current tile with `ItemPlacement.Toolbox`.
- Set `_chestPos[minerId] = miner.Pos`.
- Clear `miner.Held`.
- Emit `TreasureChestPlaced(minerId, pos)`.

When a miner walks onto a `TreasureChest` item owned by them (their `_chestPos` matches):
- Remove item from floor.
- Set `miner.Held = TreasureChest`.
- Set `_chestPos[minerId] = null`.

Other players walking onto someone else's chest have no interaction (chest item is skipped).

### Auto-deposit

Each tick, after miner movement is resolved:

```csharp
if (mode == GameMode.TreasureHunt
    && _chestPos.TryGetValue(minerId, out var cp) && cp == miner.Pos
    && miner.Held is ItemKind heldKind
    && _idolAssignments.TryGetValue(minerId, out var assign)
    && (heldKind == assign.A || heldKind == assign.B))
{
    miner.Held = null;
    _idolsFound[minerId]++;
    _events.Add(new IdolDeposited(minerId, heldKind));
}
```

### New SimEvents

```csharp
public sealed record TreasureChestPlaced(int MinerId, GridPos Pos) : SimEvent;
public sealed record IdolDeposited(int MinerId, ItemKind Kind) : SimEvent;
```

### RoundResolver

```csharp
GameMode.TreasureHunt when sim.TreasureWinner() is int w and w >= 0
    => RoundResult.Win(w),
```

`Simulation.TreasureWinner()` returns the first minerId with `_idolsFound[id] == 2`, or -1 if none.

---

## Networking / Snapshot

### New snapshot types

```csharp
public readonly record struct TreasureProgressSnapshot(int MinerId, int Found);
public readonly record struct PlacedChestSnapshot(int MinerId, int X, int Y);
```

### WorldSnapshot additions

```csharp
public sealed record WorldSnapshot(
    // … existing fields …
    IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress = null,
    IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     = null);
```

`SnapshotFactory.Capture()` populates both from `sim._idolsFound` and `sim._chestPos`. Broadcast to all peers each tick — no private data (assignments are computed client-side).

### MatchClient additions

```csharp
public IReadOnlyList<TreasureProgressSnapshot> TreasureProgress { get; private set; }
public IReadOnlyList<PlacedChestSnapshot>       PlacedChests     { get; private set; }
```

Set in `ApplyUpdate()`.

---

## HUD & Rendering

### Idol shadow panel

`Main._PhysicsProcess` computes local assignment:

```csharp
var (idolA, idolB) = TreasureAssignment.For(nm.MatchSeed, _client.LocalMinerId);
int found = _client.TreasureProgress
    .FirstOrDefault(p => p.MinerId == _client.LocalMinerId).Found;
```

Renders a 2-slot strip (e.g. bottom-left corner):
- Slot shows idol sprite at 50% alpha (shadow) when not yet deposited.
- Slot shows full-color idol sprite when deposited.

### Placed chests

`WorldRenderer` renders each `PlacedChestSnapshot` as a chest sprite at its grid position (same pipeline as existing items). Chests belonging to other players look identical — there is no visual "ownership" label.

### Idol floor sprites

Each `Idol*` ItemKind maps to its own sprite in the item atlas (same atlas used for WaterPlank, SlowMold, etc.). All 17 idol sprites need to be added to the tileset / atlas.

---

## Lobby

- Add **"Treasure Hunt"** to `_modePicker` in `Lobby.cs` (after Expedition, index 4).
- Save/load via existing `SettingsStore.SaveLobby` / `LoadLobby` — no schema change needed (gameMode already stored as int).
- In `RefreshModeControls()`: hide time picker, explosive picker, and map size picker when Treasure Hunt is selected (no time limit; no expedition-specific scaling).
- `canStart` guard already requires ≥2 players — sufficient.

---

## Art Scope

17 new item sprites required (16×16 or 32×32 px, matching existing item atlas scale):

- 8 deity idol sprites: Vishnu, Zeus, Anubis, Odin, Shiva, Buddha, Ra, Quetzalcoatl
- 9 artifact/trophy sprites: Urn, Lamp, Mace, Sceptre, Globe, Trophy Cup, Chalice, Crown, Skull
- 1 TreasureChest sprite (floor item, distinct from the existing Expedition Chest)

Silhouette versions for HUD shadow can be generated at runtime by rendering the sprite in a single dark colour.

---

## Files Changed

| File | Change |
|------|--------|
| `src/Miner49er.Core/Sim/GameMode.cs` | Add `TreasureHunt = 4` |
| `src/Miner49er.Core/Map/Item.cs` | Add `TreasureChest` + 17 `Idol*` to `ItemKind`; update `IsCarried()` |
| `src/Miner49er.Core/Sim/TreasureAssignment.cs` | New: assignment algorithm |
| `src/Miner49er.Core/Sim/SimEvent.cs` | Add `TreasureChestPlaced`, `IdolDeposited` |
| `src/Miner49er.Core/Sim/Simulation.cs` | Add chest/found/assignment state; chest placement; auto-deposit; `TreasureWinner()` |
| `src/Miner49er.Core/Sim/RoundResolver.cs` | Add TreasureHunt win condition |
| `src/Miner49er.Core/Map/MapConfig.cs` | Add `TreasureHuntMode` flag; configure in `MapConfig.For()` |
| `src/Miner49er.Core/Map/MapGenerator.cs` | Place assigned buried idols in TreasureHunt mode |
| `src/Miner49er.Core/Net/Snapshots.cs` | Add `TreasureProgressSnapshot`, `PlacedChestSnapshot`; extend `WorldSnapshot` |
| `src/Miner49er.Core/Net/SnapshotFactory.cs` | Populate new snapshot fields |
| `src/Miner49er.Core/Net/SnapshotCodec.cs` | Encode/decode new snapshot fields |
| `game/net/MatchClient.cs` | Expose `TreasureProgress`, `PlacedChests`; apply in `ApplyUpdate()` |
| `game/net/MatchHost.cs` | Set starting `Held = TreasureChest` for each miner in TreasureHunt |
| `game/Main.cs` | Render idol shadow panel in HUD for TreasureHunt mode |
| `game/WorldRenderer.cs` | Render placed chests from `PlacedChests` snapshot |
| `game/ui/Lobby.cs` | Add Treasure Hunt to mode picker; `RefreshModeControls()` |
| `assets/tiles/items.png` (or atlas) | 18 new sprites (17 idols + TreasureChest) |
