# Phase 4c-1 — Status-Effect Engine & Move-Cadence Migration

**Date:** 2026-06-09
**Status:** Design approved; ready for implementation plan.
**Parent:** Phase 4c (Items & §3.5 status effects), split into **4c-1** (this doc — the
engine + cadence foundation) and **4c-2** (items on the map + pickup/use + the five
item behaviors that consume this engine).

---

## 1. Goal

Deliver the §3.5 deliverable: a generic, deterministic, host-authoritative
**status-effect mechanism** and the **move-speed model** it feeds, with movement
cadence migrated out of the Godot adapter (`MatchHost`) and into the pure-C#
`Simulation` where it belongs. No items yet — this is the foundation 4c-2 builds on.

This phase is mostly invisible by design. Its three *feelable* outcomes:

1. A host-selectable **base movement-speed preset** (Slow / Standard / Fast).
2. The on-screen miner glide now **matches the authoritative pace** (slower through
   shallow water; in 4c-2, faster under a speed potion).
3. A throwaway **debug key** (`B`) that self-applies a speed buff, so the effect
   mechanism is tangible before items exist.

The effect engine itself (apply / refresh / expire / multiplicative-stack / clamp)
is proven entirely by Core unit tests.

---

## 2. Background — where cadence lives today

Today the per-tile move cooldown lives in `MatchHost` (the Godot adapter):
`_moveCooldown[minerId]`, a `MoveStepSeconds = 0.12` constant, multiplied by
`tile.MoveCostMultiplier()` (shallow water = ×2). `MatchHost.StepOnce` decrements the
cooldown each tick, and only calls `Simulation.TryMove` when the cooldown is zero.

Consequences this phase fixes:
- Cadence is **not** in the deterministic Core, so it is untested and can't be driven
  by sim-side status effects.
- The client (`MatchClient`) slides every miner at a **constant** `TileSize /
  MoveStepSeconds`, so the glide ignores shallow-water slowdown (and would ignore
  speed effects).

§3.5 prescribes the fix: speed lives in the sim via a per-miner `MoveCooldownRemaining`,
and the client reads the miner's effective seconds-per-tile to set its slide duration.

---

## 3. Status-effect engine (Core)

New types in `src/Miner49er.Core/Sim/`:

```csharp
public enum EffectChannel { MoveSpeed }   // 4c-2 adds MiningSpeed, VisionRadius, …
public enum EffectKind    { DebugSpeed }  // 4c-2 adds SpeedPotion, SlowMold, LongerVision, …

public sealed class StatusEffect {
    public EffectKind    Kind;            // identity for the "one instance per kind" rule
    public EffectChannel Channel;         // which formula it feeds
    public double        Magnitude;       // MoveSpeed: <1 faster, >1 slower
    public double        RemainingSeconds;
}
```

`Miner` gains an internal `List<StatusEffect> _effects` and a read-only
`IReadOnlyList<StatusEffect> Effects` accessor.

`Simulation` gains:

```csharp
public void ApplyEffect(int minerId, EffectKind kind, EffectChannel channel,
                        double magnitude, double durationSeconds);
```

Rules (straight from §3.5):
- **One instance per `Kind`.** If an effect of that `Kind` already exists on the
  miner, overwrite its `Magnitude` and reset `RemainingSeconds = durationSeconds`
  (refresh — never compound a second multiplier). Different *kinds* coexist.
- **Channel grouping.** The effective multiplier for a channel is the product of the
  `Magnitude`s of all that miner's active effects on that channel. Speed-potion (×0.6)
  and slow-mold (×1.8) are *different kinds, same channel* → they multiply and then
  the result is clamped (§4). Opposite effects partly cancel.
- **Expiry.** `Tick(dt)` decrements every effect's `RemainingSeconds` and drops those
  that reach ≤ 0.

Applying an effect to a dead miner is a no-op (defensive; the debug path only targets
the local living miner, but 4c-2 item pickups should not resurrect logic).

---

## 4. Move-speed model & cadence migration (Core)

`Miner` gains:

```csharp
public double MoveCooldownRemaining { get; internal set; }
```

`SimConfig` gains (tunable; values are §3.5's Standard preset and example clamps):

```csharp
public double BaseMoveSeconds { get; set; } = 0.12;  // Standard preset
public double MinMoveSeconds  { get; set; } = 0.05;  // clamp floor — no teleporting
public double MaxMoveSeconds  { get; set; } = 0.40;  // clamp ceiling — never frozen
```

Effective seconds-per-tile, computed against the tile the miner is **standing on**:

```csharp
double EffectiveMoveSeconds(Miner m) {
    double mult = 1.0;
    foreach (var e in m.Effects)
        if (e.Channel == EffectChannel.MoveSpeed) mult *= e.Magnitude;
    double tile = Grid.Get(m.Pos).MoveCostMultiplier();   // shallow water = ×2 (today's slowdown)
    return Math.Clamp(Config.BaseMoveSeconds * tile * mult,
                      Config.MinMoveSeconds, Config.MaxMoveSeconds);
}
```

The shallow-water slowdown is folded in as the `tile` factor — it stays a positional
multiplier (not a timed status effect), exactly as §3.5's formula separates the tile
cost from the effect modifiers.

**`TryMove` self-gates.** The new cooldown check is the first thing after the alive
check, placed **before** facing/activity mutation so cooldown-blocked input is a true
no-op (preserving today's behavior, where `MatchHost` simply didn't call `TryMove`
during cooldown):

```csharp
public bool TryMove(int id, Direction dir) {
    var m = _miners[id];
    if (!m.Alive) return false;
    if (m.MoveCooldownRemaining > 0) return false;          // NEW — gate before facing/activity
    m.Facing = dir;
    CancelActivity(m);
    var target = m.Pos + dir.ToOffset();
    if (!Grid.InBounds(target) || !Grid.Get(target).IsEnterable()) return false;  // wall-bump sets no cooldown, as today
    var from = m.Pos;
    m.Pos = target;
    _events.Add(new MinerMoved(id, from, target));
    // … existing drown (IsLethal) and FirstToReachCenter checks …
    m.MoveCooldownRemaining = EffectiveMoveSeconds(m);      // NEW — set on success, uses destination tile
    return true;
}
```

(Placement detail: `MoveCooldownRemaining` is assigned in the success path, using the
destination tile — matching today's `MatchHost`, which sets the cooldown from the tile
just moved onto. A move that drowns the miner still assigns it; harmless, the miner is
dead.)

**`Tick(dt)`** decrements `MoveCooldownRemaining` alongside the effects:

```csharp
public void Tick(double dt) {
    Elapsed += dt;
    AdvanceEffects(dt);     // decrement RemainingSeconds, drop expired
    AdvanceCooldowns(dt);   // MoveCooldownRemaining = Max(0, MoveCooldownRemaining - dt)
    var chargesThisTick = _charges.ToList();
    AdvanceActivities(dt);
    AdvanceCharges(chargesThisTick, dt);
    AdvanceFlood();
}
```

**`MatchHost` becomes a thin driver.** It drops `_moveCooldown`, `MoveStepSeconds`,
and its decrement/gate loop. Each tick it simply calls `_sim.TryMove(minerId, dir)` for
each pending direction; the sim self-gates. Cadence is preserved to within one tick (the
Standard preset still yields 0.12 s/tile on floor).

`MatchClient.MoveSpeedPixels` (derived from `MatchHost.MoveStepSeconds`) is removed — see
§5.

---

## 5. Client slide-matching (snapshot + codec + renderer)

So every peer's glide matches the authoritative pace, the snapshot carries each miner's
effective pace:

- `MinerSnapshot` gains `double MoveSeconds`.
- `SnapshotFactory.Capture` fills it from `EffectiveMoveSeconds(m)` (always ≥
  `MinMoveSeconds`, so never zero → no divide-by-zero downstream).
- `SnapshotCodec` writes/reads one extra `double` per miner.
- `MatchClient._PhysicsProcess` computes per-miner `pixelsPerSec = TileSize /
  (float)m.MoveSeconds` for its `MoveToward` glide, replacing the shared
  `MoveSpeedPixels` constant. The `MatchHost.MoveStepSeconds` dependency in
  `MatchClient` is removed.

No new sync channel is needed for the base-speed preset — clients render pace purely
from `MoveSeconds`, so all three presets and any future effect look correct on every peer.

---

## 6. Base-speed preset (lobby → netcode → sim)

Mirrors the existing time-limit picker exactly.

- **Lobby** (`game/ui/Lobby.cs`): a host-only `OptionButton` "Base Speed" with items
  Slow / Standard / Fast → `0.20 / 0.12 / 0.07` s-per-tile, default **Standard**. Added
  inside the lobby stack (now wrapped in a `CenterContainer`). The selected preset maps
  to a `float` seconds value passed to `StartMatch`.
- **Netcode** (`game/net/NetworkManager.cs`):
  - `StartMatch(GameMode mode, int timeLimitSeconds, bool flooding, float baseMoveSeconds)`.
  - `BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds, bool flooding,
    float baseMoveSeconds, long[] peerOrder)` RPC carries the `float`.
  - `public float MatchBaseMoveSeconds { get; private set; }` set in `BeginMatch`.
- **Sim** (`game/Main.cs`): the host branch builds
  `new SimConfig { BaseMoveSeconds = nm.MatchBaseMoveSeconds }`. Clients never read it
  directly — they get pace via the snapshot's `MoveSeconds`.

---

## 7. Temporary debug keybind (throwaway — removed in 4c-2)

Routed host-authoritatively so it stays deterministic. **Every line tagged**
`// DEBUG(4c-1): remove in 4c-2`.

- `Main._PhysicsProcess` edge-detects the raw `B` key (a `_debugBoostPressed` latch so it
  fires once per press) and calls `NetworkManager.SendDebugSpeed()`.
- `NetworkManager.SendDebugSpeed()`: host path calls `_matchHost.ApplyDebugSpeed(LocalId)`
  directly; client path `RpcId(1, nameof(ReceiveDebugSpeed))` to the host.
- `MatchHost.ApplyDebugSpeed(long peerId)` resolves the miner and calls
  `_sim.ApplyEffect(minerId, EffectKind.DebugSpeed, EffectChannel.MoveSpeed, 0.6, 5.0)` —
  a ×0.6 buff for 5 s. Re-pressing refreshes the 5 s, demonstrating the one-per-kind
  refresh rule live.

---

## 8. Testing

**New Core test files** (`src/Miner49er.Core.Tests/`):

- `StatusEffectTests.cs`
  - Apply an effect; after `Tick` past its duration it expires (drops from `Effects`).
  - Reapply the same `Kind` → `RemainingSeconds` refreshed, still a single instance,
    `Magnitude` overwritten.
  - Two different `Kind`s on `MoveSpeed` → magnitudes multiply.
  - Apply to a dead miner is a no-op.
- `MovementCadenceTests.cs`
  - A second `TryMove` within the cooldown window is rejected; succeeds after enough
    `Tick`.
  - Cooldown set from `EffectiveMoveSeconds`: floor tile = 0.12; shallow ×2 = 0.24; a
    ×0.6 effect = 0.072.
  - Clamp honored at `MinMoveSeconds` / `MaxMoveSeconds` (stack of buffs / debuffs).
  - **Regression:** no effects + Standard config + floor ⇒ exactly 0.12.

**Extended existing tests:**
- `SnapshotCodecTests` — round-trip the new `MoveSeconds` field.
- `SnapshotFactoryTests` — `Capture` populates `MoveSeconds` from the formula.
- **Audit** the seven `TryMove`-using test files (`SimulationMovementTests`,
  `SimulationMiningTests`, `SimulationExplosiveTests`, `FloodTests`, `RoundResolverTests`,
  `GameModeTests`, `SnapshotFactoryTests`): single-step moves are safe (a fresh miner has
  zero cooldown); any sequence that walks a miner **multiple tiles without an intervening
  `Tick`** must reset `MoveCooldownRemaining` or `Tick` between steps.

**Verification gates** (headless-only, per the dev environment): `dotnet test` green
(current 122 + new), `dotnet build` 0 errors, `godot --headless --quit-after 180` exits 0
with no `ERROR` lines.

**Playtest (the human-feel gate):** the three base-speed presets feel distinctly
different; the glide matches pace (notably slower through shallow water); the `B` key
gives a clear ~5 s speed burst that refreshes on re-press.

---

## 9. Out of scope (deferred to 4c-2)

- Item entities on the map, deterministic placement, pickup/use verb.
- The real item set: speed potion, slow mold, bigger blast, longer vision, water-plank.
- Additional `EffectChannel`s (MiningSpeed, VisionRadius) and `EffectKind`s beyond
  `DebugSpeed`, and their consumers (e.g., client fog reading a VisionRadius effect).
- Removal of the §7 debug keybind.

---

## 10. File touch-list

**Core (`src/Miner49er.Core/`):**
- `Sim/StatusEffect.cs` *(new)* — `EffectChannel`, `EffectKind`, `StatusEffect`.
- `Sim/Miner.cs` — `_effects` list + `Effects` accessor; `MoveCooldownRemaining`.
- `Sim/SimConfig.cs` — `BaseMoveSeconds`, `MinMoveSeconds`, `MaxMoveSeconds`.
- `Sim/Simulation.cs` — `ApplyEffect`, `EffectiveMoveSeconds`, `AdvanceEffects`,
  `AdvanceCooldowns`, `TryMove` gate, `Tick` wiring.
- `Net/Snapshots.cs` — `MinerSnapshot.MoveSeconds`.
- `Net/SnapshotCodec.cs` — write/read `MoveSeconds`.
- `Net/SnapshotFactory.cs` — populate `MoveSeconds`.

**Godot adapter (`game/`):**
- `net/MatchHost.cs` — drop cooldown gate; `ApplyDebugSpeed`.
- `net/MatchClient.cs` — per-miner slide from `MoveSeconds`; drop `MoveSpeedPixels`.
- `net/NetworkManager.cs` — `MatchBaseMoveSeconds`; `StartMatch`/`BeginMatch` signature;
  `SendDebugSpeed`/`ReceiveDebugSpeed`.
- `ui/Lobby.cs` — Base Speed `OptionButton`.
- `Main.cs` — build `SimConfig` with `BaseMoveSeconds`; debug-key edge detect.

**Tests (`src/Miner49er.Core.Tests/`):** `StatusEffectTests.cs` *(new)*,
`MovementCadenceTests.cs` *(new)*, extend `SnapshotCodecTests` + `SnapshotFactoryTests`,
audit the seven `TryMove` files.
