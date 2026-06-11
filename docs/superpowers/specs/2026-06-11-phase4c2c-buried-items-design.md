# Phase 4c-2c — Buried Items — Design

**Date:** 2026-06-11
**Status:** Approved design, ready for implementation plan
**Builds on:** 4c-2a (item framework + auto-apply buffs, main @ 06e2eab)

## Goal

Split map items into two flavors. Most items are **buried** inside ordinary rock — invisible
until the rock that holds them is **mined or blasted**, at which point the item drops onto the
now-open floor as a normal pickup. A few items stay **visible** on the floor in old toolboxes,
exactly as 4c-2a ships them today. Buried items can be **sensed through stone** by holding the
existing **Listen** action: nearby buried items shimmer (and softly chime) while you listen.

This adds *findability and reward-for-digging* on top of the 4c-2a pickup system. It introduces
no new netcode beyond one field on the item snapshot — the reveal is a client-side presentation
gate over already-synced data, the same trust model as fog.

## Non-goals

- No new item *kinds* — buried and toolbox items both draw from the existing
  `SpeedPotion / LongerVision / BiggerBlast` pool.
- No carried-item / inventory work (still 4c-2b's scope: plank, mold).
- Gold veins are untouched — buried items hide only in **ordinary** `Rock`, never `GoldRock`.

## Architecture

The feature reuses every 4c-2a seam. An item gains a **placement state**; map-gen places a few
on floor and the rest inside rock; the existing `PickUpItems` pass is taught to ignore buried
items; and the two existing rock-destruction sites (`CompleteActivity` mining branch,
`Detonate` blast loop) call a new `UnburyItemsAt` that flips a buried item to a loose floor
pickup. The placement state rides the existing per-tick item snapshot as one extra field. The
Listen reveal is pure Godot-side rendering/audio keyed off the local miner's Listening state and
the already-synced item list.

### Placement state machine

```
              mined or blasted                 walked over
   Buried ───────────────────────► Loose ───────────────────────► (collected, removed)
 (in rock,                       (on floor,
  hidden;                         visible dot)
  Listen-sensable)

   Toolbox ─────────────────────────────────────────────────────► (collected, removed)
 (on floor, visible in a box)        walked over
```

`Toolbox` and `Loose` are both collectible floor pickups; they differ only in how they render
(a toolbox marker vs. a bare spilled dot). `Buried` is non-collectible and invisible except
under Listen.

### Data flow

```
MapGenerator.PlaceItems (seeded)
        │  GeneratedMap.Items  (Toolbox on Floor, Buried in Rock)
        ▼
Simulation._items
   ├─ Tick: PickUpItems()  — skips Buried; Toolbox/Loose on a miner's tile → ApplyBuff + ItemPickedUp
   ├─ CompleteActivity (mine) ─┐
   └─ Detonate (blast) ────────┴─► UnburyItemsAt(pos): Buried → Loose  + ItemUnburied event
        │
        │ SnapshotFactory.Capture
        ▼
WorldSnapshot.Items[]  (ItemSnapshot gains Placement)
        │ SnapshotCodec
        ▼
MatchClient.Items  (full list incl. Buried)
   ├─ WorldRenderer: Toolbox/Loose drawn under fog as today; Buried drawn ONLY while the local
   │                 miner is Listening AND within ListenItemRevealRadius (shimmer, through rock)
   └─ MatchAudio: while Listening with a Buried item in range → soft chime panned to nearest
```

---

## Section 1 — Placement state & map-gen (Core)

**`Item` gains a placement state** (`src/Miner49er.Core/Map/Item.cs`):

```csharp
namespace Miner49er.Core;

public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }   // unchanged

/// <summary>Where an item sits and how it can be collected.</summary>
public enum ItemPlacement
{
    Toolbox,   // visible on a Floor tile, collectible on walk-over
    Buried,    // hidden inside a Rock tile; not collectible; revealed (→ Loose) when the rock is destroyed
    Loose,     // spilled onto a Floor tile after being unburied; collectible on walk-over
}

/// <summary>A collectible. Buried items sit on a Rock tile and are not collectible until the
/// rock is mined/blasted, which flips them to Loose.</summary>
public readonly record struct Item(GridPos Pos, ItemKind Kind, ItemPlacement Placement = ItemPlacement.Toolbox);
```

The `Placement = ItemPlacement.Toolbox` default keeps every existing `new Item(pos, kind)` call
site (and 4c-2a tests) meaning "visible, collectible floor item."

**`MapConfig`** gains the visible/buried split (the total budget knob is unchanged from 4c-2a):

```csharp
public int BaseItemCount  { get; set; } = 9;   // TOTAL items on the base 24×24 map (unchanged)
public int ItemsPerPlayer { get; set; } = 1;   // light scaling with player count (unchanged)
public int VisibleItemCount { get; set; } = 2;  // of the total, this many are visible toolboxes; rest are buried
```

Total stays `BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)`. Of that total,
`min(VisibleItemCount, …)` are toolboxes and the remainder are buried — "mostly buried, few
toolboxes." (At 1 player: 2 toolboxes + 7 buried; at 8 players: 2 + 14.)

**`MapGenerator.PlaceItems`** is rewritten to place both flavors (still called after `PlaceGold`,
still returns the list assigned to `GeneratedMap.Items`):

```csharp
// Items come in two flavors. A few sit visibly in toolboxes on Floor tiles; the rest are buried
// in ordinary Rock and only surface when that rock is destroyed. Both passes draw candidates in
// deterministic grid order then seed-shuffle, so host and every client agree. Kinds cycle
// round-robin over the COMBINED ordered list (toolboxes first, then buried) for a balanced spread.
private static List<Item> PlaceItems(TileGrid g, Random rng, int total, int visibleWanted,
    HashSet<GridPos> region, List<GridPos> spawns)
{
    var spawnSet = new HashSet<GridPos>(spawns);

    // Visible (toolbox) candidates: Floor in the traversable region, never a spawn tile.
    var floorCands = g.Positions()
        .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !spawnSet.Contains(p))
        .ToList();
    Shuffle(floorCands, rng);

    // Buried candidates: ordinary Rock (never GoldRock / ImpermeableRock) bordering the play
    // area, so every buried item is reachable by mining/blasting the rim.
    var rockCands = g.Positions()
        .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region))
        .ToList();
    Shuffle(rockCands, rng);

    int visible = Math.Min(Math.Min(visibleWanted, floorCands.Count), total);
    int buried  = Math.Min(total - visible, rockCands.Count);

    // Build the ordered position list (toolboxes first, then buried), then assign kinds.
    var placed = new List<(GridPos Pos, ItemPlacement Placement)>();
    for (int i = 0; i < visible; i++) placed.Add((floorCands[i], ItemPlacement.Toolbox));
    for (int i = 0; i < buried;  i++) placed.Add((rockCands[i],  ItemPlacement.Buried));

    var kinds = Enum.GetValues<ItemKind>();
    var items = new List<Item>();
    for (int i = 0; i < placed.Count; i++)
        items.Add(new Item(placed[i].Pos, kinds[i % kinds.Length], placed[i].Placement));
    return items;
}
```

`Generate` computes the counts and calls it:

```csharp
int total = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
```

`HasRegionNeighbor` already exists (used by `PlaceGold`). Buried items therefore sit on the same
rim-rock band as gold; since the candidate filter is `== TileType.Rock`, gold tiles (already
`GoldRock`) are naturally excluded, and two items can never share a tile (floor vs. rock pools
are disjoint).

---

## Section 2 — Pickup guard & reveal-on-destroy (Core)

**`PickUpItems` skips buried items.** A living miner can never stand on a Rock tile, so buried
items are already unreachable — but the guard makes intent explicit and protects against an item
that unburies *this* tick (mining) being grabbed before the miner steps onto it:

```csharp
private void PickUpItems()
{
    for (int i = _items.Count - 1; i >= 0; i--)
    {
        var item = _items[i];
        if (item.Placement == ItemPlacement.Buried) continue;   // not collectible until unburied
        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.Pos != item.Pos) continue;
            _items.RemoveAt(i);
            ApplyBuff(m.Id, item.Kind);
            _events.Add(new ItemPickedUp(m.Id, item.Pos, item.Kind));
            break; // one miner collects it
        }
    }
}
```

**`UnburyItemsAt`** flips any buried item on a tile to `Loose` and emits an event. `Item` is a
`readonly record struct`, so the flip uses a `with` expression:

```csharp
private void UnburyItemsAt(GridPos pos)
{
    for (int i = 0; i < _items.Count; i++)
    {
        var it = _items[i];
        if (it.Placement != ItemPlacement.Buried || it.Pos != pos) continue;
        _items[i] = it with { Placement = ItemPlacement.Loose };
        _events.Add(new ItemUnburied(it.Pos, it.Kind));
    }
}
```

**New event** (`SimEvent.cs`):
`public sealed record ItemUnburied(GridPos Pos, ItemKind Kind) : SimEvent;`

**Wire it into both rock-destruction sites:**

- `CompleteActivity`, mining branch — right after the tile is opened:

  ```csharp
  Grid.Set(target, TileType.Floor);
  if (wasGold) m.GoldCollected++;
  UnburyItemsAt(target);                  // ← surface any buried item here
  _events.Add(new RockMined(m.Id, target, wasGold));
  ```

- `Detonate`, blast loop — after each destroyed tile is set to Floor:

  ```csharp
  Grid.Set(p, TileType.Floor);
  if (wasGold) { … }
  UnburyItemsAt(p);                       // ← surface any buried item in the blast disc
  destroyed.Add(p);
  ```

**Ordering is already correct.** In `Tick`, `AdvanceActivities` (mining completes) runs *before*
`PickUpItems`, and `AdvanceCharges` (detonation) runs *after* it:

- **Mined item:** unburied → `Loose` before `PickUpItems`, but the miner is *adjacent* to the
  tile it just mined (not on it), so it isn't auto-collected this tick — it waits for someone to
  step onto it. Exactly the "drops as a floor pickup" behavior.
- **Blasted item:** unburied → `Loose` after `PickUpItems`, so it's first collectible next tick.
  Fine — the planter is rarely standing on the rubble anyway.

No change to buff application, charge capture, or any other 4c-2a logic.

---

## Section 3 — Netcode (snapshot sync)

One additive field, following the 4c-2a `ItemSnapshot` shape exactly.

**`Snapshots.cs`:**

```csharp
public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind, ItemPlacement Placement);
```

**`SnapshotFactory.Capture`:** project `sim.Items` to
`new ItemSnapshot(item.Pos.X, item.Pos.Y, item.Kind, item.Placement)` — i.e. carry `Placement`
through. The full list (including `Buried` entries) is sent every tick, as today; buried items
are few and static-until-destroyed.

**`SnapshotCodec`:** in the item block, after `(int X, int Y, int (ItemKind)Kind)` write/read one
more `int (ItemPlacement)Placement`; read symmetrically. Same length-prefixed pattern already in
place for items.

No pickup or unbury event crosses the wire. The client derives both from the synced list: an item
vanishing = picked up (existing 4c-2a SFX); an item whose `Placement` flips `Buried → Loose` =
unburied (new spill SFX, Section 4).

**Trust note.** Buried-item positions are already on the wire (full-state sync, like the whole
grid). The Listen reveal is a *presentation* gate, identical to how fog ships the full grid but
renders only what's lit. This stays within the established naive-sync model; hardening client
trust is out of scope for the whole project tier.

---

## Section 4 — Godot layer (toolbox render, Listen reveal, SFX)

**Expose Listening to the renderer.** `MatchClient` gains `public bool Listening;`. In
`Main._PhysicsProcess`, where `listening` is already computed, also set `_client.Listening = listening;`.

**Render (`WorldRenderer._Draw`):**

- **`Toolbox` items:** the existing fog-gated colored dot, plus a small square "toolbox" outline
  (`DrawRect` border) behind it so visible items read as sitting in a box.
- **`Loose` items:** the existing fog-gated colored dot, no box (spilled rubble).
- **`Buried` items:** drawn **only** when `_client.Listening` **and** the tile is within
  `ListenItemRevealRadius` (Chebyshev) of the local miner — rendered as a **pulsing shimmer** in
  the item's kind color (semi-transparent, alpha oscillating over time), drawn **regardless of
  fog** (you sense it through the stone). Outside Listen or beyond the radius, buried items draw
  nothing.

Add a client-side tunable near the other render consts:
`private const int ListenItemRevealRadius = 6;` (tiles) — and the pulse period (e.g. ~0.8 s).
Drive the pulse from `Time.GetTicksMsec()`; while `Listening`, `WorldRenderer` calls `QueueRedraw()`
each `_Process` frame so the shimmer animates (it otherwise redraws on snapshot apply ~30 Hz).

Revealing the kind *color* (not just "something here") is intentional — Listen becomes a scouting
tool worth standing still for. Tunable to a neutral shimmer if play-test finds it too strong.

**Spill SFX (`MatchAudio`):** alongside the existing `_prevItems` pickup diff, track each item's
`Placement`. A position whose placement went `Buried → Loose` between frames is a fresh unbury;
if near the local miner, play a short "spill/chime" `SfxLibrary` tone positionally at that tile.
(Add a `Spill`/`Unbury` placeholder tone to `SfxLibrary`, like `Pickup` but lower/grittier.)

**Listen chime (`MatchAudio`, optional polish):** while `Listening`, if ≥1 `Buried` item is within
`ListenItemRevealRadius` of the local miner, emit a soft periodic ping (~every 0.8 s) panned
toward and scaled by proximity to the nearest such item. This rides the existing listen-audio path
(`SetListening` already toggles listen audio state). Mark as cuttable if it feels noisy in
play-test — the visual shimmer is the primary reveal.

The existing 8-point rival **Compass** is unchanged; buried-item sensing is a separate shimmer/
chime layer over the same Listen state, not a second compass arrow.

---

## Section 5 — Tests (Core, xUnit)

**Placement (`MapGeneratorItemsTests`, updating the 4c-2a assertions):**
- Total count still equals `BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)` (clamped on a tiny map).
- Exactly `min(VisibleItemCount, …)` items are `Toolbox` on `Floor` tiles (never a spawn); the
  rest are `Buried` on ordinary `Rock` tiles bordering the traversable region.
- No buried item sits on `GoldRock` or `ImpermeableRock`; toolbox and buried positions are disjoint.
- Deterministic: same seed twice → identical `Items` (positions, kinds, **placements**).
- Kinds are round-robin balanced over the combined ordered list.

**Pickup guard (`SimulationItemsTests`):**
- A `Buried` item is **not** collected: with a buried item present, ticking with miners around it
  leaves it in `sim.Items` (still `Buried`).
- Regression: a `Toolbox` item is still collected on walk-over (applies the matching buff, emits
  `ItemPickedUp`) — unchanged 4c-2a behavior.
- A `Loose` item (constructed directly or produced by unbury) is collected on walk-over like a toolbox.

**Reveal-on-destroy (`SimulationItemsTests` / extend explosive tests):**
- **Mine:** a miner mining the rock at a buried item's `Pos` flips it to `Loose`, emits
  `ItemUnburied(Pos, Kind)`, and the tile becomes `Floor`. The item is not auto-collected the same
  tick (miner is adjacent), but a miner subsequently stepping onto the tile collects it.
- **Blast:** a charge whose disc covers a buried item's tile flips it to `Loose` and emits
  `ItemUnburied`. (Place a `BiggerBlast`-free baseline charge so the radius is deterministic.)
- A buried item **not** on a destroyed tile stays `Buried`.

**Codec / factory (`SnapshotCodecTests` / `SnapshotFactoryTests` extensions):**
- Round-trip a snapshot whose `Items` carry each `ItemPlacement` value (`Toolbox`, `Buried`,
  `Loose`); assert equality including `Placement`.
- `SnapshotFactory.Capture` projects `Placement` from `sim.Items`.

The Listen shimmer, toolbox marker, and chime are Godot-side presentation — verified by play-test,
no Core test.

---

## Risks & notes

- **Buried-rock reachability:** restricting buried candidates to rim rock (`HasRegionNeighbor`,
  same as gold) guarantees every buried item is reachable by mining/blasting the play-area edge,
  rather than stranding one deep in unreachable stone. Buried items therefore cluster on the
  floor/rock boundary — acceptable, and tunable later if it feels too shallow.
- **Kind skew on toolboxes:** round-robin over "toolboxes first, then buried" means the two
  toolboxes are always the first two kinds (Speed, Vision) and never BiggerBlast. Cosmetic and
  acceptable; revisit only if play-test wants blast potions visible.
- **`ItemSnapshot` is positional in the codec** — adding `Placement` means updating both the
  `Write` loop and the `Read` constructor (3 → 4 ints per item). Same edit shape as 4c-2a's
  original item block.
- **Reveal is client-side** — no new RPC. The buried positions already ride the snapshot; Listen
  only gates *rendering* them. Consistent with fog and the project's naive full-state-sync tier.
- **ReachCenter sparsity** carries over from 4c-2a (larger map, same base count) — unchanged here.
