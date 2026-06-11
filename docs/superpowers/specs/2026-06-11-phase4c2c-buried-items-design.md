# Phase 4c-2c — Buried Items — Design

**Date:** 2026-06-11
**Status:** Approved design, ready for implementation plan
**Builds on:** 4c-2a (item framework + auto-apply buffs, main @ 06e2eab)

## Goal

Split map items into two flavors. Most items are **buried** inside ordinary rock — invisible
until the rock that holds them is **mined or blasted**, at which point the item drops onto the
now-open floor as a normal pickup. A few items stay **visible** on the floor in old toolboxes,
exactly as 4c-2a ships them today. Buried items can be **sensed through stone** by holding the
existing **Listen** action: nearby suspicious spots shimmer (and softly chime) while you listen.

The shimmer is **deliberately vague** — it marks a *suspicious spot* without revealing what (or
whether anything) is there. A few suspicious spots are **decoys** that hold nothing, so the only
way to learn the truth is to dig. Listen becomes a gamble, not a map.

This adds *findability and reward-for-digging* on top of the 4c-2a pickup system. It introduces
no new netcode: buried items reuse the existing per-tick item snapshot (one extra field), and
decoys are a deterministic map-gen output every client regenerates from the shared seed. The
whole reveal is a client-side presentation gate over already-synced/regenerated data, the same
trust model as fog.

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
pickup. The placement state rides the existing per-tick item snapshot as one extra field.
Map-gen also emits a set of **decoys** — empty rock spots that look exactly like buried caches
under Listen — as a deterministic, un-synced output every client regenerates from the seed. The
Listen reveal is pure Godot-side rendering/audio keyed off the local miner's Listening state, the
already-synced item list, and the regenerated decoy set; it never reveals an item's kind.

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
MapGenerator (seeded)
   ├─ PlaceItems  → GeneratedMap.Items   (Toolbox on Floor, Buried in Rock)   ── synced ──┐
   └─ PlaceDecoys → GeneratedMap.Decoys  (empty rock spots)  ── NOT synced; client regens ─┤
                                                                                           │
Simulation._items  (host: items only; decoys are never in the sim)                        │
   ├─ Tick: PickUpItems()  — skips Buried; Toolbox/Loose on a miner's tile → ApplyBuff + ItemPickedUp
   ├─ CompleteActivity (mine) ─┐                                                           │
   └─ Detonate (blast) ────────┴─► UnburyItemsAt(pos): Buried → Loose  + ItemUnburied event│
        │ SnapshotFactory.Capture → WorldSnapshot.Items[] (ItemSnapshot gains Placement)   │
        ▼ SnapshotCodec                                                                    ▼
MatchClient.Items (full list incl. Buried)              MatchClient.Decoys (from client's map regen)
   ├─ WorldRenderer: Toolbox/Loose drawn under fog as today. Buried items AND decoys draw a
   │                 NEUTRAL shimmer (identical, no kind) ONLY while Listening AND within
   │                 ListenItemRevealRadius — through rock. A buried item shimmers while its
   │                 Placement==Buried; a decoy shimmers while its tile is still Rock.
   └─ MatchAudio: spill SFX on Buried→Loose; while Listening with any suspicious spot
                  (buried item OR decoy) in range → soft neutral chime panned to nearest
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
public int DecoyCount { get; set; } = 4;        // empty "suspicious spots" that shimmer under Listen but hold nothing
```

Total stays `BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)`. Of that total,
`min(VisibleItemCount, …)` are toolboxes and the remainder are buried — "mostly buried, few
toolboxes." (At 1 player: 2 toolboxes + 7 buried; at 8 players: 2 + 14.) On top of the items,
`DecoyCount` empty suspicious spots are placed in rock (see below); with ~7 buried at 1 player
that's roughly a third false signals — enough that Listen is a gamble, not a treasure map.
`DecoyCount` is a single fixed knob for now (tunable; could scale with players later).

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

`HasRegionNeighbor` already exists (used by `PlaceGold`). Buried items therefore sit on the same
rim-rock band as gold; since the candidate filter is `== TileType.Rock`, gold tiles (already
`GoldRock`) are naturally excluded, and two items can never share a tile (floor vs. rock pools
are disjoint).

**`MapGenerator.PlaceDecoys`** — a new sibling pass placing empty suspicious spots. It draws from
the **same rim-rock pool** as buried items so a decoy is indistinguishable from a real cache, and
excludes tiles already holding an item:

```csharp
// "Suspicious spots" with no item: deterministic rock positions that shimmer under Listen exactly
// like buried items, so the only way to tell a real cache from a decoy is to dig. Same rim-rock
// candidate pool as buried items, minus tiles already holding a (buried) item.
private static List<GridPos> PlaceDecoys(TileGrid g, Random rng, int count,
    HashSet<GridPos> region, IEnumerable<Item> items)
{
    var taken = new HashSet<GridPos>(items.Select(it => it.Pos));
    var cands = g.Positions()
        .Where(p => g.Get(p) == TileType.Rock && !taken.Contains(p) && HasRegionNeighbor(g, p, region))
        .ToList();
    Shuffle(cands, rng);
    return cands.Take(Math.Min(count, cands.Count)).ToList();
}
```

**`GeneratedMap`** gains `public required IReadOnlyList<GridPos> Decoys { get; init; }`.

`Generate` computes the counts and calls both passes (decoys after items, sharing the same `rng`
so the whole map stays deterministic for a seed):

```csharp
int total = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
var items  = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);

return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center, Items = items, Decoys = decoys };
```

Decoys are **map-gen output only** — never added to `Simulation` (the host seeds the sim from
`map.Items` alone), never synced. They never change state, so each client's deterministic map
regen (which already happens for tiles/fog) reproduces the identical decoy set for free.

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

**Decoys cross no wire at all.** They aren't items and aren't in the sim — they're a deterministic
`MapGenerator` output (`GeneratedMap.Decoys`) that every client reproduces from the shared
`MatchSeed` during its existing map regen. Nothing about a decoy ever changes, so there is no state
to sync.

**Trust note.** Buried-item positions are already on the wire (full-state sync, like the whole
grid), and decoy positions are derivable from the seed. The Listen reveal is a *presentation*
gate, identical to how fog ships the full grid but renders only what's lit. This stays within the
established naive-sync model; hardening client trust is out of scope for the whole project tier.

---

## Section 4 — Godot layer (toolbox render, Listen reveal, SFX)

**Plumb Listening and decoys to the renderer.** `MatchClient` gains `public bool Listening;` (set
each frame in `Main._PhysicsProcess`, where `listening` is already computed:
`_client.Listening = listening;`) and `public IReadOnlyList<GridPos> Decoys` (set once in `Begin`
from the client's regenerated `map.Decoys`; thread it through `Main._Ready`'s `_client.Begin(...)`
call).

**Render (`WorldRenderer._Draw`):**

- **`Toolbox` items:** the existing fog-gated colored dot, plus a small square "toolbox" outline
  (`DrawRect` border) behind it so visible items read as sitting in a box.
- **`Loose` items:** the existing fog-gated colored dot, no box (spilled rubble).
- **Suspicious spots — `Buried` items AND decoys:** drawn **only** when `_client.Listening`
  **and** the tile is within `ListenItemRevealRadius` (Chebyshev) of the local miner — rendered as
  a **neutral pulsing shimmer**: a single muted color (e.g. pale yellow/white), a soft diffuse glow
  over the tile (not a sharp icon), alpha oscillating over time, drawn **regardless of fog** (you
  sense it through the stone). **Crucially, buried items and decoys render identically** — no kind
  color, no shape difference — so the player cannot tell a real cache from an empty spot without
  digging. A **buried item** shimmers while its synced `Placement == Buried`; a **decoy** shimmers
  while its tile is still `TileType.Rock`. Once the rock is destroyed, a real item drops as a
  colored `Loose` dot (its kind finally revealed) and a decoy simply stops shimmering with nothing
  there. Outside Listen or beyond the radius, nothing draws.

Add client-side tunables near the other render consts:
`private const int ListenItemRevealRadius = 6;` (tiles) and the pulse period (e.g. ~0.8 s).
Drive the pulse from `Time.GetTicksMsec()`; while `Listening`, `WorldRenderer` calls `QueueRedraw()`
each `_Process` frame so the shimmer animates (it otherwise redraws on snapshot apply ~30 Hz).

The kind is **never** revealed by Listen — only by actually surfacing the item (drop or toolbox).
That keeps the signal honest-but-ambiguous: you learn *where to consider digging*, not *what you'll
get* or even *whether there's anything*.

**Spill SFX (`MatchAudio`):** alongside the existing `_prevItems` pickup diff, track each item's
`Placement`. A position whose placement went `Buried → Loose` between frames is a fresh unbury;
if near the local miner, play a short "spill" `SfxLibrary` tone positionally at that tile. (Add a
`Spill`/`Unbury` placeholder tone to `SfxLibrary`, like `Pickup` but lower/grittier.) This fires
only for **real** items — mining a decoy makes only the ordinary rock-mined sound, which is itself
the "nothing here" feedback.

**Listen chime (`MatchAudio`, optional polish):** while `Listening`, if ≥1 suspicious spot — a
`Buried` item **or** a still-rock decoy — is within `ListenItemRevealRadius` of the local miner,
emit a soft periodic ping (~every 0.8 s) panned toward and scaled by proximity to the nearest such
spot. The chime is the same for items and decoys (no tell). It rides the existing listen-audio path
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

**Decoys (`MapGeneratorItemsTests`):**
- `map.Decoys.Count == min(DecoyCount, candidate count)`.
- Every decoy is on an ordinary `Rock` tile bordering the traversable region — never `GoldRock`,
  `ImpermeableRock`, `Floor`, or a spawn.
- Decoys are **disjoint** from all item positions (no decoy on a real buried item).
- Deterministic: same seed twice → identical `Decoys`.
- Decoys are **not** items: building a `Simulation` from `map` (seeding only `map.Items`) leaves
  `sim.Items` free of any decoy position.

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
- **Reveal is client-side** — no new RPC. The buried positions already ride the snapshot, decoys
  ride the seed; Listen only gates *rendering* them. Consistent with fog and the project's naive
  full-state-sync tier.
- **Decoys are honest red herrings, not netcode** — they self-resolve (stop shimmering) the moment
  their rock is gone, needing zero state: the renderer just checks the live grid. The false-signal
  rate is `DecoyCount` against the buried count; tune so Listen stays a *useful gamble* — too few
  and it's a treasure map, too many and players stop trusting it. Default ~4 decoys vs. ~7 buried.
- **Decoys must read identically to buried items** — same shimmer, same chime, no kind/shape tell.
  If they ever diverge visually the bluff collapses; this is the one client-side invariant worth a
  careful play-test.
- **ReachCenter sparsity** carries over from 4c-2a (larger map, same base count) — unchanged here.
