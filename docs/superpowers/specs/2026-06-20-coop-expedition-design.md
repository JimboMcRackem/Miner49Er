# Co-op Expedition Design

## Goal

Let 2–4 players play Expedition mode together through the existing Lobby, sharing a pool of 4 lives and clearing floors as a team.

## Decisions

| Question | Answer |
|---|---|
| Lives | 4 shared (any expedition, any player count) |
| Floor advance | All living miners must reach the exit simultaneously |
| Individual death | Miner becomes spectator; partner(s) carry on; no shared life lost |
| Team wipe (all dead) | Spend 1 shared life → retry same floor with all players respawned |
| Launch path | Existing Lobby → new Expedition mode option in mode picker |
| Solo expedition | Unchanged — still via MainMenu → Solo Expedition panel |
| Map size | Scales with player count via `MapConfig.FloorConfig(floor, seed, playerCount)` |
| Score | Submitted under host's name; `_cumulativeGold` sums all miners naturally |

## Architecture

No new types. Six files touched with surgical changes.

### 1. `src/Miner49er.Core/Sim/RoundResolver.cs`

Change Expedition floor-clear from "any miner on exit" to "all alive miners on exit":

```csharp
if (mode == GameMode.Expedition)
{
    if (alive.Count == 0) return RoundResult.Loss();
    if (sim.EscapeOpen && sim.EscapeTile is { } exit)
    {
        if (alive.All(m => m.Pos == exit))
            return RoundResult.NextFloor(alive[0].Id);
    }
    return RoundResult.Ongoing();
}
```

Solo (1 miner) is unchanged: `All()` on a single-element list behaves identically to the old `FirstOrDefault`.

### 2. `src/Miner49er.Core/Map/MapConfig.cs`

Add `playerCount` parameter (default 1) to `FloorConfig` so co-op floors scale with player count:

```csharp
public static MapConfig FloorConfig(int floor, int seed, int playerCount = 1)
{
    // existing band logic unchanged
    var cfg = For(GameMode.Expedition, seed, playerCount, pits, caveIns, lava, mapScale);
    // ...
}
```

### 3. `game/net/NetworkManager.cs`

Add `mapScale` to `StartMatch` and `BeginMatch` so clients build the correct floor sizes. `BeginMatch` sets `MatchMapScale = mapScale`.

```csharp
public void StartMatch(..., int mapScale)
// Rpc: BeginMatch(..., int mapScale) → MatchMapScale = mapScale
```

### 4. `game/net/MatchHost.cs`

**Lives** — 4 for any expedition, 1 for competitive modes:
```csharp
_livesMax = nm.MatchMode == GameMode.Expedition ? 4 : 1;
```

**`AdvanceFloor`** — spawn all peers, not just the winner. Miner IDs are 1-based (`minerId = peerOrder index + 1`), so spawn index = `minerId - 1`:

```csharp
foreach (var minerId in _peerToMiner.Values)
{
    int idx = minerId - 1;
    GridPos spawn = idx < newMap.Spawns.Count ? newMap.Spawns[idx] : newMap.Spawns[0];
    // nudge East of escape tile if spawn == escapeTile
    newSim.AddMiner(minerId, spawn, invulRemaining: 3.0);
    if (_permLevels.TryGetValue(minerId, out var levels))
        newSim.SetPermLevels(minerId, levels.Speed, levels.Vision, levels.Blast);
}
// Monster placement reference = miner 1's spawn
GridPos monsterRef = _peerToMiner.Values.Contains(1) ? newMap.Spawns[0] : newMap.Spawns[0];
int monsterCount = (int)(MonsterRoster.CountFor(...) * simCfg.MonsterCountMultiplier);
var roster = MonsterSpawner.Place(newMap.Grid, monsterRef, monsterCount);
```

**Loss retry** — `expeditionLoss` block calls `AdvanceFloor(primaryMiner, sameFloor: true)`. Remove the old `soloMiner = _peerToMiner.Values.First()` comment; the new spawn-all loop handles all peers regardless.

### 5. `game/Main.cs`

Pass `nm.MatchPlayerCount` to `MapConfig.FloorConfig()` on both host and client paths so floor maps scale for multiple players.

### 6. `game/ui/Lobby.cs` + `game/audio/SettingsStore.cs`

**Lobby mode picker** — add `Expedition` option (id = `(int)GameMode.Expedition` = 3). When Expedition is selected:
- Hide `_timePicker` (no timer in expedition)
- Show `_mapSizePicker` (Small=1/Medium=2/Large=3/Huge=4)
- Hazard checkboxes remain visible (user can toggle)

**Start handler** — pass `_mapSizePicker.GetSelectedId()` into `StartMatch(..., mapScale)`.

**SettingsStore** — add `map_scale` key to `[lobby]` section:
```csharp
// LoadLobby → returns (gameMode, timeLimit, flood, pits, caveIns, lava, speed, mapScale)
// SaveLobby → persists mapScale
```

## Data flow

```
Host picks Expedition in Lobby → StartMatch(Expedition, mapScale=2, ...)
  → BeginMatch RPC → all clients: MatchMapScale=2, MatchMode=Expedition
  → Main._Ready(): MapConfig.FloorConfig(1, seed, playerCount=2)
  → Simulation built with 2 miners + monsters
  → Per tick: RoundResolver checks alive.All(m => m.Pos == exit)
  → Floor clear: MatchHost.AdvanceFloor spawns all miners on new floor
  → Team wipe: MatchHost decrements shared lives, retries same floor
  → 0 lives: BroadcastResult(hostPeer) → ResultsOverlay on all clients
```

## What does NOT change

- Solo expedition (MainMenu → Solo panel) — unchanged
- `WorldSnapshot.Lives` carries shared pool; HUD already shows it correctly
- Monster scaling (`MonsterRoster.CountFor`) uses map area, scales automatically
- Perm buffs tracked per miner ID — already correct for multi-miner
- Score formula (`100 * floor + cumulativeGold`) — `_cumulativeGold` sums all miners' gold naturally
- Results overlay text — acceptable for now ("Player X escaped!" when co-op wins)

## Tests to update

- `MapConfigFloorTests`: add overload tests for `playerCount > 1`
- `MapConfigTests`: no change (those test `For()` directly)
- No new Core tests needed for `RoundResolver` (solo path unchanged, co-op is a runtime behavior test)
