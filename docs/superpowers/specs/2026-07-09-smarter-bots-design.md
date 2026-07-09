# Smarter AI Bots — Design Spec

**Date:** 2026-07-09
**Status:** Approved
**Source:** `ToDo/Task4.txt` item #4 — "AI miners should be slightly smarter. Currently they die a lot even in the higher tiers (Foreman and up). They should be able to listen as well. As well as this, they could go to the mine exit and whistle."

---

## Overview

Three improvements to `BotBrain`, in priority order:

1. **Hazard-aware pathfinding** (real gameplay value) — higher-skill bots stop killing themselves on the hazards that dominate deep floors: scree/rockfall, crumbling floors, and lava vents.
2. **Whistle at the exit** (coordination + audio) — the first capable bot to reach an open exit whistles to rally stragglers and cue the human.
3. **Cosmetic listen pose** (immersion) — bots occasionally strike the listen pose so they look like they're playing properly.

Bots read the full `Simulation` (no fog), so "listening" gives them no information advantage; the intent behind "they should listen" is realised as (a) avoiding the hazards a listening player would spot, and (b) the cosmetic pose.

**Skill gating:** all three behaviours apply to **Miner tier and above**. Greenhorn stays deliberately oblivious.

---

## 1. Hazard-aware pathfinding

### Problem
`BotPathfinder.Passable` currently treats a tile as passable when it `IsWalkable()` or (with `passRock`) `IsMinable()`. Two consequences kill bots on floors 16+:

- **Scree self-collapse:** `passRock` bots (Foreman+) route straight through `ScreeRock`/`UnstableRock`/`VolatileRock` and mine them, triggering the rockslides that crush them.
- **Crumbling floors:** `Cracked`/`Crumbling` are `IsWalkable`, so bots walk onto them and collapse them by crossing or dwelling.
- **Lava vents:** a `passRock` bot mining rock next to a buried `LavaVent` releases lava onto itself.

(Static lava, pits, and deep water are already avoided — they are neither walkable nor minable.)

### Approach — two-pass hazard-aware BFS
Extend `BotPathfinder` with hazard awareness, then have `BotBrain` path hazard-avoiding first and fall back to the current permissive path only if boxed in (so a bot is never permanently stranded by an unavoidable hazard).

**`BotPathfinder` changes** — add an `avoidHazards` parameter to `NextDir` and `Nearest`:

```csharp
public static int NextDir(TileGrid grid, GridPos from, GridPos to, bool passRock, bool avoidHazards = false)
public static GridPos? Nearest(TileGrid grid, GridPos from, IEnumerable<GridPos> candidates, bool passRock, bool avoidHazards = false)
```

`Passable` gains grid+position context so it can test neighbours:

```csharp
private static bool Passable(TileGrid grid, GridPos p, bool passRock, bool avoidHazards)
{
    var t = grid.Get(p);
    bool basePassable = t.IsWalkable() || (passRock && t.IsMinable());
    if (!basePassable) return false;
    if (avoidHazards && IsHazard(grid, p, t)) return false;
    return true;
}

private static bool IsHazard(TileGrid grid, GridPos p, TileType t)
{
    if (t.IsScree()) return true;                       // mining it triggers a collapse
    if (t is TileType.Cracked or TileType.Crumbling) return true;  // collapses underfoot
    if (t.IsMinable() && AdjacentToVent(grid, p)) return true;     // mining it breaches a vent
    return false;
}

private static bool AdjacentToVent(TileGrid grid, GridPos p)
{
    foreach (var d in Dirs)
    {
        var nb = p + d.ToOffset();
        if (grid.InBounds(nb) && grid.Get(nb) == TileType.LavaVent) return true;
    }
    return false;
}
```

**`BotBrain` changes** — hazard-aware for Miner+, with fallback:

```csharp
bool hazardAware = Skill >= BotSkill.Miner;
int dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock, avoidHazards: hazardAware);
if (dir == -1 && hazardAware)
    dir = BotPathfinder.NextDir(sim.Grid, miner.Pos, _goal.Value, passRock, avoidHazards: false);
```

The flee path (`FleeFrom` → `NextDir`) also passes `avoidHazards: hazardAware` so a bot never flees *into* a hazard. Goal-selection `Nearest` calls stay permissive (they only choose a target; the movement pass does the avoiding).

### Notes
- For Miner-tier bots (`passRock == false`) the scree/vent clauses are effectively free — they never route through rock — so this mainly buys them crumbling-floor avoidance. It kicks in fully for the passRock tiers.
- BFS is binary passable/impassable; "avoid unless it's the only way" is expressed by the two-pass fallback rather than weighted costs.

---

## 2. Whistle at the exit

### Behaviour
When the escape is open and a Miner+ bot is standing on the exit tile, it whistles **once per floor**: fires the existing rally logic (`WhistleBots` → `ForceEscape` on every bot) and plays the whistle SFX as a cue to the human.

### `BotAction`
Add a `Whistle` flag:

```csharp
public readonly bool Whistle;
public BotAction(int dir, bool mine = false, bool plant = false, bool use = false,
                 bool throwStone = false, bool whistle = false)
```

### `BotBrain`
A `_hasWhistled` guard, reset whenever the escape is closed (each new floor starts closed):

```csharp
// near the top of Think, after the escape-open snap:
if (mode == GameMode.Expedition && sim.EscapeOpen && sim.EscapeTile is { } escTile
    && Skill >= BotSkill.Miner && miner.Pos == escTile && !_hasWhistled)
{
    _hasWhistled = true;
    return new BotAction(-1, whistle: true);
}
if (!(mode == GameMode.Expedition && sim.EscapeOpen))
    _hasWhistled = false;   // re-arm for the next floor
```

### `MatchHost`
When a bot's action has `Whistle`, run the rally and record the position for a networked SFX cue:

```csharp
if (action.Whistle)
{
    WhistleBots();
    var wp = sim.GetMiner(minerId).Pos;   // the whistling bot's tile
    _botWhistles.Add(new WhistleSnapshot(wp.X, wp.Y));
}
```

The whistle SFX is networked with the same per-tick snapshot-list pattern used for scree collapses:

- New `readonly record struct WhistleSnapshot(int X, int Y)` in `Snapshots.cs`.
- New optional `IReadOnlyList<WhistleSnapshot>? Whistles` field on `WorldSnapshot` (appended last).
- Encoded/decoded in `SnapshotCodec` (append-only, before `TileChanges`, matching the `ScreeCollapses` block).
- `MatchHost` attaches the accumulated whistles to the snapshot each tick (`snapshot with { Whistles = ... }`).

### Client
`MatchClient.ApplyUpdate` fires a `Whistled` event (world position) for each entry; `MatchAudio` plays `SfxLibrary.Whistle` positionally. Mirrors the existing `Exploded`/`ScreeCollapsed` client-event pattern.

---

## 3. Cosmetic listen pose

### Networking (the plumbing)
Listening is client-side and local-only today (`MatchClient` draws the listen sprite only for `LocalMinerId`). To show a *bot* listening, per-miner listening must be networked:

- Add `public bool Listening { get; set; }` to `Miner` (sim). Cosmetic only; set by the host from bot actions, never affects simulation outcome.
- Add `bool Listening = false` to `MinerSnapshot`; `SnapshotFactory` reads `m.Listening`; `SnapshotCodec` writes/reads it alongside the other per-miner fields.
- `WorldRenderer` draws the listen sprite for **any** visible miner whose snapshot has `Listening == true` (today the check is `Listening && m.Id == LocalMinerId`; broaden it to also cover remote miners flagged listening).

### `BotAction` and `BotBrain`
Add a `Listen` flag to `BotAction`. A Miner+ bot, when it is safe and idle (no goal step this tick, no active hazard/flee, escape not open), occasionally enters a short listening state:

- `_listenTicksRemaining` counts down a ~1–1.5 s pose.
- With a small per-reeval probability the bot starts listening; while listening it returns `new BotAction(-1, listen: true)` (stand still, pose on).
- Any real action (move/mine/flee/whistle) cancels listening.

### `MatchHost`
After computing a bot's action, mirror the flag onto the miner so it reaches the snapshot:

```csharp
sim.GetMiner(minerId).Listening = action.Listen;
```

---

## 4. Testing

`BotBrain` and `BotPathfinder` are engine-free with existing xUnit coverage (`BotBrainTests`, `BotPathfinderTests`), so hazard avoidance is developed test-first:

- `NextDir`/`Nearest` with `avoidHazards` route around a scree wall, a crumbling patch, and rock adjacent to a vent.
- A Foreman bot with a scree tile on the direct line to gold takes a step that is not into the scree.
- When a hazard fully boxes the only route, the two-pass fallback still returns a step (no freeze).
- The flee path does not select a hazard tile.
- A Miner+ bot on the open exit tile returns a `Whistle` action exactly once, then not again until the next floor.

The whistle SFX/rally wiring (host + codec) gets a codec round-trip test for `Whistles`; the listen pose and audio are rendering/host glue verified by play-test.

---

## 5. Out of scope

- No pathfinding rewrite (BFS stays; no weighted costs).
- No change to Greenhorn — it remains oblivious by design.
- Lava *spread* prediction (creeping lava) — static lava and vents are handled; chasing the spreading front is left for later.
- Unifying the human's local listen state with the new networked flag — the human's listen stays client-side; only bots use the networked flag for now.
