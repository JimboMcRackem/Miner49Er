# Treasure Hunt Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a multiplayer-only Treasure Hunt game mode where each player races to locate and deposit two privately-assigned buried idol treasures into their placed chest.

**Architecture:** New `GameMode.TreasureHunt` case threaded through core sim (idol assignment, chest/deposit mechanics), network snapshot (progress + chest positions), and Godot UI (HUD idol panel, WorldRenderer chest/idol rendering). Assignment is deterministic from match seed — no private RPC needed. All 7 tasks are sequential; each produces a testable slice.

**Tech Stack:** C# / .NET 10, Godot 4.6.3 .NET, xunit for core tests. Run tests: `dotnet test src/Miner49er.Core.Tests/ -v minimal` from repo root.

## Global Constraints

- Never `git add -A`; stage only the files listed in each task.
- Run `godot` via PowerShell only (not Bash — false "assemblies not found").
- `Miner.Held` has `internal set` — only code inside `Miner49er.Core` can set it.
- `WorldSnapshot` is a positional record — add new params as trailing optional fields to avoid breaking codec.
- Idol art assets (`res://assets/objects/idol_*.png`) do not exist yet; use `ResourceLoader.Exists()` guard and fallback color rendering for now.
- All tests live in `src/Miner49er.Core.Tests/`; test class names are unqualified (no namespace).

---

### Task 1: ItemKind expansion + TreasureAssignment helper

**Files:**
- Modify: `src/Miner49er.Core/Map/Item.cs`
- Create: `src/Miner49er.Core/Sim/TreasureAssignment.cs`
- Test: `src/Miner49er.Core.Tests/TreasureAssignmentTests.cs`

**Interfaces:**
- Produces:
  - `ItemKind.TreasureChest`, `ItemKind.IdolVishnu` … `ItemKind.IdolSkull` (17 idol values)
  - `ItemKind.IsCarried()` returns `true` for `TreasureChest` and all `Idol*` kinds
  - `TreasureAssignment.For(int seed, int minerId) → (ItemKind A, ItemKind B)`
  - `TreasureAssignment.AllAssigned(int seed, int playerCount) → ItemKind[]` (length = playerCount × 2)
  - `TreasureAssignment.AllIdols() → ItemKind[]` (all 17, fixed order)

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/TreasureAssignmentTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class TreasureAssignmentTests
{
    [Fact]
    public void For_returns_two_distinct_idols()
    {
        var (a, b) = TreasureAssignment.For(42, 1);
        Assert.NotEqual(a, b);
        Assert.True(a.IsIdol());
        Assert.True(b.IsIdol());
    }

    [Fact]
    public void AllAssigned_length_equals_playerCount_times_two()
    {
        var idols = TreasureAssignment.AllAssigned(42, 3);
        Assert.Equal(6, idols.Length);
    }

    [Fact]
    public void AllAssigned_all_unique_for_eight_players()
    {
        var idols = TreasureAssignment.AllAssigned(99, 8);
        Assert.Equal(16, idols.Length);
        Assert.Equal(idols.Length, new HashSet<ItemKind>(idols).Count);
    }

    [Fact]
    public void Different_seeds_produce_different_assignments()
    {
        var (a1, b1) = TreasureAssignment.For(1, 1);
        var (a2, b2) = TreasureAssignment.For(2, 1);
        // Extremely unlikely to collide
        Assert.False(a1 == a2 && b1 == b2);
    }

    [Fact]
    public void For_is_consistent_with_AllAssigned_slice()
    {
        int seed = 77;
        var all = TreasureAssignment.AllAssigned(seed, 4);
        for (int minerId = 1; minerId <= 4; minerId++)
        {
            var (a, b) = TreasureAssignment.For(seed, minerId);
            Assert.Equal(all[(minerId - 1) * 2],     a);
            Assert.Equal(all[(minerId - 1) * 2 + 1], b);
        }
    }

    [Fact]
    public void TreasureChest_is_carried()
    {
        Assert.True(ItemKind.TreasureChest.IsCarried());
    }

    [Fact]
    public void Idols_are_carried()
    {
        Assert.True(ItemKind.IdolVishnu.IsCarried());
        Assert.True(ItemKind.IdolSkull.IsCarried());
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~TreasureAssignment" -v minimal
```

Expected: compile error — `TreasureAssignment`, `IsIdol()`, and new `ItemKind` values do not exist.

- [ ] **Step 3: Add new ItemKind values + IsCarried/IsIdol**

Edit `src/Miner49er.Core/Map/Item.cs`. Replace the `ItemKind` enum and `IsCarried` extension:

```csharp
namespace Miner49er.Core;

public enum ItemKind
{
    SpeedPotion, LongerVision, BiggerBlast,
    WaterPlank, SlowMold, Lantern,
    Chest,
    LifePotion,
    BossChest,
    Detonator,
    Reel,
    // Treasure Hunt
    TreasureChest,
    IdolVishnu, IdolZeus, IdolAnubis, IdolOdin,
    IdolShiva, IdolBuddha, IdolRa, IdolQuetzalcoatl,
    IdolUrn, IdolLamp, IdolMace, IdolSceptre, IdolGlobe,
    IdolTrophyCup, IdolChalice, IdolCrown, IdolSkull,
}

public static class ItemKindExtensions
{
    public static bool IsCarried(this ItemKind k) =>
        k is ItemKind.WaterPlank or ItemKind.SlowMold or ItemKind.Lantern
          or ItemKind.Detonator or ItemKind.TreasureChest
          or (>= ItemKind.IdolVishnu and <= ItemKind.IdolSkull);

    public static bool IsPlaceable(this ItemKind k) =>
        k is ItemKind.SpeedPotion or ItemKind.LongerVision or ItemKind.BiggerBlast;

    public static bool IsIdol(this ItemKind k) =>
        k >= ItemKind.IdolVishnu && k <= ItemKind.IdolSkull;
}

public enum ItemPlacement { Toolbox, Buried, Loose }

public readonly record struct Item(GridPos Pos, ItemKind Kind, ItemPlacement Placement = ItemPlacement.Toolbox);
```

- [ ] **Step 4: Create TreasureAssignment.cs**

Create `src/Miner49er.Core/Sim/TreasureAssignment.cs`:

```csharp
namespace Miner49er.Core;

public static class TreasureAssignment
{
    public static (ItemKind A, ItemKind B) For(int seed, int minerId)
    {
        var pool = AllIdols();
        Shuffle(pool, seed);
        int i = (minerId - 1) * 2;
        return (pool[i], pool[i + 1]);
    }

    public static ItemKind[] AllAssigned(int seed, int playerCount)
    {
        var pool = AllIdols();
        Shuffle(pool, seed);
        return pool[..(playerCount * 2)];
    }

    public static ItemKind[] AllIdols() => new[]
    {
        ItemKind.IdolVishnu, ItemKind.IdolZeus,    ItemKind.IdolAnubis,
        ItemKind.IdolOdin,   ItemKind.IdolShiva,   ItemKind.IdolBuddha,
        ItemKind.IdolRa,     ItemKind.IdolQuetzalcoatl,
        ItemKind.IdolUrn,    ItemKind.IdolLamp,    ItemKind.IdolMace,
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

- [ ] **Step 5: Run tests — expect pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~TreasureAssignment" -v minimal
```

Expected: 7 tests passing.

- [ ] **Step 6: Run full suite — expect pass (no regressions)**

```
dotnet test src/Miner49er.Core.Tests/ -v minimal
```

Expected: all existing tests still pass.

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Map/Item.cs src/Miner49er.Core/Sim/TreasureAssignment.cs src/Miner49er.Core.Tests/TreasureAssignmentTests.cs
git commit -m "feat(treasure-hunt): add 17 idol ItemKinds + TreasureChest; TreasureAssignment helper"
```

---

### Task 2: Simulation — TreasureHunt state, chest placement, auto-deposit

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/SimulationTreasureHuntTests.cs`

**Interfaces:**
- Consumes: `TreasureAssignment.For`, `ItemKind.TreasureChest`, `ItemKind.IsIdol()`
- Produces:
  - `SimConfig.TreasureHuntMode: bool`
  - `SimEvent` subclasses: `TreasureChestPlaced(int MinerId, GridPos Pos)`, `IdolDeposited(int MinerId, ItemKind Kind)`
  - `Simulation.TreasureWinner() → int` (-1 = none)
  - `Simulation.GetTreasureProgress() → IReadOnlyList<TreasureProgressData>`
  - `Simulation.GetPlacedChests() → IReadOnlyList<PlacedChestData>`
  - `AddMiner()` auto-gives TreasureChest + assigns idols when `TreasureHuntMode = true`
  - `TryUseItem()` places chest and blocks non-owner chest pickup
  - `TryMove()` auto-deposits idol on chest tile

Note: `TreasureProgressData` and `PlacedChestData` are plain value tuples used only to supply data to SnapshotFactory. Define them as records in Simulation.cs (internal visibility is fine since SnapshotFactory is in the same assembly).

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/SimulationTreasureHuntTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class SimulationTreasureHuntTests
{
    private static Simulation TreasureSim(int playerCount, out Miner[] miners)
    {
        var cfg = new SimConfig { Seed = 42, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        miners = new Miner[playerCount];
        for (int i = 0; i < playerCount; i++)
            miners[i] = sim.AddMiner(i + 1, new GridPos(1 + i, 1));
        return sim;
    }

    [Fact]
    public void Miner_starts_holding_TreasureChest()
    {
        TreasureSim(1, out var miners);
        Assert.Equal(ItemKind.TreasureChest, miners[0].Held);
    }

    [Fact]
    public void Using_held_chest_places_it_on_floor()
    {
        var sim = TreasureSim(1, out var miners);
        sim.TryUseItem(1);
        Assert.Null(miners[0].Held);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.TreasureChest && it.Pos == miners[0].Pos);
    }

    [Fact]
    public void Only_owner_can_pick_up_placed_chest()
    {
        var sim = TreasureSim(2, out var miners);
        // Miner 1 places chest at (1,1)
        sim.TryUseItem(1);
        // Move miner 2 to (1,1) and use
        sim.TryMove(2, Direction.West); // (2,1) -> (1,1)
        sim.TryUseItem(2); // should NOT pick up miner 1's chest
        Assert.Null(miners[1].Held); // miner 2 still holds nothing (started empty after giving chest)
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.TreasureChest);
    }

    [Fact]
    public void Walking_onto_own_chest_with_assigned_idol_deposits_it()
    {
        var sim = TreasureSim(1, out var miners);
        // Place chest at (1,1)
        sim.TryUseItem(1);
        Assert.Null(miners[0].Held);
        // Give the miner their first assigned idol directly
        var (idolA, _) = TreasureAssignment.For(42, 1);
        miners[0].Held = idolA; // internal set accessible in same assembly as tests? No — Held has internal set in Core
        // Use the simulation helper instead
        sim.GiveItemForTest(1, idolA);
        // Move away and back onto chest tile
        sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.West); // step back onto (1,1) = chest tile
        sim.Tick(0);
        Assert.Equal(1, sim.GetTreasureProgress().First(p => p.MinerId == 1).Found);
    }

    [Fact]
    public void TreasureWinner_returns_minus_one_before_two_deposits()
    {
        TreasureSim(1, out _);
        var sim = TreasureSim(1, out _);
        Assert.Equal(-1, sim.TreasureWinner());
    }

    [Fact]
    public void TreasureWinner_returns_winner_after_two_deposits()
    {
        var sim = TreasureSim(1, out var miners);
        // Place chest
        sim.TryUseItem(1);
        var (idolA, idolB) = TreasureAssignment.For(42, 1);
        // Give idol A, deposit
        sim.GiveItemForTest(1, idolA);
        sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.West);
        sim.Tick(0);
        Assert.Equal(-1, sim.TreasureWinner()); // only 1 of 2
        // Give idol B, deposit
        sim.GiveItemForTest(1, idolB);
        sim.TryMove(1, Direction.East);
        sim.TryMove(1, Direction.West);
        sim.Tick(0);
        Assert.Equal(1, sim.TreasureWinner());
    }

    [Fact]
    public void GetPlacedChests_reflects_placed_chest_position()
    {
        var sim = TreasureSim(1, out _);
        Assert.Empty(sim.GetPlacedChests());
        sim.TryUseItem(1);
        var chests = sim.GetPlacedChests();
        Assert.Single(chests);
        Assert.Equal(1, chests[0].MinerId);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~SimulationTreasureHunt" -v minimal
```

Expected: compile errors — `TreasureHuntMode`, `GiveItemForTest`, `TreasureWinner`, `GetTreasureProgress`, `GetPlacedChests`, new SimEvents not found.

- [ ] **Step 3: Add `TreasureHuntMode` to SimConfig**

In `src/Miner49er.Core/Sim/SimConfig.cs`, add one property after `DynamiteEnabled`:

```csharp
public bool TreasureHuntMode { get; set; } = false;
```

- [ ] **Step 4: Add new SimEvents**

In `src/Miner49er.Core/Sim/SimEvent.cs`, append two records at the end of the file:

```csharp
public sealed record TreasureChestPlaced(int MinerId, GridPos Pos) : SimEvent;
public sealed record IdolDeposited(int MinerId, ItemKind Kind) : SimEvent;
```

- [ ] **Step 5: Extend Simulation**

In `src/Miner49er.Core/Sim/Simulation.cs` make the following changes:

**5a — Add three new fields after `_reelCharges`:**

```csharp
private readonly Dictionary<int, GridPos?> _chestPos       = new();
private readonly Dictionary<int, int>      _idolsFound     = new();
private readonly Dictionary<int, (ItemKind A, ItemKind B)> _idolAssignments = new();
```

**5b — Add data types near the top of the class (after the field declarations):**

```csharp
public readonly record struct TreasureProgressData(int MinerId, int Found);
public readonly record struct PlacedChestData(int MinerId, int X, int Y);
```

**5c — Modify `AddMiner` to initialize TreasureHunt state.** Replace the existing `AddMiner`:

```csharp
public Miner AddMiner(int id, GridPos pos, double invulRemaining = 0.0)
{
    var m = new Miner(id, pos) { InvulnerableRemaining = invulRemaining };
    _miners[id] = m;
    if (Config.TreasureHuntMode)
    {
        var (a, b) = TreasureAssignment.For(Config.Seed, id);
        _idolAssignments[id] = (a, b);
        _idolsFound[id]      = 0;
        _chestPos[id]        = null;
        m.Held               = ItemKind.TreasureChest;
    }
    return m;
}
```

**5d — Add `TryPlaceTreasureChest` private method** (place it alongside `DropLantern`):

```csharp
private bool TryPlaceTreasureChest(Miner m)
{
    if (!Grid.Get(m.Pos).IsEnterable()) return false;
    _items.Add(new Item(m.Pos, ItemKind.TreasureChest, ItemPlacement.Toolbox));
    _chestPos[m.Id] = m.Pos;
    m.Held = null;
    _events.Add(new TreasureChestPlaced(m.Id, m.Pos));
    return true;
}
```

**5e — In `TryUseItem`, step 1 (carried-item pickup), add ownership guard for TreasureChest.** Find this block inside `TryUseItem`:

```csharp
for (int i = _items.Count - 1; i >= 0; i--)
{
    var it = _items[i];
    if (it.Pos != m.Pos || it.Placement == ItemPlacement.Buried || !it.Kind.IsCarried()) continue;
    var taken = it.Kind;
```

Add the ownership check immediately after the `continue` line:

```csharp
    if (it.Pos != m.Pos || it.Placement == ItemPlacement.Buried || !it.Kind.IsCarried()) continue;
    // Only the owner can pick up a placed TreasureChest
    if (it.Kind == ItemKind.TreasureChest
        && (!_chestPos.TryGetValue(m.Id, out var ownedAt) || ownedAt != it.Pos)) continue;
    var taken = it.Kind;
```

When picked up, clear the owned position:
```csharp
    if (taken == ItemKind.TreasureChest) _chestPos[m.Id] = null;
```
Insert this line right after `m.Held = taken;` and before the `_events.Add(...)` line.

**5f — In `TryUseItem`, add `TreasureChest` to the held-item switch:**

```csharp
return held switch
{
    ItemKind.WaterPlank   => TryPlacePlank(m),
    ItemKind.SlowMold     => DropMold(m),
    ItemKind.Lantern      => DropLantern(m),
    ItemKind.Detonator    => TryPlantDetonator(m),
    ItemKind.Reel         => TryDetonateReel(m),
    ItemKind.TreasureChest => TryPlaceTreasureChest(m),
    _ => false,
};
```

**5g — In `TryMove`, add auto-deposit after the `MinerReachedCenter` block.** Find:

```csharp
if (Center is { } c && target == c && FirstToReachCenter < 0 && m.Alive)
{
    FirstToReachCenter = id;
    _events.Add(new MinerReachedCenter(id));
}
```

Add immediately after:

```csharp
// Treasure Hunt: auto-deposit when stepping onto own chest tile holding an assigned idol.
// Deposit check runs before the pickup check in TryUseItem, so depositing always wins.
if (m.Alive && Config.TreasureHuntMode
    && _chestPos.TryGetValue(id, out var chestAt) && chestAt == target
    && m.Held is ItemKind heldIdol && heldIdol.IsIdol()
    && _idolAssignments.TryGetValue(id, out var assign)
    && (heldIdol == assign.A || heldIdol == assign.B))
{
    m.Held = null;
    _idolsFound[id]++;
    _events.Add(new IdolDeposited(id, heldIdol));
}
```

**5h — Add public query methods** (place after `TryThrowStone`):

```csharp
public int TreasureWinner()
{
    foreach (var kv in _idolsFound)
        if (kv.Value >= 2 && _miners.TryGetValue(kv.Key, out var m) && m.Alive)
            return kv.Key;
    return -1;
}

public IReadOnlyList<TreasureProgressData> GetTreasureProgress()
{
    var list = new List<TreasureProgressData>(_idolsFound.Count);
    foreach (var kv in _idolsFound)
        list.Add(new TreasureProgressData(kv.Key, kv.Value));
    return list;
}

public IReadOnlyList<PlacedChestData> GetPlacedChests()
{
    var list = new List<PlacedChestData>();
    foreach (var kv in _chestPos)
        if (kv.Value is { } pos)
            list.Add(new PlacedChestData(kv.Key, pos.X, pos.Y));
    return list;
}
```

**5i — Add test helper** (only compiled in test builds — add `#if DEBUG` guard or just add as internal for now):

```csharp
// Used only by unit tests to give a miner an item without using the UI verb.
internal void GiveItemForTest(int minerId, ItemKind kind)
{
    if (_miners.TryGetValue(minerId, out var m)) m.Held = kind;
}
```

- [ ] **Step 6: Run tests — expect pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~SimulationTreasureHunt" -v minimal
```

Expected: 7 tests passing.

- [ ] **Step 7: Run full suite — no regressions**

```
dotnet test src/Miner49er.Core.Tests/ -v minimal
```

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/SimulationTreasureHuntTests.cs
git commit -m "feat(treasure-hunt): sim state, chest placement, idol auto-deposit, TreasureWinner"
```

---

### Task 3: GameMode + RoundResolver

**Files:**
- Modify: `src/Miner49er.Core/Sim/GameMode.cs`
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs`
- Test: `src/Miner49er.Core.Tests/RoundResolverTreasureHuntTests.cs`

**Interfaces:**
- Consumes: `Simulation.TreasureWinner()`
- Produces: `GameMode.TreasureHunt = 4`; `RoundResolver.Resolve()` handles TreasureHunt

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/RoundResolverTreasureHuntTests.cs`:

```csharp
using Miner49er.Core;
using Xunit;

public class RoundResolverTreasureHuntTests
{
    private static Simulation TreasureSim(int seed = 1)
    {
        var cfg = new SimConfig { Seed = seed, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMiner(2, new GridPos(3, 1));
        return sim;
    }

    [Fact]
    public void Ongoing_when_no_winner_yet()
    {
        var sim = TreasureSim();
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Win_when_treasure_winner_detected()
    {
        var sim = TreasureSim();
        // Force miner 1 to win: place chest, deposit both idols
        sim.TryUseItem(1); // place chest at (1,1)
        var (a, b) = TreasureAssignment.For(1, 1);
        sim.GiveItemForTest(1, a);
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.West);
        sim.Tick(0);
        sim.GiveItemForTest(1, b);
        sim.TryMove(1, Direction.East); sim.TryMove(1, Direction.West);
        sim.Tick(0);
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Last_man_standing_still_applies()
    {
        var sim = TreasureSim();
        sim.KillMiner(2);
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~RoundResolverTreasureHunt" -v minimal
```

Expected: `GameMode.TreasureHunt` not found.

- [ ] **Step 3: Add GameMode.TreasureHunt**

Replace the content of `src/Miner49er.Core/Sim/GameMode.cs`:

```csharp
namespace Miner49er.Core;

public enum GameMode { LastManStanding, GoldRush, ReachCenter, Expedition, TreasureHunt }
```

- [ ] **Step 4: Update RoundResolver**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, extend the `Resolve` method. Find the switch:

```csharp
return mode switch
{
    GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                              && sim.GetMiner(sim.FirstToReachCenter).Alive
        => RoundResult.Win(sim.FirstToReachCenter),
    GameMode.GoldRush when sim.TimeExpired
        => RoundResult.Win(MostGoldWinner(alive)),
    _ when sim.TimeExpired
        => RoundResult.Loss(),
    _ => RoundResult.Ongoing(),
};
```

Add the TreasureHunt case **before** the catch-all `_ when sim.TimeExpired`:

```csharp
return mode switch
{
    GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                              && sim.GetMiner(sim.FirstToReachCenter).Alive
        => RoundResult.Win(sim.FirstToReachCenter),
    GameMode.GoldRush when sim.TimeExpired
        => RoundResult.Win(MostGoldWinner(alive)),
    GameMode.TreasureHunt when sim.TreasureWinner() is int w and w >= 0
        => RoundResult.Win(w),
    _ when sim.TimeExpired
        => RoundResult.Loss(),
    _ => RoundResult.Ongoing(),
};
```

- [ ] **Step 5: Run tests — expect pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~RoundResolverTreasureHunt" -v minimal
```

Expected: 3 tests passing.

- [ ] **Step 6: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/ -v minimal
```

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Sim/GameMode.cs src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core.Tests/RoundResolverTreasureHuntTests.cs
git commit -m "feat(treasure-hunt): GameMode.TreasureHunt + RoundResolver win condition"
```

---

### Task 4: Map placement of buried idols

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Test: `src/Miner49er.Core.Tests/MapGeneratorTreasureHuntTests.cs`

**Interfaces:**
- Consumes: `TreasureAssignment.AllAssigned()`
- Produces:
  - `MapConfig.BuriedIdolKinds: ItemKind[]?` (null when not TreasureHunt)
  - `MapConfig.For(GameMode.TreasureHunt, ...)` sets `BuriedIdolKinds`, zeros out gold/items
  - `MapGenerator.Generate()` calls `PlaceBuriedIdols()` when `BuriedIdolKinds != null`

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/MapGeneratorTreasureHuntTests.cs`:

```csharp
using Miner49er.Core;
using System.Linq;
using Xunit;

public class MapGeneratorTreasureHuntTests
{
    [Fact]
    public void Generate_places_exactly_playerCount_times_two_idols()
    {
        int playerCount = 3;
        var cfg = MapConfig.For(GameMode.TreasureHunt, 42, playerCount);
        var map = MapGenerator.Generate(cfg);
        var idols = map.Items.Where(it => it.Kind.IsIdol()).ToList();
        Assert.Equal(playerCount * 2, idols.Count);
    }

    [Fact]
    public void All_placed_idols_are_buried()
    {
        var cfg = MapConfig.For(GameMode.TreasureHunt, 77, 2);
        var map = MapGenerator.Generate(cfg);
        foreach (var it in map.Items.Where(it => it.Kind.IsIdol()))
            Assert.Equal(ItemPlacement.Buried, it.Placement);
    }

    [Fact]
    public void No_gold_veins_in_treasure_hunt()
    {
        var cfg = MapConfig.For(GameMode.TreasureHunt, 1, 2);
        Assert.Equal(0, cfg.GoldVeinCount);
    }

    [Fact]
    public void Placed_idol_kinds_match_assignment_for_seed()
    {
        int seed = 55, players = 2;
        var cfg = MapConfig.For(GameMode.TreasureHunt, seed, players);
        var map = MapGenerator.Generate(cfg);
        var assigned = TreasureAssignment.AllAssigned(seed, players);
        var placed = map.Items.Where(it => it.Kind.IsIdol()).Select(it => it.Kind).ToHashSet();
        foreach (var kind in assigned)
            Assert.Contains(kind, placed);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~MapGeneratorTreasureHunt" -v minimal
```

Expected: `BuriedIdolKinds` not found; `MapConfig.For` doesn't handle `TreasureHunt`.

- [ ] **Step 3: Add `BuriedIdolKinds` to MapConfig**

In `src/Miner49er.Core/Map/MapConfig.cs`, add one property anywhere after `ChestCount`:

```csharp
public ItemKind[]? BuriedIdolKinds { get; set; } = null;
```

Then extend the `For()` static factory to handle `TreasureHunt`. Inside `For()`, after the existing `if (mode == GameMode.ReachCenter)` block, add:

```csharp
if (mode == GameMode.TreasureHunt)
{
    cfg.GoldVeinCount    = 0;
    cfg.ChestCount       = 0;
    cfg.BaseItemCount    = 0;
    cfg.VisibleItemCount = 0;
    cfg.BuriedIdolKinds  = TreasureAssignment.AllAssigned(seed, playerCount);
}
```

- [ ] **Step 4: Add `PlaceBuriedIdols` to MapGenerator and call it**

In `src/Miner49er.Core/Map/MapGenerator.cs`, add a private static method alongside `PlaceCarriedItems`:

```csharp
private static List<Item> PlaceBuriedIdols(TileGrid g, Random rng, ItemKind[] idolKinds,
    HashSet<GridPos> region, IEnumerable<Item> existing)
{
    var taken = new HashSet<GridPos>(existing.Select(it => it.Pos));
    var rockCands = g.Positions()
        .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region) && !taken.Contains(p))
        .ToList();
    Shuffle(rockCands, rng);
    var result = new List<Item>();
    for (int i = 0; i < idolKinds.Length && i < rockCands.Count; i++)
        result.Add(new Item(rockCands[i], idolKinds[i], ItemPlacement.Buried));
    return result;
}
```

Then in `Generate()`, add a call right after the `PlaceCarriedItems` line and before `PlaceDecoys`:

```csharp
items.AddRange(PlaceCarriedItems(grid, rng, config.WaterPlankCount, config.SlowMoldCount, config.LanternCount, config.DetonatorCount, region, spawns, items));
if (config.BuriedIdolKinds is { } idols)
    items.AddRange(PlaceBuriedIdols(grid, rng, idols, region, items));
var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);
```

- [ ] **Step 5: Run tests — expect pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~MapGeneratorTreasureHunt" -v minimal
```

Expected: 4 tests passing.

- [ ] **Step 6: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/ -v minimal
```

- [ ] **Step 7: Commit**

```
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/MapGeneratorTreasureHuntTests.cs
git commit -m "feat(treasure-hunt): bury assigned idols in rock; zero gold/items in TreasureHunt map"
```

---

### Task 5: Network snapshot + codec

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotTreasureHuntTests.cs`

**Interfaces:**
- Consumes: `Simulation.GetTreasureProgress()`, `Simulation.GetPlacedChests()`, `Simulation.TreasureProgressData`, `Simulation.PlacedChestData`
- Produces:
  - `TreasureProgressSnapshot(int MinerId, int Found)`
  - `PlacedChestSnapshot(int MinerId, int X, int Y)`
  - `WorldSnapshot` gains two trailing optional params
  - `SnapshotCodec.Write/Read` handles the new fields (appended at end of stream)

- [ ] **Step 1: Write failing tests**

Create `src/Miner49er.Core.Tests/SnapshotTreasureHuntTests.cs`:

```csharp
using Miner49er.Core;
using Miner49er.Core.Net;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class SnapshotTreasureHuntTests
{
    private static Simulation TreasureSim()
    {
        var cfg = new SimConfig { Seed = 1, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        return sim;
    }

    [Fact]
    public void Capture_includes_treasure_progress_when_treasure_hunt_mode()
    {
        var sim = TreasureSim();
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        Assert.NotNull(snap.TreasureProgress);
        Assert.Single(snap.TreasureProgress!);
        Assert.Equal(1, snap.TreasureProgress![0].MinerId);
        Assert.Equal(0, snap.TreasureProgress![0].Found);
    }

    [Fact]
    public void Capture_includes_placed_chests()
    {
        var sim = TreasureSim();
        sim.TryUseItem(1);  // place chest
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        Assert.NotNull(snap.PlacedChests);
        Assert.Single(snap.PlacedChests!);
    }

    [Fact]
    public void Codec_roundtrip_preserves_treasure_progress()
    {
        var sim = TreasureSim();
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.NotNull(decoded.Snapshot.TreasureProgress);
        Assert.Equal(snap.TreasureProgress![0].Found,
                     decoded.Snapshot.TreasureProgress![0].Found);
    }

    [Fact]
    public void Codec_roundtrip_preserves_placed_chests()
    {
        var sim = TreasureSim();
        sim.TryUseItem(1);
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.NotNull(decoded.Snapshot.PlacedChests);
        Assert.Single(decoded.Snapshot.PlacedChests!);
        Assert.Equal(snap.PlacedChests![0].X, decoded.Snapshot.PlacedChests![0].X);
    }

    [Fact]
    public void Codec_roundtrip_without_treasure_hunt_leaves_fields_null()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.Null(decoded.Snapshot.TreasureProgress);
        Assert.Null(decoded.Snapshot.PlacedChests);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~SnapshotTreasureHunt" -v minimal
```

Expected: `TreasureProgressSnapshot`, `PlacedChestSnapshot`, `WorldSnapshot` fields missing.

- [ ] **Step 3: Add new snapshot types and extend WorldSnapshot**

In `src/Miner49er.Core/Net/Snapshots.cs`, add two new records after `OctopusSnapshot`:

```csharp
public readonly record struct TreasureProgressSnapshot(int MinerId, int Found);
public readonly record struct PlacedChestSnapshot(int MinerId, int X, int Y);
```

Then extend `WorldSnapshot`. The record currently ends with `IReadOnlyList<ReelChargeSnapshot>? ReelCharges = null`. Add two more optional trailing params:

```csharp
public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false,
    OctopusSnapshot? Octopus = null, int Lives = 3,
    IReadOnlyList<ReelChargeSnapshot>? ReelCharges = null,
    IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress = null,
    IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     = null);
```

- [ ] **Step 4: Update SnapshotFactory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, replace the final `return` statement:

```csharp
IReadOnlyList<TreasureProgressSnapshot>? treasureProgress = null;
IReadOnlyList<PlacedChestSnapshot>?      placedChests     = null;
if (sim.Config.TreasureHuntMode)
{
    treasureProgress = sim.GetTreasureProgress()
        .Select(p => new TreasureProgressSnapshot(p.MinerId, p.Found))
        .ToList();
    placedChests = sim.GetPlacedChests()
        .Select(c => new PlacedChestSnapshot(c.MinerId, c.X, c.Y))
        .ToList();
}

return new WorldSnapshot(tick, miners, charges, items, molds, monsters,
    (float)sim.SecondsRemaining, sim.EscapeOpen, octopus, lives, reelCharges,
    treasureProgress, placedChests);
```

- [ ] **Step 5: Update SnapshotCodec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`:

**In `Write`, append after the octopus block and before tile changes:**

```csharp
w.Write(snap.TreasureProgress?.Count ?? 0);
foreach (var tp in snap.TreasureProgress ?? System.Array.Empty<TreasureProgressSnapshot>())
{ w.Write(tp.MinerId); w.Write(tp.Found); }

w.Write(snap.PlacedChests?.Count ?? 0);
foreach (var pc in snap.PlacedChests ?? System.Array.Empty<PlacedChestSnapshot>())
{ w.Write(pc.MinerId); w.Write(pc.X); w.Write(pc.Y); }
```

**In `Read`, add after the octopus block and before tile changes:**

```csharp
int tpCount = r.ReadInt32();
List<TreasureProgressSnapshot>? treasureProgress = tpCount > 0
    ? new List<TreasureProgressSnapshot>(tpCount) : null;
for (int i = 0; i < tpCount; i++)
    treasureProgress!.Add(new TreasureProgressSnapshot(r.ReadInt32(), r.ReadInt32()));

int pcCount = r.ReadInt32();
List<PlacedChestSnapshot>? placedChests = pcCount > 0
    ? new List<PlacedChestSnapshot>(pcCount) : null;
for (int i = 0; i < pcCount; i++)
    placedChests!.Add(new PlacedChestSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));
```

**Update the final return** to pass the new variables:

```csharp
return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
    monsters, secondsRemaining, escapeOpen, octopus, lives, reelCharges,
    treasureProgress, placedChests), changes);
```

- [ ] **Step 6: Run tests — expect pass**

```
dotnet test src/Miner49er.Core.Tests/ --filter "FullyQualifiedName~SnapshotTreasureHunt" -v minimal
```

Expected: 5 tests passing.

- [ ] **Step 7: Run full suite**

```
dotnet test src/Miner49er.Core.Tests/ -v minimal
```

- [ ] **Step 8: Commit**

```
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core.Tests/SnapshotTreasureHuntTests.cs
git commit -m "feat(treasure-hunt): add TreasureProgress + PlacedChests to snapshot + codec"
```

---

### Task 6: MatchHost, NetworkManager, Lobby

No core tests for this task — Godot-layer changes verified by manual playtest.

**Files:**
- Modify: `game/net/MatchHost.cs`
- Modify: `game/ui/Lobby.cs`

**Interfaces:**
- Consumes: `GameMode.TreasureHunt`, `SimConfig.TreasureHuntMode`, `MapConfig.For(GameMode.TreasureHunt, ...)`
- Produces: Host sets `TreasureHuntMode = true` in SimConfig; Lobby exposes the mode in its picker

- [ ] **Step 1: Enable TreasureHuntMode in MatchHost**

In `game/net/MatchHost.cs`, find the `Begin()` method where `f1SimCfg` is built:

```csharp
var f1SimCfg = new SimConfig
{
    BaseMoveSeconds = nm.MatchBaseMoveSeconds,
    Seed = seed,
    DynamiteEnabled = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
};
```

Add `TreasureHuntMode` to the initializer:

```csharp
var f1SimCfg = new SimConfig
{
    BaseMoveSeconds    = nm.MatchBaseMoveSeconds,
    Seed               = seed,
    DynamiteEnabled    = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
    TreasureHuntMode   = nm.MatchMode == GameMode.TreasureHunt,
};
```

- [ ] **Step 2: Add Treasure Hunt to the lobby mode picker**

In `game/ui/Lobby.cs`, find the `_modePicker` setup block:

```csharp
_modePicker.AddItem("Last Man Standing", (int)GameMode.LastManStanding);
_modePicker.AddItem("Gold Rush",         (int)GameMode.GoldRush);
_modePicker.AddItem("Reach Center",      (int)GameMode.ReachCenter);
_modePicker.AddItem("Expedition",        (int)GameMode.Expedition);
```

Add the new mode:

```csharp
_modePicker.AddItem("Last Man Standing", (int)GameMode.LastManStanding);
_modePicker.AddItem("Gold Rush",         (int)GameMode.GoldRush);
_modePicker.AddItem("Reach Center",      (int)GameMode.ReachCenter);
_modePicker.AddItem("Expedition",        (int)GameMode.Expedition);
_modePicker.AddItem("Treasure Hunt",     (int)GameMode.TreasureHunt);
```

- [ ] **Step 3: Hide irrelevant controls in Treasure Hunt mode**

In `game/ui/Lobby.cs`, find `RefreshModeControls()`:

```csharp
private void RefreshModeControls()
{
    bool isHost = NetworkManager.Instance.IsHost;
    bool expedition = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
    _timePicker.Visible       = isHost && !expedition;
    _mapSizePicker.Visible    = isHost && expedition;
    _explosivePicker.Visible  = isHost && !expedition;
}
```

Replace with:

```csharp
private void RefreshModeControls()
{
    bool isHost       = NetworkManager.Instance.IsHost;
    bool expedition   = _modePicker.GetSelectedId() == (int)GameMode.Expedition;
    bool treasure     = _modePicker.GetSelectedId() == (int)GameMode.TreasureHunt;
    bool normalMode   = !expedition && !treasure;
    _timePicker.Visible      = isHost && normalMode;
    _mapSizePicker.Visible   = isHost && expedition;
    _explosivePicker.Visible = isHost && normalMode;
}
```

- [ ] **Step 4: Build and verify the Lobby compiles**

```powershell
dotnet build src/Miner49er.Core/ -c Debug --nologo -v minimal
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```
git add game/net/MatchHost.cs game/ui/Lobby.cs
git commit -m "feat(treasure-hunt): Lobby mode picker + MatchHost SimConfig wiring"
```

---

### Task 7: MatchClient, HUD idol panel, WorldRenderer idol/chest rendering

No automated tests — visual changes verified by running the game.

**Files:**
- Modify: `game/net/MatchClient.cs`
- Modify: `game/Main.cs`
- Modify: `game/WorldRenderer.cs`

**Interfaces:**
- Consumes: `WorldSnapshot.TreasureProgress`, `WorldSnapshot.PlacedChests`, `TreasureAssignment.For()`
- Produces:
  - `MatchClient.TreasureProgress` and `MatchClient.PlacedChests` properties
  - HUD shows 2 idol name slots (gray = unfound, gold ✓ = deposited)
  - WorldRenderer draws placed chests with owner indicator; idol items render with fallback color

- [ ] **Step 1: Expose treasure data on MatchClient**

In `game/net/MatchClient.cs`, add two properties after `Lives`:

```csharp
public IReadOnlyList<TreasureProgressSnapshot> TreasureProgress { get; private set; }
    = System.Array.Empty<TreasureProgressSnapshot>();
public IReadOnlyList<PlacedChestSnapshot> PlacedChests { get; private set; }
    = System.Array.Empty<PlacedChestSnapshot>();
```

In `ApplyUpdate()`, set them (alongside the existing assignments):

```csharp
TreasureProgress = update.Snapshot.TreasureProgress
    ?? System.Array.Empty<TreasureProgressSnapshot>();
PlacedChests = update.Snapshot.PlacedChests
    ?? System.Array.Empty<PlacedChestSnapshot>();
```

- [ ] **Step 2: Add HUD idol shadow panel to Main.cs**

In `game/Main.cs`, find the end of the `foreach (var m in _client.Miners)` block — the `}` after the shop panel logic (around line 243). Add a helper method and a `_Draw()` override to render the idol panel.

First, add these `using` statements at the top if not present (they already exist in the file, check first).

Add a new private method `DrawTreasurePanel()`:

```csharp
private void DrawTreasurePanel()
{
    var nm = NetworkManager.Instance;
    if (nm.MatchMode != GameMode.TreasureHunt) return;
    var (idolA, idolB) = TreasureAssignment.For(nm.MatchSeed, _client.LocalMinerId);
    int found = _client.TreasureProgress
        .FirstOrDefault(p => p.MinerId == _client.LocalMinerId).Found;

    string NameOf(ItemKind k) => k.ToString().Replace("Idol", "");
    string SlotA = found >= 1 ? $"✓ {NameOf(idolA)}" : NameOf(idolA);
    string SlotB = found >= 2 ? $"✓ {NameOf(idolB)}" : NameOf(idolB);
    string line = $"Hunt:  {SlotA}  |  {SlotB}";
    _hud.SetHuntText(line, found);
}
```

Call `DrawTreasurePanel()` inside `_PhysicsProcess`, right after the Expedition/LMS HUD update block (after the inner `foreach` closes). Find:

```csharp
		// Death-fade overlay (expedition only): black out on death, fade in on revive.
```

Add before that line:

```csharp
		DrawTreasurePanel();
```

- [ ] **Step 3: Add SetHuntText to Hud**

Read `game/Hud.cs` briefly to understand its API (it has `SetText` and `SetHud`). Add a method to show the treasure panel text with color. Open `game/Hud.cs` and add:

```csharp
public void SetHuntText(string text, int found)
{
    // Reuse SetText; caller provides formatted string.
    // Color: gold when both found, white otherwise.
    SetText(text);
}
```

This is a thin wrapper for now — the `SetText` method already exists and handles display. If Hud needs a color override, extend it there; for now the text is sufficient.

- [ ] **Step 4: Add idol and TreasureChest rendering to WorldRenderer**

In `game/WorldRenderer.cs`:

**4a — Add an `IdolColor` and `TreasureChestColor` constant** alongside the existing colors:

```csharp
private static readonly Color IdolColor         = new("ffd700"); // gold
private static readonly Color TreasureChestColor = new("c88020"); // darker gold
```

**4b — Register idol textures in `Init()`** after the existing `LoadItemTex` calls:

```csharp
// Idol art (placeholder — files created later by PixelLab)
foreach (var kind in TreasureAssignment.AllIdols())
{
    string name = kind.ToString().ToLowerInvariant(); // e.g. "idolvishnu"
    string path = $"res://assets/objects/{name}.png";
    if (ResourceLoader.Exists(path))
        LoadItemTex(kind, path);
}
if (ResourceLoader.Exists("res://assets/objects/treasure_chest.png"))
    LoadItemTex(ItemKind.TreasureChest, "res://assets/objects/treasure_chest.png");
```

**4c — Add TreasureChest item rendering** in `_Draw()`. Find the `if (it.Kind == ItemKind.Chest)` block (around line 367) and add a parallel block for `TreasureChest` immediately before it:

```csharp
if (it.Kind == ItemKind.TreasureChest)
{
    if (_itemTex.TryGetValue(it.Kind, out var tctex))
        DrawTextureRect(tctex, r, false);
    else
    {
        DrawRect(r, TreasureChestColor);
        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(it.X * ts + ts / 2f, it.Y * ts + ts * 0.65f),
            "⊞", HorizontalAlignment.Center, -1, ts * 2 / 3, Colors.Black);
    }
    continue;
}
```

**4d — Update `ItemColor()` to handle idol kinds:**

```csharp
private static Color ItemColor(ItemKind kind) => kind switch
{
    ItemKind.SpeedPotion  => SpeedItemColor,
    ItemKind.LongerVision => VisionItemColor,
    ItemKind.BiggerBlast  => BlastItemColor,
    ItemKind.WaterPlank   => PlankItemColor,
    ItemKind.SlowMold     => MoldItemColor,
    ItemKind.Detonator    => DetonatorItemColor,
    ItemKind.TreasureChest => TreasureChestColor,
    _ when kind.IsIdol()  => IdolColor,
    _                     => SpeedItemColor,
};
```

**4e — Render placed chests from `_client.PlacedChests`** at the end of `_Draw()`, just before the closing brace of the method. Add after the lantern rendering block:

```csharp
// Placed TreasureChests: draw owner indicator (small miner-color dot above chest)
foreach (var pc in _client.PlacedChests)
{
    float cx = pc.X * ts + ts / 2f;
    float cy = pc.Y * ts;
    // Small ownership indicator dot
    var col = Miner49er.PlayerColors.At(pc.MinerId - 1);
    DrawCircle(new Vector2(cx, cy - 4f), 3f, col);
}
```

Note: `PlayerColors.At(colorIndex)` takes a color index (0–7), not a miner id. The color index is not available in `PlacedChestSnapshot`. For now, use a fixed yellow indicator and revisit when color index is added to the snapshot in a future pass.

Replace the `PlayerColors.At(pc.MinerId - 1)` line with:

```csharp
DrawCircle(new Vector2(cx, cy - 4f), 3f, TreasureChestColor);
```

- [ ] **Step 5: Build the Godot project**

```powershell
dotnet build Miner49er.csproj -c Debug --nologo -v minimal
```

Expected: 0 errors, 0 warnings (or only existing pre-existing warnings).

- [ ] **Step 6: Smoke-test in Godot (PowerShell only)**

```powershell
& godot --headless --quit 2>&1 | Select-Object -First 20
```

Expected: exits cleanly, no assembly errors.

- [ ] **Step 7: Commit**

```
git add game/net/MatchClient.cs game/Main.cs game/WorldRenderer.cs game/Hud.cs
git commit -m "feat(treasure-hunt): MatchClient exposes progress; HUD idol panel; WorldRenderer idol/chest rendering"
```

---

## Follow-up (not in this plan)

- **PixelLab art**: generate 17 idol sprites + 1 TreasureChest sprite via `mcp__pixellab__create_1_direction_object`; save to `res://assets/objects/idol_vishnu.png` etc.
- **Audio**: `IdolDeposited` / `TreasureChestPlaced` sound cues in `MatchAudio.cs`.
- **SettingsStore**: save/load the new lobby mode index (already works — int `gameMode` field covers it).
- **Chest owner color**: add `int OwnerColorIndex` to `PlacedChestSnapshot` and codec, then use `PlayerColors.At()` in WorldRenderer.
