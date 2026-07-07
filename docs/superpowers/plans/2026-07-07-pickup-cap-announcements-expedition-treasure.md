# Pickup Cap + Announcements + Expedition Treasure Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Block perm-buff item pickup when maxed; flash status-line announcements on pickup/block and tutorial hints; open Expedition gates via mythical-treasure find on every 4th floor with compass retargeting.

**Architecture:** Three user tasks, six plan tasks. Tasks 1–2 are pure sim/test. Tasks 3–4 are pure UI (Main.cs). Tasks 5–6 wire up the expedition treasure feature end-to-end (map gen → sim → client → compass). Each plan task ends with a green test run and commit.

**Tech Stack:** C# / .NET 8, Godot 4, xUnit. Bash for builds; PowerShell for Godot.

## Global Constraints

- Run tests with: `dotnet test src/Miner49er.Core.Tests` from the repo root
- Never skip `--no-verify`; fix the cause if a hook blocks
- Keep sim changes deterministic (same seed → same result)
- All `SimEvent` records are sealed records inheriting `SimEvent`
- `ItemKind` and `ItemPlacement` live in `src/Miner49er.Core/Map/Item.cs`
- `PermBlastLevel`, `PermSpeedLevel`, `PermVisionLevel` are `Miner` fields clamped to `Config.Max*`

---

## File Map

| File | What changes |
|---|---|
| `src/Miner49er.Core/Sim/SimEvent.cs` | Add `PickupBlocked` event |
| `src/Miner49er.Core/Sim/Simulation.cs` | Guard in `PickUpItems()`; treasure escape in `TryUseItem()`; skip gold-% on treasure floors |
| `src/Miner49er.Core/Sim/SimConfig.cs` | Add `ExpeditionTreasureKind` |
| `src/Miner49er.Core/Map/MapConfig.cs` | Add `ExpeditionTreasureKind`, `ExpeditionTreasureInChest`; update `FloorConfig()` |
| `src/Miner49er.Core/Map/GeneratedMap.cs` | Add `ExpeditionTreasurePos` |
| `src/Miner49er.Core/Map/MapGenerator.cs` | Add `PlaceExpeditionTreasure()`; call in `Generate()` |
| `src/Miner49er.Core.Tests/PermBuffTests.cs` | Add blocked-pickup tests |
| `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs` | Add treasure-gate tests |
| `src/Miner49er.Core.Tests/MapConfigTests.cs` | Add FloorConfig treasure tests |
| `src/Miner49er.Core.Tests/MapGeneratorTreasureHuntTests.cs` | Re-use pattern; add expedition-treasure placement test |
| `game/Main.cs` | Announcement fields + detection; tutorial hints; override status line |
| `game/net/MatchClient.cs` | Add `ExpeditionTreasurePos`; update `Begin()` + `ReceiveNextFloor` + `ReceiveUpdate` |
| `game/ui/Compass.cs` | `ComputeExitAngle()` prioritises treasure pos |

---

## Plan Task 1 — PickupBlocked sim event + guard in PickUpItems (User Task 3)

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs:878-893`
- Modify: `src/Miner49er.Core.Tests/PermBuffTests.cs`

**Interfaces:**
- Produces: `PickupBlocked(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent` — consumed by Plan Task 3 (Main.cs)
- Produces: blocked perm-buff items remain in `sim.Items` — verified by tests here

- [ ] **Step 1: Add PickupBlocked to SimEvent.cs**

After the `ItemPickedUp` line (line 20), add:

```csharp
public sealed record PickupBlocked(int MinerId, GridPos Pos, ItemKind Kind) : SimEvent;
```

- [ ] **Step 2: Add IsPermBuffMaxed helper and guard to Simulation.PickUpItems()**

In `Simulation.cs`, add this private method after `ApplyBuff`:

```csharp
private bool IsPermBuffMaxed(Miner m, ItemKind kind) => kind switch
{
    ItemKind.SpeedPotion  => m.PermSpeedLevel  >= Config.MaxPermSpeedLevel,
    ItemKind.BiggerBlast  => m.PermBlastLevel  >= Config.MaxPermBlastLevel,
    ItemKind.LongerVision => m.PermVisionLevel >= Config.MaxPermVisionLevel,
    _ => false,
};
```

Replace the body of `PickUpItems()` (lines 880-893) with:

```csharp
private void PickUpItems()
{
    for (int i = _items.Count - 1; i >= 0; i--)
    {
        var item = _items[i];
        if (item.Placement == ItemPlacement.Buried) continue;
        if (item.Kind.IsCarried()) continue;
        foreach (var m in _miners.Values)
        {
            if (!m.Alive || m.Pos != item.Pos) continue;
            if (IsPermBuffMaxed(m, item.Kind))
            {
                _events.Add(new PickupBlocked(m.Id, item.Pos, item.Kind));
                break;
            }
            _items.RemoveAt(i);
            ApplyBuff(m.Id, item.Kind);
            _events.Add(new ItemPickedUp(m.Id, item.Pos, item.Kind));
            break;
        }
    }
}
```

- [ ] **Step 3: Write failing tests in PermBuffTests.cs**

Append to `PermBuffTests.cs`:

```csharp
[Fact]
public void SpeedPotion_blocked_when_at_max_speed()
{
    var cfg = new SimConfig { MaxPermSpeedLevel = 2 };
    var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
    sim.AddMiner(1, new GridPos(1, 2));
    sim.SetPermLevels(1, 2, 0, 0); // already maxed

    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);

    Assert.Single(sim.Items); // item still on floor
    Assert.Equal(2, sim.GetMiner(1).PermSpeedLevel); // unchanged
    Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked pb && pb.Kind == ItemKind.SpeedPotion);
}

[Fact]
public void BiggerBlast_blocked_when_at_max_blast()
{
    var cfg = new SimConfig { MaxPermBlastLevel = 1 };
    var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
    sim.AddMiner(1, new GridPos(1, 2));
    sim.SetPermLevels(1, 0, 0, 1); // already maxed

    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.BiggerBlast));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);

    Assert.Single(sim.Items);
    Assert.Equal(1, sim.GetMiner(1).PermBlastLevel);
    Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked pb && pb.Kind == ItemKind.BiggerBlast);
}

[Fact]
public void LongerVision_blocked_when_at_max_vision()
{
    var cfg = new SimConfig { MaxPermVisionLevel = 3 };
    var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
    sim.AddMiner(1, new GridPos(1, 2));
    sim.SetPermLevels(1, 0, 3, 0);

    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.LongerVision));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);

    Assert.Single(sim.Items);
    Assert.Equal(3, sim.GetMiner(1).PermVisionLevel);
    Assert.Contains(sim.DrainEvents(), e => e is PickupBlocked);
}

[Fact]
public void SpeedPotion_collected_normally_when_not_maxed()
{
    var cfg = new SimConfig { MaxPermSpeedLevel = 2 };
    var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), cfg);
    sim.AddMiner(1, new GridPos(1, 2));
    sim.SetPermLevels(1, 1, 0, 0); // one below max

    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.SpeedPotion));
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);

    Assert.Empty(sim.Items);
    Assert.Equal(2, sim.GetMiner(1).PermSpeedLevel);
}
```

- [ ] **Step 4: Run tests**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: all new tests pass; existing tests still green.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/PermBuffTests.cs
git commit -m "feat(sim): block perm-buff pickup when maxed; emit PickupBlocked event"
```

---

## Plan Task 2 — Pickup announcements + tutorial hints in status line (User Task 2)

**Files:**
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: `PickupBlocked` event (stays on floor — detected by item-still-present check)
- Consumes: `_client.Items` (IReadOnlyList<ItemSnapshot>) and `_client.Miners` (IReadOnlyList<MinerSnapshot>)
- Produces: announcement text visible in HUD status row

The detection strategy (no snapshot changes needed):

- **Pickup**: track buff/tutorial items from the previous frame. When a tracked item disappears from `_client.Items` AND local miner is at that tile this frame → show pickup message.
- **Blocked**: when a perm-buff item is at the local miner's position this frame AND it also appeared in `_prevAnnounceItems` at the same position (i.e., it did NOT disappear) AND the miner just arrived (position changed this frame) → blocked.
- **Tutorial**: first time a WaterPlank or Lantern is picked up (disappears at miner pos), show hint; track with `_tutorialShown`.

- [ ] **Step 1: Add announcement fields to Main.cs**

In the field declarations section (near line 32, after `_floorBannerTimer`):

```csharp
// Pickup announcements
private string? _announcement;
private double  _announcementExpiry;   // Time.GetTicksMsec() deadline
private readonly HashSet<ItemKind> _tutorialShown = new();
private List<(int X, int Y, ItemKind Kind)> _prevAnnounceItems = new();
private GridPos? _prevLocalPos;
```

- [ ] **Step 2: Add SetAnnouncement and helper methods to Main.cs**

Add these private methods (can go near `ComputeContextHint`):

```csharp
private void SetAnnouncement(string text, double durationMs = 2500)
{
    _announcement      = text;
    _announcementExpiry = Time.GetTicksMsec() + durationMs;
}

private bool IsAnnouncementActive() =>
    _announcement != null && Time.GetTicksMsec() < _announcementExpiry;

private static bool IsPermBuff(ItemKind k) =>
    k is ItemKind.SpeedPotion or ItemKind.BiggerBlast or ItemKind.LongerVision;

private static bool IsAnnounceKind(ItemKind k) =>
    k is ItemKind.SpeedPotion or ItemKind.BiggerBlast or ItemKind.LongerVision
      or ItemKind.LifePotion or ItemKind.WaterPlank or ItemKind.Lantern;

private string PickupMessage(ItemKind kind, bool isBlocked) =>
    (kind, isBlocked) switch
    {
        (ItemKind.SpeedPotion,  true)  => "Already maxed out — Speed Tonic!",
        (ItemKind.BiggerBlast,  true)  => "Already maxed out — Bigger Blast!",
        (ItemKind.LongerVision, true)  => "Already maxed out — Keen Eyes!",
        (ItemKind.SpeedPotion,  false) => "Speed Tonic! Move faster.",
        (ItemKind.BiggerBlast,  false) => "Bigger Blast! Larger explosion radius.",
        (ItemKind.LongerVision, false) => "Keen Eyes! See further.",
        (ItemKind.LifePotion,   false) => "Life Restored!",
        (ItemKind.WaterPlank,   false) => "Water Plank — place it across deep water.",
        (ItemKind.Lantern,      false) => "Lantern — drop it to light the area.",
        _ => "",
    };
```

- [ ] **Step 3: Add ProcessPickupAnnouncements method**

```csharp
private void ProcessPickupAnnouncements(int localMinerId)
{
    // Find local miner position
    GridPos? localPos = null;
    foreach (var m in _client.Miners)
        if (m.Id == localMinerId && m.Alive) { localPos = new GridPos(m.X, m.Y); break; }
    if (localPos is not { } pos) { _prevLocalPos = null; return; }

    bool movedThisTick = pos != _prevLocalPos;

    // Build current announce-item snapshot
    var curItems = new List<(int X, int Y, ItemKind Kind)>();
    foreach (var it in _client.Items)
        if (IsAnnounceKind(it.Kind)) curItems.Add((it.X, it.Y, it.Kind));

    // Detect disappeared items at miner pos → pickup
    foreach (var prev in _prevAnnounceItems)
    {
        bool stillThere = false;
        foreach (var cur in curItems)
            if (cur.X == prev.X && cur.Y == prev.Y && cur.Kind == prev.Kind) { stillThere = true; break; }
        if (!stillThere && pos.X == prev.X && pos.Y == prev.Y)
        {
            // Tutorial: show hint only first time for WaterPlank/Lantern
            if (prev.Kind == ItemKind.WaterPlank && _tutorialShown.Add(ItemKind.WaterPlank))
                SetAnnouncement(PickupMessage(ItemKind.WaterPlank, false));
            else if (prev.Kind == ItemKind.Lantern && _tutorialShown.Add(ItemKind.Lantern))
                SetAnnouncement(PickupMessage(ItemKind.Lantern, false));
            else if (IsPermBuff(prev.Kind) || prev.Kind == ItemKind.LifePotion)
                SetAnnouncement(PickupMessage(prev.Kind, false));
        }
    }

    // Detect perm-buff item at miner pos that didn't disappear + miner just arrived → blocked
    if (movedThisTick)
    {
        foreach (var cur in curItems)
        {
            if (cur.X == pos.X && cur.Y == pos.Y && IsPermBuff(cur.Kind))
            {
                // Item still here after miner arrived → blocked
                bool wasHereLastFrame = false;
                foreach (var prev in _prevAnnounceItems)
                    if (prev.X == cur.X && prev.Y == cur.Y && prev.Kind == cur.Kind) { wasHereLastFrame = true; break; }
                if (wasHereLastFrame)
                    SetAnnouncement(PickupMessage(cur.Kind, true));
            }
        }
    }

    _prevLocalPos    = pos;
    _prevAnnounceItems = curItems;
}
```

- [ ] **Step 4: Call ProcessPickupAnnouncements in _Process and override status**

In `_Process`, just BEFORE the `string status = "Spectating"` line (~line 271), add:

```csharp
ProcessPickupAnnouncements(_client.LocalMinerId);
```

Then, right after `status` is computed (after the ternary that assigns "Ready"/"Dead — spectating"), replace the line:

```csharp
string status = m.Alive
    ? (m.Activity == ... ? "Mining…" : "Ready")
    : "Dead — spectating";
```

with:

```csharp
string rawStatus = m.Alive
    ? (m.Activity == (int)ActivityKind.Mining           ? $"Mining… {m.ActivityRemaining:0.0}s"
        : m.Activity == (int)ActivityKind.Planting          ? $"Planting… {m.ActivityRemaining:0.0}s"
        : m.Activity == (int)ActivityKind.PlantingDetonator ? $"Planting detonator… {m.ActivityRemaining:0.0}s"
        : "Ready")
    : "Dead — spectating";
string status = IsAnnouncementActive() ? _announcement! : rawStatus;
```

- [ ] **Step 5: Run tests and manual smoke test**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: all green (this change is pure UI, no sim tests affected). Then run the game and verify:
- Pick up a Speed Tonic → status row briefly shows "Speed Tonic! Move faster."
- Pick up same type when maxed → "Already maxed out — Speed Tonic!"
- Pick up WaterPlank first time → tutorial hint appears

- [ ] **Step 6: Commit**

```bash
git add game/Main.cs
git commit -m "feat(ui): flash pickup and blocked announcements in status line with tutorial hints"
```

---

## Plan Task 3 — MapConfig: ExpeditionTreasureKind + FloorConfig every-4th (User Task 1)

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs`
- Modify: `src/Miner49er.Core.Tests/MapConfigTests.cs` (add tests)

**Interfaces:**
- Produces: `MapConfig.ExpeditionTreasureKind` (nullable ItemKind) — consumed by MapGenerator (Plan Task 4) and host SimConfig (Plan Task 5)
- Produces: `MapConfig.ExpeditionTreasureInChest` (bool) — consumed by MapGenerator to choose placement

- [ ] **Step 1: Add treasure fields to MapConfig**

After the `HasShop` line (~line 22) in `MapConfig.cs`, add:

```csharp
// Expedition treasure gate — every 4th floor hides a mythical idol that opens the exit.
public ItemKind? ExpeditionTreasureKind    { get; set; } = null;
public bool      ExpeditionTreasureInChest { get; set; } = false;  // false = buried in wall, true = toolbox on floor
```

- [ ] **Step 2: Populate treasure in FloorConfig()**

In `FloorConfig()` (after the `cfg.FloodedCave` line, before `return cfg`), add:

```csharp
if (floor % 4 == 0)
{
    var treasureRng = new Random(HashCode.Combine(seed, floor, 0xFEED));
    var allIdols    = TreasureAssignment.AllIdols();
    cfg.ExpeditionTreasureKind    = allIdols[treasureRng.Next(allIdols.Length)];
    cfg.ExpeditionTreasureInChest = treasureRng.Next(2) == 0;
}
```

- [ ] **Step 3: Write failing tests for FloorConfig treasure**

Find `MapConfigTests.cs` and append:

```csharp
[Theory]
[InlineData(4)]
[InlineData(8)]
[InlineData(12)]
[InlineData(50)]
public void FloorConfig_every_4th_floor_has_expedition_treasure(int floor)
{
    var cfg = MapConfig.FloorConfig(floor, seed: 1, playerCount: 1);
    Assert.NotNull(cfg.ExpeditionTreasureKind);
    Assert.True(cfg.ExpeditionTreasureKind.Value.IsIdol());
}

[Theory]
[InlineData(1)]
[InlineData(3)]
[InlineData(5)]
[InlineData(7)]
[InlineData(49)]
public void FloorConfig_non_4th_floor_has_no_expedition_treasure(int floor)
{
    var cfg = MapConfig.FloorConfig(floor, seed: 1, playerCount: 1);
    Assert.Null(cfg.ExpeditionTreasureKind);
}

[Fact]
public void FloorConfig_floor_4_treasure_is_deterministic()
{
    var cfg1 = MapConfig.FloorConfig(4, seed: 42, playerCount: 1);
    var cfg2 = MapConfig.FloorConfig(4, seed: 42, playerCount: 1);
    Assert.Equal(cfg1.ExpeditionTreasureKind, cfg2.ExpeditionTreasureKind);
    Assert.Equal(cfg1.ExpeditionTreasureInChest, cfg2.ExpeditionTreasureInChest);
}
```

- [ ] **Step 4: Run tests**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: new FloorConfig tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/MapConfigTests.cs
git commit -m "feat(map): add ExpeditionTreasureKind to FloorConfig on every 4th floor"
```

---

## Plan Task 4 — MapGenerator: place expedition treasure + GeneratedMap field (User Task 1)

**Files:**
- Modify: `src/Miner49er.Core/Map/GeneratedMap.cs`
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs`
- Modify: `src/Miner49er.Core.Tests/MapGeneratorTreasureHuntTests.cs` (add expedition treasure test)

**Interfaces:**
- Produces: `GeneratedMap.ExpeditionTreasurePos` (nullable GridPos) — consumed by MatchClient (Plan Task 5) and Compass (Plan Task 6)

- [ ] **Step 1: Add ExpeditionTreasurePos to GeneratedMap**

In `GeneratedMap.cs`, add after `ShopPos`:

```csharp
public GridPos? ExpeditionTreasurePos { get; init; }
```

- [ ] **Step 2: Add PlaceExpeditionTreasure to MapGenerator**

In `MapGenerator.cs`, add this private static method after `PlaceBuriedIdols`:

```csharp
private static GridPos? PlaceExpeditionTreasure(TileGrid g, Random rng, ItemKind kind,
    bool inChest, HashSet<GridPos> region, List<Item> items)
{
    var taken = new HashSet<GridPos>(items.Select(it => it.Pos));

    if (inChest)
    {
        // Place as visible toolbox on a floor tile — miner must UseItem to pick up.
        var floorCands = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !taken.Contains(p))
            .ToList();
        Shuffle(floorCands, rng);
        if (floorCands.Count == 0) return null;
        var pos = floorCands[0];
        items.Add(new Item(pos, kind, ItemPlacement.Toolbox));
        return pos;
    }
    else
    {
        // Bury inside a rock tile adjacent to the traversable region.
        var rockCands = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region) && !taken.Contains(p))
            .ToList();
        Shuffle(rockCands, rng);
        if (rockCands.Count == 0) return null;
        var pos = rockCands[0];
        items.Add(new Item(pos, kind, ItemPlacement.Buried));
        return pos;
    }
}
```

- [ ] **Step 3: Call PlaceExpeditionTreasure in Generate()**

In `Generate()`, after the `PlaceBuriedIdols` block and before `PlaceDecoys`, add:

```csharp
GridPos? expeditionTreasurePos = null;
if (config.ExpeditionTreasureKind is { } ek)
    expeditionTreasurePos = PlaceExpeditionTreasure(grid, rng, ek,
        config.ExpeditionTreasureInChest, region, items);
```

And in the `return new GeneratedMap { ... }` block, add:

```csharp
ExpeditionTreasurePos = expeditionTreasurePos,
```

- [ ] **Step 4: Write failing test**

In `MapGeneratorTreasureHuntTests.cs` (or a new `MapGeneratorExpeditionTests.cs`), add:

```csharp
[Fact]
public void Generate_places_idol_item_on_expedition_treasure_floor()
{
    var cfg = MapConfig.FloorConfig(4, seed: 7, playerCount: 1);
    Assert.NotNull(cfg.ExpeditionTreasureKind); // pre-condition

    var map = MapGenerator.Generate(cfg);

    Assert.NotNull(map.ExpeditionTreasurePos);
    var treasureItem = map.Items.FirstOrDefault(it => it.Kind == cfg.ExpeditionTreasureKind.Value);
    Assert.NotEqual(default, treasureItem);
    Assert.Equal(map.ExpeditionTreasurePos.Value, treasureItem.Pos);
}

[Fact]
public void Generate_no_expedition_treasure_on_non_4th_floor()
{
    var cfg = MapConfig.FloorConfig(3, seed: 7, playerCount: 1);
    var map = MapGenerator.Generate(cfg);
    Assert.Null(map.ExpeditionTreasurePos);
}
```

- [ ] **Step 5: Run tests**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Map/GeneratedMap.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core.Tests/
git commit -m "feat(map): place expedition treasure idol on every 4th floor in MapGenerator"
```

---

## Plan Task 5 — Sim + MatchClient + Main.cs: treasure opens escape gate (User Task 1)

**Files:**
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Modify: `src/Miner49er.Core.Tests/SimulationExpeditionTests.cs`
- Modify: `game/net/MatchClient.cs`
- Modify: `game/Main.cs`

**Interfaces:**
- Produces: `SimConfig.ExpeditionTreasureKind` — guards `OnGoldCleared`, triggers escape in `TryUseItem`
- Produces: `MatchClient.ExpeditionTreasurePos` — exposed to Compass (Plan Task 6)

- [ ] **Step 1: Add ExpeditionTreasureKind to SimConfig**

In `SimConfig.cs`, after `RequireChestForEscape`, add:

```csharp
// When set, the escape opens when this idol is picked up (not on 50 % gold).
public ItemKind? ExpeditionTreasureKind { get; set; } = null;
```

- [ ] **Step 2: Guard OnGoldCleared in Simulation.cs**

In `OnGoldCleared()` (line ~1143), add `&& Config.ExpeditionTreasureKind is null` to the escape condition:

```csharp
private void OnGoldCleared()
{
    if (_goldRemaining > 0) _goldRemaining--;
    if (!EscapeOpen && EscapeTile is not null
        && StartingGoldCount > 0 && GoldCollectedFraction >= 0.5
        && Config.ExpeditionTreasureKind is null)
    {
        EscapeOpen = true;
        _events.Add(new EscapeOpened());
    }
}
```

- [ ] **Step 3: Open escape on treasure pickup in TryUseItem**

In `TryUseItem()`, after the line `m.Held = taken;` (and before `return true`), add:

```csharp
if (Config.ExpeditionTreasureKind is { } tk && taken == tk && !EscapeOpen && EscapeTile is not null)
{
    EscapeOpen = true;
    _events.Add(new EscapeOpened());
}
```

- [ ] **Step 4: Write failing sim tests**

Append to `SimulationExpeditionTests.cs`:

```csharp
[Fact]
public void Treasure_idol_pickup_opens_escape_on_treasure_floor()
{
    var grid = new TileGrid(5, 5, TileType.Floor);
    var cfg  = new SimConfig { ExpeditionTreasureKind = ItemKind.IdolVishnu };
    var sim  = new Simulation(grid, cfg, escapeTile: new GridPos(0, 2));
    var m    = sim.AddMiner(1, new GridPos(1, 2));

    // Place idol as toolbox (carried item, requires UseItem)
    sim.AddItem(new Item(new GridPos(2, 2), ItemKind.IdolVishnu, ItemPlacement.Toolbox));

    // Walk onto it
    sim.TryMove(1, Direction.East);
    sim.Tick(0.0);
    Assert.False(sim.EscapeOpen); // walking onto carried item doesn't auto-pick-up

    // Use it
    sim.TryMove(1, Direction.East); // still at (2,2) — set facing first
    sim.TryUseItem(1);

    Assert.Equal(ItemKind.IdolVishnu, sim.GetMiner(1).Held);
    Assert.True(sim.EscapeOpen);
    Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
}

[Fact]
public void Treasure_floor_ignores_gold_percentage()
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    grid.Set(new GridPos(2, 1), TileType.GoldRock);
    grid.Set(new GridPos(4, 1), TileType.GoldRock);
    var cfg = new SimConfig { PickaxeSeconds = 0.1, ExpeditionTreasureKind = ItemKind.IdolZeus };
    var sim = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
    var m   = sim.AddMiner(1, new GridPos(1, 1));

    // Mine 50% of gold
    m.Facing = Direction.East;
    sim.TryStartMining(1);
    sim.Tick(0.1);
    sim.DrainEvents();

    Assert.False(sim.EscapeOpen); // gold % reached 50 % but ignored because treasure floor
}

[Fact]
public void Non_treasure_floor_still_opens_on_gold_percentage()
{
    var grid = new TileGrid(6, 3, TileType.Floor);
    grid.Set(new GridPos(2, 1), TileType.GoldRock);
    grid.Set(new GridPos(4, 1), TileType.GoldRock);
    var cfg = new SimConfig { PickaxeSeconds = 0.1 }; // no ExpeditionTreasureKind
    var sim = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
    var m   = sim.AddMiner(1, new GridPos(1, 1));

    m.Facing = Direction.East;
    sim.TryStartMining(1);
    sim.Tick(0.1);

    Assert.True(sim.EscapeOpen);
}
```

- [ ] **Step 5: Run tests**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: all new tests pass.

- [ ] **Step 6: Wire up SimConfig in MatchHost (Main.cs)**

`f1SimCfg` is created before `hostMap` in `Main.cs`. The treasure kind comes from `hostMapCfg` (the `MapConfig`), not the generated map. Add this assignment right before the `var hostMap = MapGenerator.Generate(hostMapCfg)` call:

```csharp
// hostMapCfg already has ExpeditionTreasureKind set (from FloorConfig)
f1SimCfg.ExpeditionTreasureKind = nm.MatchMode == GameMode.Expedition
    ? hostMapCfg.ExpeditionTreasureKind
    : null;
```

This reads from `hostMapCfg.ExpeditionTreasureKind` (set by `FloorConfig` in Plan Task 3), which is available at that point.

- [ ] **Step 7: Add ExpeditionTreasurePos to MatchClient**

In `MatchClient.cs`, add a public property after `ShopPos`:

```csharp
public GridPos? ExpeditionTreasurePos { get; private set; }
```

In `Begin()`, add after `ShopPos = shopPos;`:

```csharp
ExpeditionTreasurePos = expeditionTreasurePos;
```

Update `Begin()` signature to include the new param (with default null):

```csharp
public void Begin(TileGrid grid, IReadOnlyList<GridPos> decoys, int localMinerId, Node2D sceneRoot,
    GridPos? escapeTile = null, GridPos? shopPos = null, GridPos? centerTile = null,
    GridPos? expeditionTreasurePos = null)
```

In `ReceiveNextFloor` (where `EscapeTile = newMap.EscapeTile` is set), add:

```csharp
ExpeditionTreasurePos = newMap.ExpeditionTreasurePos;
```

In `ReceiveUpdate` (where `EscapeOpen = update.Snapshot.EscapeOpen` is set), replace it with:

```csharp
bool wasOpen = EscapeOpen;
EscapeOpen = update.Snapshot.EscapeOpen;
if (!wasOpen && EscapeOpen)
    ExpeditionTreasurePos = null; // treasure found; pivot compass to exit
```

- [ ] **Step 8: Pass expedition treasure pos from Main.cs to MatchClient**

In `Main.cs`, in the `_client.Begin(...)` call (~line 81), update:

```csharp
_client.Begin(map.Grid, map.Decoys, localMinerId, this, clientEscape, map.ShopPos, clientCenter,
    nm.MatchMode == GameMode.Expedition ? map.ExpeditionTreasurePos : null);
```

- [ ] **Step 9: Run tests + smoke**

```
dotnet test src/Miner49er.Core.Tests
```

Then run Expedition mode. On floor 4, verify the escape does NOT open on 50% gold, and DOES open when the idol is picked up.

- [ ] **Step 10: Commit**

```bash
git add src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs \
  src/Miner49er.Core.Tests/SimulationExpeditionTests.cs \
  game/net/MatchClient.cs game/Main.cs
git commit -m "feat(expedition): treasure idol pickup opens escape gate on every 4th floor"
```

---

## Plan Task 6 — Compass retargeting to treasure pos (User Task 1)

**Files:**
- Modify: `game/ui/Compass.cs`

**Interfaces:**
- Consumes: `_client.ExpeditionTreasurePos` (GridPos?) — set by Plan Task 5
- Produces: green compass needle points at treasure before found; pivots to exit after

- [ ] **Step 1: Update ComputeExitAngle() in Compass.cs**

Replace the existing `ComputeExitAngle()` (lines 71-84) with:

```csharp
private float? ComputeExitAngle()
{
    // On treasure floors: point at the treasure until it is found, then pivot to the exit.
    GridPos? target;
    if (_client.ExpeditionTreasurePos is { } treasurePos)
        target = treasurePos;
    else if (NetworkManager.Instance.MatchMode == GameMode.ReachCenter)
        target = _client.CenterTile;
    else
        target = _client.EscapeTile;

    if (target is not { } et) return null;
    GridPos? self = null;
    foreach (var m in _client.Miners)
        if (m.Id == _client.LocalMinerId && m.Alive) { self = new GridPos(m.X, m.Y); break; }
    if (self is null || (et.X == self.Value.X && et.Y == self.Value.Y)) return null;
    float dx = et.X - self.Value.X, dy = et.Y - self.Value.Y;
    return Mathf.Atan2(dy, dx);
}
```

- [ ] **Step 2: Run tests**

```
dotnet test src/Miner49er.Core.Tests
```

Expected: all green (Compass.cs has no unit tests — verified manually).

- [ ] **Step 3: Manual smoke test**

Run Expedition, reach floor 4. Enter listen mode. Verify:
- Before finding the idol: compass green needle points toward the treasure location
- After picking up the idol: escape opens + needle pivots to the exit ladder

- [ ] **Step 4: Commit**

```bash
git add game/ui/Compass.cs
git commit -m "feat(compass): point at expedition treasure idol before found, then pivot to exit"
```

---

## Done

All three user tasks implemented. Update `ToDo/Tasks3.txt` by moving items 1-3 to `Done/Tasks3.txt` and run a final full test pass:

```
dotnet test src/Miner49er.Core.Tests
```
