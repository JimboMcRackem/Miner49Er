# Glowing Crystals Implementation Plan

## Goal

Add `CrystalRock` wall tiles that glow, illuminate surrounding tunnels via real fog reveal, and drop a portable `CrystalShard` item when mined.

## Design Decisions

| Question | Decision |
|---|---|
| Fog interaction | Real fog reveal (static lantern behaviour) |
| Physical location | Embedded in rock walls (`CrystalRock` tile type) |
| Light radius | 5 tiles (same as miner default vision) |
| Patch density | Sporadic clusters — ~60% of map regions get one, feels geological |
| Shard pickup | Loose floor item; walk over to collect (standard) |
| Shard held radius | 3 tiles |
| Shard floor radius | 2 tiles |

---

## Architecture

### 1. `TileType.CrystalRock` (`src/Miner49er.Core/Grid/TileType.cs`)

New enum value added to `TileType`. Extension updates:

- `IsMinable()` → `true`
- `IsBlastable()` → `true`
- `BlocksSight()` → `true`
- All other extensions unchanged (not walkable, not water, not lethal)

When mined or blasted, the simulation converts the tile to `Floor` (existing `RockMined` / blast path) **and** emits a `CrystalShardDropped` sim event so the host spawns a `CrystalShard` item at that position.

### 2. `ItemKind.CrystalShard` (`src/Miner49er.Core/Map/Item.cs`)

New enum value. Behaviour:

- **Loose on floor**: treated as an extra light source (2-tile radius) in fog computation; glows visually
- **Held by miner**: treated as an extra light source (3-tile radius) centred on the carrier; soft cyan halo drawn around carrier sprite
- No expiry; can be dropped like any carried item
- Not sold in shop; not a quest item

### 3. Map Generation (`src/Miner49er.Core/Map/MapGenerator.cs`)

New method `PlaceCrystalPatches(TileGrid grid, Random rng, int patchCount)` called in `Generate()` after `KeepLargestRegion()`, before spawn placement.

**Algorithm:**
1. Divide the map into a 4×4 grid of rectangular regions
2. For each region, roll RNG — ~60% chance the region gets a patch (gives geological sparsity)
3. In each chosen region, pick a random `Rock` tile that has at least one `Floor` cardinal neighbour (ensures the crystal is on a reachable wall face)
4. BFS-grow from that seed: spread to adjacent `Rock` tiles that also border `Floor`, stopping when the patch reaches 3–5 tiles (size seeded per patch via RNG)
5. Convert all patch tiles to `CrystalRock`

**`MapConfig`** gets one new field: `int CrystalPatchCount` (default `0`). Floor configs that should have crystals set this to a positive value (e.g., `4`). The 4×4 region grid scales to the actual map dimensions, so patch count stays proportional.

### 4. Fog Computation (`game/net/MatchClient.cs`)

Two new cached sets maintained on the client:

- **`_crystalPositions: HashSet<GridPos>`** — populated on map load by scanning `Grid` for `CrystalRock`. Updated when a `TileChange` converts `CrystalRock` → `Floor` (remove that position).
- **`_shardFloorPositions: HashSet<GridPos>`** — populated from loose `CrystalShard` items in the current `WorldSnapshot`. Rebuilt each tick from snapshot (items list is already diffed).

Fog computation (currently line ~543) becomes:

```csharp
const int CrystalWallRadius = 5;
const int CrystalShardRadius = 2;
const int CrystalHeldRadius  = 3;

var visible = Visibility.Compute(Grid, minerPos, m.VisionRadius);

foreach (var cp in _crystalPositions)
    visible.UnionWith(Visibility.Compute(Grid, cp, CrystalWallRadius));

foreach (var sp in _shardFloorPositions)
    visible.UnionWith(Visibility.Compute(Grid, sp, CrystalShardRadius));

// Held shard: find miner carrying CrystalShard in snapshot
foreach (var miner in Miners)
    if (miner.Held == (int)ItemKind.CrystalShard)
        visible.UnionWith(Visibility.Compute(Grid, new GridPos(miner.X, miner.Y), CrystalHeldRadius));

Fog.Update(visible);
```

`FogRenderer` needs no changes — crystal-lit tiles fall into the existing `IsVisible` path and render fully bright. Visual glow comes from `WorldRenderer`.

Water polygon cache (`_waterTiles` / `_waterPolys`) is unaffected — crystal walls are never water tiles.

### 5. Rendering (`game/WorldRenderer.cs`)

**`CrystalRock` wall tiles** (tile overlay pass, alongside `GoldRock` / `LavaVent`):

- Sprite: `res://assets/tiles/singletiles/crystal_rock.png` drawn over the base rock Wang tile
- Procedural fallback (if sprite absent): 3–5 thin elongated `DrawPolygon` facets per tile at seeded angles, colours cycling through `#a060ff` / `#60c0ff` / `#c080ff`
- Glow: radial gradient texture (cyan-tinted, reusing `BuildRadialGlowTex()` colour param) at 2.5× tile size, ~40% alpha, centred on tile
- Pulse: glow alpha modulated at 0.6 Hz via `Mathf.Sin(wTime * Mathf.Pi * 2f / 1.65f)`

**`CrystalShard` floor item** (items draw pass):

- Sprite: `res://assets/objects/item_crystal_shard.png`
- Fallback: small 4-point cyan diamond `DrawPolygon` + `DrawCircle` glow
- Ambient glow: small white-cyan radial gradient beneath the sprite, 30% alpha

**`CrystalShard` held** (miner draw pass, same point as stun stars):

- Soft cyan `DrawCircle` halo at carrier's visual position, radius `ts * 0.55f`, alpha ~25%

### 6. Network

No protocol changes. The existing `TileChange` packet (already used for rock mining, lava, cave-ins) propagates `CrystalRock → Floor` to all clients, which drop that position from `_crystalPositions` and rebuild fog next tick. `CrystalShard` items flow through the existing `ItemSnapshot` / `WorldSnapshot` path.

The `CrystalShardDropped` sim event is consumed by `MatchHost` to spawn the item, identical to how other item-spawn events are handled.

---

## Sprites Required (PixelLab)

| File | Description |
|---|---|
| `assets/tiles/singletiles/crystal_rock.png` | 32×32 rock wall with embedded glowing crystal veins, top-down view, purple/cyan hues |
| `assets/objects/item_crystal_shard.png` | 32×32 loose glowing crystal fragment on floor, bright cyan/violet facets |

---

## Testing

- **`TileTypeTests`**: `CrystalRock.IsMinable()` is true; `CrystalRock.BlocksSight()` is true; `CrystalRock.IsWalkable()` is false
- **`MapGeneratorCrystalTests`**: all `CrystalRock` tiles border at least one `Floor` tile; crystal count per floor is within expected range; no crystals placed when `CrystalPatchCount = 0`
- **`FogCrystalTests`**: tiles within radius 5 of a `CrystalRock` are visible; tiles beyond radius 5 are not; mining the crystal (tile → Floor) removes those tiles from visibility on next compute
