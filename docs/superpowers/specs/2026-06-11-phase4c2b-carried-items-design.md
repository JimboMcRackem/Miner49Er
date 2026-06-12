# Phase 4c-2b — Carried Items Design

**Status:** Approved (design); spec pending user review
**Date:** 2026-06-11
**Builds on:** 4c-2a (auto-applied timed buffs) and 4c-2c (item placement: Toolbox/Buried/Loose, decoys, Listen reveal)

## Goal

Add a 1-slot inventory and a context-sensitive **Use** verb (Space, already bound to
`InputBindings.UseItem` and currently unused), plus two **carried** items the player
grabs and triggers deliberately:

- **Water-plank** — places a permanent, walkable, flood-immune `Plank` tile on the faced
  water tile (bridges shallow *or* deep water).
- **Slow-mold** — drops a timed trap patch on the user's own tile; any *other* miner who
  steps onto it is slowed for a few seconds. The patch decays after a tunable lifetime.

The three existing items (SpeedPotion, LongerVision, BiggerBlast) keep their current
**auto-apply on walk-over** behavior unchanged. This is a mixed model: buffs auto-apply,
the two new items are carried and triggered.

## Approach (chosen: A — reuse existing channels)

Each effect uses the representation that already fits it, and both lean on mechanisms
already in the codebase:

- **Plank is terrain** → a new `TileType.Plank`, placed through the existing `TileChange`
  snapshot path (the same mechanism rock-mining and flooding already use). Flood-immunity
  falls out for free because the flood only converts `Floor`/water tiles.
- **Mold is a timed placed object** → a new synced entity list (`MoldPatch{Pos,
  RemainingSeconds}`) modeled exactly like the existing items/charges lists, applying a
  `MoveSpeed` `StatusEffect` through the existing §3.5 status-effect engine.
- **Held item rides `MinerSnapshot`** as an `int` (-1 = empty) for the HUD.

Rejected alternatives: **B** (everything as one entity list — forces the plank to stop
being a real tile, fighting the `TileType` walkability model); **C** (everything as tiles
— tiles have no per-instance lifetime, so mold's decay can't ride the grid).

---

## Section 1 — Core data model: carried vs auto-apply

Two new kinds join the enum:

```csharp
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold }
```

A single predicate draws the carried/auto-apply line:

```csharp
public static bool IsCarried(this ItemKind k) => k is ItemKind.WaterPlank or ItemKind.SlowMold;
```

The miner gains a single inventory slot:

```csharp
// Miner.cs
public ItemKind? Held { get; internal set; }   // null = empty hand
```

`PickUpItems()` (the walk-over path) gains one guard so carried kinds are **not**
auto-collected — they are left on the ground for the Use verb to grab:

```csharp
// in PickUpItems loop, after the existing Buried guard:
if (item.Kind.IsCarried()) continue;   // grabbed via Use verb, not walk-over
```

No new collections at this layer; the mold list arrives in Section 4.

---

## Section 2 — The Space verb (pickup / swap / use)

`InputBindings.UseItem` (Space) becomes a single context-sensitive action. The input →
host path mirrors the existing mine/plant plumbing exactly.

**Input (client).** `InputSender._PhysicsProcess` adds one edge-triggered read:

```csharp
bool mine = Input.IsActionJustPressed(InputBindings.Pickaxe);
bool plant = Input.IsActionJustPressed(InputBindings.Plant);
bool use   = Input.IsActionJustPressed(InputBindings.UseItem);
if (mine || plant || use) NetworkManager.Instance.SendAction(mine, plant, use);
```

**Transport.** `SendAction(bool,bool,bool)`, the `[Rpc] ReceiveAction(bool,bool,bool)`,
and `MatchHost.SetAction(peerId, mine, plant, use)` all gain the third bool. `MatchHost`
adds a `_pendingUse` `HashSet<int>` drained in `StepOnce` → `_sim.TryUseItem(minerId)`,
the same shape as `_pendingMine`/`_pendingPlant`.

**Host logic — `Simulation.TryUseItem(int id)`** resolves context in priority order:

1. **Standing on a carried ground item** → pick it up. Empty hand: take it. Full hand:
   **swap** — the held kind is dropped onto the current tile as a `Loose` item and the
   ground item is taken (net: one item on the tile, the other in hand). Emits `ItemPickedUp`.
2. **Otherwise, use what's held** (no-op if hand empty):
   - `WaterPlank` → place a plank on the faced water tile (Section 3); clears `Held`.
   - `SlowMold` → drop a mold patch on the miner's own tile (Section 4); clears `Held`.

A use that can't apply (plank not facing water, empty hand, dead miner) is a silent no-op.

```csharp
public bool TryUseItem(int id)
{
    var m = _miners[id];
    if (!m.Alive) return false;

    // 1. pickup / swap when standing on a carried ground item
    for (int i = _items.Count - 1; i >= 0; i--)
    {
        var it = _items[i];
        if (it.Pos != m.Pos || it.Placement == ItemPlacement.Buried || !it.Kind.IsCarried()) continue;
        var taken = it.Kind;
        if (m.Held is { } heldKind) _items[i] = new Item(m.Pos, heldKind, ItemPlacement.Loose);
        else                        _items.RemoveAt(i);
        m.Held = taken;
        _events.Add(new ItemPickedUp(m.Id, m.Pos, taken));
        return true;
    }

    // 2. use held
    if (m.Held is not { } held) return false;
    return held switch
    {
        ItemKind.WaterPlank => TryPlacePlank(m),   // Section 3
        ItemKind.SlowMold   => DropMold(m),        // Section 4
        _ => false,
    };
}
```

---

## Section 3 — Water-plank

A new walkable, flood-immune tile type:

```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank }
```

`TileTypeExtensions` treats `Plank` as solid dry ground:

| predicate | `Plank` |
|---|---|
| `IsWalkable` | true (spawns/fog/drip treat it as ground) |
| `IsEnterable` | true |
| `IsLethal` | false |
| `MoveCostMultiplier` | 1.0 (no slow, unlike shallow water's 2.0) |
| `IsWater` | false |
| `IsMinable` / `IsBlastable` | false |

**Placement — `TryPlacePlank(Miner m)`:** the faced tile must be in-bounds and water
(shallow *or* deep). On success it becomes `Plank`, the hand clears, `PlankPlaced` fires:

```csharp
private bool TryPlacePlank(Miner m)
{
    var target = m.Pos + m.Facing.ToOffset();
    if (!Grid.InBounds(target) || !Grid.Get(target).IsWater()) return false;
    Grid.Set(target, TileType.Plank);
    m.Held = null;
    _events.Add(new PlankPlaced(target));
    return true;
}
```

**Flood-immunity is free.** `AdvanceFlood` only converts tiles that are `Floor` or water
(`if (cur != TileType.Floor && !cur.IsWater()) continue;`). `Plank` is neither, so the
flood permanently skips it — a plank laid across the deep-water zone stays solid for the
rest of the match.

**Sync.** `PlankPlaced` rides the existing `TileChange` path. `MatchHost.StepOnce` adds:

```csharp
case PlankPlaced pp:
    changes.Add(new TileChange(pp.Pos.X, pp.Pos.Y, false, TileType.Plank));
    break;
```

New event: `public sealed record PlankPlaced(GridPos Pos) : SimEvent;`

---

## Section 4 — Slow-mold

Mold is a timed placed entity (not a tile, because it needs a per-instance countdown):

```csharp
// Sim/MoldPatch.cs
public sealed class MoldPatch
{
    public GridPos Pos { get; }
    public double RemainingSeconds { get; internal set; }
    internal MoldPatch(GridPos pos, double seconds) { Pos = pos; RemainingSeconds = seconds; }
}

// Simulation.cs
private readonly List<MoldPatch> _molds = new();
public IReadOnlyList<MoldPatch> Molds => _molds;
```

**Drop — `DropMold(Miner m)`** (from the Use verb): drops a patch on the miner's own tile
with the configured lifetime, clears the hand, emits `MoldDropped`. Re-dropping on a tile
that already has a patch **refreshes** its timer rather than stacking a duplicate.

```csharp
private bool DropMold(Miner m)
{
    var existing = _molds.FirstOrDefault(mo => mo.Pos == m.Pos);
    if (existing is not null) existing.RemainingSeconds = Config.MoldSeconds;
    else _molds.Add(new MoldPatch(m.Pos, Config.MoldSeconds));
    m.Held = null;
    _events.Add(new MoldDropped(m.Pos));
    return true;
}
```

**Decay** — a new `AdvanceMolds(dt)` step in `Tick` (alongside `AdvanceEffects` /
`AdvanceCharges`) counts each patch down and removes it at zero, emitting `MoldExpired`:

```csharp
private void AdvanceMolds(double dt)
{
    for (int i = _molds.Count - 1; i >= 0; i--)
    {
        _molds[i].RemainingSeconds -= dt;
        if (_molds[i].RemainingSeconds <= 0)
        {
            _events.Add(new MoldExpired(_molds[i].Pos));
            _molds.RemoveAt(i);
        }
    }
}
```

**The slow — applied on step-on, not on standing.** The slow fires inside `TryMove`, the
moment a miner *enters* a mold tile. Because `DropMold` happens under a stationary miner
(no `TryMove`), the **placer is not slowed by their own mold** — they're only caught if
they leave and step back on. Any miner who walks onto the patch eats a `SlowMold`
`MoveSpeed` effect (magnitude > 1) for `MoldSlowSeconds`, which lingers after they walk off
(handled by the existing refresh-don't-compound engine):

```csharp
// in TryMove, after the move + lethal/center checks, before setting the move cooldown:
if (m.Alive && _molds.Any(mo => mo.Pos == target))
    ApplyEffect(id, EffectKind.SlowMold, EffectChannel.MoveSpeed,
                Config.MoldSlowFactor, Config.MoldSlowSeconds);
```

`EffectKind` gains `SlowMold` (already reserved by a comment in `StatusEffect.cs`). Because
`MoveSpeed` effects multiply the move cadence and `MoldSlowFactor > 1`, this slows; it
stacks multiplicatively with shallow-water but, being one-per-kind, never compounds with
itself.

New events:
`public sealed record MoldDropped(GridPos Pos) : SimEvent;`
`public sealed record MoldExpired(GridPos Pos) : SimEvent;`

---

## Section 5 — Sync (snapshot + codec)

Two additions: the **held item** on the miner, and the **mold list** as a new collection.

**Held item.** `MinerSnapshot` gains a trailing `int Held` (-1 = empty, else `(int)ItemKind`):

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held);
```

`SnapshotFactory.Capture` maps `m.Held is { } h ? (int)h : -1`. `SnapshotCodec` writes/reads
one extra `int` at the end of the miner block.

**Mold list.**

```csharp
public readonly record struct MoldSnapshot(int X, int Y, double RemainingSeconds);

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    float SecondsRemaining = -1f);
```

`SnapshotFactory` captures
`sim.Molds.Select(mo => new MoldSnapshot(mo.Pos.X, mo.Pos.Y, mo.RemainingSeconds))`.
`SnapshotCodec.Write`/`Read` gain a count-prefixed mold block (X, Y, RemainingSeconds)
right after the items block, mirroring how items serialize. `RemainingSeconds` is synced so
clients can fade the patch as it decays.

**Plank needs no new sync** — it travels as a `TileChange`. The existing `ItemSnapshot`
already carries the new `WaterPlank`/`SlowMold` kinds for ground items waiting to be
grabbed (its `Kind` field is already `ItemKind`; the enum simply grew).

---

## Section 6 — Godot rendering + SFX

**Plank tile.** `WorldRenderer` already maps `TileType` → color when drawing the grid; add
a plank-brown entry (`PlankColor` ≈ `b5803a`, distinct from rock/toolbox). Being a tile,
fog/visibility already cover it; `IsWalkable` true means fog/drip treat it as ground.

**Mold patches.** `MatchClient` gains a `Molds` list populated each tick from
`MoldSnapshot` (parallel to items/decoys). `WorldRenderer` draws each as a sickly-green
patch (`MoldColor` ≈ `6f8f3a`) on its tile, with **alpha driven by `RemainingSeconds`** so
it visibly fades over its last few seconds. Molds draw only within the local miner's fog,
consistent with other world entities.

**Held-item HUD.** The local miner's `Held` (from `MinerSnapshot`) shows as a small
indicator (`P` plank, `M` mold) in a fixed corner slot; empty hand = empty slot.

**SFX** (`SfxLibrary` tones + `MatchAudio` snapshot diffs, the existing diff-driven pattern):

| event | client-side detection | tone |
|---|---|---|
| pickup / swap | local miner's `Held` changed this tick | `Grab` |
| plank placed | a `TileChange` to `TileType.Plank` near the local miner | `Plank` (wooden knock) |
| mold dropped | `Molds` list gained a patch | `Squelch` |
| stepped on mold | derived from a sudden local `MoveSeconds` jump on a mold tile, or omitted | `Squelch` (lower) |

Miner status effects aren't in the snapshot (only the *resolved* `MoveSeconds` /
`VisionRadius` are). Rather than widen the wire format for one audio cue, the step-on-mold
sound is derived from a `MoveSeconds` jump or simply dropped if unreliable — it is audio
polish, not core. The other three cues are clean diffs.

---

## Section 7 — Map seeding + tunables

**Seeding.** Plank and mold enter the world as ordinary ground items `MapGenerator`
scatters, like the visible buff items — placed as visible `Toolbox`/`Loose` items on
walkable Floor tiles (not buried). `MapConfig` gains two counts:

```csharp
public int WaterPlankCount { get; set; } = 3;
public int SlowMoldCount   { get; set; } = 3;
```

`MapGenerator`'s item-placement step adds `WaterPlankCount` `WaterPlank` and `SlowMoldCount`
`SlowMold` items to the visible scatter pool, on walkable Floor tiles, deterministic from
the existing map seed (so all clients regenerate identically — no extra sync). They surface
in `GeneratedMap.Items` and the host seeds them via the existing `AddItem` loop.

**SimConfig tunables** (defaults to confirm during play-test):

```csharp
public double MoldSeconds     { get; set; } = 20.0;  // patch lifetime before it decays
public double MoldSlowFactor  { get; set; } = 1.6;   // move-cadence ×1.6 (slower) when stepped on
public double MoldSlowSeconds { get; set; } = 3.0;   // how long the slow lingers after stepping on
```

`MoldSlowFactor > 1` slows (consistent with shallow-water's 2.0, opposite the speed-potion's
0.6). Plank has no tunables — placement is binary and the tile is permanent.

---

## Section 8 — Testing

All sim logic is engine-free (xUnit in `Miner49er.Core.Tests`); the existing 161 tests stay
green. New coverage:

**Inventory & Use verb (`TryUseItem`):**
- Walking over a carried item does **not** auto-collect it (still on ground, hand empty).
- Walking over a buff item **does** auto-apply (regression guard on the `IsCarried` skip).
- Use while on a carried item, empty hand → item moves into `Held`, leaves ground.
- Use while on a carried item, full hand → **swap**: old held kind is now a `Loose` ground
  item on the tile, new kind in hand, exactly one item on the tile.
- Use with empty hand, not on an item → no-op, returns false.
- Dead miner → no-op.

**Water-plank (`TryPlacePlank`):**
- Facing shallow water → tile becomes `Plank`, hand clears, `PlankPlaced` emitted.
- Facing deep water → same (any-water rule).
- Facing non-water → no-op, hand keeps the plank.
- A `Plank` is walkable, non-lethal; a flood tick over its position leaves it `Plank`
  (flood-immunity).
- `TileType` predicate table for `Plank` (walkable/enterable/not-lethal/not-water/cost 1.0).

**Slow-mold:**
- Drop places a `MoldPatch` with `RemainingSeconds == MoldSeconds`, hand clears,
  `MoldDropped` emitted.
- Re-drop on an occupied tile **refreshes** the timer, no duplicate patch.
- The **placer is not slowed** (dropped under a standing miner → no `SlowMold` effect).
- A miner who **moves onto** a mold tile gains a `SlowMold` `MoveSpeed` effect of
  `MoldSlowFactor` for `MoldSlowSeconds`.
- Decay: after `MoldSeconds` of ticks the patch is gone and `MoldExpired` fired.
- The slow lingers after stepping off and refreshes (never compounds) on re-entry.

**Sync round-trip (`SnapshotCodec`):**
- A `WorldSnapshot` with held items (incl. -1 empty) and a populated mold list survives
  `Write`→`Read` byte-for-byte equal.
- `MinerSnapshot.Held` and `MoldSnapshot.RemainingSeconds` carry through
  `SnapshotFactory.Capture`.

**Map seeding:**
- `MapGenerator` with the new counts yields exactly `WaterPlankCount` plank +
  `SlowMoldCount` mold items, all on walkable Floor tiles, deterministic for a fixed seed.

The Godot layer (renderer colors, HUD slot, SFX diffs) is verified by play-test, consistent
with the rest of the adapter.
