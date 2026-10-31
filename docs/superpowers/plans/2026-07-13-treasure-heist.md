# Treasure Heist Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a competitive multiplayer game mode, **Treasure Heist**, where players dig out one buried treasure and carry-and-hold it; the winner is decided by who holds it at the buzzer or who banks the most hold time.

**Architecture:** All rules live in the engine-free `Miner49er.Core` sim behind `GameMode.TreasureHeist` and `SimConfig.TreasureHeistMode`, driven by a single `AdvanceTreasureHeist(dt)` pass in `Tick` (modeled on the existing Prize `CarryRelic` logic, which already drops a carried item centrally when its holder is stunned or dies). State reaches clients through a new `TreasureSnapshot` + `HoldTimeSnapshot` list on `WorldSnapshot` (same pattern as `PrizeEventSnapshot`). Godot only renders snapshots.

**Tech Stack:** C# / .NET 10, Godot 4.6 (C#), xUnit for Core tests. Host-authoritative ENet multiplayer.

## Global Constraints

- Determinism: all mode logic in `Miner49er.Core`; no `Godot.*` types in Core. Clients render `WorldSnapshot` only.
- Mode gate: new stun/death/stone behaviour is guarded by `SimConfig.TreasureHeistMode` so no other mode changes behaviour.
- Winner is **always** decided by the treasure. Kills never win on their own; the universal last-man-standing auto-win in `RoundResolver` is suppressed for this mode.
- `StoneCount` is hard-capped at 9 (existing `AddStones` clamp) — do not exceed it.
- Reuse existing infra: buried-item placement (`MapConfig.BuriedIdolKinds`/`PlaceBuriedIdols`), the `CarryRelic` carry/drop pattern, stone/pickaxe stun, `Compass` rose, minimap prize-ping, snapshot codec pattern.
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Never `git add -A`; stage explicit paths. Do not stage `.import` LF-churn, `.uid`, `Temp/`, `Done/`, spec/plan docs.
- Run `godot` via PowerShell only (a Bash shim breaks headless import).
- Balance defaults (verbatim): carry slow ×1.35, sneak = 8.0s hold / 6-tile radius / 10.0s cooldown, sudden-death 3.0s, respawn 5.0s, starting stones 9, default match length 5 min when "No Time Limit" is picked.

---

## File Structure

**Core (`src/Miner49er.Core`)**
- `Sim/GameMode.cs` — add `TreasureHeist` enum member.
- `Sim/SimConfig.cs` — mode flags + tunables.
- `Sim/SimEvent.cs` — `TreasureFound`, `TreasureRecovered`, `TreasureSneaking`, `TreasureDropped`.
- `Sim/Simulation.cs` — treasure state + `AdvanceTreasureHeist(dt)` + accessors + starting stones + carry-slow + thrown-stone pickup + drop-all.
- `Map/MapConfig.cs` — bury the single treasure kind for this mode.
- `Sim/RoundResolver.cs` — Treasure Heist win branch; suppress universal LMS for it.
- `Net/Snapshots.cs`, `Net/SnapshotCodec.cs`, `Net/SnapshotFactory.cs` — `TreasureSnapshot` + `HoldTimeSnapshot`.
- `AI/BotBrain.cs` — treasure-seeking intent.

**Godot (`game`)**
- `net/MatchHost.cs` — map treasure `SimEvent`s to transient snapshot fields.
- `net/NetworkManager.cs` — host-only `MatchTreasureWinByCumulative` + `MatchTreasureRespawn`; `StartMatch` params.
- `net/MatchClient.cs` — expose treasure state + events.
- `WorldRenderer.cs` — draw loose/carried treasure + dizzy stars.
- `ui/MinimapOverlay.cs` — carrier ping.
- `ui/Compass.cs` — needle to treasure in this mode.
- `Main.cs` — build heist `SimConfig`; toasts; HUD; starting stones.
- `ui/Lobby.cs` + `audio/SettingsStore.cs` — mode + Win-by/Respawns toggles, persisted.

**Tests (`src/Miner49er.Core.Tests`)**
- `TreasureHeistTests.cs` — sim lifecycle, win rules, respawns, sneak, stones.
- `RoundResolverTreasureHeistTests.cs` — resolver outcomes.
- `SnapshotCodecTreasureHeistTests.cs` — codec round-trip.
- `BotBrainTreasureHeistTests.cs` — bot intent.

---

## PHASE 1 — Core sim

### Task 1: Mode enum, config, and starting stones

**Files:**
- Modify: `src/Miner49er.Core/Sim/GameMode.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (in `AddMiner`)
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs` (new)

**Interfaces:**
- Produces: `GameMode.TreasureHeist`; `SimConfig.TreasureHeistMode`, `TreasureWinByCumulative`, `TreasureRespawnEnabled`, `TreasureCarrySlowFactor`, `TreasureSneakSeconds`, `TreasureSneakRadius`, `TreasureSneakCooldown`, `SuddenDeathHoldSeconds`, `RespawnSeconds`, `StartingStones`.

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/TreasureHeistTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class TreasureHeistTests
{
    internal static SimConfig Cfg() => new SimConfig
    {
        Mode = GameMode.TreasureHeist,
        TreasureHeistMode = true,
        BaseMoveSeconds = 0.05,
        TreasureSneakSeconds = 8.0,
        TreasureSneakRadius = 6,
        TreasureSneakCooldown = 10.0,
        SuddenDeathHoldSeconds = 3.0,
        RespawnSeconds = 5.0,
        StartingStones = 9,
    };
    internal static TileGrid Grid(int w = 15, int h = 15) => new TileGrid(w, h, TileType.Floor);

    [Fact]
    public void New_miner_starts_with_configured_stones()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: FAIL — compile error, `SimConfig` has no `TreasureHeistMode` (and `GameMode.TreasureHeist` missing).

- [ ] **Step 3: Add the enum member**

In `src/Miner49er.Core/Sim/GameMode.cs`, extend the enum:

```csharp
public enum GameMode { LastManStanding, GoldRush, ReachCenter, Expedition, TreasureHunt, DemolitionDerby, TreasureHeist }
```

- [ ] **Step 4: Add config fields**

In `src/Miner49er.Core/Sim/SimConfig.cs`, after the `TreasureHuntMode` line, add:

```csharp
    // Treasure Heist: one buried treasure carried & held; winner decided by the treasure.
    public bool TreasureHeistMode { get; set; } = false;
    public bool TreasureWinByCumulative { get; set; } = false; // false = hold-at-buzzer
    public bool TreasureRespawnEnabled { get; set; } = true;   // false = death match (permadeath)
    public double TreasureCarrySlowFactor { get; set; } = 1.35; // move-cadence multiplier while carrying
    public double TreasureSneakSeconds { get; set; } = 8.0;
    public int    TreasureSneakRadius  { get; set; } = 6;      // Chebyshev tiles
    public double TreasureSneakCooldown { get; set; } = 10.0;
    public double SuddenDeathHoldSeconds { get; set; } = 3.0;
    public double RespawnSeconds { get; set; } = 5.0;
    public int    StartingStones { get; set; } = 9;
```

- [ ] **Step 5: Grant starting stones in AddMiner**

In `src/Miner49er.Core/Sim/Simulation.cs`, find `AddMiner` (it sets `SpawnPos = pos`). Immediately after the miner is created and added to `_miners`, add:

```csharp
        if (Config.TreasureHeistMode) m.StoneCount = Config.StartingStones;
```

(Match the exact local variable name used for the new miner in that method — it is the `Miner` just constructed.)

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: PASS (1 test).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/GameMode.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): GameMode.TreasureHeist + config + starting stones

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 2: Bury the treasure and detect unearthing

**Files:**
- Modify: `src/Miner49er.Core/Map/MapConfig.cs` (bury one kind for heist)
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (treasure state, events wiring, `AdvanceTreasureHeist` skeleton, accessors)
- Modify: `src/Miner49er.Core/Sim/SimEvent.cs`
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs`

**Interfaces:**
- Consumes: `SimConfig.TreasureHeistMode` (Task 1); the existing `_items` list of `Item(Pos, Kind, Placement)` with `ItemPlacement.Buried`/`Loose`; `ItemUnburied` reveal path.
- Produces: `SimEvent`s `TreasureFound(int MinerId)`, `TreasureRecovered(int MinerId)`, `TreasureSneaking(int MinerId)`, `TreasureDropped(int MinerId)`; `Simulation` public accessors `TreasureUnearthed` (bool), `TreasureFoundYet` (bool), `TreasurePos` (GridPos), `TreasureHolderId` (int), `HoldSecondsOf(int)` (double); internal test seam `ForceTreasureLooseForTest(GridPos)`.

- [ ] **Step 1: Add the SimEvents**

In `src/Miner49er.Core/Sim/SimEvent.cs`, after the `PrizeExpired` line, add:

```csharp
public sealed record TreasureFound(int MinerId) : SimEvent;
public sealed record TreasureRecovered(int MinerId) : SimEvent;
public sealed record TreasureSneaking(int MinerId) : SimEvent;
public sealed record TreasureDropped(int MinerId) : SimEvent;
```

- [ ] **Step 2: Write the failing test**

Add to `TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Stepping_onto_unearthed_treasure_picks_it_up_and_fires_found()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5)); // treasure loose under miner 1
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        Assert.True(sim.TreasureFoundYet);
        Assert.Contains(sim.DrainEvents(), e => e is TreasureFound { MinerId: 1 });
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: FAIL — `ForceTreasureLooseForTest`/`TreasureHolderId`/`TreasureFoundYet` do not exist.

- [ ] **Step 4: Add treasure state + accessors + advance skeleton**

In `src/Miner49er.Core/Sim/Simulation.cs`, near the prize-event state fields, add:

```csharp
    // ── Treasure Heist state ──────────────────────────────────────────────
    private bool     _treasureUnearthed;
    private bool     _treasureFound;
    private GridPos  _treasurePos;
    private int      _treasureHolderId = -1;
    private readonly Dictionary<int, double> _holdSeconds = new();
    private double   _sneakHold;
    private double   _sneakCooldownRemaining;
    private double   _suddenDeathHold;
    private int      _suddenDeathWinner = -1;
    private readonly Dictionary<int, double> _respawnTimers = new();

    public bool    TreasureUnearthed => _treasureUnearthed;
    public bool    TreasureFoundYet  => _treasureFound;
    public GridPos TreasurePos       => _treasurePos;
    public int     TreasureHolderId  => _treasureHolderId;
    public int     SuddenDeathWinner => _suddenDeathWinner;
    public double  HoldSecondsOf(int minerId) => _holdSeconds.GetValueOrDefault(minerId);

    internal void ForceTreasureLooseForTest(GridPos pos)
    {
        _treasureUnearthed = true;
        _treasurePos = pos;
        _treasureHolderId = -1;
    }
```

- [ ] **Step 5: Add the AdvanceTreasureHeist pass and call it from Tick**

In `Tick(double dt)`, immediately after `AdvancePrizeEvent(dt);` add:

```csharp
        AdvanceTreasureHeist(dt);
```

Then add the method (fuller lifecycle is completed in Tasks 3–7; this step handles unearth-detection and pickup with found/recovered):

```csharp
    private void AdvanceTreasureHeist(double dt)
    {
        if (!Config.TreasureHeistMode) return;

        // Unearth detection: once the buried treasure item is revealed (Loose), adopt it as
        // the mode's tracked treasure token and remove it from the generic item list so the
        // normal pickup/buff path never touches it.
        if (!_treasureUnearthed)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Kind == _treasureKind && _items[i].Placement == ItemPlacement.Loose)
                {
                    _treasurePos = _items[i].Pos;
                    _items.RemoveAt(i);
                    _treasureUnearthed = true;
                    break;
                }
            }
        }

        if (_treasureUnearthed)
            UpdateTreasureCarry(dt);
    }

    private void UpdateTreasureCarry(double dt)
    {
        if (_treasureHolderId < 0)
        {
            // Loose: first alive miner standing on it grabs it.
            var picker = _miners.Values.FirstOrDefault(m => m.Alive && m.StunRemaining <= 0 && m.Pos == _treasurePos);
            if (picker != null)
            {
                _treasureHolderId = picker.Id;
                if (!_treasureFound) { _treasureFound = true; _events.Add(new TreasureFound(picker.Id)); }
                else _events.Add(new TreasureRecovered(picker.Id));
            }
        }
        else if (!_miners.TryGetValue(_treasureHolderId, out var h) || !h.Alive || h.StunRemaining > 0)
        {
            // Holder incapacitated: drop it where they stand, grabbable again.
            if (_miners.TryGetValue(_treasureHolderId, out var dropper))
            {
                _treasurePos = dropper.Pos;
                _events.Add(new TreasureDropped(_treasureHolderId));
            }
            _treasureHolderId = -1;
            _sneakHold = 0;
        }
        else
        {
            _treasurePos = h.Pos; // the treasure rides the carrier
            _holdSeconds[h.Id] = _holdSeconds.GetValueOrDefault(h.Id) + dt;
        }
    }
```

Add a `_treasureKind` field near the other treasure state and initialize it in the constructor from the map's buried treasure. Add near the state block:

```csharp
    private ItemKind _treasureKind = ItemKind.IdolUrn;
```

In the `Simulation` constructor, after items are loaded from the map, add:

```csharp
        if (Config.TreasureHeistMode)
        {
            var buried = _items.FirstOrDefault(it => it.Placement == ItemPlacement.Buried);
            if (buried.Kind != default) { _treasureKind = buried.Kind; _treasurePos = buried.Pos; }
        }
```

(If `Item` is a class rather than a struct, guard with a null check instead of `!= default`; match the existing `_items` element type.)

- [ ] **Step 6: Bury the treasure for heist matches**

In `src/Miner49er.Core/Map/MapConfig.cs`, find the `For(...)` factory where `BuriedIdolKinds` is set for `TreasureHunt`. Add a Treasure Heist branch that buries exactly one kind:

```csharp
        if (mode == GameMode.TreasureHeist)
            cfg.BuriedIdolKinds = new[] { ItemKind.IdolUrn };
```

(Place this alongside the existing `TreasureHunt` assignment. `IdolUrn` already exists and has a sprite.)

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Miner49er.Core/Sim/SimEvent.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): bury treasure, detect unearth, carry pickup + found/recovered

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 3: Drop-all on stun/death + carry slow

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs`

**Interfaces:**
- Consumes: `UpdateTreasureCarry` (Task 2) already drops the treasure when the holder is stunned/dead.
- Produces: held-item drop-all (`DropHeldItem`); carry-slow applied in `EffectiveMoveSeconds`.

- [ ] **Step 1: Write the failing tests**

Add to `TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Stun_drops_the_treasure_for_others_to_grab()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        sim.GetMiner(1).StunRemaining = 0.8;        // internal setter (test assembly sees internals)
        sim.Tick(0.1);
        Assert.Equal(-1, sim.TreasureHolderId);     // dropped
        Assert.Equal(new GridPos(5, 5), sim.TreasurePos);
    }

    [Fact]
    public void Stun_drops_held_item_onto_a_free_tile()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.GetMiner(1).Held = ItemKind.Lantern;
        sim.GetMiner(1).StunRemaining = 0.8;
        sim.Tick(0.1);
        Assert.Null(sim.GetMiner(1).Held);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.Lantern && it.Placement == ItemPlacement.Loose);
    }

    [Fact]
    public void Carrying_the_treasure_slows_the_holder()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(5, 5));
        double baseline = sim.EffectiveMoveSeconds(1);
        sim.ForceTreasureLooseForTest(new GridPos(5, 5));
        sim.Tick(0.1);
        Assert.Equal(1, sim.TreasureHolderId);
        Assert.True(sim.EffectiveMoveSeconds(1) > baseline);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: the held-item and carry-slow tests FAIL (drop-held not implemented; move seconds unchanged). The treasure-drop test may already pass from Task 2.

- [ ] **Step 3: Implement held-item drop-all**

In `AdvanceTreasureHeist`, after the `if (_treasureUnearthed) UpdateTreasureCarry(dt);` line, add a pass that drops the held item of any incapacitated miner:

```csharp
        foreach (var m in _miners.Values)
            if (m.Held is { } held && (!m.Alive || m.StunRemaining > 0))
                DropHeldItem(m, held);
```

Add the helper:

```csharp
    private static readonly GridPos[] Neighbours8 =
    {
        new(1,0), new(-1,0), new(0,1), new(0,-1),
        new(1,1), new(1,-1), new(-1,1), new(-1,-1),
    };

    private void DropHeldItem(Miner m, ItemKind kind)
    {
        // Prefer an adjacent free floor tile so the miner does not instantly re-grab it on wake.
        GridPos dest = m.Pos;
        foreach (var off in Neighbours8)
        {
            var p = new GridPos(m.Pos.X + off.X, m.Pos.Y + off.Y);
            if (Grid.InBounds(p) && Grid.Get(p) == TileType.Floor && _items.All(it => it.Pos != p))
            { dest = p; break; }
        }
        _items.Add(new Item(dest, kind, ItemPlacement.Loose)); // match the Item ctor arity/shape in this file
        m.Held = null;
    }
```

(Verify the `Item` constructor signature in this file — the plant/loose items are created elsewhere in `Simulation.cs`; copy that exact call shape, e.g. field names/positional order.)

- [ ] **Step 4: Apply carry slow in EffectiveMoveSeconds**

Find `EffectiveMoveSeconds` (it clamps between `MinMoveSeconds` and `MaxMoveSeconds`, ~line 290–300). Before the final clamp, multiply the cadence by the carry-slow factor when this miner holds the treasure:

```csharp
        if (Config.TreasureHeistMode && m.Id == _treasureHolderId)
            seconds *= Config.TreasureCarrySlowFactor;
```

(Use the actual accumulator variable name in that method for `seconds`, and insert before the `Math.Clamp(..., MinMoveSeconds, MaxMoveSeconds)` return.)

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): drop held item + treasure on stun/death; carry slow

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 4: Sneak toast + thrown-stone pickups + map scatter

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs` (sneak detection; thrown-stone pickup)
- Modify: `src/Miner49er.Core/Map/MapGenerator.cs` (light stone scatter for heist)
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs`

**Interfaces:**
- Consumes: `UpdateTreasureCarry` holder state; `TryThrowStone` landing tile; `ItemKind.Stone`.
- Produces: `TreasureSneaking` event; loose `Stone` items on throw + at generation.

- [ ] **Step 1: Write the failing tests**

Add to `TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Lone_carrier_triggers_sneaking_toast_after_threshold()
    {
        var cfg = Cfg(); cfg.TreasureSneakSeconds = 2.0; cfg.TreasureSneakRadius = 3;
        var sim = new Simulation(Grid(20, 20), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddMiner(2, new GridPos(18, 18)); // far away rival
        sim.ForceTreasureLooseForTest(new GridPos(2, 2));
        sim.Tick(0.1); // miner 1 grabs it
        sim.DrainEvents();
        for (int i = 0; i < 30; i++) sim.Tick(0.1); // 3s of lone carrying
        Assert.Contains(sim.DrainEvents(), e => e is TreasureSneaking { MinerId: 1 });
    }

    [Fact]
    public void Thrown_stone_lands_as_a_pickup_in_heist()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.SetFacingForTest(1, Direction.East);
        sim.TryThrowStone(1);
        Assert.Contains(sim.Items, it => it.Kind == ItemKind.Stone && it.Placement == ItemPlacement.Loose);
    }
```

If `SetFacingForTest` does not already exist, add it as an internal seam next to the other test seams:

```csharp
    internal void SetFacingForTest(int id, Direction dir) { if (_miners.TryGetValue(id, out var m)) m.Facing = dir; }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: FAIL — no sneak event; thrown stone leaves no pickup.

- [ ] **Step 3: Implement sneak detection**

In `AdvanceTreasureHeist`, decrement the cooldown and, at the end of `UpdateTreasureCarry` (or right after calling it), add sneak accrual. Extend `UpdateTreasureCarry`'s carrier branch (`else { _treasurePos = h.Pos; ... }`) to include:

```csharp
            _sneakCooldownRemaining = Math.Max(0, _sneakCooldownRemaining - dt);
            int nearest = _miners.Values
                .Where(r => r.Id != h.Id && r.Alive)
                .Select(r => r.Pos.ChebyshevTo(h.Pos))
                .DefaultIfEmpty(int.MaxValue).Min();
            if (nearest > Config.TreasureSneakRadius) _sneakHold += dt; else _sneakHold = 0;
            if (_sneakHold >= Config.TreasureSneakSeconds && _sneakCooldownRemaining <= 0)
            {
                _events.Add(new TreasureSneaking(h.Id));
                _sneakCooldownRemaining = Config.TreasureSneakCooldown;
                _sneakHold = 0;
            }
```

- [ ] **Step 4: Thrown stones leave a pickup**

In `TryThrowStone`, after `_events.Add(new StoneThrown(minerId, land));`, add:

```csharp
        if (Config.TreasureHeistMode && Grid.Get(land) == TileType.Floor && _items.All(it => it.Pos != land))
            _items.Add(new Item(land, ItemKind.Stone, ItemPlacement.Loose));
```

- [ ] **Step 5: Scatter stones at generation**

In `src/Miner49er.Core/Map/MapGenerator.cs`, in the generation path used when `BuriedIdolKinds` is set for heist (or gate on a new `MapConfig.ScatterStones` count), place ~6 loose `Stone` items on random floor tiles. Add to `MapConfig.cs`:

```csharp
    public int ScatterStones { get; set; } = 0; // Treasure Heist: loose stones seeded on the floor
```

Set it in `MapConfig.For` for `TreasureHeist` (`cfg.ScatterStones = 6;`), and in `MapGenerator.Generate`, after items are placed, add:

```csharp
        for (int i = 0; i < config.ScatterStones; i++)
        {
            var p = RandomFreeFloor(grid, rng, items); // reuse the helper used for other loose placements
            if (p is { } pos) items.Add(new Item(pos, ItemKind.Stone, ItemPlacement.Loose));
        }
```

(Use whatever existing helper the generator has for picking a free floor tile; if none, inline a small scan for a `TileType.Floor` tile with no existing item.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: PASS (7 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core/Map/MapGenerator.cs src/Miner49er.Core/Map/MapConfig.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): sneak toast + thrown-stone pickups + stone scatter

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 5: Respawns and sudden-death accrual

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/TreasureHeistTests.cs`

**Interfaces:**
- Consumes: `Config.TreasureRespawnEnabled`, `Config.RespawnSeconds`, `Config.SuddenDeathHoldSeconds`; existing `TimeExpired`; `LifeRestored` event.
- Produces: respawn revival; `_suddenDeathWinner` set for the resolver (Task 6).

- [ ] **Step 1: Write the failing tests**

Add to `TreasureHeistTests.cs`:

```csharp
    [Fact]
    public void Dead_miner_respawns_after_delay_when_respawns_enabled()
    {
        var cfg = Cfg(); cfg.TreasureRespawnEnabled = true; cfg.RespawnSeconds = 1.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        Assert.False(sim.GetMiner(1).Alive);
        for (int i = 0; i < 12; i++) sim.Tick(0.1); // > 1s
        Assert.True(sim.GetMiner(1).Alive);
        Assert.Equal(new GridPos(3, 3), sim.GetMiner(1).Pos); // back at spawn
        Assert.Equal(9, sim.GetMiner(1).StoneCount);
    }

    [Fact]
    public void Death_match_keeps_dead_miner_out()
    {
        var cfg = Cfg(); cfg.TreasureRespawnEnabled = false;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.KillMiner(1);
        for (int i = 0; i < 100; i++) sim.Tick(0.1);
        Assert.False(sim.GetMiner(1).Alive);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: the respawn test FAILs (miner stays dead).

- [ ] **Step 3: Implement respawns + sudden-death accrual**

At the end of `AdvanceTreasureHeist`, add:

```csharp
        if (Config.TreasureRespawnEnabled)
            AdvanceRespawns(dt);

        // Sudden-death accrual: after the clock expires, a lone uncontested holder wins.
        if (TimeExpired && _treasureHolderId >= 0 && _miners.TryGetValue(_treasureHolderId, out var sh) && sh.Alive)
        {
            int nearest = _miners.Values
                .Where(r => r.Id != sh.Id && r.Alive)
                .Select(r => r.Pos.ChebyshevTo(sh.Pos))
                .DefaultIfEmpty(int.MaxValue).Min();
            if (nearest > Config.TreasureSneakRadius) _suddenDeathHold += dt; else _suddenDeathHold = 0;
            if (_suddenDeathHold >= Config.SuddenDeathHoldSeconds) _suddenDeathWinner = sh.Id;
        }
        else _suddenDeathHold = 0;
```

Add the respawn helper:

```csharp
    private void AdvanceRespawns(double dt)
    {
        foreach (var m in _miners.Values)
        {
            if (m.Alive) { _respawnTimers.Remove(m.Id); continue; }
            double t = _respawnTimers.GetValueOrDefault(m.Id) + dt;
            if (t < Config.RespawnSeconds) { _respawnTimers[m.Id] = t; continue; }
            _respawnTimers.Remove(m.Id);
            m.Alive = true;
            m.Pos = m.SpawnPos;
            m.DeathCause = DeathCause.None;
            m.StunRemaining = 0;
            m.Held = null;
            m.StoneCount = Config.StartingStones;
            m.EffectsInternal.Clear();
            _events.Add(new LifeRestored(m.Id));
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter TreasureHeistTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): unlimited respawns + sudden-death accrual

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 6: RoundResolver win logic

**Files:**
- Modify: `src/Miner49er.Core/Sim/RoundResolver.cs`
- Test: `src/Miner49er.Core.Tests/RoundResolverTreasureHeistTests.cs` (new)

**Interfaces:**
- Consumes: `sim.TimeExpired`, `sim.TreasureHolderId`, `sim.HoldSecondsOf(id)`, `sim.SuddenDeathWinner`, `sim.Miners`.
- Produces: `RoundResult` for `GameMode.TreasureHeist`.

- [ ] **Step 1: Write the failing tests**

Create `src/Miner49er.Core.Tests/RoundResolverTreasureHeistTests.cs`:

```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class RoundResolverTreasureHeistTests
{
    private static Simulation Buzzer(out int nobody)
    {
        nobody = -1;
        var cfg = TreasureHeistTests.Cfg(); cfg.TreasureWinByCumulative = false;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        return sim;
    }

    [Fact]
    public void Not_over_before_time_expires()
    {
        var sim = Buzzer(out _);
        Assert.False(RoundResolver.Resolve(sim, GameMode.TreasureHeist).IsOver);
    }

    [Fact]
    public void Buzzer_holder_at_expiry_wins()
    {
        var sim = Buzzer(out _);
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        sim.Tick(0.1); // miner 1 grabs
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Cumulative_most_time_wins()
    {
        var cfg = TreasureHeistTests.Cfg(); cfg.TreasureWinByCumulative = true;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        for (int i = 0; i < 20; i++) sim.Tick(0.1); // miner 1 banks ~2s
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Death_match_wipe_resolves_by_cumulative_before_timer()
    {
        var cfg = TreasureHeistTests.Cfg();
        cfg.TreasureWinByCumulative = true; cfg.TreasureRespawnEnabled = false;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        for (int i = 0; i < 10; i++) sim.Tick(0.1); // miner 1 banks time
        sim.KillMiner(1); sim.KillMiner(2);          // everyone eliminated, clock NOT expired
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId); // most cumulative time wins
    }
}
```

Add the test seam to `Simulation.cs` next to the other seams (sets the existing expiry flag; use the real field name backing `TimeExpired`):

```csharp
    internal void SetTimeExpiredForTest() { Elapsed = double.MaxValue; TimeLimitSeconds = 1; }
```

(If `TimeExpired` is computed differently, set whatever makes `sim.TimeExpired == true`. Inspect the `TimeExpired` getter and drive it true — do not add a second source of truth.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Miner49er.Core.Tests --filter RoundResolverTreasureHeist`
Expected: FAIL — resolver returns Ongoing/Loss for TreasureHeist.

- [ ] **Step 3: Implement the resolver branch**

In `src/Miner49er.Core/Sim/RoundResolver.cs`, move the `TreasureHeist` decision **above** the universal last-man-standing check so survivor count never resolves it. Right after the Expedition block, add:

```csharp
        if (mode == GameMode.TreasureHeist)
            return ResolveTreasureHeist(sim);
```

Then add the method:

```csharp
    private static RoundResult ResolveTreasureHeist(Simulation sim)
    {
        // Death match wipe: everyone eliminated (respawns off) -> decide now by most time.
        // Guarded on !RespawnEnabled because in respawn mode "all dead" is a transient state.
        if (!sim.RespawnEnabled && sim.Miners.All(m => !m.Alive))
            return CumulativeWinnerOrDraw(sim);

        if (!sim.TimeExpired)
            return RoundResult.Ongoing();

        var miners = sim.Miners.ToList();

        if (!sim.WinByCumulative())
        {
            // Buzzer: holder at expiry wins; else sudden-death.
            if (sim.TreasureHolderId >= 0) return RoundResult.Win(sim.TreasureHolderId);
            if (sim.SuddenDeathWinner >= 0) return RoundResult.Win(sim.SuddenDeathWinner);
            return RoundResult.Ongoing(); // keep ticking through sudden-death
        }

        // Most-time: highest cumulative; tie or all-zero -> holder, else sudden-death.
        double best = miners.Select(m => sim.HoldSecondsOf(m.Id)).DefaultIfEmpty(0).Max();
        if (best > 0)
        {
            var leaders = miners.Where(m => sim.HoldSecondsOf(m.Id) == best).ToList();
            if (leaders.Count == 1) return RoundResult.Win(leaders[0].Id);
        }
        if (sim.TreasureHolderId >= 0) return RoundResult.Win(sim.TreasureHolderId);
        if (sim.SuddenDeathWinner >= 0) return RoundResult.Win(sim.SuddenDeathWinner);
        if (best <= 0) return RoundResult.Loss(); // nobody ever held it and none left to contest -> draw
        return RoundResult.Ongoing();
    }

    private static RoundResult CumulativeWinnerOrDraw(Simulation sim)
    {
        double best = sim.Miners.Select(m => sim.HoldSecondsOf(m.Id)).DefaultIfEmpty(0).Max();
        if (best <= 0) return RoundResult.Loss(); // nobody ever held it -> draw
        var leaders = sim.Miners.Where(m => sim.HoldSecondsOf(m.Id) == best).ToList();
        return leaders.Count == 1 ? RoundResult.Win(leaders[0].Id) : RoundResult.Loss();
    }
```

Expose the config flags on `Simulation` (near the other treasure accessors):

```csharp
    public bool WinByCumulative() => Config.TreasureWinByCumulative;
    public bool RespawnEnabled => Config.TreasureRespawnEnabled;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Miner49er.Core.Tests --filter RoundResolverTreasureHeist`
Expected: PASS (3 tests).

- [ ] **Step 5: Full Core test run + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → expect all green.

```bash
git add src/Miner49er.Core/Sim/RoundResolver.cs src/Miner49er.Core/Sim/Simulation.cs src/Miner49er.Core.Tests/RoundResolverTreasureHeistTests.cs src/Miner49er.Core.Tests/TreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): RoundResolver win logic (buzzer/cumulative/sudden-death)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## PHASE 2 — Networking

### Task 7: TreasureSnapshot + HoldTimeSnapshot + codec + factory

**Files:**
- Modify: `src/Miner49er.Core/Net/Snapshots.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs`
- Modify: `src/Miner49er.Core/Net/SnapshotFactory.cs`
- Test: `src/Miner49er.Core.Tests/SnapshotCodecTreasureHeistTests.cs` (new)

**Interfaces:**
- Consumes: `Simulation` treasure accessors (`TreasureUnearthed`, `TreasureHolderId`, `TreasurePos`, `HoldSecondsOf`, `Miners`).
- Produces: `TreasureSnapshot(byte State, int X, int Y, int HolderId, float SuddenDeathProgress)`, `HoldTimeSnapshot(int MinerId, float Seconds)`; `WorldSnapshot.Treasure` (optional) + `WorldSnapshot.HoldTimes` (optional list).

**Reference:** copy the exact shape of the existing `PrizeEventSnapshot` handling in each file (added earlier this session). It shows the record-struct declaration, the trailing optional `WorldSnapshot` param, the guarded `w.Write(x.HasValue)` encode/decode, and the `SnapshotFactory` population — mirror it.

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/SnapshotCodecTreasureHeistTests.cs`:

```csharp
using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecTreasureHeistTests
{
    [Fact]
    public void Treasure_snapshot_round_trips()
    {
        var snap = new WorldSnapshot(
            Tick: 7,
            Miners: new List<MinerSnapshot>(),
            Charges: new List<ChargeSnapshot>(),
            Items: new List<ItemSnapshot>(),
            Molds: new List<MoldSnapshot>(),
            Monsters: new List<MonsterSnapshot>(),
            Treasure: new TreasureSnapshot(1, 4, 9, 2, 0.5f),
            HoldTimes: new List<HoldTimeSnapshot> { new(2, 3.25f), new(1, 1.0f) });

        var bytes = SnapshotCodec.Write(snap);
        var back = SnapshotCodec.Read(bytes);

        Assert.NotNull(back.Treasure);
        Assert.Equal(4, back.Treasure!.Value.X);
        Assert.Equal(2, back.Treasure!.Value.HolderId);
        Assert.Equal(0.5f, back.Treasure!.Value.SuddenDeathProgress, 3);
        Assert.NotNull(back.HoldTimes);
        Assert.Equal(2, back.HoldTimes!.Count);
        Assert.Equal(3.25f, back.HoldTimes![0].Seconds, 3);
    }
}
```

(Use the actual `SnapshotCodec` entry method names — `Write`/`Read` or `Encode`/`Decode` — as they appear in the file; match the `WorldSnapshot` positional/named-arg convention used by the existing prize codec test `SnapshotCodecPrizeTests.cs`.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter SnapshotCodecTreasureHeist`
Expected: FAIL — `TreasureSnapshot`/`HoldTimeSnapshot`/`Treasure`/`HoldTimes` do not exist.

- [ ] **Step 3: Declare the snapshot types**

In `src/Miner49er.Core/Net/Snapshots.cs`, next to `PrizeEventSnapshot`, add:

```csharp
public readonly record struct TreasureSnapshot(
    byte State, int X, int Y, int HolderId, float SuddenDeathProgress); // State: 0=buried,1=loose,2=carried
public readonly record struct HoldTimeSnapshot(int MinerId, float Seconds);
```

And extend the `WorldSnapshot` record's trailing optional params (after `PrizeClaim`):

```csharp
    TreasureSnapshot?                        Treasure         = null,
    IReadOnlyList<HoldTimeSnapshot>?         HoldTimes        = null);
```

- [ ] **Step 4: Encode/decode in SnapshotCodec**

In `src/Miner49er.Core/Net/SnapshotCodec.cs`, after the `PrizeEvent`/`PrizeClaim` block, add symmetric code. Encode:

```csharp
        w.Write(snap.Treasure.HasValue);
        if (snap.Treasure is { } tr)
        {
            w.Write(tr.State); w.Write(tr.X); w.Write(tr.Y); w.Write(tr.HolderId); w.Write(tr.SuddenDeathProgress);
        }
        var holds = snap.HoldTimes ?? System.Array.Empty<HoldTimeSnapshot>();
        w.Write(holds.Count);
        foreach (var h in holds) { w.Write(h.MinerId); w.Write(h.Seconds); }
```

Decode (mirror, into locals `treasure` and `holdTimes`, then pass to the `WorldSnapshot` ctor as `Treasure: treasure, HoldTimes: holdTimes`):

```csharp
        TreasureSnapshot? treasure = null;
        if (r.ReadBoolean())
            treasure = new TreasureSnapshot(r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadSingle());
        int holdCount = r.ReadInt32();
        var holdTimes = new List<HoldTimeSnapshot>(holdCount);
        for (int i = 0; i < holdCount; i++) holdTimes.Add(new HoldTimeSnapshot(r.ReadInt32(), r.ReadSingle()));
```

(Match the reader/writer variable names in the file — the prize block uses the same `w`/`r`. Keep read order identical to write order; append at the very end of the section so field ordering stays consistent.)

- [ ] **Step 5: Populate in SnapshotFactory**

In `src/Miner49er.Core/Net/SnapshotFactory.cs`, mirror the prize population:

```csharp
        TreasureSnapshot? treasure = null;
        IReadOnlyList<HoldTimeSnapshot>? holdTimes = null;
        if (sim.Config.TreasureHeistMode)
        {
            byte state = (byte)(!sim.TreasureUnearthed ? 0 : sim.TreasureHolderId >= 0 ? 2 : 1);
            treasure = new TreasureSnapshot(state, sim.TreasurePos.X, sim.TreasurePos.Y, sim.TreasureHolderId, 0f);
            holdTimes = sim.Miners.Select(m => new HoldTimeSnapshot(m.Id, (float)sim.HoldSecondsOf(m.Id))).ToList();
        }
```

and pass `Treasure: treasure, HoldTimes: holdTimes` to the returned `WorldSnapshot`. If `sim.Config` is not public, use whatever accessor the factory already uses to read mode flags (or add `public SimConfig Config => Config;`-style exposure consistent with the file).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter SnapshotCodecTreasureHeist`
Expected: PASS.

- [ ] **Step 7: Full Core suite + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → all green.

```bash
git add src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs src/Miner49er.Core/Net/SnapshotFactory.cs src/Miner49er.Core.Tests/SnapshotCodecTreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): TreasureSnapshot + HoldTimeSnapshot codec + factory

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 8: MatchHost transient toast events

**Files:**
- Modify: `game/net/MatchHost.cs`
- Modify: `src/Miner49er.Core/Net/Snapshots.cs` (transient toast field)
- Modify: `src/Miner49er.Core/Net/SnapshotCodec.cs` (encode/decode the transient)

**Interfaces:**
- Consumes: `SimEvent`s `TreasureFound`, `TreasureRecovered`, `TreasureSneaking`, `TreasureDropped`.
- Produces: `WorldSnapshot.TreasureToast` (a one-tick transient the client turns into a banner).

**Reference:** `MatchHost.cs` already drains events and sets one-tick transients like `PrizeClaim`. Mirror exactly.

- [ ] **Step 1: Add a transient toast snapshot**

In `Snapshots.cs` add:

```csharp
public readonly record struct TreasureToastSnapshot(byte Kind, int MinerId); // 0=Found,1=Recovered,2=Sneaking,3=Dropped
```

Add trailing optional `TreasureToastSnapshot? TreasureToast = null` to `WorldSnapshot`, and encode/decode it in `SnapshotCodec.cs` with the same guarded `HasValue` pattern as `PrizeClaim` (write bool + `Kind` byte + `MinerId` int; read symmetrically; pass `TreasureToast: treasureToast`).

- [ ] **Step 2: Map events in MatchHost**

In `game/net/MatchHost.cs`, next to `PrizeClaimSnapshot? prizeClaim = null;` and its `DrainEvents` switch, add:

```csharp
        TreasureToastSnapshot? treasureToast = null;
```

In the event switch:

```csharp
            case TreasureFound tf:     treasureToast = new TreasureToastSnapshot(0, tf.MinerId); break;
            case TreasureRecovered tr: treasureToast = new TreasureToastSnapshot(1, tr.MinerId); break;
            case TreasureSneaking ts:  treasureToast = new TreasureToastSnapshot(2, ts.MinerId); break;
            case TreasureDropped td:   treasureToast = new TreasureToastSnapshot(3, td.MinerId); break;
```

After the loop:

```csharp
        if (treasureToast is { } tt) snapshot = snapshot with { TreasureToast = tt };
```

- [ ] **Step 3: Verify build + commit**

Run: `dotnet build Miner49er.sln -c Debug` → 0 errors.

```bash
git add game/net/MatchHost.cs src/Miner49er.Core/Net/Snapshots.cs src/Miner49er.Core/Net/SnapshotCodec.cs
git commit -F - <<'EOF'
feat(heist): host maps treasure events to a one-tick toast transient

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## PHASE 3 — Godot presentation

### Task 9: NetworkManager host-only flags + Lobby toggles + persistence

**Files:**
- Modify: `game/net/NetworkManager.cs`
- Modify: `game/audio/SettingsStore.cs`
- Modify: `game/ui/Lobby.cs`

**Interfaces:**
- Produces: `NetworkManager.MatchTreasureWinByCumulative` (bool), `NetworkManager.MatchTreasureRespawn` (bool), set in `StartMatch`; lobby controls `_heistWinByPicker`, `_heistRespawnPicker`; `SettingsStore` keys `heist_winby`, `heist_respawn`.

**Reference:** this session added `MatchPrizeEvents`/`MatchPrizeFrequency` as host-only props threaded through `StartMatch` (not the `BeginMatch` RPC) and the lobby cadence picker with `SettingsStore` persistence — mirror that end-to-end.

- [ ] **Step 1: NetworkManager props + StartMatch params**

In `game/net/NetworkManager.cs`, near `MatchPrizeFrequency`, add:

```csharp
    // Host-only (host runs the sim): Treasure Heist rules.
    public bool MatchTreasureWinByCumulative { get; private set; }
    public bool MatchTreasureRespawn { get; private set; } = true;
```

Add params to `StartMatch(...)` after `int prizeFrequency = 1`: `bool treasureWinByCumulative = false, bool treasureRespawn = true`. In the body near `MatchPrizeFrequency = prizeFrequency;` set:

```csharp
        MatchTreasureWinByCumulative = treasureWinByCumulative;
        MatchTreasureRespawn = treasureRespawn;
```

- [ ] **Step 2: SettingsStore persistence**

In `game/audio/SettingsStore.cs`, extend the `LoadLobby` return tuple and `SaveLobby` params with `bool heistWinByCumulative, bool heistRespawn` (defaults `false`, `true`), reading/writing keys `"heist_winby"` and `"heist_respawn"` in the `lobby` section — same pattern as `prize_freq`/`advanced`.

- [ ] **Step 3: Lobby controls**

In `game/ui/Lobby.cs`: add fields `_heistWinByPicker`, `_heistRespawnPicker`; destructure the two new saved values; add `"Treasure Heist"` to `_modePicker` with a `ModeDescription` case; add two `OptionButton`s inside `_advancedBox`:

```csharp
        _heistWinByPicker = new OptionButton();
        _heistWinByPicker.AddItem("Win: Hold at buzzer", 0);
        _heistWinByPicker.AddItem("Win: Most time",      1);
        _heistWinByPicker.Select(savedHeistWinBy ? 1 : 0);
        _advancedBox.AddChild(_heistWinByPicker);

        _heistRespawnPicker = new OptionButton();
        _heistRespawnPicker.AddItem("Respawns: Unlimited",   1);
        _heistRespawnPicker.AddItem("Respawns: Death match", 0);
        _heistRespawnPicker.Select(savedHeistRespawn ? 0 : 1);
        _advancedBox.AddChild(_heistRespawnPicker);
```

In `RefreshModeControls`, gate visibility with `bool heist = _modePicker.GetSelectedId() == (int)GameMode.TreasureHeist;` then `_heistWinByPicker.Visible = isHost && heist;` and `_heistRespawnPicker.Visible = isHost && heist;`. In the start handler, pass to `SaveLobby` and `StartMatch`: `treasureWinByCumulative = _heistWinByPicker.GetSelectedId() == 1`, `treasureRespawn = _heistRespawnPicker.GetSelectedId() == 1`.

- [ ] **Step 4: Build + commit**

Run: `dotnet build Miner49er.sln -c Debug` → 0 errors.

```bash
git add game/net/NetworkManager.cs game/audio/SettingsStore.cs game/ui/Lobby.cs
git commit -F - <<'EOF'
feat(heist): lobby mode + Win-by/Respawns toggles, host-wired + persisted

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 10: Main.cs — heist SimConfig + toasts + HUD; MatchClient state

**Files:**
- Modify: `game/net/MatchClient.cs`
- Modify: `game/Main.cs`

**Interfaces:**
- Consumes: `NetworkManager.MatchTreasureWinByCumulative`, `MatchTreasureRespawn`; `WorldSnapshot.Treasure`, `HoldTimes`, `TreasureToast`.
- Produces: `MatchClient.Treasure`, `MatchClient.HoldTimes`, `MatchClient.TreasureToast` event; `Main` HUD + banners.

- [ ] **Step 1: MatchClient exposes treasure state + toast event**

In `game/net/MatchClient.cs`, mirror `PrizeEvent`/`PrizeWon`: add `public TreasureSnapshot? Treasure { get; private set; }`, `public IReadOnlyList<HoldTimeSnapshot> HoldTimes { get; private set; } = System.Array.Empty<HoldTimeSnapshot>();`, and `public event Action<byte, int>? TreasureToast;`. In `ApplyUpdate` (before `_terrainMap?.UpdateTiles(...)`), set `Treasure = update.Snapshot.Treasure; HoldTimes = update.Snapshot.HoldTimes ?? System.Array.Empty<HoldTimeSnapshot>();` and, when `update.Snapshot.TreasureToast is { } tt`, fire `TreasureToast?.Invoke(tt.Kind, tt.MinerId);`.

- [ ] **Step 2: Main builds the heist SimConfig**

In `game/Main.cs`, in the `f1SimCfg` construction (where `PrizeEventsEnabled` is set), add:

```csharp
                TreasureHeistMode = nm.MatchMode == GameMode.TreasureHeist,
                TreasureWinByCumulative = nm.MatchTreasureWinByCumulative,
                TreasureRespawnEnabled = nm.MatchTreasureRespawn,
```

(Starting stones flow automatically via `AddMiner` from Task 1.)

- [ ] **Step 3: Toast subscriptions**

After `AddChild(_client)` (next to the prize subscriptions):

```csharp
        _client.TreasureToast += (kind, minerId) =>
        {
            string who = NameForMiner(minerId);
            string msg = kind switch
            {
                0 => $"{who} has found it!",
                1 => $"{who} recovered it!",
                2 => $"{who} is sneaking away with it!",
                _ => $"{who} dropped the treasure!",
            };
            SetAnnouncement(msg, 3500);
            _deathFeed?.PushMessage(msg);
        };
```

- [ ] **Step 4: HUD**

Add a Treasure-Heist HUD line updated each frame from `_client.Treasure` / `_client.HoldTimes`: "Held by: X" (or "loose"/"buried") in Buzzer mode, or a compact top-3 hold-time list in Most-time mode. Follow the existing HUD label pattern in `Main.cs` (e.g. the Treasure Hunt idol panel). The match clock stays on the existing timer HUD.

- [ ] **Step 5: Build + import + commit**

Run: `dotnet build Miner49er.sln -c Debug` → 0 errors.
Run (PowerShell): `godot --headless --import --path "D:\Projects\Miner49er" --quit` → clean.

```bash
git add game/net/MatchClient.cs game/Main.cs
git commit -F - <<'EOF'
feat(heist): client treasure state + toasts + HUD; host SimConfig wiring

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

### Task 11: WorldRenderer treasure + dizzy stars; minimap ping; compass

**Files:**
- Modify: `game/WorldRenderer.cs`
- Modify: `game/ui/MinimapOverlay.cs`
- Modify: `game/ui/Compass.cs`

**Interfaces:**
- Consumes: `MatchClient.Treasure`; `MinerSnapshot.StunRemaining` (already present).

- [ ] **Step 1: Draw the treasure**

In `game/WorldRenderer.cs`, in the draw pass (near the prize-marker block), when `_client.Treasure is { } t` and `t.State != 0` (not buried) and the tile is visible/spectated: draw the treasure using `_itemTex[ItemKind.IdolUrn]` if loaded (else a gold diamond fallback). When carried (`t.HolderId >= 0`), lift it above the carrier by `ts * 0.75f` (reuse the prize carried-lift), tracking `t.X/t.Y`.

- [ ] **Step 2: Dizzy stars over stunned miners**

```csharp
        foreach (var m in _client.Miners)
        {
            if (m.StunRemaining <= 0f || !m.Alive) continue;
            if (!_client.Fog.IsVisible(new GridPos(m.X, m.Y)) && !spectating) continue;
            var c = new Vector2(m.X * ts + ts / 2f, m.Y * ts + ts / 2f - ts * 0.5f);
            float baseA = (float)Time.GetTicksMsec() / 220f;
            for (int i = 0; i < 3; i++)
            {
                float a = baseA + i * Mathf.Tau / 3f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f) * ts * 0.28f;
                DrawString(ThemeDB.FallbackFont, p, "*", HorizontalAlignment.Center, -1, ts / 2, new Color(1f, 0.9f, 0.3f));
            }
        }
```

(`spectating` is the same local the surrounding draw code already computes; reuse it. `MinerSnapshot` already carries `StunRemaining`.)

- [ ] **Step 3: Minimap ping the carrier**

In `game/ui/MinimapOverlay.cs`, mirror the prize-ping: when `_client.Treasure is { HolderId: >= 0 } t`, draw a pulsing blip at `t.X/t.Y`.

- [ ] **Step 4: Compass needle to the treasure**

In `game/ui/Compass.cs`, extend `ComputeExitAngle` (or add a parallel target): when `NetworkManager.Instance.MatchMode == GameMode.TreasureHeist` and `_client.Treasure is { } t`, point the green needle at `new GridPos(t.X, t.Y)`. Make the rose render in this mode without the Listen key (set the rose `Active`/`Visible` when mode is Treasure Heist), leaving the Listen arcs unchanged.

- [ ] **Step 5: Build + import + commit**

Run: `dotnet build Miner49er.sln -c Debug` → 0 errors.
Run (PowerShell): `godot --headless --import --path "D:\Projects\Miner49er" --quit` → clean.

```bash
git add game/WorldRenderer.cs game/ui/MinimapOverlay.cs game/ui/Compass.cs
git commit -F - <<'EOF'
feat(heist): render treasure, dizzy stars, carrier ping, treasure compass

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## PHASE 4 — Bots

### Task 12: BotBrain treasure-seeking intent

**Files:**
- Modify: `src/Miner49er.Core/AI/BotBrain.cs`
- Test: `src/Miner49er.Core.Tests/BotBrainTreasureHeistTests.cs` (new)

**Interfaces:**
- Consumes: `Simulation` treasure accessors; existing `BotBrain` movement/intent helpers and `BotSkill` gating.
- Produces: a Treasure-Heist branch in the bot's per-tick decision.

- [ ] **Step 1: Write the failing test**

Create `src/Miner49er.Core.Tests/BotBrainTreasureHeistTests.cs`:

```csharp
using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotBrainTreasureHeistTests
{
    [Fact]
    public void Bot_moves_toward_the_loose_treasure()
    {
        var cfg = TreasureHeistTests.Cfg();
        var sim = new Simulation(TreasureHeistTests.Grid(20, 20), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.ForceTreasureLooseForTest(new GridPos(10, 2)); // due east
        var brain = new BotBrain(1, BotSkill.Miner, seed: 123);
        int before = sim.GetMiner(1).Pos.X;
        for (int i = 0; i < 10; i++) { brain.Act(sim); sim.Tick(0.2); }
        Assert.True(sim.GetMiner(1).Pos.X > before);
    }
}
```

(Match the real `BotBrain` constructor + per-tick entry point — inspect `BotBrain.cs` and use them verbatim; e.g. `Act(sim)` / `Update(sim, dt)`.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/Miner49er.Core.Tests --filter BotBrainTreasureHeist`
Expected: FAIL — bot ignores the treasure.

- [ ] **Step 3: Implement the intent**

In `src/Miner49er.Core/AI/BotBrain.cs`, add a branch at the top of the per-tick decision (before generic mining/wander), gated on `sim.Config.TreasureHeistMode`:

- If `sim.TreasureHolderId == this bot's id` → step **away** from the nearest rival (toward `SpawnPos`/open space).
- Else if `sim.TreasureHolderId >= 0` (a rival carries it) → path toward the carrier's tile; if adjacent-and-facing, pickaxe-stun; if within stone range and `BotSkill >= Miner`, throw a stone (reuse the bot's existing stone-throw call if present).
- Else if `sim.TreasureUnearthed` → path toward `sim.TreasurePos` and step onto it.
- Else → path toward `sim.TreasurePos` (buried) and mine the wall to unearth it.

Reuse the brain's existing greedy step-toward-target and mine-the-faced-tile helpers — no A*. Follow the existing `BotSkill` gating style in the file.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/Miner49er.Core.Tests --filter BotBrainTreasureHeist`
Expected: PASS.

- [ ] **Step 5: Full suite + build + import + commit**

Run: `dotnet test src/Miner49er.Core.Tests` → all green.
Run: `dotnet build Miner49er.sln -c Debug` → 0 errors.
Run (PowerShell): `godot --headless --import --path "D:\Projects\Miner49er" --quit` → clean.

```bash
git add src/Miner49er.Core/AI/BotBrain.cs src/Miner49er.Core.Tests/BotBrainTreasureHeistTests.cs
git commit -F - <<'EOF'
feat(heist): bots seek/contest the treasure

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## Final verification (before finishing the branch)

- [ ] `dotnet test src/Miner49er.Core.Tests` — all green (existing + new heist tests).
- [ ] `dotnet build Miner49er.sln -c Debug` — 0 warnings, 0 errors.
- [ ] `godot --headless --import --path "D:\Projects\Miner49er" --quit` (PowerShell) — import clean.
- [ ] Manual playtest (host + 1 client or bots): mode selectable; compass leads to buried urn; dig it out → "found it"; carry slows + pings minimap; stone/pickaxe stun drops it + dizzy stars → "dropped"/"recovered"; sneak toast after a lone run; Buzzer win = holder at 0:00; Most-time = leaderboard; Unlimited respawn revives at spawn; Death match keeps the dead out but treasure still decides.
- [ ] Then use **superpowers:finishing-a-development-branch**.
