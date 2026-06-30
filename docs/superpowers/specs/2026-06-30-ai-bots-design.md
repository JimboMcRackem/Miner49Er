# AI Bot Miners Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add computer-controlled miner bots that the host can add to any lobby, playable across all five game modes, with four distinct skill levels.

**Architecture:** Bots are host-side only — they have miner IDs in the simulation but no network peer. `MatchHost` runs a `BotBrain` per bot each tick and injects results directly into the existing pending-input dictionaries. The lobby shows bots as always-ready player entries with a remove button.

**Tech Stack:** Pure C# (`Miner49er.Core`), Godot UI (`game/ui/Lobby.cs`), host driver (`game/net/MatchHost.cs`).

---

## Global Constraints

- Bots run host-side only; no RPC traffic; no changes to the snapshot/codec/client path
- `BotBrain` and `BotPathfinder` live in `Miner49er.Core` — zero Godot dependency
- All existing 536+ tests must continue to pass
- Maximum 8 total players (real + bots); bots fill slots after real players
- Bot miner IDs are assigned after real player IDs (real = 1..N, bots = N+1, N+2…)
- Fake peer IDs for bots use negative longs (−1001, −1002, …) — never overlap ENet peer IDs
- Bot names drawn from pool: Dusty, Rocky, Gravel, Nugget, Pickaxe, Blaster, Shafty, Copperhead; duplicates get a number suffix

---

## 1. Skill Levels

Enum `BotSkill` in `Miner49er.Core/AI/BotSkill.cs`:

```csharp
public enum BotSkill { Greenhorn, Miner, Foreman, DynamiteDan }
```

| Level | Display name | Re-eval interval | Pathfinding | Special |
|-------|-------------|-----------------|-------------|---------|
| Greenhorn | Greenhorn | 30 ticks | Random walk, avoids nothing | 40% chance to mine when facing rock |
| Miner | Miner | 15 ticks | BFS around rocks (no mining through walls) | Mode-aware goals |
| Foreman | Foreman | 7 ticks | BFS through rocks (plans to mine) | Uses items |
| DynamiteDan | Dynamite Dan | 3 ticks | BFS through rocks | Plants charges on clusters, Listen, throws stones |

---

## 2. New Files

### `src/Miner49er.Core/AI/BotSkill.cs`
Enum only.

### `src/Miner49er.Core/AI/BotAction.cs`
```csharp
public readonly struct BotAction
{
    public readonly int Dir;    // -1 = stand still
    public readonly bool Mine;
    public readonly bool Plant;
    public readonly bool Use;
    public readonly bool Throw;
    public BotAction(int dir, bool mine = false, bool plant = false, bool use = false, bool @throw = false)
    { Dir = dir; Mine = mine; Plant = plant; Use = use; Throw = @throw; }
    public static readonly BotAction Idle = new(-1);
}
```

### `src/Miner49er.Core/AI/BotPathfinder.cs`
```csharp
public static class BotPathfinder
{
    // BFS from `from` toward `to`. Returns the Direction int (0=N,1=E,2=S,3=W) of the
    // first step, or -1 if already there / unreachable.
    // passRock=true: treats Rock tiles as passable (bot will mine through them).
    // passRock=false: treats Rock tiles as walls.
    public static int NextDir(TileGrid grid, GridPos from, GridPos to, bool passRock);

    // Returns the nearest reachable GridPos from `candidates` by BFS distance from `from`.
    // Returns null if candidates is empty or none reachable.
    public static GridPos? Nearest(TileGrid grid, GridPos from, IEnumerable<GridPos> candidates, bool passRock);
}
```

Passable tiles for BFS: `Floor`, `Cracked`, `Crumbling`, `Plank`, `GoldRock` (always), `Rock` (only when `passRock=true`). Impassable: `Water`, `DeepWater`, `Lava`, `Pit`, `Wall`.

### `src/Miner49er.Core/AI/BotBrain.cs`
```csharp
public sealed class BotBrain
{
    public int MinerId { get; }
    public BotSkill Skill { get; }

    public BotBrain(int minerId, BotSkill skill, int seed);

    // Called once per host tick. Returns the action to inject for this bot.
    public BotAction Think(Simulation sim, GameMode mode);
}
```

Internal state: `_goal` (current target `GridPos?`), `_goalType` (enum), `_ticksUntilReeval` (countdown), `_rng`.

---

## 3. Bot Behaviour Per Skill

### Greenhorn
- Every 30 ticks: pick a random floor tile within 10 tiles as new goal
- Navigate toward goal: take first available direction that is floor/passable; if blocked, pick a random direction
- If facing a rock and `_rng.NextDouble() < 0.4`: set `Mine = true`
- No mode awareness

### Miner
- Every 15 ticks: find the nearest GoldRock tile (Manhattan distance); fall back to random floor tile if none
- Pathfind with `passRock=false` (navigates around walls)
- Set `Mine=true` when facing a rock that is the goal or directly adjacent to path
- Mode overrides (checked before gold goal):
  - **Reach Center**: goal = map center tile
  - **Treasure Hunt**: if any assigned idol is on the floor (loose, not buried), goal = that idol's pos

### Foreman
- Every 7 ticks: find nearest GoldRock by BFS distance (`passRock=true`)
- Pathfind with `passRock=true`; set `Mine=true` whenever the next-step tile is Rock or GoldRock
- Item use: if holding SpeedPotion/LongerVision → `Use=true` immediately
- Mode overrides:
  - **Reach Center**: goal = map center
  - **Expedition**: goal = escape tile if `EscapeOpen`, else nearest gold
  - **Treasure Hunt**: use Listen if idol positions unknown; goal = nearest assigned idol (buried or floor)

### Dynamite Dan
- Every 3 ticks: same gold-seeking as Foreman
- Charge planting: if carrying a charge and ≥3 adjacent tiles are GoldRock → `Plant=true`
- Listen: if in Treasure Hunt and assigned idols not yet found → `Use=true` (Listen)
- Stone throwing: if a rival miner is within Chebyshev distance 2 and bot has stones → `Throw=true`
- Last Man Standing: once a charge is placed, target nearest living rival miner

---

## 4. Modified Files

### `game/net/MatchHost.cs`

Add:
```csharp
private readonly Dictionary<int, BotBrain> _botBrains = new();
```

`Begin()` gains a `List<(int minerId, BotSkill skill)> bots` parameter. For each bot: construct a `BotBrain` and store it; add the miner ID to `_pendingDir` (initialised to -1) but NOT to `_peerToMiner`.

In `StepOnce()`, before the existing `foreach (_pendingDir)` loop:
```csharp
foreach (var (minerId, brain) in _botBrains)
{
    var action = brain.Think(_sim, nm.MatchMode);
    _pendingDir[minerId] = action.Dir;
    if (action.Mine)  _pendingMine.Add(minerId);
    if (action.Plant) _pendingPlant.Add(minerId);
    if (action.Use)   _pendingUse.Add(minerId);
    if (action.Throw) _pendingThrow.Add(minerId);
}
```

### `game/net/NetworkManager.cs`

Add bot peer IDs to `Players` dict during `StartMatch`. Bot entries use negative fake peer IDs.

Add:
```csharp
public IReadOnlyList<long> BotPeerIds { get; private set; } = Array.Empty<long>();
```

`StartMatch` gains a `List<(BotSkill skill, string name, int colorIndex)>? bots = null` parameter. Host assigns fake peer IDs (-1001, -1002…), adds entries to `Players` (always `Ready=true`), passes miner IDs + skills to `MatchHost.Begin()`.

`BroadcastResult` already uses `_peerToMiner` to find the winner peer. Bot miner IDs are not in `_peerToMiner`, so the host must map bot miner ID → fake peer ID via a new `_botMinerToPeer: Dictionary<int, long>`. When a bot wins, `BroadcastResult(fakePeerId)` is called; clients already have the bot's fake peer ID in their `Players` dict (synced during lobby — see below), so the result screen shows the correct name.

**Client sync:** When the host adds or removes a bot, it re-broadcasts the full player list to all clients (same mechanism used when real players join/ready). This ensures every client's `Players` dict contains bot entries with their fake peer IDs and display names before the match starts.

### `game/ui/Lobby.cs`

Add to host-only controls section:

- `_addBotBtn` — `Button { Text = "+ Add Bot" }`; clicking shows a small `PopupMenu` with the four skill names
- On skill selected: call `NetworkManager.Instance.AddBot(skill)` (new method); bot appears in player list immediately
- Bot entries in player list: `"Dusty (Greenhorn)  [READY]  [✕]"`; the ✕ button calls `NetworkManager.Instance.RemoveBot(fakePeerId)`
- `_addBotBtn.Disabled = Players.Count >= 8`
- Bots count toward the ≥2 start condition (already satisfied since `Players` contains them)

---

## 5. Bot Name Assignment

```csharp
private static readonly string[] BotNamePool =
    { "Dusty", "Rocky", "Gravel", "Nugget", "Pickaxe", "Blaster", "Shafty", "Copperhead" };
```

Assign names sequentially from the pool; if all 8 are used (impossible with ≤8 total), append index. Duplicate names (if pool exhausted) get " 2", " 3" suffix.

---

## 6. Tests

**`src/Miner49er.Core.Tests/AI/BotPathfinderTests.cs`**

- `NextDir_reaches_adjacent_target` — 3×3 grid, bot at centre, target N → returns 0 (North)
- `NextDir_returns_minus1_when_already_there`
- `NextDir_navigates_around_wall_passRock_false` — wall between bot and target, BFS finds detour
- `NextDir_drills_through_rock_passRock_true` — rock between bot and target → returns direction into rock
- `Nearest_returns_closest_candidate`
- `Nearest_returns_null_when_no_candidates`

**`src/Miner49er.Core.Tests/AI/BotBrainTests.cs`**

- `Greenhorn_returns_action_every_tick` — just asserts no exception + valid Dir range (-1..3)
- `Miner_heads_toward_gold_rock` — gold tile directly north, bot south of it → Dir = North after Think()
- `Miner_mines_when_facing_gold` — bot adjacent to gold, goal = gold → Mine = true
- `Foreman_uses_item_when_holding_speed_potion` — bot holding SpeedPotion → Use = true
- `DynamiteDan_plants_on_gold_cluster` — bot holding charge, 3 adjacent gold tiles → Plant = true

---

## 7. Out of Scope

- Bots do not buy from the shop
- Bots do not communicate or cooperate in Expedition co-op (each acts independently)
- Bot difficulty does not adapt at runtime (no dynamic difficulty)
- Bots are not saved/restored across sessions
- No bot-vs-bot only matches without a human host (host must be a real player)
- Fix for `SettingsStore.LoadLobby()` TreasureHunt mode clamp bug — separate issue
