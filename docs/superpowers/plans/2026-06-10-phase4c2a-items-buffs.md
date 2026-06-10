# Phase 4c-2a — Item Framework & Auto-Apply Buffs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministically-placed collectible items and the first three buff items (speed potion, longer vision, bigger blast) that auto-apply timed status effects on walk-over, synced to clients and rendered under fog; retire the 4c-1 debug key.

**Architecture:** Items live in pure `Miner49er.Core` as a map-gen output and an authoritative list on `Simulation`, synced as a full list in the per-tick snapshot (like charges). The two new effect channels reuse the generic 4c-1 `StatusEffect` engine; only their aggregation differs (VisionRadius/BlastRadius are additive, MoveSpeed multiplies). Base vision radius migrates into `SimConfig` so the sim ships each miner's effective radius for the client fog.

**Tech Stack:** C# / .NET 8, xUnit, Godot 4.6.3 (.NET/Mono). Core is engine-free and unit-tested; the Godot layer transports bytes and renders.

**Conventions:**
- `src/Miner49er.Core/` and tests use **4-space** indentation; `game/` files use **TABs**.
- Build/test: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` (works from Bash or PowerShell). Game build: `dotnet build Miner49er.csproj`.
- Headless smoke (optional, **PowerShell only**): `godot --headless --quit-after 180` → exit 0, no `ERROR`.
- Commit trailer: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Start on a feature branch (the executing skill creates the worktree/branch). Do **not** commit the pre-existing untracked `assets/Splash.*` or the CRLF-only `project.godot` diff.

---

## File Structure

**Core — new files:**
- `src/Miner49er.Core/Map/Item.cs` — `ItemKind` enum + `Item` record struct.

**Core — modified:**
- `src/Miner49er.Core/Map/MapConfig.cs` — `BaseItemCount`, `ItemsPerPlayer`.
- `src/Miner49er.Core/Map/GeneratedMap.cs` — `Items`.
- `src/Miner49er.Core/Map/MapGenerator.cs` — `PlaceItems` pass.
- `src/Miner49er.Core/Sim/StatusEffect.cs` — real `EffectChannel` / `EffectKind`.
- `src/Miner49er.Core/Sim/SimConfig.cs` — base vision radius + buff tunables.
- `src/Miner49er.Core/Sim/Simulation.cs` — item list, pickup pass, `Effective*` helpers.
- `src/Miner49er.Core/Sim/Charge.cs` — `BlastBonus`.
- `src/Miner49er.Core/Sim/SimEvent.cs` — `ItemPickedUp`.
- `src/Miner49er.Core/Net/Snapshots.cs` — `ItemSnapshot`, `WorldSnapshot.Items`, `MinerSnapshot.VisionRadius`.
- `src/Miner49er.Core/Net/SnapshotCodec.cs` + `SnapshotFactory.cs` — sync the above.

**Game — modified:**
- `game/Main.cs` — seed items into host sim; remove debug B-key.
- `game/net/NetworkManager.cs` — remove `SendDebugSpeed`/`ReceiveDebugSpeed`.
- `game/net/MatchHost.cs` — remove `ApplyDebugSpeed`.
- `game/net/MatchClient.cs` — expose synced items; fog reads synced vision radius.
- `game/WorldRenderer.cs` — draw items under fog.
- `game/SfxLibrary.cs` + `game/net/MatchAudio.cs` — pickup SFX.

**Tests — new:**
- `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs`
- `src/Miner49er.Core.Tests/SimulationItemsTests.cs`

**Tests — modified:**
- `StatusEffectTests.cs`, `MovementCadenceTests.cs` (migrate off debug kinds; add aggregation tests).
- `SimulationExplosiveTests.cs` (blast capture).
- `SnapshotCodecTests.cs`, `SnapshotFactoryTests.cs` (items + vision radius).

---

## Task 1: Item entity & deterministic placement (Core)

**Files:**
- Create: `src/Miner49er.Core/Map/Item.cs`
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`, `src/Miner49er.Core/Map/GeneratedMap.cs`, `src/Miner49er.Core/Map/MapGenerator.cs`
- Test: `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs`

- [ ] **Step 1: Create the item types**

Create `src/Miner49er.Core/Map/Item.cs`:

```csharp
namespace Miner49er.Core;

/// <summary>Kinds of collectible item placed on the map. 4c-2a ships the three
/// auto-apply buffs; 4c-2b appends carried items (WaterPlank, SlowMold).</summary>
public enum ItemKind { SpeedPotion, LongerVision, BiggerBlast }

/// <summary>A collectible sitting on a Floor tile, removed when a miner walks over it.</summary>
public readonly record struct Item(GridPos Pos, ItemKind Kind);
```

- [ ] **Step 2: Add the item-count knobs to MapConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, after the `GoldVeinCount` line (line 13), add:

```csharp
    public int BaseItemCount { get; set; } = 9;   // items on the base map
    public int ItemsPerPlayer { get; set; } = 1;  // light scaling with player count / map growth
```

- [ ] **Step 3: Add Items to GeneratedMap**

Replace the body of `src/Miner49er.Core/Map/GeneratedMap.cs`:

```csharp
namespace Miner49er.Core;

public sealed class GeneratedMap
{
    public required TileGrid Grid { get; init; }
    public required IReadOnlyList<GridPos> Spawns { get; init; }
    public required GridPos Center { get; init; }
    public required IReadOnlyList<Item> Items { get; init; }
}
```

- [ ] **Step 4: Write the failing placement tests**

Create `src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs`:

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
        Assert.Equal(a.Items, b.Items); // same positions and kinds, same order
    }

    [Fact]
    public void Items_land_on_floor_and_never_on_a_spawn()
    {
        var map = MapGenerator.Generate(Cfg(players: 4));
        var spawns = map.Spawns.ToHashSet();
        Assert.NotEmpty(map.Items);
        foreach (var item in map.Items)
        {
            Assert.Equal(TileType.Floor, map.Grid.Get(item.Pos));
            Assert.DoesNotContain(item.Pos, spawns);
        }
    }

    [Fact]
    public void Item_count_scales_with_player_count()
    {
        Assert.Equal(9, MapGenerator.Generate(Cfg(players: 1)).Items.Count);  // 9 + 1*0
        Assert.Equal(12, MapGenerator.Generate(Cfg(players: 4)).Items.Count); // 9 + 1*3
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

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MapGeneratorItemsTests`
Expected: FAIL to compile — `GeneratedMap` has no `Items` (and `PlaceItems` not wired). Good; that's the failing state.

- [ ] **Step 6: Implement PlaceItems and wire it into Generate**

In `src/Miner49er.Core/Map/MapGenerator.cs`, change the tail of `Generate` (lines 23-25) from:

```csharp
        PlaceGold(grid, rng, config.GoldVeinCount, region);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center };
```

to:

```csharp
        PlaceGold(grid, rng, config.GoldVeinCount, region);
        int itemCount = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
        var items = PlaceItems(grid, rng, itemCount, region, spawns);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center, Items = items };
```

Then add this method next to `PlaceGold` (e.g. after the `PlaceGold` method, before `HasRegionNeighbor`):

```csharp
    // Items sit on Floor tiles inside the traversable region, never on a spawn.
    // Candidates are drawn in deterministic grid order, then seed-shuffled, so the
    // result is identical on host and every client. Kinds cycle round-robin over
    // ItemKind in placement order for a balanced, predictable spread.
    private static List<Item> PlaceItems(TileGrid g, Random rng, int count,
        HashSet<GridPos> region, List<GridPos> spawns)
    {
        var spawnSet = new HashSet<GridPos>(spawns);
        var candidates = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !spawnSet.Contains(p))
            .ToList();
        Shuffle(candidates, rng);

        var kinds = Enum.GetValues<ItemKind>();
        var items = new List<Item>();
        int take = Math.Min(count, candidates.Count);
        for (int i = 0; i < take; i++)
            items.Add(new Item(candidates[i], kinds[i % kinds.Length]));
        return items;
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter MapGeneratorItemsTests`
Expected: PASS (4 tests).

- [ ] **Step 8: Run the full Core suite (catch the GeneratedMap break)**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS. (No other code constructs `GeneratedMap`, so the new required `Items` only affected `Generate`.)

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorItemsTests.cs
git commit -m "feat(core): deterministic item placement at map-gen

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Effect channels/kinds, tunables, aggregations + retire debug key

This task renames the throwaway debug effect kinds to the real item kinds and removes
their only game-side consumer in the **same commit**, so both `Miner49er.Core.Tests` and the
Godot project keep compiling. It also adds the per-buff `SimConfig` tunables and the two new
aggregation helpers.

**Files:**
- Modify: `src/Miner49er.Core/Sim/StatusEffect.cs`, `src/Miner49er.Core/Sim/SimConfig.cs`, `src/Miner49er.Core/Sim/Simulation.cs`
- Modify (game, remove debug): `game/net/MatchHost.cs`, `game/net/NetworkManager.cs`, `game/Main.cs`
- Test: `src/Miner49er.Core.Tests/StatusEffectTests.cs`, `src/Miner49er.Core.Tests/MovementCadenceTests.cs`

- [ ] **Step 1: Write the failing aggregation tests**

Append to `src/Miner49er.Core.Tests/StatusEffectTests.cs`, before the closing brace:

```csharp
    [Fact]
    public void EffectiveVisionRadius_adds_bonus_while_active_then_reverts()
    {
        var sim = Sim();
        Assert.Equal(5, sim.EffectiveVisionRadius(1)); // base from SimConfig
        sim.ApplyEffect(1, EffectKind.LongerVision, EffectChannel.VisionRadius, 3, 2.0);
        Assert.Equal(8, sim.EffectiveVisionRadius(1)); // 5 + 3
        sim.Tick(2.1);                                 // expire
        Assert.Equal(5, sim.EffectiveVisionRadius(1));
    }

    [Fact]
    public void EffectiveBlastBonus_sums_active_blast_effects()
    {
        var sim = Sim();
        Assert.Equal(0, sim.EffectiveBlastBonus(1));
        sim.ApplyEffect(1, EffectKind.BiggerBlast, EffectChannel.BlastRadius, 1, 5.0);
        Assert.Equal(1, sim.EffectiveBlastBonus(1));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter StatusEffectTests`
Expected: FAIL to compile — `EffectiveVisionRadius` / `EffectiveBlastBonus` / `EffectKind.LongerVision` don't exist yet.

- [ ] **Step 3: Replace the effect enums**

In `src/Miner49er.Core/Sim/StatusEffect.cs`, replace the two enum lines:

```csharp
public enum EffectChannel { MoveSpeed }            // 4c-2 adds MiningSpeed, VisionRadius, …
public enum EffectKind { DebugSpeed, DebugSlow }   // 4c-2 replaces these with SpeedPotion, SlowMold, …
```

with:

```csharp
public enum EffectChannel { MoveSpeed, VisionRadius, BlastRadius }
public enum EffectKind { SpeedPotion, LongerVision, BiggerBlast }  // 4c-2b appends SlowMold
```

- [ ] **Step 4: Add the base vision radius and buff tunables to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, after the `MaxMoveSeconds` line (line 14), add:

```csharp

    public int VisionRadius { get; set; } = 5;   // base fog radius (migrated from MatchClient)

    public double SpeedPotionFactor { get; set; } = 0.6;   // move-cadence multiplier while active
    public double SpeedPotionSeconds { get; set; } = 8.0;
    public int VisionBonus { get; set; } = 3;              // +tiles of fog radius while active
    public double VisionSeconds { get; set; } = 12.0;
    public int BlastBonus { get; set; } = 1;               // +radius on charges planted while active
    public double BlastSeconds { get; set; } = 12.0;
```

- [ ] **Step 5: Add the aggregation helpers to Simulation**

In `src/Miner49er.Core/Sim/Simulation.cs`, immediately after the `EffectiveMoveSeconds(Miner m)` method (ends at line 104), add:

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

- [ ] **Step 6: Migrate the existing tests off the debug kinds**

In `src/Miner49er.Core.Tests/StatusEffectTests.cs`, replace every `EffectKind.DebugSpeed` with
`EffectKind.SpeedPotion`, and the single `EffectKind.DebugSlow` (in
`Different_kinds_coexist_as_separate_instances`) with `EffectKind.BiggerBlast`. The channels and
magnitudes are unchanged (kind and channel are independent parameters).

In `src/Miner49er.Core.Tests/MovementCadenceTests.cs`, replace every `EffectKind.DebugSpeed` with
`EffectKind.SpeedPotion` and every `EffectKind.DebugSlow` with `EffectKind.BiggerBlast`. All these
effects keep `EffectChannel.MoveSpeed` and their magnitudes, so the multiply math is unchanged
(`Two_move_speed_effects_multiply` still applies two distinct kinds on the MoveSpeed channel →
`0.12 * 0.5 * 1.5 = 0.09`).

- [ ] **Step 7: Remove the debug key from the game (keeps the Godot build green)**

In `game/net/MatchHost.cs`, delete the `ApplyDebugSpeed` method (lines 49-54, including the
`// DEBUG(4c-1): remove in 4c-2` comment):

```csharp
	// DEBUG(4c-1): remove in 4c-2
	public void ApplyDebugSpeed(long peerId)
	{
		if (_peerToMiner.TryGetValue(peerId, out int minerId))
			_sim.ApplyEffect(minerId, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0);
	}

```

In `game/net/NetworkManager.cs`, delete the `SendDebugSpeed` method and the `ReceiveDebugSpeed`
RPC (the block around lines 224-233):

```csharp
	// DEBUG(4c-1): remove in 4c-2
	public void SendDebugSpeed()
	{
		if (IsHost) { _matchHost?.ApplyDebugSpeed(LocalId); return; }
		RpcId(1, nameof(ReceiveDebugSpeed));
	}

	// DEBUG(4c-1): remove in 4c-2
	[Rpc(MultiplayerApi.RpcMode.AnyPeer)]
	public void ReceiveDebugSpeed() => _matchHost?.ApplyDebugSpeed(Multiplayer.GetRemoteSenderId());

```

In `game/Main.cs`, delete the field declaration (line 19):

```csharp
	private bool _debugBoostPressed; // DEBUG(4c-1): remove in 4c-2
```

and the B-key block at the end of `_PhysicsProcess` (lines 129-132):

```csharp
		// DEBUG(4c-1): remove in 4c-2 — press B to self-apply a ×0.6 speed buff for 5s
		bool boost = Input.IsPhysicalKeyPressed(Key.B);
		if (boost && !_debugBoostPressed) NetworkManager.Instance.SendDebugSpeed();
		_debugBoostPressed = boost;
```

- [ ] **Step 8: Run the Core suite and the game build**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (existing tests migrated, 2 new aggregation tests green).

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors (no remaining `DebugSpeed`/`ApplyDebugSpeed` references).

- [ ] **Step 9: Commit**

```bash
git add src/Miner49er.Core/Sim/StatusEffect.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/StatusEffectTests.cs src/Miner49er.Core.Tests/MovementCadenceTests.cs game/net/MatchHost.cs game/net/NetworkManager.cs game/Main.cs
git commit -m "feat(core): real effect channels/kinds + vision/blast aggregation; remove 4c-1 debug key

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Item pickup pass & auto-apply buffs (Core)

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`, `src/Miner49er.Core/Sim/SimEvent.cs`
- Test: `src/Miner49er.Core.Tests/SimulationItemsTests.cs`

- [ ] **Step 1: Write the failing pickup tests**

Create `src/Miner49er.Core.Tests/SimulationItemsTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationItemsTests
{
    private static Simulation Sim(out Miner m)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        m = sim.AddMiner(1, new GridPos(1, 2));
        return sim;
    }

    [Fact]
    public void Walking_onto_an_item_collects_it_and_applies_the_buff()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));

        sim.TryMove(1, Direction.East); // step onto (2,2)
        sim.Tick(0.0);                  // pickup pass runs in Tick

        Assert.Empty(sim.Items);
        var e = Assert.Single(m.Effects);
        Assert.Equal(EffectKind.SpeedPotion, e.Kind);
        Assert.Equal(EffectChannel.MoveSpeed, e.Channel);
    }

    [Fact]
    public void LongerVision_item_raises_effective_vision_radius()
    {
        var sim = Sim(out _);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);
        Assert.Equal(8, sim.EffectiveVisionRadius(1)); // 5 + VisionBonus(3)
    }

    [Fact]
    public void A_collected_item_is_gone_for_everyone_else()
    {
        var sim = Sim(out _);
        sim.AddMiner(2, new GridPos(3, 2));
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));

        sim.TryMove(1, Direction.East); // miner 1 onto (2,2)
        sim.Tick(0.0);                  // collected by 1
        sim.TryMove(2, Direction.West); // miner 2 onto (2,2)
        sim.Tick(0.0);

        Assert.Empty(sim.Items);
        Assert.Empty(sim.GetMiner(2).Effects); // nothing left to pick up
    }

    [Fact]
    public void A_dead_miner_does_not_collect_an_item_under_it()
    {
        var sim = Sim(out var m);
        sim.AddItem(new Item(new GridPos(1, 2), ItemKind.SpeedPotion)); // on the miner's tile
        sim.KillMiner(1);
        sim.Tick(0.0);
        Assert.Single(sim.Items);
    }

    [Fact]
    public void Pickup_emits_an_ItemPickedUp_event()
    {
        var sim = Sim(out _);
        sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
        sim.TryMove(1, Direction.East);
        sim.Tick(0.0);

        var ev = Assert.Single(sim.DrainEvents().OfType<ItemPickedUp>());
        Assert.Equal(1, ev.MinerId);
        Assert.Equal(new GridPos(2, 2), ev.Pos);
        Assert.Equal(ItemKind.BiggerBlast, ev.Kind);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationItemsTests`
Expected: FAIL to compile — `sim.AddItem`, `sim.Items`, and `ItemPickedUp` don't exist.

- [ ] **Step 3: Add the ItemPickedUp event**

In `src/Miner49er.Core/Sim/SimEvent.cs`, add after the `TileFlooded` line (line 12):

```csharp
public sealed record ItemPickedUp(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent;
```

- [ ] **Step 4: Add the item list to Simulation**

In `src/Miner49er.Core/Sim/Simulation.cs`, after the `_charges` field (line 9) add a backing
list, and after the `Charges` property (line 13) expose it:

```csharp
    private readonly List<Item> _items = new();
```

```csharp
    public IReadOnlyList<Item> Items => _items;

    public void AddItem(Item item) => _items.Add(item);   // host seeds these from GeneratedMap.Items
```

- [ ] **Step 5: Add the pickup pass and buff dispatch**

In `src/Miner49er.Core/Sim/Simulation.cs`, add these two methods (e.g. just after
`AdvanceActivities`):

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
                break; // one miner collects it
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

- [ ] **Step 6: Call the pickup pass from Tick (after movement/activities)**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `Tick` (lines 186-197), add `PickUpItems();`
right after `AdvanceActivities(dt);`:

```csharp
    public void Tick(double dt)
    {
        Elapsed += dt;
        AdvanceEffects(dt);
        AdvanceCooldowns(dt);
        // Snapshot charges before advancing activities so newly-planted charges
        // (spawned this tick) are not advanced until the next tick.
        var chargesThisTick = _charges.ToList();
        AdvanceActivities(dt);
        PickUpItems();
        AdvanceCharges(chargesThisTick, dt);
        AdvanceFlood();
    }
```

- [ ] **Step 7: Run the pickup tests, then the full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationItemsTests`
Expected: PASS (5 tests).

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core.Tests/SimulationItemsTests.cs
git commit -m "feat(core): item pickup pass auto-applies timed buffs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Bigger-blast captured at plant (Core)

**Files:**
- Modify: `src/Miner49er.Core/Sim/Charge.cs`, `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`

- [ ] **Step 1: Write the failing capture tests**

Append to `src/Miner49er.Core.Tests/SimulationExplosiveTests.cs`, before the closing brace:

```csharp
    // A vertical rock column at x=2; miner at (1,3) faces it. Returns a sim ready to plant.
    private static Simulation FacingRockColumnEast(SimConfig cfg)
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        for (int y = 0; y < 7; y++) grid.Set(new GridPos(2, y), TileType.Rock);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 3));
        sim.TryMove(1, Direction.East); // blocked by rock, faces east toward (2,3)
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Charge_planted_with_blast_buff_captures_the_bonus_and_blasts_wider()
    {
        var sim = FacingRockColumnEast(
            new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastRockRadius = 1 });
        sim.ApplyEffect(1, EffectKind.BiggerBlast, EffectChannel.BlastRadius, 1, 12.0);

        sim.TryStartPlanting(1);
        sim.Tick(0.0); // create charge at (2,3)
        Assert.Equal(1, Assert.Single(sim.Charges).BlastBonus);

        sim.Tick(3.0); // detonate, Manhattan radius 1+1 = 2
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1))); // distance-2 rock gone
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 5)));
    }

    [Fact]
    public void Charge_planted_without_blast_buff_uses_the_base_radius()
    {
        var sim = FacingRockColumnEast(
            new SimConfig { PlantSeconds = 0.0, FuseSeconds = 3.0, BlastRockRadius = 1 });
        sim.TryStartPlanting(1);
        sim.Tick(0.0);
        Assert.Equal(0, Assert.Single(sim.Charges).BlastBonus);

        sim.Tick(3.0); // Manhattan radius 1 only
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1))); // distance-2 rock survives
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationExplosiveTests`
Expected: FAIL to compile — `Charge.BlastBonus` doesn't exist.

- [ ] **Step 3: Add BlastBonus to Charge**

Replace `src/Miner49er.Core/Sim/Charge.cs` with:

```csharp
namespace Miner49er.Core;

public sealed class Charge
{
    public int OwnerId { get; }
    public GridPos WallPos { get; }
    public double FuseRemaining { get; internal set; }
    public int BlastBonus { get; }   // owner's blast-radius bonus captured at plant time

    internal Charge(int ownerId, GridPos wallPos, double fuse, int blastBonus)
    {
        OwnerId = ownerId;
        WallPos = wallPos;
        FuseRemaining = fuse;
        BlastBonus = blastBonus;
    }
}
```

- [ ] **Step 4: Capture the bonus at plant and apply it at detonation**

In `src/Miner49er.Core/Sim/Simulation.cs`, in `CompleteActivity` (planting branch, line 328),
change:

```csharp
            _charges.Add(new Charge(m.Id, target, Config.FuseSeconds));
```

to:

```csharp
            _charges.Add(new Charge(m.Id, target, Config.FuseSeconds, EffectiveBlastBonus(m.Id)));
```

Then in `Detonate` (lines 273-303), change the rock-radius line (line 278) from:

```csharp
        int r = Config.BlastRockRadius;
```

to:

```csharp
        int r = Config.BlastRockRadius + charge.BlastBonus;
```

and the kill-radius check (line 297) from:

```csharp
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius)
```

to:

```csharp
            if (m.Alive && m.Pos.ChebyshevTo(charge.WallPos) <= Config.BlastKillRadius + charge.BlastBonus)
```

- [ ] **Step 5: Run the explosive tests, then the full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter SimulationExplosiveTests`
Expected: PASS (existing + 2 new).

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/Charge.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationExplosiveTests.cs
git commit -m "feat(core): bigger-blast buff captured on the charge at plant time

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Snapshot sync — item list + per-miner vision radius (Core)

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`, `src/Miner49er.Core/Net/SnapshotCodec.cs`, `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`

- [ ] **Step 1: Update the snapshot codec tests (and existing constructions)**

In `src/Miner49er.Core.Tests/SnapshotCodecTests.cs`, the existing `MinerSnapshot` and
`WorldSnapshot` constructions need the new fields. Replace the `Round_trips_all_fields` body's
snapshot construction and add item assertions — replace lines 11-21 with:

```csharp
        var update = new TickUpdate(
            new WorldSnapshot(
                Tick: 7,
                Miners: new List<MinerSnapshot>
                {
                    new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8),
                    new(2, 9, 0, 0, false, 0, 0, 0.0, 0.24, 5),
                },
                Charges: new List<ChargeSnapshot> { new(1, 8, 8, 1.25) },
                Items: new List<ItemSnapshot> { new(6, 1, ItemKind.SpeedPotion), new(2, 5, ItemKind.BiggerBlast) },
                SecondsRemaining: 42.5f),
            TileChanges: new List<TileChange> { new(8, 8, true, TileType.DeepWater), new(2, 2, false) });
```

and add these assertions before the closing brace of `Round_trips_all_fields` (after line 36):

```csharp
        Assert.Equal(8, back.Snapshot.Miners[0].VisionRadius);
        Assert.Equal(5, back.Snapshot.Miners[1].VisionRadius);
        Assert.Equal(2, back.Snapshot.Items.Count);
        Assert.Equal(update.Snapshot.Items[0], back.Snapshot.Items[0]);
        Assert.Equal(update.Snapshot.Items[1], back.Snapshot.Items[1]);
```

In the same file, fix `Round_trips_empty_collections` (line 43) — add the empty `Items` list:

```csharp
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(), new List<ItemSnapshot>()),
```

and add after `Assert.Empty(back.Snapshot.Charges);`:

```csharp
        Assert.Empty(back.Snapshot.Items);
```

- [ ] **Step 2: Add the factory test for items + vision radius**

Append to `src/Miner49er.Core.Tests/SnapshotFactoryTests.cs`, before the closing brace:

```csharp
    [Fact]
    public void Captures_items_and_effective_vision_radius()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddItem(new Item(new GridPos(4, 1), ItemKind.LongerVision));
        sim.ApplyEffect(1, EffectKind.LongerVision, EffectChannel.VisionRadius, 3, 5.0);

        var snap = SnapshotFactory.Capture(sim, tick: 3);

        Assert.Equal(8, Assert.Single(snap.Miners).VisionRadius); // 5 + 3
        var item = Assert.Single(snap.Items);
        Assert.Equal(4, item.X);
        Assert.Equal(1, item.Y);
        Assert.Equal(ItemKind.LongerVision, item.Kind);
    }
```

- [ ] **Step 3: Run them to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "SnapshotCodecTests|SnapshotFactoryTests"`
Expected: FAIL to compile — `ItemSnapshot`, `WorldSnapshot.Items`, and `MinerSnapshot.VisionRadius` don't exist.

- [ ] **Step 4: Add the snapshot records**

Replace `src/Miner49er.Core/Net/Snapshots.cs` with:

```csharp
using System.Collections.Generic;

namespace Miner49er.Core.Net;

public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius);

public readonly record struct ChargeSnapshot(int OwnerId, int X, int Y, double FuseRemaining);

public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind);

/// <summary>One floor cell that changed; FromBlast drives the flash, NewType is
/// the tile it became (Floor for mining/blasts, water for the flood).</summary>
public readonly record struct TileChange(int X, int Y, bool FromBlast, TileType NewType = TileType.Floor);

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, float SecondsRemaining = -1f);

public sealed record TickUpdate(WorldSnapshot Snapshot, IReadOnlyList<TileChange> TileChanges);
```

- [ ] **Step 5: Encode/decode the new fields**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`, in `Write`, add the miner vision radius after
`MoveSeconds` (line 24):

```csharp
            w.Write(m.MoveSeconds); w.Write(m.VisionRadius);
```

and add the item block after the charge loop (after line 31, before the tile-change block):

```csharp
        w.Write(snap.Items.Count);
        foreach (var it in snap.Items)
        {
            w.Write(it.X); w.Write(it.Y); w.Write((int)it.Kind);
        }
```

In `Read`, update the miner construction (lines 54-56) to read the extra int:

```csharp
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(), r.ReadInt32()));
```

add the item read after the charge loop (after line 61):

```csharp
        int itemCount = r.ReadInt32();
        var items = new List<ItemSnapshot>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemSnapshot(r.ReadInt32(), r.ReadInt32(), (ItemKind)r.ReadInt32()));
```

and pass `items` into the returned `WorldSnapshot` (line 68):

```csharp
        return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, secondsRemaining), changes);
```

- [ ] **Step 6: Populate items and vision radius in the factory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, replace the body of `Capture` with:

```csharp
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id)))
            .ToList();

        var charges = sim.Charges
            .Select(c => new ChargeSnapshot(c.OwnerId, c.WallPos.X, c.WallPos.Y, c.FuseRemaining))
            .ToList();

        var items = sim.Items
            .Select(it => new ItemSnapshot(it.Pos.X, it.Pos.Y, it.Kind))
            .ToList();

        return new WorldSnapshot(tick, miners, charges, items, (float)sim.SecondsRemaining);
```

- [ ] **Step 7: Run the net tests, then the full suite**

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj --filter "SnapshotCodecTests|SnapshotFactoryTests"`
Expected: PASS.

Run: `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTests.cs src/Miner49er.Core.Tests/SnapshotFactoryTests.cs
git commit -m "feat(core): sync item list and per-miner vision radius in snapshots

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Godot wiring — seed items, render under fog, fog uses synced radius

No unit tests (Godot glue); verified by build + the user's play-test. The Core sync from Task 5
already compiles the game; this task makes items appear, render, and drive fog at runtime.

**Files:**
- Modify: `game/Main.cs` (host seeds items), `game/net/MatchClient.cs` (expose items, synced fog), `game/WorldRenderer.cs` (draw items under fog)

- [ ] **Step 1: Seed the placed items into the host simulation**

In `game/Main.cs`, the host builds `sim` from a freshly-regenerated map (lines 46-58). That
regen is a *separate* `GeneratedMap` from `map`; seed the host sim from **its own** items so
host and clients agree (both derive from the same seed). Change the host block: capture the
host map in a local and add an item-seeding loop. Replace lines 46-58:

```csharp
			var sim = new Simulation(
				MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount)).Grid,
				new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds },
				map.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding);
			var peerToMiner = new System.Collections.Generic.Dictionary<long, int>();
			for (int i = 0; i < nm.PeerOrder.Length; i++)
			{
				int minerId = i + 1;
				sim.AddMiner(minerId, map.Spawns[i]);
				peerToMiner[nm.PeerOrder[i]] = minerId;
			}
```

with:

```csharp
			var hostMap = MapGenerator.Generate(MapConfig.For(nm.MatchMode, seed, playerCount));
			var sim = new Simulation(
				hostMap.Grid,
				new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds },
				hostMap.Center,
				nm.MatchTimeLimitSeconds > 0 ? nm.MatchTimeLimitSeconds : (double?)null,
				nm.MatchFlooding);
			foreach (var item in hostMap.Items)
				sim.AddItem(item);
			var peerToMiner = new System.Collections.Generic.Dictionary<long, int>();
			for (int i = 0; i < nm.PeerOrder.Length; i++)
			{
				int minerId = i + 1;
				sim.AddMiner(minerId, hostMap.Spawns[i]);
				peerToMiner[nm.PeerOrder[i]] = minerId;
			}
```

(Note: `hostMap.Spawns` matches `map.Spawns` since both regen from the same seed; using
`hostMap` consistently keeps the host self-contained.)

- [ ] **Step 2: Expose the synced item list on MatchClient and read the synced vision radius for fog**

In `game/net/MatchClient.cs`:

Delete the constant (line 14):

```csharp
	public const int VisionRadius = 5;
```

Add an items field and property (next to `_charges` / `Charges`). After line 19
(`public IReadOnlyList<ChargeSnapshot> Charges => _charges;`) add:

```csharp
	public IReadOnlyList<ItemSnapshot> Items => _items;
```

After the `_charges` field (line 25) add:

```csharp
	private List<ItemSnapshot> _items = new();
```

In `ApplyUpdate`, after `_charges = new List<ChargeSnapshot>(update.Snapshot.Charges);` (line 74)
add:

```csharp
		_items = new List<ItemSnapshot>(update.Snapshot.Items);
```

Replace `UpdateFog` (lines 113-118) so it uses the local miner's synced radius:

```csharp
	private void UpdateFog()
	{
		foreach (var m in _miners)
			if (m.Id == LocalMinerId && m.Alive)
				Fog.Update(Visibility.Compute(Grid, new GridPos(m.X, m.Y), m.VisionRadius));
	}
```

- [ ] **Step 3: Draw items under fog in WorldRenderer**

In `game/WorldRenderer.cs`, add item colors after the `FlashColor` definition (line 20):

```csharp
	private static readonly Color SpeedItemColor = new("4ad06a");  // green
	private static readonly Color VisionItemColor = new("4ad0d0"); // cyan
	private static readonly Color BlastItemColor = new("e08a2f");  // orange
```

In `_Draw`, after the charge loop (line 63) and before the flash loop, add the item pass —
drawn only on tiles the local fog currently shows:

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

- [ ] **Step 4: Build the game**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Optional headless smoke (PowerShell only)**

Run (PowerShell): `godot --headless --quit-after 180`
Expected: exit 0, no `ERROR` / `Assemblies not found` lines. (If running from Bash, this will
falsely fail — use PowerShell. See the dev-environment notes.)

- [ ] **Step 6: Commit**

```bash
git add game/Main.cs game/net/MatchClient.cs game/WorldRenderer.cs
git commit -m "feat(game): seed items into host sim, render under fog, fog uses synced vision radius

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Pickup SFX (Godot)

Derived client-side from the synced item list shrinking — same approach as the splash-on-drown
derivation already in `MatchAudio`. No netcode.

**Files:**
- Modify: `game/SfxLibrary.cs`, `game/net/MatchAudio.cs`

- [ ] **Step 1: Add a Pickup placeholder sound**

In `game/SfxLibrary.cs`, add after the `Splash` line (line 22):

```csharp
	public static AudioStream Pickup => Get("pickup", () => Tone(0.12f, 700f, 1200f)); // bright rising blip
```

- [ ] **Step 2: Track the previous item set and play on removal near the local miner**

In `game/net/MatchAudio.cs`, add a field after `_prevAlive` (line 21):

```csharp
		private readonly HashSet<(int x, int y)> _prevItems = new();
```

At the end of `_Process` (after the `foreach (var m in _client.Miners)` loop closes, i.e. after
line 81), add the item-diff pass:

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

Add the `LocalTile` helper near `WorldOf` (after line 121):

```csharp
		private (int x, int y)? LocalTile()
		{
			foreach (var m in _client.Miners)
				if (m.Id == _client.LocalMinerId && m.Alive) return (m.X, m.Y);
			return null;
		}
```

- [ ] **Step 3: Build the game**

Run: `dotnet build Miner49er.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add game/SfxLibrary.cs game/net/MatchAudio.cs
git commit -m "feat(game): pickup SFX derived from the synced item list

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final verification & play-test

- [ ] **Full Core suite green:** `dotnet test src/Miner49er.Core.Tests/Miner49er.Core.Tests.csproj` → all pass (137 prior + ~13 new ≈ 150).
- [ ] **Game builds:** `dotnet build Miner49er.csproj` → 0 errors.
- [ ] **Headless smoke (PowerShell):** `godot --headless --quit-after 180` → exit 0.
- [ ] **User play-test** (host + client): items render as colored dots only inside the lit area;
  walking over a green dot speeds you up; cyan widens your fog; orange then planting a charge
  yields a visibly bigger blast; a pickup blip plays; the item disappears for everyone; the old
  B-key debug boost no longer does anything.
- [ ] After play-test passes → **REQUIRED SUB-SKILL:** superpowers:finishing-a-development-branch.

---

## Notes & risks

- **Tick ordering:** `PickUpItems` runs right after `AdvanceActivities` in `Tick`, which is after
  `MatchHost` applies `TryMove` for the tick — so a miner who stepped onto an item this tick
  collects it the same tick. Charges are advanced from a pre-tick snapshot, so pickup ordering
  relative to `AdvanceCharges` is irrelevant.
- **Positional codec:** adding `MinerSnapshot.VisionRadius` (10th field) and an item block means
  `Write`, `Read`, and `SnapshotFactory.Capture` must all change together — Task 5 does this in
  one commit, and `SnapshotCodecTests` round-trips them.
- **ReachCenter sparsity:** the 40×40 ReachCenter map uses the same `BaseItemCount`; acceptable
  for now, tunable via `MapConfig.For` later.
- **No new RPCs:** items and per-miner vision radius are additive snapshot fields; the whole
  feature stays within the naive full-state-sync model.
- **Build-green ordering:** Task 2 renames the effect enum **and** removes its only game consumer
  in the same commit, so neither the Core test project nor the Godot project ever references a
  deleted `EffectKind`.
```
