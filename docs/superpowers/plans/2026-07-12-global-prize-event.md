# Global Prize Event Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a host-authoritative, networked prize-event subsystem that periodically spawns an announced, minimap-pinged prize competitive players rush and fight over.

**Architecture:** All event/claim/reward logic lives in `Miner49er.Core.Simulation` (host-authoritative; clients only render snapshots). One event at a time via a small state machine advanced in `Tick`. Rotates 4 claim types; rewards are mode-appropriate. Clients render a new optional `PrizeEventSnapshot`.

**Tech Stack:** C# / .NET 10, `Miner49er.Core` (engine-free sim), Godot 4.6 presentation, xUnit tests. Design spec: `docs/superpowers/specs/2026-07-12-global-prize-event-design.md`.

## Global Constraints

- Repo conventions (standing): never `git add -A`; stage feature files by explicit path; never stage `*.import` LF-churn, `*.uid`, `Temp/`, `Done/`, `.superpowers/`, `_preview_*`, or unrelated pre-existing edits. Spec/plan docs under `docs/superpowers/` stay **untracked** (do not commit them).
- Branch off `main` before committing; ff-merge + push only when the user asks.
- Run `godot` headless import via **PowerShell only** (Bash shim breaks headless). Build/test via `dotnet` (either shell).
- Commit messages end with: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- Every task must keep `dotnet build Miner49er.sln` clean (0 warnings, 0 errors) and all Core tests green.
- Competitive modes only: `LastManStanding, GoldRush, ReachCenter, TreasureHunt, DemolitionDerby`. Expedition is excluded everywhere.

---

## File Structure

**Core (new/modified):**
- `src/Miner49er.Core/Sim/PrizeEvent.cs` — **new**: `PrizeType`, `PrizeState` enums (small, no behaviour).
- `src/Miner49er.Core/Sim/SimConfig.cs` — add `Mode`, `PrizeEventsEnabled`, and prize tunables.
- `src/Miner49er.Core/Sim/SimEvent.cs` — add `PrizeTelegraphed`, `PrizeClaimed`, `PrizeExpired`.
- `src/Miner49er.Core/Sim/Simulation.cs` — state fields, `AdvancePrizeEvent`, spawn selection, per-type claim, `ApplyPrizeReward`, public accessors.
- `src/Miner49er.Core/Net/Snapshots.cs` — `PrizeEventSnapshot` + `WorldSnapshot.PrizeEvent` field.
- `src/Miner49er.Core/Net/SnapshotCodec.cs` — encode/decode.
- `src/Miner49er.Core/Net/SnapshotFactory.cs` — capture the active event into the snapshot.

**Godot (modified):**
- `game/net/MatchHost.cs` — pass `Mode`/`PrizeEventsEnabled` into `SimConfig`; drain prize events → banner/feed data.
- `game/net/NetworkManager.cs`, `game/net/MatchClient.cs` — thread the toggle; expose prize snapshot + events to the UI.
- `game/audio/SettingsStore.cs`, `game/ui/Lobby.cs` — lobby toggle + persistence.
- `game/Main.cs` — banners + DeathFeed lines from prize events.
- `game/ui/MinimapOverlay.cs`, `game/WorldRenderer.cs` — minimap ping + on-map marker + progress ring + relic visuals.

**Tests (new):**
- `src/Miner49er.Core.Tests/PrizeEventTests.cs` — grows per phase.

---

## PHASE 1 — Framework + Grab-and-go (complete vertical slice)

### Task 1: Prize types + sim events + config

**Files:**
- Create: `src/Miner49er.Core/Sim/PrizeEvent.cs`
- Modify: `src/Miner49er.Core/Sim/SimConfig.cs`, `src/Miner49er.Core/Sim/SimEvent.cs`

**Interfaces — Produces:**
- `enum PrizeType { GrabAndGo, MineOut, HoldPoint, CarryRelic }`
- `enum PrizeState { Idle, Telegraph, Active }`
- `SimConfig.Mode` (GameMode), `SimConfig.PrizeEventsEnabled` (bool), tunables.
- `PrizeTelegraphed(PrizeType Type, GridPos Pos)`, `PrizeClaimed(int MinerId, PrizeType Type)`, `PrizeExpired() : SimEvent`.

- [ ] **Step 1: Create the enums**

`src/Miner49er.Core/Sim/PrizeEvent.cs`:
```csharp
namespace Miner49er.Core;

public enum PrizeType { GrabAndGo, MineOut, HoldPoint, CarryRelic }
public enum PrizeState { Idle, Telegraph, Active }
```

- [ ] **Step 2: Add config** — in `SimConfig.cs` add:
```csharp
    // Global prize events (competitive only). Mode lets the sim pick mode-appropriate rewards.
    public GameMode Mode { get; set; } = GameMode.GoldRush;
    public bool PrizeEventsEnabled { get; set; } = false;
    public double PrizeFirstDelaySeconds { get; set; } = 150.0;
    public double PrizeIntervalSeconds  { get; set; } = 150.0;
    public double PrizeJitterSeconds    { get; set; } = 30.0;
    public double PrizeTelegraphSeconds { get; set; } = 5.0;
    public double PrizeExpirySeconds    { get; set; } = 60.0;
    public double PrizeMineSeconds      { get; set; } = 4.0;
    public double PrizeHoldSeconds      { get; set; } = 5.0;
    public int    PrizeHoldRadius       { get; set; } = 3;
    public int    PrizeMinPlayerSpacing { get; set; } = 4;
    public int    PrizeGoldReward       { get; set; } = 20;
```

- [ ] **Step 3: Add events** — in `SimEvent.cs` after `PortalUsed`:
```csharp
public sealed record PrizeTelegraphed(PrizeType Type, GridPos Pos) : SimEvent;
public sealed record PrizeClaimed(int MinerId, PrizeType Type) : SimEvent;
public sealed record PrizeExpired : SimEvent;
```

- [ ] **Step 4: Build** — `dotnet build src/Miner49er.Core/Miner49er.Core.csproj`. Expected: 0 errors.

- [ ] **Step 5: Commit** — `git add src/Miner49er.Core/Sim/PrizeEvent.cs src/Miner49er.Core/Sim/SimConfig.cs src/Miner49er.Core/Sim/SimEvent.cs` then commit `feat(prize): prize-event types, config, and sim events`.

---

### Task 2: Lifecycle state machine + spawn selection

**Files:**
- Modify: `src/Miner49er.Core/Sim/Simulation.cs`
- Test: `src/Miner49er.Core.Tests/PrizeEventTests.cs`

**Interfaces — Produces (public accessors for the snapshot/tests):**
- `PrizeState PrizeState { get; }`, `PrizeType PrizeType { get; }`, `GridPos PrizePos { get; }`
- `double PrizeClaimProgress { get; }`, `int PrizeHolderId { get; }`, `double PrizeSecondsRemaining { get; }`

**Consumes:** `_rng`, `_miners`, `Grid`, `Config`, `_events`, existing `Tick(double)`.

- [ ] **Step 1: Write failing lifecycle tests** — `src/Miner49er.Core.Tests/PrizeEventTests.cs`:
```csharp
using System.Linq;
using Miner49er.Core;
using Xunit;

public class PrizeEventTests
{
    private static SimConfig Cfg() => new SimConfig
    {
        BaseMoveSeconds = 0.01, Mode = GameMode.GoldRush, PrizeEventsEnabled = true,
        PrizeFirstDelaySeconds = 1.0, PrizeTelegraphSeconds = 0.5, PrizeExpirySeconds = 2.0,
        PrizeIntervalSeconds = 1.0, PrizeJitterSeconds = 0.0, PrizeMinPlayerSpacing = 2,
    };
    private static TileGrid Grid(int w = 15, int h = 15) => new TileGrid(w, h, TileType.Floor);

    [Fact]
    public void Stays_idle_before_first_delay()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(0.5);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Arms_to_telegraph_then_active()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); // past first delay -> telegraph
        Assert.Equal(PrizeState.Telegraph, sim.PrizeState);
        Assert.Single(sim.DrainEvents().OfType<PrizeTelegraphed>());
        sim.Tick(0.6); // past telegraph -> active
        Assert.Equal(PrizeState.Active, sim.PrizeState);
    }

    [Fact]
    public void Unclaimed_event_expires_and_rearms()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); sim.Tick(0.6);          // active
        sim.Tick(2.1);                          // past expiry
        Assert.Contains(sim.DrainEvents(), e => e is PrizeExpired);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Disabled_never_leaves_idle()
    {
        var cfg = Cfg(); cfg.PrizeEventsEnabled = false;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(5.0);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Spawn_tile_is_open_and_clear_of_players()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); // telegraph -> pos chosen
        Assert.True(sim.Grid.Get(sim.PrizePos).IsEnterable());
        Assert.True(sim.PrizePos.ChebyshevTo(new GridPos(2, 2)) >= 2);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`PrizeState` etc. don't exist). `dotnet test src/Miner49er.Core.Tests --filter PrizeEventTests`.

- [ ] **Step 3: Add state fields + accessors** near the other `Simulation` fields:
```csharp
    private PrizeState _prizeState = PrizeState.Idle;
    private PrizeType  _prizeType;
    private GridPos    _prizePos;
    private double     _prizeArmTimer;
    private double     _prizeStateTimer;
    private double     _prizeClaimProgress;
    private int        _prizeHolderId = -1;

    public PrizeState PrizeState => _prizeState;
    public PrizeType  PrizeType  => _prizeType;
    public GridPos    PrizePos   => _prizePos;
    public double     PrizeClaimProgress   => _prizeClaimProgress;
    public int        PrizeHolderId        => _prizeHolderId;
    public double     PrizeSecondsRemaining => _prizeStateTimer;
```
Initialize the arm timer once (in the constructor, or lazily on first competitive tick):
```csharp
    private bool _prizeArmed;
    private void EnsurePrizeArmed()
    {
        if (_prizeArmed) return;
        _prizeArmed = true;
        _prizeArmTimer = Config.PrizeFirstDelaySeconds;
    }
```

- [ ] **Step 4: Add `AdvancePrizeEvent` and call it from `Tick`** — add `AdvancePrizeEvent(dt);` in `Tick` (after `AdvanceCooldowns(dt);`), and:
```csharp
    private void AdvancePrizeEvent(double dt)
    {
        if (!Config.PrizeEventsEnabled || Config.Mode == GameMode.Expedition) return;
        EnsurePrizeArmed();
        switch (_prizeState)
        {
            case PrizeState.Idle:
                _prizeArmTimer -= dt;
                if (_prizeArmTimer <= 0) EnterPrizeTelegraph();
                break;
            case PrizeState.Telegraph:
                _prizeStateTimer -= dt;
                if (_prizeStateTimer <= 0) { _prizeState = PrizeState.Active; _prizeStateTimer = Config.PrizeExpirySeconds; }
                break;
            case PrizeState.Active:
                _prizeStateTimer -= dt;
                UpdatePrizeClaim(dt);
                if (_prizeState == PrizeState.Active && _prizeStateTimer <= 0) ExpirePrize();
                break;
        }
    }

    private void EnterPrizeTelegraph()
    {
        _prizeType = (PrizeType)_rng.Next(4);
        _prizePos = SelectPrizeTile();
        _prizeClaimProgress = 0;
        _prizeHolderId = -1;
        _prizeState = PrizeState.Telegraph;
        _prizeStateTimer = Config.PrizeTelegraphSeconds;
        _events.Add(new PrizeTelegraphed(_prizeType, _prizePos));
    }

    private void ExpirePrize()
    {
        _events.Add(new PrizeExpired());
        ResetPrize();
    }

    private void ClaimPrize(int minerId)
    {
        ApplyPrizeReward(minerId);
        _events.Add(new PrizeClaimed(minerId, _prizeType));
        ResetPrize();
    }

    private void ResetPrize()
    {
        _prizeState = PrizeState.Idle;
        _prizeHolderId = -1;
        _prizeClaimProgress = 0;
        _prizeArmTimer = Config.PrizeIntervalSeconds + _rng.NextDouble() * Config.PrizeJitterSeconds;
    }

    private GridPos SelectPrizeTile()
    {
        GridPos best = default; int bestScore = int.MinValue; bool found = false;
        int spacing = Config.PrizeMinPlayerSpacing;
        for (int attempt = 0; attempt < 200; attempt++)
        {
            var p = new GridPos(_rng.Next(Grid.Width), _rng.Next(Grid.Height));
            var t = Grid.Get(p);
            if (t != TileType.Floor) continue; // safe, dry, walkable
            bool tooClose = _miners.Values.Any(m => m.Alive && m.Pos.ChebyshevTo(p) < spacing);
            if (tooClose) continue;
            // Prefer tiles that minimize the max distance to living players (pulls them together).
            int maxD = _miners.Values.Where(m => m.Alive)
                .Select(m => m.Pos.ChebyshevTo(p)).DefaultIfEmpty(0).Max();
            int score = -maxD;
            if (score > bestScore) { bestScore = score; best = p; found = true; }
            if (attempt > 40 && found) break; // enough sampling
        }
        if (!found) // relaxed fallback: any floor tile
            best = _miners.Values.Any()
                ? FirstFloorTile()
                : new GridPos(Grid.Width / 2, Grid.Height / 2);
        return best;
    }

    private GridPos FirstFloorTile()
    {
        for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
                if (Grid.Get(new GridPos(x, y)) == TileType.Floor) return new GridPos(x, y);
        return new GridPos(Grid.Width / 2, Grid.Height / 2);
    }

    // Filled in Task 3 (GrabAndGo) and Phase 2/3 (other types).
    private void UpdatePrizeClaim(double dt) { }
    // Filled in Task 3 (Gold Rush) and Phase 4 (full table).
    private void ApplyPrizeReward(int minerId) { }
```

- [ ] **Step 5: Run — expect PASS** for the 5 lifecycle tests. `dotnet test src/Miner49er.Core.Tests --filter PrizeEventTests`.

- [ ] **Step 6: Commit** — stage `Simulation.cs` + `PrizeEventTests.cs`; `feat(prize): event lifecycle state machine + fair spawn selection`.

---

### Task 3: Grab-and-go claim + Gold Rush reward

**Files:** Modify `Simulation.cs`; extend `PrizeEventTests.cs`.

**Interfaces — Consumes:** `PrizeState`, `_prizePos` (Task 2). **Produces:** working `UpdatePrizeClaim` (GrabAndGo) + `ApplyPrizeReward` (GoldRush).

- [ ] **Step 1: Failing tests**:
```csharp
    [Fact]
    public void GrabAndGo_claims_when_a_miner_steps_on_it_and_pays_gold()
    {
        var cfg = Cfg(); cfg.PrizeGoldReward = 25;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); sim.Tick(0.6);          // active; prize at some tile
        // Force a known type + position via reflection-free helper: re-roll until GrabAndGo.
        // (Test uses the seam below.)
        var pos = sim.PrizePos;
        sim.SetMinerPositionForTest(1, pos);   // test seam (Step 3)
        sim.Tick(0.05);
        Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed pc && pc.MinerId == 1);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }
```
Note: to keep the test deterministic across the 4 random types, seed the sim RNG so the first type is GrabAndGo, or add a test-only `ForcePrizeForTest(PrizeType, GridPos)` seam. Prefer the seam:
```csharp
    // test-only helpers on Simulation:
    internal void ForcePrizeForTest(PrizeType type, GridPos pos)
    { _prizeArmed = true; _prizeType = type; _prizePos = pos; _prizeState = PrizeState.Active;
      _prizeStateTimer = Config.PrizeExpirySeconds; _prizeClaimProgress = 0; _prizeHolderId = -1; }
    internal void SetMinerPositionForTest(int id, GridPos p) { if (_miners.TryGetValue(id, out var m)) m.Pos = p; }
```
Rewrite the test to `sim.ForcePrizeForTest(PrizeType.GrabAndGo, new GridPos(7,7)); sim.SetMinerPositionForTest(1, new GridPos(7,7)); sim.Tick(0.05);`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement claim + reward:**
```csharp
    private void UpdatePrizeClaim(double dt)
    {
        switch (_prizeType)
        {
            case PrizeType.GrabAndGo:
                foreach (var m in _miners.Values)
                    if (m.Alive && m.Pos == _prizePos) { ClaimPrize(m.Id); return; }
                break;
            // MineOut/HoldPoint/CarryRelic added in later phases.
        }
    }

    private void ApplyPrizeReward(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        switch (Config.Mode)
        {
            case GameMode.GoldRush:
            default:
                m.GoldCollected += Config.PrizeGoldReward;
                break;
            // Full table in Phase 4.
        }
    }
```
Add the two `internal` test seams from Step 1.

- [ ] **Step 4: Run — expect PASS** (all PrizeEventTests). Also run the full Core suite to confirm no regressions.

- [ ] **Step 5: Commit** — `feat(prize): grab-and-go claim + Gold Rush gold reward`.

---

### Task 4: Networking — snapshot + codec + capture

**Files:** `Net/Snapshots.cs`, `Net/SnapshotCodec.cs`, `Net/SnapshotFactory.cs`, `game/net/MatchHost.cs`.

**Interfaces — Produces:** `PrizeEventSnapshot`; `WorldSnapshot.PrizeEvent`; populated on the wire.

- [ ] **Step 1: Add the snapshot record** — in `Snapshots.cs`:
```csharp
public readonly record struct PrizeEventSnapshot(
    byte Type, byte State, int X, int Y, float ClaimProgress, int HolderId, float SecondsRemaining);
```
Add to `WorldSnapshot` params (after `DynamiteThrows`):
```csharp
    IReadOnlyList<DynamiteThrowSnapshot>?    DynamiteThrows   = null,
    PrizeEventSnapshot?                      PrizeEvent       = null);
```

- [ ] **Step 2: Encode** — in `SnapshotCodec` Write, after the DynamiteThrows block:
```csharp
        w.Write(snap.PrizeEvent.HasValue);
        if (snap.PrizeEvent is { } pe)
        { w.Write(pe.Type); w.Write(pe.State); w.Write(pe.X); w.Write(pe.Y);
          w.Write(pe.ClaimProgress); w.Write(pe.HolderId); w.Write(pe.SecondsRemaining); }
```

- [ ] **Step 3: Decode** — mirror in Read (after the dynamiteThrows block):
```csharp
        PrizeEventSnapshot? prizeEvent = null;
        if (r.ReadBoolean())
            prizeEvent = new PrizeEventSnapshot(
                r.ReadByte(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(),
                r.ReadSingle(), r.ReadInt32(), r.ReadSingle());
```
Pass `PrizeEvent: prizeEvent` into the `WorldSnapshot(...)` constructor call.

- [ ] **Step 4: Capture in `SnapshotFactory.Capture`** — build the snapshot when active:
```csharp
        PrizeEventSnapshot? prize = sim.PrizeState == PrizeState.Idle ? null
            : new PrizeEventSnapshot((byte)sim.PrizeType, (byte)sim.PrizeState,
                sim.PrizePos.X, sim.PrizePos.Y, (float)sim.PrizeClaimProgress,
                sim.PrizeHolderId, (float)sim.PrizeSecondsRemaining);
```
and add `PrizeEvent: prize` to the returned `WorldSnapshot`.

- [ ] **Step 5: Add a codec round-trip test** in the existing snapshot-codec test file (mirror an existing round-trip): set a `PrizeEvent`, Write→Read, assert equality. Run it.

- [ ] **Step 6: Build full solution** (`dotnet build Miner49er.sln`) — 0 errors. Commit `feat(prize): network the active event via PrizeEventSnapshot`.

---

### Task 5: Lobby toggle plumbing

**Files:** `game/audio/SettingsStore.cs`, `game/ui/Lobby.cs`, `game/net/NetworkManager.cs`, `game/net/MatchHost.cs`.

**Interfaces — Produces:** host-set `PrizeEventsEnabled` reaching `SimConfig`.

- [ ] **Step 1:** Extend `SettingsStore.LoadLobby/SaveLobby` with a `prizeEvents` bool (mirror an existing bool like `flooding`). Default true.
- [ ] **Step 2:** In `Lobby.cs`, add a `CheckBox`/toggle "Prize Events" in the host options column (mirror the flooding/pits toggles). Hide it for Expedition (`_modePicker` == Expedition). Persist via SaveLobby.
- [ ] **Step 3:** Thread the flag through `NetworkManager.StartMatch(...)` params and the `MatchStarting` payload (mirror `MatchFlooding`), storing `NetworkManager.MatchPrizeEvents`.
- [ ] **Step 4:** In `MatchHost` where it builds `SimConfig`, set `Mode = NetworkManager.Instance.MatchMode` and `PrizeEventsEnabled = NetworkManager.Instance.MatchPrizeEvents && MatchMode != GameMode.Expedition`.
- [ ] **Step 5:** Build; play-agnostic. Commit `feat(prize): lobby toggle + host wiring for prize events`.

---

### Task 6: Presentation — banner, minimap ping, on-map marker

**Files:** `game/net/MatchClient.cs`, `game/Main.cs`, `game/ui/MinimapOverlay.cs`, `game/WorldRenderer.cs`.

**Interfaces — Consumes:** `WorldSnapshot.PrizeEvent`, prize sim events surfaced to `Main`.

- [ ] **Step 1:** In `MatchClient.ApplyUpdate`, expose the latest `PrizeEvent` snapshot (a public property `PrizeEvent`) and raise C# events for telegraph/claim/expire (derive from `DeathFeed`-style event surfacing already used for whistles/portals). Store prize pos for renderers.
- [ ] **Step 2:** In `Main.cs`, on telegraph → `SetAnnouncement("⚡ Prize incoming!", 4000)`; on claim → `SetAnnouncement($"{Name} claimed the prize!", 3000)` + a `DeathFeed` line; on expire → optional quiet banner.
- [ ] **Step 3:** In `MinimapOverlay`, draw a pulsing marker at the prize tile while `PrizeEvent != null` (mirror how player dots are drawn; pulse via `Time.GetTicksMsec`).
- [ ] **Step 4:** In `WorldRenderer._Draw`, draw a glowing on-map marker at the prize tile when in view or spectating (placeholder: a pulsing gold diamond). Progress ring + per-type icons come in later phases.
- [ ] **Step 5:** PowerShell headless import + a manual host test (Gold Rush, short `PrizeFirstDelaySeconds` for testing). Commit `feat(prize): banner, minimap ping, and on-map marker`.

**Phase 1 gate:** a full vertical slice — a grab-and-go prize spawns, is announced/pinged, claimed for gold, and re-arms. Merge to `main` on user approval via superpowers:finishing-a-development-branch.

---

## PHASE 2 — MineOut + HoldPoint

### Task 7: MineOut claim

**Files:** `Simulation.cs`; `PrizeEventTests.cs`.

- [ ] **Step 1: Failing tests** — accrues while standing; resets on leave/stun; claims at threshold:
```csharp
    [Fact]
    public void MineOut_accrues_while_standing_and_claims_at_threshold()
    {
        var cfg = Cfg(); cfg.PrizeMineSeconds = 1.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.ForcePrizeForTest(PrizeType.MineOut, new GridPos(7, 7));
        sim.Tick(0.5); Assert.True(sim.PrizeClaimProgress > 0.4 && sim.PrizeState == PrizeState.Active);
        sim.Tick(0.6); Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed);
    }

    [Fact]
    public void MineOut_resets_when_channeler_leaves_or_is_stunned()
    {
        var cfg = Cfg(); cfg.PrizeMineSeconds = 2.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.ForcePrizeForTest(PrizeType.MineOut, new GridPos(7, 7));
        sim.Tick(0.5); Assert.True(sim.PrizeClaimProgress > 0);
        sim.SetMinerPositionForTest(1, new GridPos(9, 9)); // walked off
        sim.Tick(0.1); Assert.Equal(0.0, sim.PrizeClaimProgress, 3);
    }
```

- [ ] **Step 2: Run — FAIL. Step 3: Implement** the `MineOut` branch in `UpdatePrizeClaim`:
```csharp
            case PrizeType.MineOut:
            {
                var channeler = _miners.Values.FirstOrDefault(m =>
                    m.Alive && m.StunRemaining <= 0 && m.Pos == _prizePos);
                if (channeler == null) { _prizeClaimProgress = 0; _prizeHolderId = -1; break; }
                if (_prizeHolderId != channeler.Id) { _prizeHolderId = channeler.Id; _prizeClaimProgress = 0; }
                _prizeClaimProgress += dt / Config.PrizeMineSeconds;
                if (_prizeClaimProgress >= 1.0) ClaimPrize(channeler.Id);
                break;
            }
```

- [ ] **Step 4: Run — PASS.** Commit `feat(prize): mine-out channel claim`.

### Task 8: HoldPoint claim

- [ ] **Step 1: Failing tests** — accrues solo-in-radius; pauses empty; resets when contested:
```csharp
    [Fact]
    public void HoldPoint_claims_when_held_solo() { /* one miner in radius, tick > hold, expect claim */ }
    [Fact]
    public void HoldPoint_resets_when_contested() { /* two miners in radius -> progress 0 */ }
```
(Write both fully, mirroring Task 7's style, using `ForcePrizeForTest(PrizeType.HoldPoint, pos)`.)

- [ ] **Step 2: FAIL. Step 3: Implement** the `HoldPoint` branch:
```csharp
            case PrizeType.HoldPoint:
            {
                var inRing = _miners.Values.Where(m => m.Alive &&
                    m.Pos.ChebyshevTo(_prizePos) <= Config.PrizeHoldRadius).ToList();
                if (inRing.Count == 0) break;                 // pause, keep progress
                if (inRing.Count > 1) { _prizeClaimProgress = 0; _prizeHolderId = -1; break; } // contested
                var holder = inRing[0];
                if (_prizeHolderId != holder.Id) { _prizeHolderId = holder.Id; _prizeClaimProgress = 0; }
                _prizeClaimProgress += dt / Config.PrizeHoldSeconds;
                if (_prizeClaimProgress >= 1.0) ClaimPrize(holder.Id);
                break;
            }
```

- [ ] **Step 4: PASS.** Commit `feat(prize): hold-the-point claim with contest reset`.

### Task 9: Progress-ring rendering

- [ ] In `WorldRenderer`, when the prize snapshot is MineOut/HoldPoint, draw an arc from `ClaimProgress` (0..1) around the prize tile (`DrawArc`), tinted by holder color if `HolderId >= 0`. Manual verify. Commit `feat(prize): claim-progress ring for mine-out/hold-point`.

**Phase 2 gate:** merge on approval.

---

## PHASE 3 — CarryRelic

### Task 10: Relic pickup / carry / drop / bank

**Files:** `Simulation.cs`; `PrizeEventTests.cs`.

- [ ] **Step 1: Failing tests** — pickup sets holder; carrying to spawn banks + claims; death/stun drops it:
```csharp
    [Fact]
    public void Relic_is_picked_up_carried_to_spawn_and_banked() { /* ForcePrize CarryRelic; step on; move to spawn tile; expect PrizeClaimed */ }
    [Fact]
    public void Relic_drops_on_holder_death() { /* holder killed -> HolderId == -1, prize still active at drop pos */ }
```

- [ ] **Step 2: FAIL. Step 3: Implement** the `CarryRelic` branch. Track the holder + the relic tile; use each miner's spawn tile (record spawn positions at `AddMiner` if not already available — add `Miner.SpawnPos` if needed):
```csharp
            case PrizeType.CarryRelic:
            {
                if (_prizeHolderId < 0)
                {
                    var picker = _miners.Values.FirstOrDefault(m => m.Alive && m.Pos == _prizePos);
                    if (picker != null) _prizeHolderId = picker.Id;
                }
                else if (!_miners.TryGetValue(_prizeHolderId, out var h) || !h.Alive || h.StunRemaining > 0)
                {
                    if (_miners.TryGetValue(_prizeHolderId, out var dropper)) _prizePos = dropper.Pos;
                    _prizeHolderId = -1;                       // dropped, grabbable again
                }
                else
                {
                    _prizePos = h.Pos;                          // relic rides the holder
                    if (h.Pos == h.SpawnPos) ClaimPrize(h.Id); // banked
                }
                break;
            }
```
Add `Miner.SpawnPos` (set in `AddMiner`) if it doesn't exist.

- [ ] **Step 4: PASS.** Commit `feat(prize): carry-it-back relic claim`.

### Task 11: Relic carry visual

- [ ] In `WorldRenderer`, draw the relic on the ground when `HolderId < 0`, and a carried marker above the holder when `HolderId >= 0` (mirror the idol/chest carried indicator). Commit `feat(prize): relic ground + carried visuals`.

**Phase 3 gate:** merge on approval.

---

## PHASE 4 — Per-mode rewards + polish

### Task 12: Full reward table

**Files:** `Simulation.cs`; `PrizeEventTests.cs`.

- [ ] **Step 1: Failing tests** — one per mode reward (Gold Rush gold [exists]; Treasure Hunt idol credit; LMS invuln > 0 + blast effect; Reach Center MoveSpeed effect; Derby stones +3 + blast):
```csharp
    [Fact]
    public void TreasureHunt_reward_credits_an_idol() { /* Mode=TreasureHunt; force+claim; assert GetTreasureProgress reflects +1 for claimer */ }
    [Fact]
    public void LMS_reward_grants_invulnerability() { /* Mode=LastManStanding; claim; assert GetMiner.InvulnerableRemaining > 0 */ }
    [Fact]
    public void ReachCenter_reward_applies_move_speed_effect() { /* Mode=ReachCenter; claim; assert an active MoveSpeed effect */ }
```

- [ ] **Step 2: FAIL. Step 3: Implement** the full `ApplyPrizeReward` switch (reusing existing infra — `EffectChannel.MoveSpeed` already exists; invuln via `m.InvulnerableRemaining`; idol via `_idolsFound[id]++`; `AddStones`; `ApplyEffect(BlastRadius)`):
```csharp
    private void ApplyPrizeReward(int minerId)
    {
        if (!_miners.TryGetValue(minerId, out var m)) return;
        switch (Config.Mode)
        {
            case GameMode.GoldRush:
                m.GoldCollected += Config.PrizeGoldReward; break;
            case GameMode.TreasureHunt:
                _idolsFound[minerId] = _idolsFound.GetValueOrDefault(minerId) + 1; break;
            case GameMode.LastManStanding:
                m.InvulnerableRemaining = Math.Max(m.InvulnerableRemaining, 4.0);
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius, BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
            case GameMode.ReachCenter:
                ApplyEffect(minerId, EffectKind.Haste, EffectChannel.MoveSpeed, /* magnitude */ 1, 8.0); break;
            case GameMode.DemolitionDerby:
                AddStones(minerId, 3);
                ApplyEffect(minerId, EffectKind.BiggerBlast, EffectChannel.BlastRadius, BuffTuning.BlastMagnitude, BuffTuning.BlastSeconds);
                break;
            default:
                m.GoldCollected += Config.PrizeGoldReward; break;
        }
    }
```
Verify `EffectKind` has a suitable member for haste (add `EffectKind.Haste` if needed) and the exact `ApplyEffect` magnitude/sign for `MoveSpeed` by reading how sight/blast buffs are applied. Adjust magnitudes to taste.

- [ ] **Step 4: PASS** + full Core suite green. Commit `feat(prize): mode-appropriate reward table`.

### Task 13: DeathFeed lines + per-type on-map icons + tuning

- [ ] Add per-type icons to the on-map marker (relic/gem/flag/point) and distinct DeathFeed copy per type/claim. Manual playtest each mode with a shortened `PrizeFirstDelaySeconds`. Restore default tunables. Commit `feat(prize): per-type icons + feed copy + tuning`.

### Task 14: Final review + finish

- [ ] Run the full solution build + Core tests. Use **superpowers:finishing-a-development-branch** to verify tests, merge, and push.
- [ ] Update memory (`throw-dynamite-status` sibling: a new `prize-events-status.md`) once merged.

---

## Self-Review Notes
- **Spec coverage:** lifecycle (Task 2), all 4 claim types (Tasks 3,7,8,10), mode rewards (Task 12), spawn fairness (Task 2), toggle/modes (Task 5), networking (Task 4), presentation (Tasks 6,9,11,13) — all covered.
- **Type consistency:** `PrizeState`/`PrizeType` enums, `PrizeEventSnapshot` fields, and `Simulation` accessor names are used identically across networking and rendering tasks.
- **Known verify-during-task items:** exact `ApplyEffect` signature/magnitude and whether `EffectKind.Haste` needs adding (Task 12); whether `Miner.SpawnPos` exists (Task 10); mirror the precise `SettingsStore.LoadLobby` tuple shape (Task 5). Each is called out in its task.
