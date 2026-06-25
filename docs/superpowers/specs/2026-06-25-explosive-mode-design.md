# Explosive Mode Design

**Date:** 2026-06-25

## Goal

Add a three-way explosive mode setting to multiplayer non-co-op lobbies (LastManStanding, GoldRush, ReachCenter). The host selects one option; the choice is propagated to all clients via the existing BeginMatch RPC so map generation and simulation stay deterministic.

## Modes

| Mode | Timed charges | Detonators spawn |
|------|--------------|-----------------|
| **Dynamite** (default) | Yes | No |
| **Detonator Specials** | Yes | `playerCount` |
| **Detonators Only** | No | `playerCount` |

- "Timed charges" = the always-available blast mechanic (plant → 3 s fuse → explode).
- "Detonators spawn" = `ItemKind.Detonator` floor pickups placed by map generator.
- Detonator specials: one detonator per player so everyone has access to at least one.
- Detonators only: timed charges are blocked at the simulation level; players must find and use detonators to blast. The detonator mechanic (plant → retreat ≥ 5 tiles → trigger reel) is already fully implemented in Core.

## Architecture

### New enum `ExplosiveMode` (Core)

```csharp
// src/Miner49er.Core/Sim/ExplosiveMode.cs
namespace Miner49er.Core;
public enum ExplosiveMode { Dynamite = 0, DetonatorSpecials = 1, DetonatorsOnly = 2 }
```

### `SimConfig` — new field

```csharp
public bool DynamiteEnabled { get; set; } = true;
```

### `Simulation.TryStartPlanting()` — gate

```csharp
if (!Config.DynamiteEnabled) return false;
```

This is the only simulation change needed. The host's SimConfig carries `DynamiteEnabled = false` when mode is DetonatorsOnly; the guard prevents `ActivityKind.Planting` from ever starting.

### `MapConfig.For()` — new parameter

```csharp
public static MapConfig For(GameMode mode, int seed, int playerCount,
    bool pits = false, bool caveIns = false, bool lava = false,
    int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite)
```

Sets:
```csharp
cfg.DetonatorCount = explosive == ExplosiveMode.Dynamite ? 0 : playerCount;
```

Both host and client call `MapConfig.For()` so both need the `explosive` param to generate identical maps (same detonator placement).

`FloorConfig` (Expedition) is unaffected — it continues to set `DetonatorCount` by floor number and does not use `ExplosiveMode`.

### Network propagation

`NetworkManager.StartMatch` gains `ExplosiveMode explosive`:

```csharp
public void StartMatch(GameMode mode, int timeLimitSeconds, bool flooding,
    bool pits, bool caveIns, bool lava, float baseMoveSeconds,
    int mapScale = 1, ExplosiveMode explosive = ExplosiveMode.Dynamite)
```

`BeginMatch` RPC gains `int explosive` (serialised as int, cast on receipt):

```csharp
[Rpc(MultiplayerApi.RpcMode.Authority)]
public void BeginMatch(int seed, int playerCount, int mode, int timeLimitSeconds,
    bool flooding, bool pits, bool caveIns, bool lava,
    float baseMoveSeconds, int mapScale, int explosive, long[] peerOrder)
```

New property on `NetworkManager`:
```csharp
public ExplosiveMode MatchExplosive { get; private set; }
```

Set in `BeginMatch`:
```csharp
MatchExplosive = (ExplosiveMode)explosive;
```

### `Main.cs`

Both `clientMapCfg` and `hostMapCfg` pass `nm.MatchExplosive`:

```csharp
MapConfig.For(nm.MatchMode, seed, playerCount,
    nm.MatchPits, nm.MatchCaveIns, nm.MatchLava,
    nm.MatchMapScale, nm.MatchExplosive)
```

Host's SimConfig:
```csharp
var f1SimCfg = new SimConfig
{
    BaseMoveSeconds = nm.MatchBaseMoveSeconds,
    Seed = seed,
    DynamiteEnabled = nm.MatchExplosive != ExplosiveMode.DetonatorsOnly,
};
```

### Lobby UI

Replace the three hazard checkboxes area with an `_explosivePicker` OptionButton (host-only visibility matches existing pattern):

```
"Dynamite"           → index 0 → ExplosiveMode.Dynamite
"Detonator Specials" → index 1 → ExplosiveMode.DetonatorSpecials
"Detonators Only"    → index 2 → ExplosiveMode.DetonatorsOnly
```

The picker is hidden for non-host players (same as `_pitsCheck`, `_caveInCheck`, `_lavaCheck`).

### `SettingsStore`

`LoadLobby` return tuple gains `explosive: int` (clamped 0–2, default 0).  
`SaveLobby` signature gains `int explosive`.

Both are persisted under `lobby` section key `"explosive"`.

## Scope

- Multiplayer modes only (LastManStanding, GoldRush, ReachCenter). Expedition is unaffected.
- No new art needed — detonator sprite (`assets/objects/item_detonator.png`) already exists.
- No HUD change needed — the existing held-item display already shows the detonator/reel.
- No SFX change — existing charge/detonation sounds apply.

## Files

| File | Change |
|------|--------|
| `src/Miner49er.Core/Sim/ExplosiveMode.cs` | New — enum |
| `src/Miner49er.Core/Sim/SimConfig.cs` | Add `DynamiteEnabled` |
| `src/Miner49er.Core/Sim/Simulation.cs` | Guard in `TryStartPlanting` |
| `src/Miner49er.Core/Map/MapConfig.cs` | `For()` gains `explosive` param |
| `game/net/NetworkManager.cs` | `StartMatch`, `BeginMatch` RPC, `MatchExplosive` |
| `game/Main.cs` | Pass `MatchExplosive` to both `MapConfig.For()` calls; set `DynamiteEnabled` |
| `game/ui/Lobby.cs` | Add `_explosivePicker` OptionButton |
| `game/audio/SettingsStore.cs` | `LoadLobby`/`SaveLobby` gain `explosive` |

## Tests

- `SimulationExplosiveTests.cs` gains cases:
  - `DynamiteEnabled=false` → `TryStartPlanting` returns false
  - `DynamiteEnabled=true` → `TryStartPlanting` succeeds normally (regression)
- `MapConfigTests.cs` gains cases:
  - `For(..., explosive: DetonatorSpecials)` with `playerCount=3` → `DetonatorCount == 3`
  - `For(..., explosive: DetonatorsOnly)` with `playerCount=2` → `DetonatorCount == 2`
  - `For(..., explosive: Dynamite)` → `DetonatorCount == 0`
