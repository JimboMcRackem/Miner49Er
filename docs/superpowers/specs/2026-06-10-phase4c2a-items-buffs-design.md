# Phase 4c-2a — Item Framework & Auto-Apply Buffs — Design

**Date:** 2026-06-10
**Status:** Approved design, ready for implementation plan
**Builds on:** 4c-1 (status-effect engine + move-cadence migration, main @ d584a3f)

## Goal

Add collectible **items** to the map and the **first three buff items**, all consuming the
4c-1 status-effect engine. Items are deterministically placed at map generation, render
under fog like everything else, and **auto-apply** an instant timed self-buff when a miner
walks over them. This sub-phase also **removes the throwaway 4c-1 debug key**.

The three buffs:

| Item          | Effect channel | Aggregation | Effect                                   |
|---------------|----------------|-------------|------------------------------------------|
| Speed potion  | `MoveSpeed`    | multiply    | move cadence × `SpeedPotionFactor` for N s |
| Longer vision | `VisionRadius` | additive    | fog radius + `VisionBonus` for N s         |
| Bigger blast  | `BlastRadius`  | additive    | charges planted while active get +`BlastBonus` radius |

**Out of scope (deferred to 4c-2b):** the 1-slot inventory, the `use_item` verb, the
water-plank, and the slow-mold (carried items). 4c-2a ships only auto-apply pickups.

## Architecture

The change follows the patterns 4c-1 established. Items live in pure `Miner49er.Core` as a
deterministic map-gen output and an authoritative list on `Simulation`. They sync to clients
as a full list inside the per-tick snapshot, exactly like charges. The two new effect
channels reuse the generic `StatusEffect` engine; only their *aggregation rule* differs
(MoveSpeed multiplies, VisionRadius/BlastRadius add). Base vision radius migrates from the
Godot `MatchClient` constant into `SimConfig`, mirroring the 4c-1 base-move-speed migration,
so the sim can compute each miner's effective radius and ship it in the snapshot.

### Data flow

```
MapGenerator.PlaceItems (seeded)
        │  GeneratedMap.Items
        ▼
Simulation._items  ──Tick: PickUpItems()──►  ApplyEffect(buff)  +  ItemPickedUp event
        │                                            │
        │ SnapshotFactory.Capture                    │ EffectiveVisionRadius / EffectiveBlastBonus
        ▼                                            ▼
WorldSnapshot.Items[]   MinerSnapshot.VisionRadius   Charge.BlastBonus (captured at plant)
        │ SnapshotCodec
        ▼
MatchClient (renders items under fog; fog reads local miner's synced VisionRadius)
MatchAudio (diffs item list → pickup SFX near local miner)
```

---

## Section 1 — Item entity & deterministic placement (Core)

**New types** (`src/Miner49er.Core/Map/Item.cs`):

```csharp
namespace Miner49er.Core;

public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }   // 4c-2b appends WaterPlank, SlowMold
public readonly record struct Item(GridPos Pos, ItemKind Kind);
```

**`GeneratedMap`** gains `public required IReadOnlyList<Item> Items { get; init; }`.

**`MapConfig`** gains item-count knobs, scaled by map size:

```csharp
public int BaseItemCount { get; set; } = 9;      // items on the base 24×24 map
public int ItemsPerPlayer { get; set; } = 1;     // light scaling with player count / map growth
```

The effective count is `BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)`, computed in
`PlaceItems`. (This mirrors how width/height already scale by `SizePerPlayer * (PlayerCount - 1)`,
so bigger maps get proportionally more items.) `MapConfig.For` leaves the defaults for all
modes; ReachCenter's larger map naturally gets the same base count (acceptable — tune later
if the larger map feels sparse).

**`MapGenerator.PlaceItems`** — a new pass invoked in `Generate` **after** `PlaceGold`,
returning the placed list (added to `GeneratedMap`):

- Candidate tiles: positions in the **largest traversable `region`** that are `TileType.Floor`
  (so never water, never rock), excluding any tile in `spawns` (don't drop an item under a
  starting miner). Gold sits on Rock, so gold tiles are already excluded by the Floor filter.
- `Shuffle` the candidates with the same seeded `rng` (reuse the existing private `Shuffle`).
- Take `count = BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)` (clamped to the candidate
  count if the map is tiny).
- Assign kinds **round-robin** over `ItemKind` values in placement order (`i % kindCount`) — a
  deterministic, balanced spread rather than random-per-tile, so each match has a predictable
  mix.

`Generate` returns `new GeneratedMap { Grid = grid, Spawns = spawns, Center = center, Items = items }`.

The **host** seeds `Simulation` from `map.Items`. The **client** ignores its own regenerated
`map.Items` for rendering and uses the authoritative snapshot list instead (see Section 3) —
the client still regenerates the map for tiles/fog as today.

---

## Section 2 — Pickup, buffs & the two new channels (Core)

**Effect enums** (`src/Miner49er.Core/Sim/StatusEffect.cs`) — replace the throwaway debug kinds:

```csharp
public enum EffectChannel { MoveSpeed, VisionRadius, BlastRadius }
public enum EffectKind   { SpeedPotion, LongerVision, BiggerBlast }   // DebugSpeed/DebugSlow deleted
```

**`SimConfig`** gains the base vision radius (migrated from `MatchClient`) and per-buff tunables:

```csharp
public int VisionRadius { get; set; } = 5;          // base fog radius (was MatchClient const)

public double SpeedPotionFactor { get; set; } = 0.6; // move-cadence multiplier while active
public double SpeedPotionSeconds { get; set; } = 8.0;

public int VisionBonus { get; set; } = 3;            // +tiles of fog radius while active
public double VisionSeconds { get; set; } = 12.0;

public int BlastBonus { get; set; } = 1;             // +radius on charges planted while active
public double BlastSeconds { get; set; } = 12.0;
```

**`Simulation`** holds the authoritative item list:

```csharp
private readonly List<Item> _items = new();
public IReadOnlyList<Item> Items => _items;

public void AddItem(Item item) => _items.Add(item);   // host seeds these from GeneratedMap.Items
```

**Pickup pass** — `PickUpItems()` runs in `Tick` **after** movement/activity resolution
(placed right after `AdvanceActivities` / before or after `AdvanceCharges`; pickup is
position-based and independent of charges, so order among them doesn't matter — but it must
run after any movement so a miner who just stepped onto an item collects it this tick):

```csharp
private void PickUpItems()
{
    for (int i = _items.Count - 1; i >= 0; i--)
    {
        var item = _items[i];
        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.Pos != item.Pos) continue;
            _items.RemoveAt(i);
            ApplyBuff(m.Id, item.Kind);
            _events.Add(new ItemPickedUp(m.Id, item.Pos, item.Kind));
            break;   // one miner collects it
        }
    }
}

private void ApplyBuff(int minerId, ItemKind kind)
{
    switch (kind)
    {
        case ItemKind.SpeedPotion:
            ApplyEffect(minerId, EffectKind.SpeedPotion, EffectChannel.MoveSpeed,
                        Config.SpeedPotionFactor, Config.SpeedPotionSeconds);
            break;
        case ItemKind.LongerVision:
            ApplyEffect(minerId, EffectKind.LongerVision, EffectChannel.VisionRadius,
                        Config.VisionBonus, Config.VisionSeconds);
            break;
        case ItemKind.BiggerBlast:
            ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius,
                        Config.BlastBonus, Config.BlastSeconds);
            break;
    }
}
```

`Tick` gains a `PickUpItems();` call after `AdvanceActivities(dt)`.

**New event** (`SimEvent.cs`): `public sealed record ItemPickedUp(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent;`

**Per-channel aggregation** — new `Effective*` helpers on `Simulation`, alongside the existing
`EffectiveMoveSeconds` (which is unchanged — it already multiplies `MoveSpeed` magnitudes):

```csharp
public int EffectiveVisionRadius(int minerId) => EffectiveVisionRadius(_miners[minerId]);
private int EffectiveVisionRadius(Miner m)
{
    int bonus = 0;
    foreach (var e in m.EffectsInternal)
        if (e.Channel == EffectChannel.VisionRadius) bonus += (int)e.Magnitude;
    return Config.VisionRadius + bonus;
}

public int EffectiveBlastBonus(int minerId) => EffectiveBlastBonus(_miners[minerId]);
private int EffectiveBlastBonus(Miner m)
{
    int bonus = 0;
    foreach (var e in m.EffectsInternal)
        if (e.Channel == EffectChannel.BlastRadius) bonus += (int)e.Magnitude;
    return bonus;
}
```

**Bigger-blast captured at plant.** The blast buff is timed, but a fuse outlives most buff
windows, so the bonus is **snapshotted when the charge is planted** rather than read at
detonation. The charge "remembers" the planter's bonus:

- `Charge` gains `public int BlastBonus { get; }`, set via the constructor.
- In `CompleteActivity` (planting branch), build the charge with the owner's current bonus:
  `_charges.Add(new Charge(m.Id, target, Config.FuseSeconds, EffectiveBlastBonus(m.Id)));`
- `Detonate` adds it to both radii:
  `int r = Config.BlastRockRadius + charge.BlastBonus;` and the kill check uses
  `Config.BlastKillRadius + charge.BlastBonus`.

This makes the buff intuitive (grab it, plant, big boom) regardless of fuse timing, and
detonation needs no back-reference to the (possibly dead) owner.

---

## Section 3 — Netcode (snapshot sync)

Two additions, both following the existing `Charges` / `MoveSeconds` shapes exactly.

**`Snapshots.cs`:**

```csharp
public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind);

public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius);   // + VisionRadius

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, float SecondsRemaining = -1f);   // + Items
```

**`SnapshotFactory.Capture`:** populate `m.VisionRadius` from `sim.EffectiveVisionRadius(m.Id)`
(like `MoveSeconds` uses `EffectiveMoveSeconds`), and build `Items` by projecting `sim.Items`
to `ItemSnapshot(item.Pos.X, item.Pos.Y, item.Kind)`.

**`SnapshotCodec`:**
- Miner block writes/reads the extra `int VisionRadius` after `MoveSeconds`.
- After the charge block, write `Items.Count` then each `(int X, int Y, int (ItemKind)Kind)`;
  read symmetrically. (Same length-prefixed pattern as charges.)

Items are full-listed every tick — they're few and static-until-collected; the list simply
shrinks on pickup. No pickup event crosses the wire; the client infers a pickup from an item
vanishing from the list (used by the SFX in Section 4).

---

## Section 4 — Godot layer (render, fog, debug-key removal)

**Render (`WorldRenderer`):** `MatchClient` exposes the snapshot item list (e.g.
`public IReadOnlyList<ItemSnapshot> Items`). `WorldRenderer._Draw` draws each item as a
placeholder colored glyph keyed by `Kind` — e.g. a small filled diamond/circle:
green = SpeedPotion, cyan = LongerVision, orange = BiggerBlast — **only on tiles the local fog
marks visible**, so items hidden in the dark stay hidden (consistent with tile fog). Use the
existing fog source `FogRenderer`/`MatchClient` already consults for tile visibility.

**Fog (`MatchClient`):** delete `public const int VisionRadius = 5;`. `UpdateFog` reads the
**local miner's synced `VisionRadius`** from its `MinerSnapshot` and calls
`Visibility.Compute(Grid, origin, localMiner.VisionRadius)`. (Base now lives in `SimConfig`;
the host computes the effective value and ships it per miner.)

**Remove the throwaway debug key** (all `// DEBUG(4c-1)`-tagged):
- `Main.cs`: the `_debugBoostPressed` field and the B-key edge-detect block in `_PhysicsProcess`.
- `NetworkManager.cs`: `SendDebugSpeed` / `ReceiveDebugSpeed`.
- `MatchHost.cs`: `ApplyDebugSpeed`.

**Pickup SFX (`MatchAudio`):** add a `Pickup` placeholder to `SfxLibrary` (e.g. a short bright
`Tone`, like `Drip` but quicker/higher). In `MatchAudio._Process`, keep a `_prevItems`
snapshot (a `HashSet<(int x, int y)>` of item positions). Each frame, any position present
last frame but absent now is a collected item; if it coincides with (or is adjacent to) the
local miner's position, play `SfxLibrary.Pickup` positionally at that tile. Client-derived from
the synced item list, no netcode — same approach as the splash-on-drown derivation already in
this file.

---

## Section 5 — Tests (Core, xUnit)

**Placement (`MapGeneratorItemsTests` or extend existing map tests):**
- Deterministic: `Generate` with the same seed twice yields identical `Items` (positions + kinds).
- Items land only on `Floor` tiles inside the traversable region; never on a spawn tile.
- Count equals `BaseItemCount + ItemsPerPlayer * (PlayerCount - 1)` (and is clamped on a tiny map).
- Kinds are round-robin balanced over the placement order.

**Pickup (`SimulationItemsTests`):**
- A living miner moving onto an item removes it from `sim.Items` and gains the matching effect;
  ticking advances the move + pickup.
- A second miner arriving at the same tile finds nothing (item already gone).
- A dead miner on an item tile does not collect it.
- `ItemPickedUp` event is drained with the right `MinerId` / `Pos` / `Kind`.

**Aggregation (`StatusEffectTests` extensions):**
- `EffectiveVisionRadius` = `Config.VisionRadius + VisionBonus` while a LongerVision effect is
  active; reverts to base after expiry.
- `EffectiveBlastBonus` sums active BlastRadius magnitudes; 0 with none active.
- `EffectiveMoveSeconds` regression: SpeedPotion multiplies cadence as before.

**Bigger-blast capture (`SimulationExplosiveTests` extension):**
- A charge planted while a BiggerBlast effect is active carries `BlastBonus`; detonation
  destroys rock / kills out to the enlarged radius.
- A charge planted after the buff expired carries no bonus (baseline radius).

**Codec/factory (`SnapshotCodecTests` / `SnapshotFactoryTests` extensions):**
- Round-trip a snapshot with a non-empty `Items` list and miners carrying a non-default
  `VisionRadius`; assert equality.
- `SnapshotFactory.Capture` populates `Items` from `sim.Items` and `VisionRadius` from
  `EffectiveVisionRadius`.

**Migration:** update existing `StatusEffectTests` / `MovementCadenceTests` that referenced
`EffectKind.DebugSpeed` / `DebugSlow` to use `SpeedPotion` (and the new kinds) instead.

---

## Risks & notes

- **Tick ordering for pickup:** `PickUpItems` must run after movement so a miner who stepped
  onto an item this tick collects it. Placing the call right after `AdvanceActivities(dt)` in
  `Tick` satisfies this (movement happens via `TryMove` outside `Tick`; by the time `Tick`
  runs the miner is already on the tile).
- **`MinerSnapshot` is positional in the codec** — adding `VisionRadius` means updating both
  the `Write` loop and the `Read` constructor call (9 → 10 fields). 4c-1 already did this for
  `MoveSeconds`; same edit shape.
- **ReachCenter sparsity:** the larger 40×40 ReachCenter map uses the same `BaseItemCount`;
  acceptable for now, tunable via `MapConfig.For` later if it feels thin.
- **No new RPCs.** Everything rides the existing per-tick snapshot; the item list and per-miner
  vision radius are pure additive fields. This keeps 4c-2a within the naive full-state-sync model.
