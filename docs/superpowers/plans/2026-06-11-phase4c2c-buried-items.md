# Phase 4c-2c — Buried Items — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Most map items become buried in ordinary rock — invisible until mined/blasted, when they drop as floor pickups — while a few stay visible in toolboxes; the existing Listen action reveals nearby buried items and decoys as identical neutral shimmers, so finding them is a gamble.

**Architecture:** Builds entirely on 4c-2a's item system. An item gains an `ItemPlacement` state (`Toolbox`/`Buried`/`Loose`); map-gen places a few toolboxes on floor and the rest buried in rim rock, plus a set of empty "decoy" rock spots. The existing `PickUpItems` pass skips buried items; the two rock-destruction sites (mining, blast) call a new `UnburyItemsAt` that flips buried→loose. Placement rides the existing item snapshot (one extra field); decoys are a deterministic, un-synced map-gen output every client regenerates from the seed. The Listen reveal is pure Godot-side rendering/audio.

**Tech Stack:** C# / .NET 8, pure-C# `Miner49er.Core` (xUnit-tested), Godot 4.6.3 (.NET) adapter in `game/`.

**Spec:** `docs/superpowers/specs/2026-06-11-phase4c2c-buried-items-design.md`

**Conventions (read before starting):**
- Core code is 4-space indent; `game/` code is TAB indent. Match the file you're editing.
- Commit messages MUST end with the trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Build/test with `dotnet` (works from Bash or PowerShell). Do **not** run `godot` from the Bash tool — only PowerShell — but no task here requires running `godot`; `dotnet build` suffices.
- Do **not** stage the pre-existing untracked working-tree changes (`assets/Splash.png*`, CRLF-only `project.godot`, `game/Splash.tscn`). Stage only the files each task names.
- Core test command: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
- Full build (incl. Godot game project): `dotnet build Miner49er.sln`

---

## File Structure

**Modified (Core):**
- `src/Miner49er.Core/Map/Item.cs` — add `ItemPlacement` enum + `Placement` field on `Item`.
- `src/Miner49er.Core/Map/MapConfig.cs` — add `VisibleItemCount`, `DecoyCount`.
- `src/Miner49er.Core/Map/MapGenerator.cs` — split `PlaceItems` into toolbox/buried; add `PlaceDecoys`; wire both into `Generate`.
- `src/Miner49er.Core/Map/GeneratedMap.cs` — add `Decoys`.
- `src/Miner49er.Core/Sim/SimEvent.cs` — add `ItemUnburied`.
- `src/Miner49er.Core/Sim/Simulation.cs` — `PickUpItems` buried-guard; `UnburyItemsAt`; call it in mining + blast.
- `src/Miner49er.Core/Net/Snapshots.cs` — `ItemSnapshot` gains `Placement`.
- `src/Miner49er.Core/Net/SnapshotCodec.cs` — write/read `Placement`.
- `src/Miner49er.Core/Net/SnapshotFactory.cs` — project `Placement`.

**Modified (Godot):**
- `game/net/MatchClient.cs` — `Listening` field, `Decoys` field, `Begin` signature.
- `game/Main.cs` — pass `map.Decoys` to `Begin`; set `_client.Listening`.
- `game/WorldRenderer.cs` — toolbox box, loose dot, neutral Listen shimmer for buried items + decoys.
- `game/audio/SfxLibrary.cs` — `Spill` tone.
- `game/net/MatchAudio.cs` — spill SFX on Buried→Loose.

**Modified (tests):**
- `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs` — rewritten for the toolbox/buried split.
- `src/Miner49er.Core.Tests/SimulationItemsTests.cs` — buried-guard + unbury tests.
- `src/Miner49er.Core.Tests/SnapshotCodecTests.cs` / `SnapshotFactoryTests.cs` — `Placement` round-trip.

**Created (tests):**
- `src/Miner49er.Core.Tests/MapGeneratorDecoysTests.cs` — decoy placement.

---

## Task 1: Item placement state + toolbox/buried map-gen split

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Map/MapConfig.cs:14`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs:24-25` (Generate call) and `:262-277` (PlaceItems)
- Test: `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs` (full rewrite)

- [ ] **Step 1: Add the placement state to `Item.cs`**

Replace the body of `src/Miner49er.Core/Map/Item.cs` with:

```csharp
namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map.</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }

/// <summary>Where an item sits and how it can be collected.</summary>
public enum ItemPlacement
{
    Toolbox,   // visible on a Floor tile, collectible on walk-over
    Buried,    // hidden inside a Rock tile; not collectible until the rock is mined/blasted, which flips it to Loose
    Loose,     // spilled onto a Floor tile after being unburied; collectible on walk-over
}

/// <summary>A collectible. Buried items sit on a Rock tile and are not collectible
/// until the rock is destroyed, which flips them to Loose.</summary>
public readonly record struct Item(GridPos Pos, ItemKind Kind, ItemPlacement Placement = ItemPlacement.Toolbox);
```

- [ ] **Step 2: Add `VisibleItemCount` to `MapConfig.cs`**

In `src/Miner49er.Core/Map/MapConfig.cs`, after line 15 (`public int ItemsPerPlayer ...`) add:

```csharp
    public int VisibleItemCount { get; set; } = 2;  // of the total, this many are visible toolboxes; rest are buried
```

- [ ] **Step 3: Rewrite `MapGeneratorItemsTests.cs` for the split (failing test)**

Replace the entire contents of `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs` with:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorItemsTests
{
    private static MapConfig Cfg(int seed = 7, int players = 1) =>
        new() { Seed = seed, PlayerCount = players };

    [Fact]
    public void Placement_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(Cfg());
        var b = MapGenerator.Generate(Cfg());
        Assert.Equal(a.Items, b.Items); // same positions, kinds, and placements, same order
    }

    [Fact]
    public void Total_item_count_scales_with_player_count()
    {
        Assert.Equal(9, MapGenerator.Generate(Cfg(players: 1)).Items.Count);  // 9 + 1*0
        Assert.Equal(12, MapGenerator.Generate(Cfg(players: 4)).Items.Count); // 9 + 1*3
    }

    [Fact]
    public void Exactly_VisibleItemCount_are_toolboxes_on_floor_never_a_spawn()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var spawns = map.Spawns.ToHashSet();
        var toolboxes = map.Items.Where(it => it.Placement == ItemPlacement.Toolbox).ToList();
        Assert.Equal(2, toolboxes.Count); // VisibleItemCount default
        foreach (var it in toolboxes)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(it.Pos));
            Assert.DoesNotContain(it.Pos, spawns);
        }
    }

    [Fact]
    public void The_rest_are_buried_in_ordinary_rock()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var buried = map.Items.Where(it => it.Placement == ItemPlacement.Buried).ToList();
        Assert.Equal(map.Items.Count - 2, buried.Count);
        Assert.NotEmpty(buried);
        foreach (var it in buried)
            Assert.Equal(TileType.Rock, map.Grid.Get(it.Pos)); // plain rock, never GoldRock/Impermeable/Floor
    }

    [Fact]
    public void Toolbox_and_buried_positions_are_disjoint()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.Equal(map.Items.Count, map.Items.Select(it => it.Pos).Distinct().Count());
    }

    [Fact]
    public void Kinds_are_assigned_round_robin_in_placement_order()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var kinds = System.Enum.GetValues<ItemKind>();
        for (int i = 0; i < map.Items.Count; i++)
            Assert.Equal(kinds[i % kinds.Length], map.Items[i].Kind);
    }
}
```

- [ ] **Step 4: Run the tests — verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorItemsTests"`
Expected: FAIL — the split tests fail because `PlaceItems` still puts every item on Floor as a toolbox (e.g. `The_rest_are_buried_in_ordinary_rock` finds 0 buried).

- [ ] **Step 5: Rewrite `PlaceItems` to place toolboxes + buried**

In `src/Miner49er.Core/Map/MapGenerator.cs`, replace the existing `PlaceItems` method (the comment block + method, currently lines ~258-277) with:

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
        int buried = Math.Min(total - visible, rockCands.Count);

        var placed = new List<(GridPos Pos, ItemPlacement Placement)>();
        for (int i = 0; i < visible; i++) placed.Add((floorCands[i], ItemPlacement.Toolbox));
        for (int i = 0; i < buried; i++) placed.Add((rockCands[i], ItemPlacement.Buried));

        var kinds = Enum.GetValues<ItemKind>();
        var items = new List<Item>();
        for (int i = 0; i < placed.Count; i++)
            items.Add(new Item(placed[i].Pos, kinds[i % kinds.Length], placed[i].Placement));
        return items;
    }
```

- [ ] **Step 6: Update the `Generate` call to pass `VisibleItemCount`**

In `src/Miner49er.Core/Map/MapGenerator.cs`, replace lines 24-25:

```csharp
        int itemCount = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
        var items = PlaceItems(grid, rng, itemCount, region, spawns);
```

with:

```csharp
        int total = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
        var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
```

- [ ] **Step 7: Run the tests — verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorItemsTests"`
Expected: PASS (6 tests).

- [ ] **Step 8: Run the full Core suite to catch regressions**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green — 4c-2a tests still pass because `new Item(pos, kind)` defaults to `Toolbox`).

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs
git commit -m "feat(core): split items into visible toolboxes and buried rock caches

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Decoys (empty suspicious spots)

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/GeneratedMap.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (Generate + new `PlaceDecoys`)
- Test: `src/Miner49er.Core.Tests/MapGeneratorDecoysTests.cs` (create)

- [ ] **Step 1: Add `DecoyCount` to `MapConfig.cs`**

In `src/Miner49er.Core/Map/MapConfig.cs`, immediately after the `VisibleItemCount` line you added in Task 1, add:

```csharp
    public int DecoyCount { get; set; } = 4;        // empty "suspicious spots" that shimmer under Listen but hold nothing
```

- [ ] **Step 2: Add `Decoys` to `GeneratedMap.cs`**

In `src/Miner49er.Core/Map/GeneratedMap.cs`, add a property after `Items`:

```csharp
    public required IReadOnlyList<GridPos> Decoys { get; init; }
```

The class becomes:

```csharp
namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
    public required IReadOnlyList<GridPos> Decoys { get; init; }
}
```

- [ ] **Step 3: Write the decoy tests (failing — won't compile yet)**

Create `src/Miner49er.Core.Tests/MapGeneratorDecoysTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapGeneratorDecoysTests
{
    private static MapConfig Cfg(int seed = 7, int players = 1) =>
        new() { Seed = seed, PlayerCount = players };

    [Fact]
    public void Decoy_count_matches_config()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.Equal(4, map.Decoys.Count); // DecoyCount default
    }

    [Fact]
    public void Decoys_sit_on_ordinary_rock()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        Assert.NotEmpty(map.Decoys);
        foreach (var d in map.Decoys)
            Assert.Equal(TileType.Rock, map.Grid.Get(d)); // never GoldRock/Impermeable/Floor
    }

    [Fact]
    public void Decoys_are_disjoint_from_item_positions()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var itemPositions = map.Items.Select(it => it.Pos).ToHashSet();
        foreach (var d in map.Decoys)
            Assert.DoesNotContain(d, itemPositions);
    }

    [Fact]
    public void Decoy_placement_is_deterministic_for_a_seed()
    {
        var a = MapGenerator.Generate(Cfg());
        var b = MapGenerator.Generate(Cfg());
        Assert.Equal(a.Decoys, b.Decoys);
    }
}
```

- [ ] **Step 4: Run the tests — verify they fail to compile / fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorDecoysTests"`
Expected: FAIL — compile error "'GeneratedMap' does not contain ... Decoys" is resolved by Step 2, but `PlaceDecoys`/wiring don't exist yet, so the build fails (`Generate` doesn't set the now-required `Decoys`).

- [ ] **Step 5: Add `PlaceDecoys` and wire it into `Generate`**

In `src/Miner49er.Core/Map/MapGenerator.cs`, add this method right after `PlaceItems`:

```csharp
    // "Suspicious spots" with no item: deterministic rock positions that shimmer under Listen
    // exactly like buried items, so the only way to tell a real cache from a decoy is to dig. Same
    // rim-rock candidate pool as buried items, minus tiles already holding a (buried) item.
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

Then in `Generate`, replace the line that builds `items` and the `return` (currently around lines 25-27):

```csharp
        var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center, Items = items };
```

with:

```csharp
        var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
        var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center, Items = items, Decoys = decoys };
```

(`System.Linq` is already globally imported in Core, so `items.Select(...)` compiles.)

- [ ] **Step 6: Run the tests — verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~MapGeneratorDecoysTests"`
Expected: PASS (4 tests).

- [ ] **Step 7: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorDecoysTests.cs
git commit -m "feat(core): place deterministic decoy spots in rock (empty Listen signals)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Pickup guard + reveal-on-destroy

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (`PickUpItems`, new `UnburyItemsAt`, mining branch in `CompleteActivity`, blast loop in `Detonate`)
- Test: `src/Miner49er.Core.Tests/SimulationItemsTests.cs` (add tests)

- [ ] **Step 1: Add the `ItemUnburied` event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, after the `ItemPickedUp` line add:

```csharp
public sealed record ItemUnburied(GridPos Pos, ItemKind Kind) : SimEvent;
```

- [ ] **Step 2: Write the failing tests**

Append these tests inside the `SimulationItemsTests` class in `src/Miner49er.Core.Tests/SimulationItemsTests.cs` (before the closing brace):

```csharp
    [Fact]
    public void A_buried_item_is_not_collected_by_walking()
    {
        var sim = Sim(out var m); // all-floor grid; the guard, not the tile, must block collection
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Buried));

        sim.TryMove(1, Direction.East); // step onto (2,2)
        sim.Tick(0.0);

        Assert.Single(sim.Items);       // still there
        Assert.Empty(m.Effects);        // no buff applied
    }

    [Fact]
    public void A_loose_item_is_collected_on_walk_over()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Loose));

        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        Assert.Empty(sim.Items);
        Assert.Single(m.Effects);
    }

    [Fact]
    public void Mining_a_buried_items_rock_unburies_it_to_loose()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion, ItemPlacement.Buried));

        sim.TryMove(1, Direction.East);              // blocked by rock, sets facing East
        sim.TryStartMining(1);
        sim.Tick(sim.Config.PickaxeSeconds + 0.01);  // mining completes this tick

        var item = Assert.Single(sim.Items);
        Assert.Equal(ItemPlacement.Loose, item.Placement);          // unburied
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2)));
        var ev = Assert.Single(sim.DrainEvents().OfType<ItemUnburied>());
        Assert.Equal(new GridPos(2, 2), ev.Pos);
        Assert.Equal(ItemKind.SpeedPotion, ev.Kind);
    }

    [Fact]
    public void Blasting_unburies_items_on_destroyed_tiles_only()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock); // wall to plant on
        grid.Set(new GridPos(3, 1), TileType.Rock); // buried item's rock, Manhattan-1 from the wall
        grid.Set(new GridPos(5, 5), TileType.Rock); // a far buried item, outside the blast
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(3, 1), ItemKind.BiggerBlast, ItemPlacement.Buried));
        sim.AddItem(new Item(new GridPos(5, 5), ItemKind.SpeedPotion, ItemPlacement.Buried));

        sim.TryMove(1, Direction.East);             // blocked by rock at (3,2), faces East
        sim.TryStartPlanting(1);
        sim.Tick(sim.Config.PlantSeconds + 0.01);   // charge planted
        sim.Tick(sim.Config.FuseSeconds + 0.01);    // detonates (the planter dies in its own blast — irrelevant here)

        Assert.Equal(ItemPlacement.Loose, sim.Items.Single(i => i.Pos == new GridPos(3, 1)).Placement);
        Assert.Equal(ItemPlacement.Buried, sim.Items.Single(i => i.Pos == new GridPos(5, 5)).Placement);
        Assert.Contains(sim.DrainEvents().OfType<ItemUnburied>(), e => e.Pos == new GridPos(3, 1));
    }
```

- [ ] **Step 3: Run the tests — verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationItemsTests"`
Expected: FAIL — `A_buried_item_is_not_collected_by_walking` fails (buried item is currently collected), and the mining/blast tests fail (no `UnburyItemsAt`, item stays `Buried`).

- [ ] **Step 4: Guard `PickUpItems` against buried items**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `PickUpItems`, add the guard as the first statement inside the loop (right after `var item = _items[i];`):

```csharp
            if (item.Placement == ItemPlacement.Buried) continue;   // not collectible until unburied
```

- [ ] **Step 5: Add `UnburyItemsAt`**

In `src/Miner49er.Core/Sim/Simulation.cs`, add this method directly below `PickUpItems`:

```csharp
    // Flips any buried item on a tile to a loose floor pickup. Called wherever rock becomes floor
    // (mining completes, blast disc) so a destroyed cache spills its item onto the open tile.
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

- [ ] **Step 6: Unbury on mining**

In `CompleteActivity`, in the mining branch, add the call right after the tile is set to Floor. The branch becomes:

```csharp
        if (kind == ActivityKind.Mining)
        {
            if (!Grid.InBounds(target) || !Grid.Get(target).IsMinable()) return;
            bool wasGold = Grid.Get(target) == TileType.GoldRock;
            Grid.Set(target, TileType.Floor);
            if (wasGold) m.GoldCollected++;
            UnburyItemsAt(target);
            _events.Add(new RockMined(m.Id, target, wasGold));
        }
```

- [ ] **Step 7: Unbury on blast**

In `Detonate`, inside the destruction loop, add the call after the tile is set to Floor. The relevant lines become:

```csharp
                bool wasGold = Grid.Get(p) == TileType.GoldRock;
                Grid.Set(p, TileType.Floor);
                if (wasGold)
                {
                    var owner = _miners[charge.OwnerId];
                    if (owner.Alive) owner.GoldCollected++;
                }
                UnburyItemsAt(p);
                destroyed.Add(p);
```

- [ ] **Step 8: Run the tests — verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SimulationItemsTests"`
Expected: PASS (original 5 + new 4 = 9 tests).

- [ ] **Step 9: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationItemsTests.cs
git commit -m "feat(core): buried items resist pickup and unbury when their rock is destroyed

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Sync item placement in snapshots

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs:11`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs:36,72`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs:22`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

- [ ] **Step 1: Add `Placement` to `ItemSnapshot`**

In `src/Miner49er.Core/Net/Snapshots.cs`, replace line 11:

```csharp
public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind);
```

with:

```csharp
public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind, ItemPlacement Placement);
```

- [ ] **Step 2: Update the codec tests (failing)**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, in `Round_trips_all_fields`, replace the `Items:` list:

```csharp
                Items: new List<ItemSnapshot> { new(6, 1, ItemKind.SpeedPotion), new(2, 5, ItemKind.BiggerBlast) },
```

with:

```csharp
                Items: new List<ItemSnapshot>
                {
                    new(6, 1, ItemKind.SpeedPotion, ItemPlacement.Toolbox),
                    new(2, 5, ItemKind.BiggerBlast, ItemPlacement.Buried),
                    new(3, 3, ItemKind.LongerVision, ItemPlacement.Loose),
                },
```

and replace the two item assertions near the end of that test:

```csharp
        Assert.Equal(2, back.Snapshot.Items.Count);
        Assert.Equal(update.Snapshot.Items[0], back.Snapshot.Items[0]);
        Assert.Equal(update.Snapshot.Items[1], back.Snapshot.Items[1]);
```

with:

```csharp
        Assert.Equal(3, back.Snapshot.Items.Count);
        Assert.Equal(update.Snapshot.Items[0], back.Snapshot.Items[0]);
        Assert.Equal(update.Snapshot.Items[1], back.Snapshot.Items[1]);
        Assert.Equal(update.Snapshot.Items[2], back.Snapshot.Items[2]);
        Assert.Equal(ItemPlacement.Buried, back.Snapshot.Items[1].Placement);
```

- [ ] **Step 3: Update the factory test (failing)**

In `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`, in `Captures_items_and_effective_vision_radius`, replace:

```csharp
        sim.AddItem(new Item(new GridPos(4, 1), ItemKind.LongerVision));
```

with:

```csharp
        sim.AddItem(new Item(new GridPos(4, 1), ItemKind.LongerVision, ItemPlacement.Buried));
```

and add one assertion after the existing `Assert.Equal(ItemKind.LongerVision, item.Kind);`:

```csharp
        Assert.Equal(ItemPlacement.Buried, item.Placement);
```

- [ ] **Step 4: Run the tests — verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SnapshotCodecTests|FullyQualifiedName~SnapshotFactoryTests"`
Expected: FAIL — `ItemSnapshot` now requires a 4th arg, so the codec/factory (still 3-arg) fail to compile, and once compiling, `Placement` round-trips as default until the codec carries it.

- [ ] **Step 5: Carry `Placement` through the codec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`, in `Write`, replace the item-writing line (36):

```csharp
            w.Write(it.X); w.Write(it.Y); w.Write((int)it.Kind);
```

with:

```csharp
            w.Write(it.X); w.Write(it.Y); w.Write((int)it.Kind); w.Write((int)it.Placement);
```

and in `Read`, replace the item-reading line (72):

```csharp
            items.Add(new ItemSnapshot(r.ReadInt32(), r.ReadInt32(), (ItemKind)r.ReadInt32()));
```

with:

```csharp
            items.Add(new ItemSnapshot(r.ReadInt32(), r.ReadInt32(), (ItemKind)r.ReadInt32(), (ItemPlacement)r.ReadInt32()));
```

- [ ] **Step 6: Project `Placement` in the factory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, replace line 22:

```csharp
            .Select(it => new ItemSnapshot(it.Pos.X, it.Pos.Y, it.Kind))
```

with:

```csharp
            .Select(it => new ItemSnapshot(it.Pos.X, it.Pos.Y, it.Kind, it.Placement))
```

- [ ] **Step 7: Run the tests — verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "FullyQualifiedName~SnapshotCodecTests|FullyQualifiedName~SnapshotFactoryTests"`
Expected: PASS.

- [ ] **Step 8: Run the full Core suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(core): sync item placement state in the per-tick snapshot

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Godot — toolbox/loose render + Listen shimmer for buried items & decoys

**Files:**
- Modify: `game/net/MatchClient.cs` (add `Listening`, `Decoys`, `Begin` param)
- Modify: `game/Main.cs:33` (Begin call) and `:120` (set Listening)
- Modify: `game/WorldRenderer.cs` (render + shimmer)

No new Core tests — this is presentation, verified by compile here and play-test by the user later. Remember: `game/` uses **TAB** indentation.

- [ ] **Step 1: Expose `Listening` and `Decoys` on `MatchClient`**

In `game/net/MatchClient.cs`, after the `LocalMinerId` property (line 20) add:

```csharp
	public bool Listening; // set by Main each frame; gates the buried-item shimmer
	public IReadOnlyList<GridPos> Decoys { get; private set; } = System.Array.Empty<GridPos>();
```

Change the `Begin` signature (line 33) to accept decoys:

```csharp
	public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot)
	{
		Grid = grid;
		LocalMinerId = localMinerId;
		Decoys = decoys;
```

(Leave the rest of `Begin` unchanged.)

- [ ] **Step 2: Pass decoys + set `Listening` in `Main.cs`**

In `game/Main.cs`, replace the `Begin` call (line 33):

```csharp
		_client.Begin(map.Grid, localMinerId, this);
```

with:

```csharp
		_client.Begin(map.Grid, map.Decoys, localMinerId, this);
```

Then in `_PhysicsProcess`, right after the line `_compass.Active = listening;` (line 120) add:

```csharp
			_client.Listening = listening;
```

- [ ] **Step 3: Render toolboxes/loose items and the Listen shimmer**

In `game/WorldRenderer.cs`:

(a) After the existing color consts (after line 24, `BlastItemColor`), add:

```csharp
	private static readonly Color ToolboxColor = new("9a7b4f");  // muted box outline behind a visible item
	private static readonly Color ShimmerColor = new("f5f0c0");  // neutral pale glow — NO kind tell
	private const int ListenItemRevealRadius = 6;                // tiles; Chebyshev radius for sensing through rock
```

(b) Replace the entire items-drawing `foreach` block (currently lines 69-82):

```csharp
		foreach (var it in _client.Items)
		{
			var ip = new GridPos(it.X, it.Y);
			if (!_client.Fog.IsVisible(ip)) continue; // hidden in the dark, like tiles
			var icol = it.Kind switch
			{
				ItemKind.SpeedPotion => SpeedItemColor,
				ItemKind.LongerVision => VisionItemColor,
				ItemKind.BiggerBlast => BlastItemColor,
				_ => SpeedItemColor,
			};
			var icenter = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
			DrawCircle(icenter, ts * 0.22f, icol);
		}
```

with:

```csharp
		foreach (var it in _client.Items)
		{
			if (it.Placement == ItemPlacement.Buried) continue; // buried items only show under Listen (below)
			var ip = new GridPos(it.X, it.Y);
			if (!_client.Fog.IsVisible(ip)) continue; // hidden in the dark, like tiles
			var icol = it.Kind switch
			{
				ItemKind.SpeedPotion => SpeedItemColor,
				ItemKind.LongerVision => VisionItemColor,
				ItemKind.BiggerBlast => BlastItemColor,
				_ => SpeedItemColor,
			};
			var icenter = new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts / 2f);
			if (it.Placement == ItemPlacement.Toolbox)
			{
				float bs = ts * 0.5f; // a small box behind the dot for visible toolbox items
				DrawRect(new Rect2(icenter.X - bs / 2f, icenter.Y - bs / 2f, bs, bs), ToolboxColor, false, 2f);
			}
			DrawCircle(icenter, ts * 0.22f, icol);
		}

		// Listen reveal: buried items and decoys shimmer IDENTICALLY (sensed through rock) while the
		// local miner holds Listen, within ListenItemRevealRadius. Neutral color, drawn regardless of fog.
		if (_client.Listening && TryLocalTile(out var lt))
		{
			float t = (float)Time.GetTicksMsec() / 1000f;
			float a = 0.18f + 0.22f * (0.5f + 0.5f * Mathf.Sin(t * Mathf.Pi * 2f / 0.8f)); // ~0.8s pulse
			var shimmer = ShimmerColor with { A = a };
			foreach (var it in _client.Items)
				if (it.Placement == ItemPlacement.Buried && WithinReveal(lt, it.X, it.Y))
					DrawShimmer(it.X, it.Y, shimmer, ts);
			foreach (var d in _client.Decoys)
				if (_client.Grid.Get(d) == TileType.Rock && WithinReveal(lt, d.X, d.Y))
					DrawShimmer(d.X, d.Y, shimmer, ts);
		}
```

(c) Add these helpers as private methods at the end of the class (before the final closing brace):

```csharp
	private bool TryLocalTile(out GridPos tile)
	{
		foreach (var m in _client.Miners)
			if (m.Id == _client.LocalMinerId && m.Alive) { tile = new GridPos(m.X, m.Y); return true; }
		tile = default;
		return false;
	}

	private static bool WithinReveal(GridPos local, int x, int y) =>
		Mathf.Max(Mathf.Abs(local.X - x), Mathf.Abs(local.Y - y)) <= ListenItemRevealRadius;

	private void DrawShimmer(int x, int y, Color col, int ts)
	{
		var c = new Vector2(x * ts + ts / 2f, y * ts + ts / 2f);
		DrawCircle(c, ts * 0.42f, col); // soft diffuse glow; identical for items and decoys
	}
```

(`WorldRenderer` already has `using Miner49er.Core;`, so `ItemPlacement`, `GridPos`, and `TileType` resolve. `_Process` already calls `QueueRedraw()` every frame, so the pulse animates with no extra wiring.)

- [ ] **Step 4: Build the whole solution — verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors. (Visual correctness — toolbox boxes, neutral shimmer through rock, decoys indistinguishable from buried — is play-tested by the user after the branch is complete.)

- [ ] **Step 5: Commit**

```bash
git add game/net/MatchClient.cs game/Main.cs game/WorldRenderer.cs
git commit -m "feat(game): render toolboxes, and shimmer buried items + decoys under Listen

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Godot — spill SFX when an item unburies

**Files:**
- Modify: `game/audio/SfxLibrary.cs`
- Modify: `game/net/MatchAudio.cs`

`game/` uses **TAB** indentation.

- [ ] **Step 1: Add a `Spill` placeholder tone**

In `game/audio/SfxLibrary.cs`, after the `Pickup` line (23) add:

```csharp
	public static AudioStream Spill => Get("spill", () => Tone(0.18f, 500f, 160f)); // gritty falling — rubble spilling an item
```

- [ ] **Step 2: Track item placement and play the spill on Buried→Loose**

In `game/net/MatchAudio.cs`:

(a) After the `_prevItems` field (line 22) add:

```csharp
	private readonly Dictionary<(int x, int y), ItemPlacement> _prevPlacement = new();
```

(b) In `_Process`, the item section currently reads (lines 84-96):

```csharp
		var localTile = LocalTile();
		foreach (var prev in _prevItems)
		{
			bool stillThere = false;
			foreach (var it in _client.Items)
				if (it.X == prev.x && it.Y == prev.y) { stillThere = true; break; }
			// An item that vanished next to the local miner = a pickup; play near it.
			if (!stillThere && localTile is { } lt
				&& System.Math.Abs(lt.x - prev.x) <= 1 && System.Math.Abs(lt.y - prev.y) <= 1)
				OneShot(SfxLibrary.Pickup, WorldOf(prev.x, prev.y));
		}
		_prevItems.Clear();
		foreach (var it in _client.Items) _prevItems.Add((it.X, it.Y));
```

Replace that whole block with:

```csharp
		var localTile = LocalTile();
		foreach (var prev in _prevItems)
		{
			bool stillThere = false;
			foreach (var it in _client.Items)
				if (it.X == prev.x && it.Y == prev.y) { stillThere = true; break; }
			// An item that vanished next to the local miner = a pickup; play near it.
			if (!stillThere && localTile is { } lt
				&& System.Math.Abs(lt.x - prev.x) <= 1 && System.Math.Abs(lt.y - prev.y) <= 1)
				OneShot(SfxLibrary.Pickup, WorldOf(prev.x, prev.y));
		}

		// An item that flipped Buried -> Loose at the same tile = freshly unburied; spill near the local miner.
		foreach (var it in _client.Items)
		{
			if (it.Placement == ItemPlacement.Loose
				&& _prevPlacement.TryGetValue((it.X, it.Y), out var prevP) && prevP == ItemPlacement.Buried
				&& localTile is { } lt2
				&& System.Math.Abs(lt2.x - it.X) <= 1 && System.Math.Abs(lt2.y - it.Y) <= 1)
				OneShot(SfxLibrary.Spill, WorldOf(it.X, it.Y));
		}

		_prevItems.Clear();
		_prevPlacement.Clear();
		foreach (var it in _client.Items)
		{
			_prevItems.Add((it.X, it.Y));
			_prevPlacement[(it.X, it.Y)] = it.Placement;
		}
```

(`MatchAudio` already has `using Miner49er.Core;`, so `ItemPlacement` resolves.)

- [ ] **Step 3: Build the whole solution — verify it compiles**

Run: `dotnet build Miner49er.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add game/audio/SfxLibrary.cs game/net/MatchAudio.cs
git commit -m "feat(game): spill SFX when a buried item is unearthed near the local miner

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## After all tasks

1. Run the full Core suite once more: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` — expect all green (151 prior + ~14 new ≈ 165).
2. Build the whole solution: `dotnet build Miner49er.sln` — expect 0 errors.
3. Hand back for the user's play-test (visual shimmer, toolbox boxes, decoys indistinguishable from real caches, mining/blasting drops loose items, spill SFX). **Do not merge** until the user approves the play-test.
4. Then use superpowers:finishing-a-development-branch to complete the branch.

**Listen chime (deferred):** the spec lists an optional proximity chime while listening near a suspicious spot. It is intentionally **not** in this plan (YAGNI for the first cut — the shimmer is the primary reveal). Revisit after play-test if the visual alone feels too quiet.

---

## Self-Review

**Spec coverage:**
- §1 placement state + map-gen split → Task 1. Decoys (config, `GeneratedMap.Decoys`, `PlaceDecoys`) → Task 2. ✓
- §2 pickup guard + `UnburyItemsAt` + `ItemUnburied` + mining/blast wiring + tick ordering → Task 3 (ordering is inherent: `AdvanceActivities`→`PickUpItems`→`AdvanceCharges`, unchanged). ✓
- §3 `ItemSnapshot.Placement` codec + factory; decoys un-synced → Task 4 (decoys never enter netcode — confirmed, only `GeneratedMap.Decoys` → client regen). ✓
- §4 toolbox/loose render, neutral shimmer for buried+decoys through rock within radius, `Listening`/`Decoys` plumbing, spill SFX → Tasks 5 & 6. Listen chime explicitly deferred. ✓
- §5 tests: placement split + determinism + round-robin (Task 1), decoys (Task 2), buried-not-collected + unbury-on-mine + unbury-on-blast + loose-collectible (Task 3), codec/factory round-trip placement (Task 4). ✓

**Placeholder scan:** none — every code step shows complete code; every run step shows the command + expected result.

**Type consistency:** `ItemPlacement { Toolbox, Buried, Loose }` and `Item(GridPos, ItemKind, ItemPlacement = Toolbox)` defined in Task 1 and used consistently in Tasks 3-6. `ItemSnapshot(int, int, ItemKind, ItemPlacement)` defined Task 4, consumed in Tasks 5-6. `GeneratedMap.Decoys : IReadOnlyList<GridPos>` (Task 2) → `MatchClient.Decoys` (Task 5). `UnburyItemsAt(GridPos)` / `ItemUnburied(Pos, Kind)` consistent across Task 3. `MatchClient.Begin(TileGrid, IReadOnlyList<GridPos>, int, Node2D)` (Task 5) matches the `Main.cs` call site updated in the same task. ✓
