# Phase 4c-2b — Carried Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 1-slot inventory and a context-sensitive Use verb (Space), with two carried items — a water-plank that bridges water with a permanent flood-immune `Plank` tile, and a slow-mold that drops a timed trap patch slowing miners who step on it.

**Architecture:** Pure-C# `Miner49er.Core` engine (xUnit-tested, 4-space indent) holds all sim logic; the thin Godot adapter (`game/`, TAB indent) renders and transports. Plank is a new `TileType` synced via the existing `TileChange` path; mold is a new synced timed-entity list applying a `MoveSpeed` status effect; the held item rides `MinerSnapshot`.

**Tech Stack:** Godot 4.6.3 (.NET/Mono) + C#, .NET 8, xUnit. Build: `dotnet build Miner49er.sln`. Test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`.

**Spec:** `docs/superpowers/specs/2026-06-11-phase4c2b-carried-items-design.md`

---

## File Structure

**Core (engine, unit-tested):**
- `src/Miner49er.Core/Map/Item.cs` — add `WaterPlank`/`SlowMold` kinds + `IsCarried` predicate (T1)
- `src/Miner49er.Core/Sim/Miner.cs` — add `Held` slot (T1)
- `src/Miner49er.Core/Sim/StatusEffect.cs` — add `SlowMold` effect kind (T1)
- `src/Miner49er.Core/Sim/SimConfig.cs` — mold tunables (T1)
- `src/Miner49er.Core/Grid/TileType.cs` — add `Plank` + walkability (T2)
- `src/Miner49er.Core/Sim/SimEvent.cs` — `PlankPlaced`, `MoldDropped`, `MoldExpired` (T3/T4)
- `src/Miner49er.Core/Sim/MoldPatch.cs` — new timed entity (T4)
- `src/Miner49er.Core/Sim/Simulation.cs` — `TryUseItem`, plank placement, mold drop/decay/slow, `PickUpItems` guard (T1/T3/T4)
- `src/Miner49er.Core/Map/MapConfig.cs` + `Map/MapGenerator.cs` — seed carried items (T5)
- `src/Miner49er.Core/Net/Snapshots.cs` + `Net/SnapshotCodec.cs` + `Net/SnapshotFactory.cs` — sync (T6)

**Godot adapter (build + play-test verified):**
- `game/net/NetworkManager.cs` + `game/net/MatchHost.cs` + `game/net/InputSender.cs` — Use-verb transport (T7)
- `game/WorldRenderer.cs` + `game/net/MatchClient.cs` + `game/Main.cs` — render + HUD (T8)
- `game/audio/SfxLibrary.cs` + `game/net/MatchAudio.cs` — SFX (T8)

---

## Task 1: Core data model + config tunables

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Sim/Miner.cs:11` (after `GoldCollected`)
- Modify: `src/Miner49er.Core/Sim/StatusEffect.cs:4`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`PickUpItems`)
- Test: `src/Miner49er.Core.Tests/SimulationCarriedItemsTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationCarriedItemsTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class SimulationCarriedItemsTests
{
    private static Simulation Sim(out Miner m)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void A_new_miner_starts_with_an_empty_hand()
    {
        Sim(out var m);
        Assert.Null(m.Held);
    }

    [Fact]
    public void Walking_over_a_carried_item_does_not_auto_collect_it()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));
        sim.TryMove(1, Direction.East); // step onto (2,2)
        sim.Tick(0.0);                  // walk-over pickup pass runs in Tick
        Assert.Single(sim.Items);       // still on the ground
        Assert.Null(m.Held);            // not taken
    }

    [Fact]
    public void WaterPlank_and_SlowMold_report_as_carried()
    {
        Assert.True(ItemKind.WaterPlank.IsCarried());
        Assert.True(ItemKind.SlowMold.IsCarried());
        Assert.False(ItemKind.SpeedPotion.IsCarried());
        Assert.False(ItemKind.LongerVision.IsCarried());
        Assert.False(ItemKind.BiggerBlast.IsCarried());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationCarriedItemsTests"`
Expected: FAIL — `ItemKind` has no `WaterPlank`/`SlowMold`, no `IsCarried`, `Miner.Held` undefined (compile errors).

- [ ] **Step 3: Extend `ItemKind` and add `IsCarried`**

In `src/Miner49er.Core/Map/Item.cs`, replace the enum line and add an extension class below it:

```csharp
/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast, WaterPlank, SlowMold }

public static class ItemKindExtensions
{
    /// <summary>Carried kinds are not auto-applied on walk-over; they go into the
    /// 1-slot inventory and are triggered with the Use verb. The other kinds auto-apply.</summary>
    public static bool IsCarried(this ItemKind k) => k is ItemKind.WaterPlank or ItemKind.SlowMold;
}
```

- [ ] **Step 4: Add the inventory slot to `Miner`**

In `src/Miner49er.Core/Sim/Miner.cs`, after `public int GoldCollected { get; internal set; }` (line 11):

```csharp
    public ItemKind? Held { get; internal set; }   // null = empty hand (1-slot inventory)
```

- [ ] **Step 5: Add the `SlowMold` effect kind**

In `src/Miner49er.Core/Sim/StatusEffect.cs`, replace the `EffectKind` line:

```csharp
public enum EffectKind { SpeedPotion, LongerVision, BiggerBlast, SlowMold }
```

- [ ] **Step 6: Add mold tunables to `SimConfig`**

In `src/Miner49er.Core/Sim/SimConfig.cs`, after the `BlastSeconds` line (line 23):

```csharp

    public double MoldSeconds { get; set; } = 20.0;      // patch lifetime before it decays
    public double MoldSlowFactor { get; set; } = 1.6;    // move-cadence multiplier when stepped on (>1 = slower)
    public double MoldSlowSeconds { get; set; } = 3.0;   // how long the slow lingers after stepping on
```

- [ ] **Step 7: Guard carried kinds out of the walk-over pickup**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `PickUpItems`, after the existing Buried guard (`if (item.Placement == ItemPlacement.Buried) continue;`):

```csharp
            if (item.Kind.IsCarried()) continue;        // grabbed via the Use verb, not walk-over
```

- [ ] **Step 8: Run the new test + the full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — new tests green, all existing 161 still green (buff items remain non-carried, so `SimulationItemsTests` auto-apply tests are unaffected).

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Sim/Miner.cs src/Miner49er.Core/Sim/StatusEffect.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationCarriedItemsTests.cs
git commit -m "feat(core): carried item kinds, 1-slot inventory, mold tunables

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Water-plank tile type

**Files:**
- Modify: `src/Miner49er.Core/Grid/TileType.cs`
- Test: `src/Miner49er.Core.Tests/TileTypePlankTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/TileTypePlankTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TileTypePlankTests
{
    [Fact]
    public void Plank_is_walkable_and_enterable()
    {
        Assert.True(TileType.Plank.IsWalkable());
        Assert.True(TileType.Plank.IsEnterable());
    }

    [Fact]
    public void Plank_is_not_lethal_and_not_water()
    {
        Assert.False(TileType.Plank.IsLethal());
        Assert.False(TileType.Plank.IsWater());
    }

    [Fact]
    public void Plank_has_no_move_slowdown()
    {
        Assert.Equal(1.0, TileType.Plank.MoveCostMultiplier(), 3);
    }

    [Fact]
    public void Plank_cannot_be_mined_or_blasted()
    {
        Assert.False(TileType.Plank.IsMinable());
        Assert.False(TileType.Plank.IsBlastable());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~TileTypePlankTests"`
Expected: FAIL — `TileType.Plank` does not exist (compile error).

- [ ] **Step 3: Add `Plank` to the enum**

In `src/Miner49er.Core/Grid/TileType.cs`, replace the enum line:

```csharp
public enum TileType { Floor, Rock, GoldRock, ImpermeableRock, ShallowWater, DeepWater, Plank }
```

- [ ] **Step 4: Make `Plank` walkable and enterable**

In the same file, update `IsWalkable` and `IsEnterable`:

```csharp
    /// <summary>Safe to stand on (used for spawns, fog, drip placement, reachability).</summary>
    public static bool IsWalkable(this TileType t) => t is TileType.Floor or TileType.ShallowWater or TileType.Plank;

    /// <summary>A miner may move onto this tile. Deep water is enterable but lethal.</summary>
    public static bool IsEnterable(this TileType t) =>
        t is TileType.Floor or TileType.ShallowWater or TileType.DeepWater or TileType.Plank;
```

`IsLethal` (DeepWater only), `MoveCostMultiplier` (ShallowWater special-case else 1.0), `IsWater`, `IsMinable`, `IsBlastable` already return the correct values for `Plank` without changes.

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~TileTypePlankTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Grid/TileType.cs src/Miner49er.Core.Tests/TileTypePlankTests.cs
git commit -m "feat(core): walkable flood-immune Plank tile type

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Use verb (pickup/swap) + water-plank placement

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `PlankPlaced`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (add `TryUseItem`, `TryPlacePlank`)
- Test: `src/Miner49er.Core.Tests/SimulationUseVerbTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationUseVerbTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationUseVerbTests
{
    [Fact]
    public void Using_while_standing_on_a_carried_item_picks_it_up()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        Assert.True(sim.TryUseItem(1));
        Assert.Equal(ItemKind.WaterPlank, m.Held);
        Assert.Empty(sim.Items);
    }

    [Fact]
    public void Using_with_a_full_hand_on_a_ground_item_swaps_them()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 2));
        sim.AddItem(new Item(new GridPos(1, 2), ItemKind.SlowMold));   // under the miner
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank)); // one tile east

        Assert.True(sim.TryUseItem(1));              // pick up the mold
        Assert.Equal(ItemKind.SlowMold, m.Held);
        sim.TryMove(1, Direction.East);              // move onto the plank tile
        Assert.True(sim.TryUseItem(1));              // swap

        Assert.Equal(ItemKind.WaterPlank, m.Held);
        var onGround = Assert.Single(sim.Items.Where(i => i.Pos == new GridPos(2, 2)));
        Assert.Equal(ItemKind.SlowMold, onGround.Kind);          // dropped held item
        Assert.Equal(ItemPlacement.Loose, onGround.Placement);
    }

    [Fact]
    public void Using_an_empty_hand_on_an_empty_tile_is_a_noop()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        Assert.False(sim.TryUseItem(1));
    }

    [Fact]
    public void Using_a_water_plank_facing_water_lays_a_plank_tile()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.ShallowWater); // north of the miner
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        sim.TryUseItem(1);                          // pick up the plank (hand = WaterPlank)
        sim.GetMiner(1).Facing = Direction.North;   // face the water without moving onto it
        Assert.True(sim.TryUseItem(1));             // place the plank northward

        Assert.Equal(TileType.Plank, sim.Grid.Get(new GridPos(2, 1)));
        Assert.Null(m.Held);                        // hand emptied
        Assert.Single(sim.DrainEvents().OfType<PlankPlaced>());
    }

    [Fact]
    public void Using_a_water_plank_facing_non_water_does_nothing()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.WaterPlank));

        sim.TryUseItem(1);
        sim.GetMiner(1).Facing = Direction.North;
        Assert.False(sim.TryUseItem(1));            // rock is not water
        Assert.Equal(ItemKind.WaterPlank, m.Held);  // still held
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void A_plank_tile_survives_a_flood_tick()
    {
        // 5x5: edges flood inward. Place a plank on a tile inside the flood zone and tick the clock.
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), timeLimitSeconds: 10.0, flooding: true);
        sim.Grid.Set(new GridPos(1, 1), TileType.Plank); // a tile the flood would otherwise convert
        sim.Tick(10.0);                                   // full progress -> flood reaches inner ring
        Assert.Equal(TileType.Plank, sim.Grid.Get(new GridPos(1, 1)));
    }
}
```

(`sim.GetMiner(1).Facing = ...` is valid from tests: `Miner49er.Core.csproj` sets `[InternalsVisibleTo("Miner49er.Core.Tests")]`, so the test assembly can write `internal set` members like `Facing`.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationUseVerbTests"`
Expected: FAIL — `TryUseItem` and `PlankPlaced` undefined.

- [ ] **Step 3: Add the `PlankPlaced` event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, after the `ItemUnburied` line:

```csharp
public sealed record PlankPlaced(GridPos Pos) : SimEvent;
```

- [ ] **Step 4: Implement `TryUseItem` and `TryPlacePlank`**

In `src/Miner49er.Core/Sim/Simulation.cs`, add these methods (place them just after `PickUpItems`):

```csharp
    /// <summary>The Use verb (Space). Context-sensitive: if the miner stands on a carried
    /// ground item, pick it up (swapping with whatever is held); otherwise use the held item.</summary>
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

        // 2. use what is held
        if (m.Held is not { } held) return false;
        return held switch
        {
            ItemKind.WaterPlank => TryPlacePlank(m),
            _ => false,   // SlowMold wired in Task 4
        };
    }

    // Lays a permanent, flood-immune Plank tile on the faced water tile (shallow or deep).
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

- [ ] **Step 5: Run the test + full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — new use-verb/plank tests green, all prior tests still green.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationUseVerbTests.cs
git commit -m "feat(core): Use verb with pickup/swap and water-plank placement

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Slow-mold (entity, drop, decay, step-on slow)

**Files:**
- Create: `src/Miner49er.Core/Sim/MoldPatch.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs` (add `MoldDropped`, `MoldExpired`)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (mold list, `DropMold`, `AdvanceMolds`, slow in `TryMove`, dispatch arm in `TryUseItem`)
- Test: `src/Miner49er.Core.Tests/SimulationMoldTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SimulationMoldTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMoldTests
{
    private static Simulation MoldSim(out Miner placer)
    {
        var sim = new Simulation(new TileGrid(7, 7, TileType.Floor), new SimConfig());
        placer = sim.AddMiner(1, new GridPos(3, 3));
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold)); // under the placer
        sim.TryUseItem(1);                                            // pick it up
        return sim;
    }

    [Fact]
    public void Dropping_a_mold_places_a_patch_and_empties_the_hand()
    {
        var sim = MoldSim(out var placer);
        Assert.True(sim.TryUseItem(1));     // empty hand, on empty tile -> use held -> drop mold
        Assert.Null(placer.Held);
        var patch = Assert.Single(sim.Molds);
        Assert.Equal(new GridPos(3, 3), patch.Pos);
        Assert.Equal(sim.Config.MoldSeconds, patch.RemainingSeconds, 3);
        Assert.Single(sim.DrainEvents().OfType<MoldDropped>());
    }

    [Fact]
    public void The_placer_standing_on_their_own_mold_is_not_slowed()
    {
        var sim = MoldSim(out var placer);
        sim.TryUseItem(1);          // drop under self
        sim.Tick(0.1);
        Assert.Empty(placer.Effects); // dropping is not "stepping on"
    }

    [Fact]
    public void A_miner_stepping_onto_a_mold_is_slowed()
    {
        var sim = MoldSim(out _);
        sim.TryUseItem(1);                       // mold at (3,3)
        var other = sim.AddMiner(2, new GridPos(2, 3));
        sim.TryMove(2, Direction.East);          // step onto (3,3)
        var e = Assert.Single(other.Effects);
        Assert.Equal(EffectKind.SlowMold, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
        Assert.Equal(sim.Config.MoldSlowFactor, e.Magnitude, 3);
    }

    [Fact]
    public void A_mold_patch_decays_and_expires()
    {
        var sim = MoldSim(out _);
        sim.TryUseItem(1);
        sim.Tick(sim.Config.MoldSeconds + 0.01);
        Assert.Empty(sim.Molds);
        Assert.Single(sim.DrainEvents().OfType<MoldExpired>());
    }

    [Fact]
    public void Re_dropping_on_an_existing_patch_refreshes_without_duplicating()
    {
        var sim = MoldSim(out var placer);
        sim.TryUseItem(1);                 // drop #1
        sim.Tick(5.0);                     // patch down to ~15s
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold));
        sim.TryUseItem(1);                 // pick up the new one
        sim.TryUseItem(1);                 // drop #2 on the same tile -> refresh
        var patch = Assert.Single(sim.Molds);
        Assert.Equal(sim.Config.MoldSeconds, patch.RemainingSeconds, 3);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationMoldTests"`
Expected: FAIL — `Simulation.Molds`, `MoldPatch`, `MoldDropped`, `MoldExpired` undefined; `SlowMold` dispatch returns false (drop is a no-op).

- [ ] **Step 3: Create the `MoldPatch` entity**

Create `src/Miner49er.Core/Sim/MoldPatch.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>A timed trap patch dropped by the slow-mold item. Any miner who steps
/// onto its tile is slowed; the patch decays after a configured lifetime.</summary>
public sealed class MoldPatch
{
    public GridPos Pos { get; }
    public double RemainingSeconds { get; internal set; }

    internal MoldPatch(GridPos pos, double seconds) { Pos = pos; RemainingSeconds = seconds; }
}
```

- [ ] **Step 4: Add the mold events**

In `src/Miner49er.Core/Sim/SimEvent.cs`, after the `PlankPlaced` line:

```csharp
public sealed record MoldDropped(GridPos Pos) : SimEvent;
public sealed record MoldExpired(GridPos Pos) : SimEvent;
```

- [ ] **Step 5: Add the mold list and expose it**

In `src/Miner49er.Core/Sim/Simulation.cs`, alongside the other private collections (after `private readonly List<Item> _items = new();`):

```csharp
    private readonly List<MoldPatch> _molds = new();
```

And alongside the other public read-only collections (after `public IReadOnlyList<Item> Items => _items;`):

```csharp
    public IReadOnlyList<MoldPatch> Molds => _molds;
```

- [ ] **Step 6: Wire the `SlowMold` dispatch arm and add `DropMold`**

In `TryUseItem`, change the switch to handle `SlowMold`:

```csharp
        return held switch
        {
            ItemKind.WaterPlank => TryPlacePlank(m),
            ItemKind.SlowMold   => DropMold(m),
            _ => false,
        };
```

Add `DropMold` next to `TryPlacePlank`:

```csharp
    // Drops a timed trap patch on the miner's own tile (refreshing an existing one).
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

- [ ] **Step 7: Decay molds each tick**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `Tick`, add `AdvanceMolds(dt);` after `AdvanceEffects(dt);`:

```csharp
        Elapsed += dt;
        AdvanceEffects(dt);
        AdvanceMolds(dt);
        AdvanceCooldowns(dt);
```

Add the method (next to `AdvanceEffects`):

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

- [ ] **Step 8: Apply the slow when a miner steps onto a mold**

In `TryMove`, just before the final `m.MoveCooldownRemaining = EffectiveMoveSeconds(m);` line, add:

```csharp
        if (m.Alive && _molds.Any(mo => mo.Pos == target))
            ApplyEffect(id, EffectKind.SlowMold, EffectChannel.MoveSpeed,
                        Config.MoldSlowFactor, Config.MoldSlowSeconds);

```

(Placing it before the cooldown line means the slow also lengthens the cadence the miner pays leaving the tile.)

- [ ] **Step 9: Run the test + full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — all mold tests green, all prior green.

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Sim/MoldPatch.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationMoldTests.cs
git commit -m "feat(core): slow-mold trap patch with step-on slow and decay

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Map seeding for carried items

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Test: `src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs` (create)

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorCarriedItemsTests
{
    [Fact]
    public void Generates_the_configured_number_of_plank_and_mold_items()
    {
        var cfg = new MapConfig { Seed = 99, PlayerCount = 4, WaterPlankCount = 3, SlowMoldCount = 3 };
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(3, map.Items.Count(i => i.Kind == ItemKind.WaterPlank));
        Assert.Equal(3, map.Items.Count(i => i.Kind == ItemKind.SlowMold));
    }

    [Fact]
    public void Carried_items_sit_on_walkable_floor_tiles()
    {
        var cfg = new MapConfig { Seed = 7, PlayerCount = 3 };
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(i => i.Kind.IsCarried()))
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(it.Pos));
            Assert.NotEqual(ItemPlacement.Buried, it.Placement);
        }
    }

    [Fact]
    public void Buff_items_are_unaffected_by_the_carried_item_pass()
    {
        // The buried/toolbox buff scatter must only ever contain the three buff kinds.
        var cfg = new MapConfig { Seed = 21, PlayerCount = 4 };
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(i => i.Placement == ItemPlacement.Buried))
            Assert.False(it.Kind.IsCarried());
    }

    [Fact]
    public void Carried_item_placement_is_deterministic_for_a_fixed_seed()
    {
        var a = MapGenerator.Generate(new MapConfig { Seed = 123, PlayerCount = 5 });
        var b = MapGenerator.Generate(new MapConfig { Seed = 123, PlayerCount = 5 });
        var ca = a.Items.Where(i => i.Kind.IsCarried()).Select(i => (i.Pos, i.Kind)).ToList();
        var cb = b.Items.Where(i => i.Kind.IsCarried()).Select(i => (i.Pos, i.Kind)).ToList();
        Assert.Equal(ca, cb);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorCarriedItemsTests"`
Expected: FAIL — `WaterPlankCount`/`SlowMoldCount` undefined; no carried items generated; buried scatter currently cycles all 5 kinds so the buff-purity test fails.

- [ ] **Step 3: Add the counts to `MapConfig`**

In `src/Miner49er.Core/Map/MapConfig.cs`, after the `DecoyCount` line (line 17):

```csharp
    public int WaterPlankCount { get; set; } = 3;   // visible carried water-planks scattered on Floor
    public int SlowMoldCount { get; set; } = 3;     // visible carried slow-molds scattered on Floor
```

- [ ] **Step 4: Restrict the buried/toolbox scatter to buff kinds**

In `src/Miner49er.Core/Map/MapGenerator.cs`, in `PlaceItems`, change the `kinds` line so only auto-apply (non-carried) kinds fill the toolbox/buried scatter:

```csharp
        var kinds = Enum.GetValues<ItemKind>().Where(k => !k.IsCarried()).ToArray();
```

- [ ] **Step 5: Add a carried-item placement pass and call it from `Generate`**

In `src/Miner49er.Core/Map/MapGenerator.cs`, in `Generate`, replace the items/decoys lines:

```csharp
        var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
        items.AddRange(PlaceCarriedItems(grid, rng, config.WaterPlankCount, config.SlowMoldCount, region, spawns, items));
        var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);
```

(`PlaceItems` already returns a `List<Item>`, so `AddRange` is valid.)

Add the new method (place it just after `PlaceItems`):

```csharp
    // Carried items (water-plank, slow-mold) sit visibly on Floor tiles in the traversable region,
    // never on a spawn or a tile already holding an item. Deterministic: ordered grid scan then
    // seed-shuffle, planks first then molds, so host and every client agree.
    private static List<Item> PlaceCarriedItems(TileGrid g, Random rng, int plankCount, int moldCount,
        HashSet<GridPos> region, List<GridPos> spawns, IEnumerable<Item> existing)
    {
        var taken = new HashSet<GridPos>(existing.Select(it => it.Pos));
        var spawnSet = new HashSet<GridPos>(spawns);
        var cands = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor
                        && !spawnSet.Contains(p) && !taken.Contains(p))
            .ToList();
        Shuffle(cands, rng);

        var result = new List<Item>();
        int idx = 0;
        for (int i = 0; i < plankCount && idx < cands.Count; i++, idx++)
            result.Add(new Item(cands[idx], ItemKind.WaterPlank, ItemPlacement.Toolbox));
        for (int i = 0; i < moldCount && idx < cands.Count; i++, idx++)
            result.Add(new Item(cands[idx], ItemKind.SlowMold, ItemPlacement.Toolbox));
        return result;
    }
```

- [ ] **Step 6: Run the test + full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS. Note: existing `MapGeneratorItemsTests` may assert kind distribution over the scatter; if any such test now sees only buff kinds it should still hold (buff kinds were always the first three). If a determinism golden test (`MapDeterminismTests`) compares item lists, re-running both generations stays internally consistent — confirm it still passes; it should, since carried placement is seed-driven and appended deterministically.

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorCarriedItemsTests.cs
git commit -m "feat(core): scatter water-plank and slow-mold items on the map

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Snapshot sync (held item + mold list)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` (fix call sites + assert new fields)
- Test: `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs` (add capture test)

- [ ] **Step 1: Write the failing test**

In `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`, add:

```csharp
    [Fact]
    public void Captures_held_item_and_mold_patches()
    {
        var sim = new Simulation(new TileGrid(7, 7, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.SlowMold));
        sim.TryUseItem(1);   // hand = SlowMold
        sim.AddItem(new Item(new GridPos(3, 3), ItemKind.WaterPlank));
        sim.TryUseItem(1);   // swap: hand = WaterPlank, mold not dropped yet
        // drop a mold from a second miner so a patch exists:
        sim.AddMiner(2, new GridPos(1, 1));
        sim.AddItem(new Item(new GridPos(1, 1), ItemKind.SlowMold));
        sim.TryUseItem(2);   // hand2 = SlowMold
        sim.TryUseItem(2);   // drop -> patch at (1,1)

        var snap = SnapshotFactory.Capture(sim, tick: 5);

        Assert.Equal((int)ItemKind.WaterPlank, snap.Miners.Single(m => m.Id == 1).Held);
        Assert.Equal(-1, snap.Miners.Single(m => m.Id == 2).Held); // empty hand
        var patch = Assert.Single(snap.Molds);
        Assert.Equal(1, patch.X);
        Assert.Equal(1, patch.Y);
        Assert.Equal(sim.Config.MoldSeconds, patch.RemainingSeconds, 3);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SnapshotFactoryTests"`
Expected: FAIL — `MinerSnapshot.Held` and `WorldSnapshot.Molds`/`MoldSnapshot` do not exist (compile error across the test assembly).

- [ ] **Step 3: Extend the snapshot records**

In `src/Miner49er.Core/Net/Snapshots.cs`, replace `MinerSnapshot`, add `MoldSnapshot`, and extend `WorldSnapshot`:

```csharp
public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held);

public readonly record struct ChargeSnapshot(int OwnerId, int X, int Y, double FuseRemaining);

public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind, ItemPlacement Placement);

public readonly record struct MoldSnapshot(int X, int Y, double RemainingSeconds);
```

And the `WorldSnapshot` record (add `Molds` before the defaulted `SecondsRemaining`):

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds, float SecondsRemaining = -1f);
```

- [ ] **Step 4: Extend the codec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`, in `Write`, add `Held` to the miner block:

```csharp
            w.Write(m.MoveSeconds); w.Write(m.VisionRadius); w.Write(m.Held);
```

After the items-writing block (after its closing `}`), add the mold block:

```csharp
        w.Write(snap.Molds.Count);
        foreach (var mo in snap.Molds)
        {
            w.Write(mo.X); w.Write(mo.Y); w.Write(mo.RemainingSeconds);
        }
```

In `Read`, add the 11th miner field:

```csharp
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
                r.ReadInt32(), r.ReadInt32()));
```

After the items-reading loop, add the mold-reading block:

```csharp
        int moldCount = r.ReadInt32();
        var molds = new List<MoldSnapshot>(moldCount);
        for (int i = 0; i < moldCount; i++)
            molds.Add(new MoldSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadDouble()));
```

And update the final `WorldSnapshot` construction to pass `molds`:

```csharp
        return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds, secondsRemaining), changes);
```

- [ ] **Step 5: Extend the factory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, add `Held` to the miner projection and a mold projection, and pass `molds`:

```csharp
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
                m.Held is { } h ? (int)h : -1))
            .ToList();

        var charges = sim.Charges
            .Select(c => new ChargeSnapshot(c.OwnerId, c.WallPos.X, c.WallPos.Y, c.FuseRemaining))
            .ToList();

        var items = sim.Items
            .Select(it => new ItemSnapshot(it.Pos.X, it.Pos.Y, it.Kind, it.Placement))
            .ToList();

        var molds = sim.Molds
            .Select(mo => new MoldSnapshot(mo.Pos.X, mo.Pos.Y, mo.RemainingSeconds))
            .ToList();

        return new WorldSnapshot(tick, miners, charges, items, molds, (float)sim.SecondsRemaining);
```

- [ ] **Step 6: Fix the existing codec round-trip test call sites**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, update `Round_trips_all_fields`:

- Add a `Held` value to each `MinerSnapshot`:
```csharp
                    new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8, (int)ItemKind.WaterPlank),
                    new(2, 9, 0, 0, false, 0, 0, 0.0, 0.24, 5, -1),
```
- Add a `Molds:` argument before `SecondsRemaining:`:
```csharp
                Molds: new List<MoldSnapshot> { new(4, 6, 12.5), new(0, 1, 3.0) },
                SecondsRemaining: 42.5f),
```
- Add assertions after the existing item assertions:
```csharp
        Assert.Equal((int)ItemKind.WaterPlank, back.Snapshot.Miners[0].Held);
        Assert.Equal(-1, back.Snapshot.Miners[1].Held);
        Assert.Equal(2, back.Snapshot.Molds.Count);
        Assert.Equal(update.Snapshot.Molds[0], back.Snapshot.Molds[0]);
        Assert.Equal(update.Snapshot.Molds[1], back.Snapshot.Molds[1]);
```

Update `Round_trips_empty_collections` to pass an empty mold list:

```csharp
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>()),
```

and add:

```csharp
        Assert.Empty(back.Snapshot.Molds);
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS — codec + factory round-trip the new fields; all prior tests green.

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(core): sync held item and mold patches in the snapshot

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Use-verb transport (host + network + input)

Godot adapter — verified by `dotnet build` (no unit tests; the engine layer is covered above; behavior is play-tested in Task 8).

**Files:**
- Modify: `game/net/NetworkManager.cs` (`SendAction`, `ReceiveAction`)
- Modify: `game/net/MatchHost.cs` (`SetAction`, `_pendingUse`, `StepOnce` drain + `PlankPlaced` TileChange)
- Modify: `game/net/InputSender.cs` (read the Use action)

- [ ] **Step 1: Add the `use` bool to the transport**

In `game/net/NetworkManager.cs`, replace `SendAction` and `ReceiveAction`:

```csharp
	public void SendAction(bool mine, bool plant, bool use)
	{
		if (IsHost) { _matchHost?.SetAction(LocalId, mine, plant, use); return; }
		RpcId(1, nameof(ReceiveAction), mine, plant, use);
	}
```

```csharp
	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveAction(bool mine, bool plant, bool use) =>
		_matchHost?.SetAction(Multiplayer.GetRemoteSenderId(), mine, plant, use);
```

- [ ] **Step 2: Queue and apply the Use action in `MatchHost`**

In `game/net/MatchHost.cs`, add the pending set near `_pendingPlant`:

```csharp
	private readonly HashSet<int> _pendingUse = new();
```

Replace `SetAction`:

```csharp
	public void SetAction(long peerId, bool mine, bool plant, bool use)
	{
		if (!_peerToMiner.TryGetValue(peerId, out int minerId)) return;
		if (mine) _pendingMine.Add(minerId);
		if (plant) _pendingPlant.Add(minerId);
		if (use) _pendingUse.Add(minerId);
	}
```

In `StepOnce`, after the plant drain (`foreach (var minerId in _pendingPlant) ...; _pendingPlant.Clear();`):

```csharp
			foreach (var minerId in _pendingUse) _sim.TryUseItem(minerId);
			_pendingUse.Clear();
```

In the event switch in `StepOnce`, add a `PlankPlaced` case so the new tile reaches clients:

```csharp
					case PlankPlaced pp:
						changes.Add(new TileChange(pp.Pos.X, pp.Pos.Y, false, TileType.Plank));
						break;
```

- [ ] **Step 3: Read the Use action on the client**

In `game/net/InputSender.cs`, replace the action block in `_PhysicsProcess`:

```csharp
			bool mine = Input.IsActionJustPressed(InputBindings.Pickaxe);
			bool plant = Input.IsActionJustPressed(InputBindings.Plant);
			bool use = Input.IsActionJustPressed(InputBindings.UseItem);
			if (mine || plant || use) NetworkManager.Instance.SendAction(mine, plant, use);
```

- [ ] **Step 4: Build**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeds, 0 errors. (No other callers of `SendAction`/`ReceiveAction`/`SetAction` exist; if the build flags one, update it to pass `use: false`.)

- [ ] **Step 5: Commit**

```bash
git add game/net/NetworkManager.cs game/net/MatchHost.cs game/net/InputSender.cs
git commit -m "feat(game): transport the Use verb and sync placed planks

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Godot rendering, HUD, and SFX

Godot adapter — verified by `dotnet build` then play-test.

**Files:**
- Modify: `game/net/MatchClient.cs` (hold + expose the mold list)
- Modify: `game/WorldRenderer.cs` (plank tile color, plank/mold item colors, mold patch render)
- Modify: `game/Main.cs` (held-item HUD text)
- Modify: `game/audio/SfxLibrary.cs` (Grab/Plank/Squelch tones)
- Modify: `game/net/MatchAudio.cs` (Use-verb SFX diffs)

- [ ] **Step 1: Hold and expose the mold list on the client**

In `game/net/MatchClient.cs`, add the backing field (near `_items`):

```csharp
	private List<MoldSnapshot> _molds = new();
```

Add the public accessor (near `Items`):

```csharp
	public IReadOnlyList<MoldSnapshot> Molds => _molds;
```

In `ApplyUpdate`, after `_items = new List<ItemSnapshot>(update.Snapshot.Items);`:

```csharp
		_molds = new List<MoldSnapshot>(update.Snapshot.Molds);
```

- [ ] **Step 2: Render the plank tile and mold patches**

In `game/WorldRenderer.cs`, add colors near the other `static readonly Color` fields:

```csharp
	private static readonly Color PlankColor = new("b5803a");      // laid water-plank bridge
	private static readonly Color MoldColor = new("6f8f3a");       // slow-mold trap patch
	private static readonly Color PlankItemColor = new("c8a060");  // carried water-plank pickup
	private static readonly Color MoldItemColor = new("8fae4f");   // carried slow-mold pickup
```

Add `Plank` to the tile color switch (before the `_ => FloorColor` arm):

```csharp
				TileType.Plank => PlankColor,
```

Add plank/mold to the item color switch (before its `_ => SpeedItemColor` arm):

```csharp
					ItemKind.WaterPlank => PlankItemColor,
					ItemKind.SlowMold => MoldItemColor,
```

Add mold-patch drawing. After the items-drawing `foreach` block (the `foreach (var it in _client.Items)` loop) and before the Listen-reveal block, add:

```csharp
			// Slow-mold patches: drawn within fog, fading out over their last second as they decay.
			foreach (var mo in _client.Molds)
			{
				var mp = new GridPos(mo.X, mo.Y);
				if (!_client.Fog.IsVisible(mp)) continue;
				float alpha = Mathf.Clamp((float)mo.RemainingSeconds, 0f, 1f) * 0.5f + 0.25f;
				var col = MoldColor with { A = alpha };
				DrawRect(new Rect2(mo.X * ts, mo.Y * ts, ts, ts), col);
			}
```

- [ ] **Step 3: Show the held item in the HUD**

In `game/Main.cs`, inside `_PhysicsProcess`, within the `if (m.Id == _client.LocalMinerId)` block, replace the `_hud.SetText(...)` line so it appends the held item:

```csharp
					string timeStr = _client.SecondsRemaining >= 0 ? $"    Time: {_client.SecondsRemaining:0}s" : "";
					string heldStr = m.Held switch
					{
						(int)ItemKind.WaterPlank => "    Held: Plank",
						(int)ItemKind.SlowMold => "    Held: Mold",
						_ => "",
					};
					_hud.SetText($"Gold: {m.Gold}    {status}{timeStr}{heldStr}");
```

(`MinerSnapshot.Held` is `int`, -1 when empty; `ItemKind` is in the `Miner49er.Core` namespace already imported by `Main.cs`.)

- [ ] **Step 4: Add the SFX tones**

In `game/audio/SfxLibrary.cs`, add near the other stream properties (after `Spill`):

```csharp
	public static AudioStream Grab => Get("grab", () => Tone(0.10f, 600f, 900f));   // crisp pickup/swap
	public static AudioStream Plank => Get("plank", () => Noise(0.10f, 800f, decay: true)); // wooden knock
	public static AudioStream Squelch => Get("squelch", () => Tone(0.16f, 380f, 140f)); // wet mold plop
```

- [ ] **Step 5: Add the Use-verb SFX diffs in `MatchAudio`**

In `game/net/MatchAudio.cs`, add tracking fields near the other `_prev*` dictionaries:

```csharp
	private readonly Dictionary<int, int> _prevHeld = new();
	private readonly HashSet<(int x, int y)> _prevMolds = new();
```

In `_Process`, inside the `foreach (var m in _client.Miners)` loop, add a held-change cue for the local miner (after the existing `_prevAlive[m.Id] = m.Alive;` line, still inside the loop):

```csharp
			if (m.Id == _client.LocalMinerId)
			{
				int prevHeld = _prevHeld.TryGetValue(m.Id, out var ph) ? ph : -1;
				if (m.Held != prevHeld)
				{
					if (m.Held != -1)
						OneShot(SfxLibrary.Grab, WorldOf(m.X, m.Y));        // picked up / swapped
					else if (prevHeld == (int)ItemKind.WaterPlank)
						OneShot(SfxLibrary.Plank, WorldOf(m.X, m.Y));       // laid a plank (hand emptied)
					// SlowMold -> empty (mold dropped) is covered by the new-patch Squelch below
				}
				_prevHeld[m.Id] = m.Held;
			}
```

After the unbury (`Spill`) block and before the `_prevItems.Clear();` line, add the mold-drop cue:

```csharp
			// New mold patches (not present last frame) -> squelch near the local miner.
			var moldNow = new HashSet<(int x, int y)>();
			foreach (var mo in _client.Molds) moldNow.Add((mo.X, mo.Y));
			foreach (var key in moldNow)
				if (!_prevMolds.Contains(key) && localTile is { } lt3
					&& System.Math.Abs(lt3.x - key.x) <= 8 && System.Math.Abs(lt3.y - key.y) <= 8)
					OneShot(SfxLibrary.Squelch, WorldOf(key.x, key.y));
			_prevMolds.Clear();
			foreach (var key in moldNow) _prevMolds.Add(key);
```

`Grab` fires on pickup/swap (hand becomes non-empty); `Plank` fires when a held water-plank is used (hand goes WaterPlank→empty); `Squelch` fires when a new mold patch appears. All three are pure client-side state diffs — no `TileChange` access needed.

- [ ] **Step 6: Build**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeds, 0 errors.

- [ ] **Step 7: Play-test verification**

Run the game via PowerShell (NOT the Bash tool): `godot --path . ` (or launch the editor). Verify, ideally with two clients:
- Walking onto a plank/mold pickup does not auto-collect it; pressing Space grabs it (HUD shows "Held: Plank"/"Held: Mold"), and pressing Space on another pickup swaps (the old one drops).
- Facing a water tile and pressing Space lays a brown plank you can then walk across; deep-water planks are safe and survive flooding.
- Pressing Space (with a mold held) drops a green patch on your tile; you are not slowed standing on it; another miner walking onto it is visibly slower; the patch fades and disappears after ~20s.
- Pickup/swap plays `Grab`; mold drop plays `Squelch`.

- [ ] **Step 8: Commit**

```bash
git add game/net/MatchClient.cs game/WorldRenderer.cs game/Main.cs game/audio/SfxLibrary.cs game/net/MatchAudio.cs
git commit -m "feat(game): render planks/molds, held-item HUD, and Use-verb SFX

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final verification

After all tasks:

- [ ] `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` — all green (161 prior + new).
- [ ] `dotnet build Miner49er.sln` — 0 errors.
- [ ] Play-test the four behaviors in Task 8 Step 7.
- [ ] Use superpowers:finishing-a-development-branch to merge after the play-test passes.
