namespace Miner49er.Core;

public static class MapGenerator
{
    private static readonly Direction[] Card =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    public static GeneratedMap Generate(MapConfig config)
    {
        var rng = new Random(config.Seed);
        int width = config.BaseWidth + config.SizePerPlayer * (config.PlayerCount - 1);
        int height = config.BaseHeight + config.SizePerPlayer * (config.PlayerCount - 1);

        var grid = new TileGrid(width, height, TileType.Rock);
        RandomFill(grid, rng, config.InitialFloorChance);
        for (int i = 0; i < config.SmoothingSteps; i++) Smooth(grid);

        KeepLargestRegion(grid);
        PlaceWater(grid, rng, config);
        if (config.Pits)
            PlacePits(grid, rng, config.PitSiteCount + (config.PlayerCount - 1),
                      config.PitClusterChance, config.PitClusterMax);
        if (config.Lava)
            PlaceLava(grid, rng, config.LavaPoolCount, config.LavaPoolGrowChance, config.LavaPoolMax);
        var region = LargestTraversableRegion(grid);
        var spawns = PlaceSpawns(grid, config.PlayerCount, region);
        var center = NearestFloorToCenter(grid, region);
        PlaceGold(grid, rng, config.GoldVeinCount, region);
        int total = config.BaseItemCount + config.ItemsPerPlayer * (config.PlayerCount - 1);
        var items = PlaceItems(grid, rng, total, config.VisibleItemCount, region, spawns);
        items.AddRange(PlaceChests(grid, rng, config.ChestCount, region, spawns, items));
        items.AddRange(PlaceCarriedItems(grid, rng, config.WaterPlankCount, config.SlowMoldCount, config.LanternCount, region, spawns, items));
        var decoys = PlaceDecoys(grid, rng, config.DecoyCount, region, items);
        if (config.CaveIns)
            PlaceCracks(grid, rng, config.CrackSiteCount + (config.PlayerCount - 1),
                        config.CrackPatchGrowChance, config.CrackPatchMax,
                        region, spawns, center, items);
        if (config.Lava)
            PlaceLavaVents(grid, rng, config.LavaVentCount + (config.PlayerCount - 1), region, items, decoys);
        return new GeneratedMap
        {
            Grid = grid, Spawns = spawns, Center = center, Items = items, Decoys = decoys,
            EscapeTile = spawns.Count > 0 ? spawns[0] : null,
        };
    }

    private static bool IsBorder(TileGrid g, GridPos p) =>
        p.X == 0 || p.Y == 0 || p.X == g.Width - 1 || p.Y == g.Height - 1;

    private static void RandomFill(TileGrid g, Random rng, float floorChance)
    {
        foreach (var p in g.Positions())
        {
            if (IsBorder(g, p)) { g.Set(p, TileType.ImpermeableRock); continue; }
            g.Set(p, rng.NextDouble() < floorChance ? TileType.Floor : TileType.Rock);
        }
    }

    private static void Smooth(TileGrid g)
    {
        var next = new TileType[g.Width * g.Height];
        foreach (var p in g.Positions())
        {
            if (IsBorder(g, p)) { next[p.Y * g.Width + p.X] = TileType.ImpermeableRock; continue; }
            int rockNeighbors = CountRockNeighbors(g, p);
            TileType result = rockNeighbors > 4 ? TileType.Rock
                            : rockNeighbors < 4 ? TileType.Floor
                            : g.Get(p);
            next[p.Y * g.Width + p.X] = result;
        }
        foreach (var p in g.Positions()) g.Set(p, next[p.Y * g.Width + p.X]);
    }

    private static int CountRockNeighbors(TileGrid g, GridPos p)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = new GridPos(p.X + dx, p.Y + dy);
                if (!g.InBounds(n) || g.Get(n) != TileType.Floor) count++;
            }
        return count;
    }

    private static void KeepLargestRegion(TileGrid g)
    {
        var visited = new HashSet<GridPos>();
        List<GridPos> largest = new();
        foreach (var p in g.Positions())
        {
            if (g.Get(p) != TileType.Floor || visited.Contains(p)) continue;
            var region = Flood(g, p, visited);
            if (region.Count > largest.Count) largest = region;
        }
        var keep = new HashSet<GridPos>(largest);
        foreach (var p in g.Positions())
            if (g.Get(p) == TileType.Floor && !keep.Contains(p))
                g.Set(p, TileType.Rock);
    }

    private static List<GridPos> Flood(TileGrid g, GridPos start, HashSet<GridPos> visited)
    {
        var region = new List<GridPos>();
        var stack = new Stack<GridPos>();
        stack.Push(start); visited.Add(start);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            region.Add(p);
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (g.InBounds(n) && g.Get(n) == TileType.Floor && visited.Add(n))
                    stack.Push(n);
            }
        }
        return region;
    }

    private static bool IsTraversable(TileType t) => t is TileType.Floor or TileType.ShallowWater;

    private static void PlaceWater(TileGrid g, Random rng, MapConfig cfg)
    {
        for (int i = 0; i < cfg.PoolCount; i++) CarvePool(g, rng, cfg);
        for (int i = 0; i < cfg.RiverCount; i++) CarveRiver(g, rng, cfg);
        PromoteDeep(g, rng, cfg);
    }

    // Carves scattered bottomless pits over Floor: mostly single tiles, with an
    // occasional small cluster. Pits are not traversable, so the region recomputed
    // after this pass excludes them and every later placement pass avoids them.
    // Reachability is preserved structurally — the chosen region is always the
    // single largest connected traversable component.
    private static void PlacePits(TileGrid g, Random rng, int siteCount,
                                  double clusterChance, int clusterMax)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
        Shuffle(floors, rng);
        int placed = 0;
        foreach (var seed in floors)
        {
            if (placed >= siteCount) break;
            if (g.Get(seed) != TileType.Floor) continue;   // consumed by a prior cluster
            g.Set(seed, TileType.Pit);
            if (rng.NextDouble() < clusterChance)
                GrowPit(g, rng, seed, rng.Next(2, clusterMax + 1));
            placed++;
        }
    }

    // Grows a pit cluster to `size` total tiles by random flood over adjacent Floor.
    private static void GrowPit(TileGrid g, Random rng, GridPos seed, int size)
    {
        var frontier = new List<GridPos> { seed };
        int count = 1;                                       // seed tile already a pit
        while (count < size && frontier.Count > 0)
        {
            int fromIdx = rng.Next(frontier.Count);
            var from = frontier[fromIdx];
            var nbrs = Card.Select(d => from + d.ToOffset())
                           .Where(n => g.InBounds(n) && g.Get(n) == TileType.Floor)
                           .ToList();
            if (nbrs.Count == 0) { frontier.RemoveAt(fromIdx); continue; }   // drop this exact entry, not a value-search
            var n = nbrs[rng.Next(nbrs.Count)];
            g.Set(n, TileType.Pit);
            frontier.Add(n);
            count++;
        }
    }

    // Carves static lava pools/lines over Floor. Like pits, lava is not traversable, so this
    // runs BEFORE the region recompute and reachability is preserved structurally (the kept
    // region routes around it). Skips any tile adjacent to water so gen-time lava never sits
    // on a quench boundary — quench is a runtime interaction.
    private static void PlaceLava(TileGrid g, Random rng, int siteCount,
                                  double growChance, int poolMax)
    {
        var floors = g.Positions()
            .Where(p => g.Get(p) == TileType.Floor && !IsWaterAdjacent(g, p))
            .ToList();
        Shuffle(floors, rng);
        int placed = 0;
        foreach (var seed in floors)
        {
            if (placed >= siteCount) break;
            if (g.Get(seed) != TileType.Floor || IsWaterAdjacent(g, seed)) continue;   // consumed/now-bordering
            g.Set(seed, TileType.Lava);
            if (rng.NextDouble() < growChance)
                GrowLava(g, rng, seed, rng.Next(2, poolMax + 1));
            placed++;
        }
    }

    // Grows a lava pool to `size` tiles by random flood over adjacent Floor that is not
    // water-adjacent, so a static pool never touches water.
    private static void GrowLava(TileGrid g, Random rng, GridPos seed, int size)
    {
        var frontier = new List<GridPos> { seed };
        int count = 1;
        while (count < size && frontier.Count > 0)
        {
            int fromIdx = rng.Next(frontier.Count);
            var from = frontier[fromIdx];
            var nbrs = Card.Select(d => from + d.ToOffset())
                           .Where(n => g.InBounds(n) && g.Get(n) == TileType.Floor && !IsWaterAdjacent(g, n))
                           .ToList();
            if (nbrs.Count == 0) { frontier.RemoveAt(fromIdx); continue; }
            var n = nbrs[rng.Next(nbrs.Count)];
            g.Set(n, TileType.Lava);
            frontier.Add(n);
            count++;
        }
    }

    // Buries dormant lava vents in rim rock (plain Rock bordering the play region), so breaching
    // the edge by mining/blasting releases them. Vents sit in rock and never affect the traversable
    // region. Skips tiles holding a buried item or a decoy so nothing is orphaned. Runs after the
    // objective passes.
    private static void PlaceLavaVents(TileGrid g, Random rng, int count,
                                       HashSet<GridPos> region, List<Item> items, List<GridPos> decoys)
    {
        var blocked = new HashSet<GridPos>(items.Select(it => it.Pos));
        foreach (var d in decoys) blocked.Add(d);
        var cands = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && !blocked.Contains(p) && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(cands, rng);
        foreach (var p in cands.Take(Math.Min(count, cands.Count)))
            g.Set(p, TileType.LavaVent);
    }

    // Carves cracked-floor "areas" over Floor for the cave-in hazard. Runs AFTER the
    // spawn/center/gold/item passes (cracks are walkable, so they don't change
    // reachability), and explicitly skips those objective tiles. Each site flood-grows
    // a small blob, biased toward multi-tile patches since these are deliberately areas.
    private static void PlaceCracks(TileGrid g, Random rng, int siteCount,
                                    double growChance, int patchMax,
                                    HashSet<GridPos> region, List<GridPos> spawns,
                                    GridPos center, List<Item> items)
    {
        var blocked = new HashSet<GridPos>(spawns) { center };
        foreach (var it in items) blocked.Add(it.Pos);

        var floors = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !blocked.Contains(p))
            .ToList();
        Shuffle(floors, rng);

        int placed = 0;
        foreach (var seed in floors)
        {
            if (placed >= siteCount) break;
            if (g.Get(seed) != TileType.Floor) continue;   // consumed by a prior patch
            g.Set(seed, TileType.Cracked);
            if (rng.NextDouble() < growChance)
                GrowCrack(g, rng, seed, rng.Next(2, patchMax + 1), blocked);
            placed++;
        }
    }

    // Grows a crack patch to `size` total tiles by random flood over adjacent Floor,
    // never overrunning a blocked (spawn/center/item) tile.
    private static void GrowCrack(TileGrid g, Random rng, GridPos seed, int size, HashSet<GridPos> blocked)
    {
        var frontier = new List<GridPos> { seed };
        int count = 1;
        while (count < size && frontier.Count > 0)
        {
            int fromIdx = rng.Next(frontier.Count);
            var from = frontier[fromIdx];
            var nbrs = Card.Select(d => from + d.ToOffset())
                           .Where(n => g.InBounds(n) && g.Get(n) == TileType.Floor && !blocked.Contains(n))
                           .ToList();
            if (nbrs.Count == 0) { frontier.RemoveAt(fromIdx); continue; }
            var n = nbrs[rng.Next(nbrs.Count)];
            g.Set(n, TileType.Cracked);
            frontier.Add(n);
            count++;
        }
    }

    private static GridPos? RandomFloor(TileGrid g, Random rng)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
        return floors.Count == 0 ? null : floors[rng.Next(floors.Count)];
    }

    private static void CarvePool(TileGrid g, Random rng, MapConfig cfg)
    {
        var c = RandomFloor(g, rng);
        if (c is null) return;
        int r = rng.Next(cfg.PoolRadiusMin, cfg.PoolRadiusMax + 1);
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > r) continue;
                var p = new GridPos(c.Value.X + dx, c.Value.Y + dy);
                if (g.InBounds(p) && g.Get(p) == TileType.Floor)
                    g.Set(p, TileType.ShallowWater);
            }
    }

    private static void CarveRiver(TileGrid g, Random rng, MapConfig cfg)
    {
        var start = RandomFloor(g, rng);
        if (start is null) return;
        var pos = start.Value;
        int len = rng.Next(cfg.RiverLengthMin, cfg.RiverLengthMax + 1);
        var dir = Card[rng.Next(Card.Length)];
        for (int i = 0; i < len; i++)
        {
            if (g.InBounds(pos) && g.Get(pos) == TileType.Floor)
                g.Set(pos, TileType.ShallowWater);
            if (rng.NextDouble() < 0.3) dir = Card[rng.Next(Card.Length)];
            var next = pos + dir.ToOffset();
            if (!g.InBounds(next) || g.Get(next) == TileType.ImpermeableRock)
                dir = Card[rng.Next(Card.Length)];
            else
                pos = next;
        }
    }

    private static void PromoteDeep(TileGrid g, Random rng, MapConfig cfg)
    {
        // Decide on the pre-promotion grid so order is irrelevant: an interior
        // shallow tile (all 4 neighbours water) may become deep. Boundary water
        // stays shallow, guaranteeing every deep tile is ringed by water.
        var interior = new List<GridPos>();
        foreach (var p in g.Positions())
        {
            if (g.Get(p) != TileType.ShallowWater) continue;
            bool allWater = true;
            foreach (var d in Card)
            {
                var n = p + d.ToOffset();
                if (!g.InBounds(n) || !g.Get(n).IsWater()) { allWater = false; break; }
            }
            if (allWater) interior.Add(p);
        }
        foreach (var p in interior)
            if (rng.NextDouble() < cfg.DeepWaterChance)
                g.Set(p, TileType.DeepWater);
    }

    private static HashSet<GridPos> LargestTraversableRegion(TileGrid g)
    {
        var visited = new HashSet<GridPos>();
        HashSet<GridPos> largest = new();
        foreach (var p in g.Positions())
        {
            if (!IsTraversable(g.Get(p)) || visited.Contains(p)) continue;
            var region = new HashSet<GridPos>();
            var stack = new Stack<GridPos>();
            stack.Push(p); visited.Add(p); region.Add(p);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var d in Card)
                {
                    var n = cur + d.ToOffset();
                    if (g.InBounds(n) && IsTraversable(g.Get(n)) && visited.Add(n))
                    {
                        region.Add(n);
                        stack.Push(n);
                    }
                }
            }
            if (region.Count > largest.Count) largest = region;
        }
        return largest;
    }

    private static bool IsWaterAdjacent(TileGrid g, GridPos p)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (g.InBounds(n) && g.Get(n).IsWater()) return true;
        }
        return false;
    }

    // Spawns land as far apart as the map allows: the first at an extreme corner, each
    // next maximising its minimum distance to those already chosen, so two players sit
    // diagonally opposite, three/four spread to thirds/quarters. Candidates are scanned in
    // deterministic grid order (SelectFarthest is order-stable) so host and clients agree —
    // no rng needed. Prefers floor away from water, relaxing that only if too few remain.
    private static List<GridPos> PlaceSpawns(TileGrid g, int count, HashSet<GridPos> region)
    {
        var floors = region.Where(p => g.Get(p) == TileType.Floor && !IsWaterAdjacent(g, p))
            .OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (floors.Count < count) // relax water-adjacency rule if too few
            floors = region.Where(p => g.Get(p) == TileType.Floor)
                .OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (floors.Count == 0)   // degenerate: all traversable tiles are water; use any traversable tile
            floors = region.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        return SpawnPlacement.SelectFarthest(floors, count);
    }

    private static GridPos NearestFloorToCenter(TileGrid g, HashSet<GridPos> region)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        var nearest = region.Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .Cast<GridPos?>()
            .FirstOrDefault();
        // A traversable region always contains floor in practice; fall back to the
        // raw geometric centre rather than throwing if a degenerate map has none.
        return nearest ?? c;
    }

    private static void PlaceGold(TileGrid g, Random rng, int veins, HashSet<GridPos> region)
    {
        var candidates = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(candidates, rng);
        foreach (var p in candidates.Take(veins)) g.Set(p, TileType.GoldRock);
    }

    // Items come in two flavors. A few sit visibly in toolboxes on Floor tiles; the rest are buried
    // in ordinary Rock and only surface when that rock is destroyed. Both passes draw candidates in
    // deterministic grid order then seed-shuffle, so host and every client agree. Kinds cycle
    // round-robin over the COMBINED ordered list (toolboxes first, then buried) for a balanced spread.
    private static List<Item> PlaceItems(TileGrid g, Random rng, int total, int visibleWanted,
        HashSet<GridPos> region, List<GridPos> spawns)
    {
        var spawnSet = new HashSet<GridPos>(spawns);

        // Visible (toolbox) candidates: Floor in the traversable region, never a spawn tile.
        var floorCands = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor && !spawnSet.Contains(p))
            .ToList();
        Shuffle(floorCands, rng);

        // Buried candidates: ordinary Rock (never GoldRock / ImpermeableRock) bordering the play
        // area, so every buried item is reachable by mining/blasting the rim.
        var rockCands = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(rockCands, rng);

        int visible = Math.Min(Math.Min(visibleWanted, floorCands.Count), total);
        int buried = Math.Min(total - visible, rockCands.Count);

        var placed = new List<(GridPos Pos, ItemPlacement Placement)>();
        for (int i = 0; i < visible; i++) placed.Add((floorCands[i], ItemPlacement.Toolbox));
        for (int i = 0; i < buried; i++) placed.Add((rockCands[i], ItemPlacement.Buried));

        var kinds = new[] { ItemKind.SpeedPotion, ItemKind.LongerVision, ItemKind.BiggerBlast };
        var items = new List<Item>();
        for (int i = 0; i < placed.Count; i++)
            items.Add(new Item(placed[i].Pos, kinds[i % kinds.Length], placed[i].Placement));
        return items;
    }

    // Carried items (water-plank, slow-mold, lantern) sit visibly on Floor tiles in the traversable
    // region, never on a spawn or a tile already holding an item. Deterministic: ordered grid scan
    // then seed-shuffle, planks first then molds then lanterns, so host and every client agree.
    private static List<Item> PlaceCarriedItems(TileGrid g, Random rng, int plankCount, int moldCount,
        int lanternCount, HashSet<GridPos> region, List<GridPos> spawns, IEnumerable<Item> existing)
    {
        var taken = new HashSet<GridPos>(existing.Select(it => it.Pos));
        var spawnSet = new HashSet<GridPos>(spawns);
        var cands = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor
                        && !spawnSet.Contains(p) && !taken.Contains(p))
            .ToList();
        Shuffle(cands, rng);

        var result = new List<Item>();
        int idx = 0;
        for (int i = 0; i < plankCount && idx < cands.Count; i++, idx++)
            result.Add(new Item(cands[idx], ItemKind.WaterPlank, ItemPlacement.Toolbox));
        for (int i = 0; i < moldCount && idx < cands.Count; i++, idx++)
            result.Add(new Item(cands[idx], ItemKind.SlowMold, ItemPlacement.Toolbox));
        for (int i = 0; i < lanternCount && idx < cands.Count; i++, idx++)
            result.Add(new Item(cands[idx], ItemKind.Lantern, ItemPlacement.Toolbox));
        return result;
    }

    private static List<Item> PlaceChests(TileGrid g, Random rng, int count,
        HashSet<GridPos> region, List<GridPos> spawns, IEnumerable<Item> existing)
    {
        if (count <= 0) return new List<Item>();
        var taken    = new HashSet<GridPos>(existing.Select(it => it.Pos));
        var spawnSet = new HashSet<GridPos>(spawns);
        var cands = g.Positions()
            .Where(p => region.Contains(p) && g.Get(p) == TileType.Floor
                        && !spawnSet.Contains(p) && !taken.Contains(p))
            .ToList();
        Shuffle(cands, rng);
        var result = new List<Item>();
        for (int i = 0; i < count && i < cands.Count; i++)
            result.Add(new Item(cands[i], ItemKind.Chest, ItemPlacement.Toolbox));
        return result;
    }

    // "Suspicious spots" with no item: deterministic rock positions that shimmer under Listen
    // exactly like buried items, so the only way to tell a real cache from a decoy is to dig. Same
    // rim-rock candidate pool as buried items, minus tiles already holding a (buried) item.
    private static List<GridPos> PlaceDecoys(TileGrid g, Random rng, int count,
        HashSet<GridPos> region, IEnumerable<Item> items)
    {
        var taken = new HashSet<GridPos>(items.Select(it => it.Pos));
        var cands = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && !taken.Contains(p) && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(cands, rng);
        return cands.Take(Math.Min(count, cands.Count)).ToList();
    }

    private static bool HasRegionNeighbor(TileGrid g, GridPos p, HashSet<GridPos> region)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (region.Contains(n)) return true;
        }
        return false;
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Produces the fixed-structure boss floor: a 40x40 arena with deep water
    /// filling the interior, cross-shaped shallow-water corridors for navigation, a 5x5
    /// central floor island, and the victory chest one tile south of center.</summary>
    public static GeneratedMap GenerateBossFloor(int seed)
    {
        const int W = 40, H = 40;
        var grid = new TileGrid(W, H);

        // Fill interior with deep water.
        foreach (var p in grid.Positions())
            grid.Set(p, TileType.DeepWater);

        // Border — impermeable rock.
        for (int x = 0; x < W; x++)
        {
            grid.Set(new GridPos(x, 0),     TileType.ImpermeableRock);
            grid.Set(new GridPos(x, H - 1), TileType.ImpermeableRock);
        }
        for (int y = 0; y < H; y++)
        {
            grid.Set(new GridPos(0,     y), TileType.ImpermeableRock);
            grid.Set(new GridPos(W - 1, y), TileType.ImpermeableRock);
        }

        int cx = W / 2, cy = H / 2;

        // Horizontal corridor (2 tiles wide, rows cy and cy+1).
        for (int x = 1; x < W - 1; x++)
        {
            grid.Set(new GridPos(x, cy),     TileType.ShallowWater);
            grid.Set(new GridPos(x, cy + 1), TileType.ShallowWater);
        }

        // Vertical corridor (2 tiles wide, columns cx and cx+1).
        for (int y = 1; y < H - 1; y++)
        {
            grid.Set(new GridPos(cx,     y), TileType.ShallowWater);
            grid.Set(new GridPos(cx + 1, y), TileType.ShallowWater);
        }

        // Central island — 5x5 Floor tiles (Chebyshev distance <= 2 from center).
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                grid.Set(new GridPos(cx + dx, cy + dy), TileType.Floor);

        var center   = new GridPos(cx, cy);
        var chestPos = new GridPos(cx, cy + 1);

        var items = new List<Item>
        {
            new Item(chestPos, ItemKind.BossChest, ItemPlacement.Toolbox),
        };

        // Spawn at the bottom of the south corridor, one row from the border.
        var spawn = new GridPos(cx, H - 2);

        return new GeneratedMap
        {
            Grid       = grid,
            Spawns     = new List<GridPos> { spawn },
            Center     = center,
            Items      = items,
            Decoys     = System.Array.Empty<GridPos>(),
            EscapeTile = new GridPos(cx, 1),   // top of north corridor
        };
    }
}
