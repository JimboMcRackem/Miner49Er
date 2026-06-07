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
        var region = LargestTraversableRegion(grid);
        var spawns = PlaceSpawns(grid, rng, config.PlayerCount, config.MinSpawnDistance, region);
        var center = NearestFloorToCenter(grid, region);
        PlaceGold(grid, rng, config.GoldVeinCount, region);

        return new GeneratedMap { Grid = grid, Spawns = spawns, Center = center };
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

    private static bool IsWater(TileType t) => t is TileType.ShallowWater or TileType.DeepWater;
    private static bool IsTraversable(TileType t) => t is TileType.Floor or TileType.ShallowWater;

    private static void PlaceWater(TileGrid g, Random rng, MapConfig cfg)
    {
        for (int i = 0; i < cfg.PoolCount; i++) CarvePool(g, rng, cfg);
        for (int i = 0; i < cfg.RiverCount; i++) CarveRiver(g, rng, cfg);
        PromoteDeep(g, rng, cfg);
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
                if (!g.InBounds(n) || !IsWater(g.Get(n))) { allWater = false; break; }
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
            if (g.InBounds(n) && IsWater(g.Get(n))) return true;
        }
        return false;
    }

    private static List<GridPos> PlaceSpawns(TileGrid g, Random rng, int count, int minDistance, HashSet<GridPos> region)
    {
        var floors = region.Where(p => g.Get(p) == TileType.Floor && !IsWaterAdjacent(g, p)).ToList();
        if (floors.Count < count) // fallback: relax the water-adjacency rule if too few
            floors = region.Where(p => g.Get(p) == TileType.Floor).ToList();
        Shuffle(floors, rng);
        var spawns = new List<GridPos>();
        int distance = minDistance;
        while (spawns.Count < count && distance >= 0)
        {
            spawns.Clear();
            foreach (var p in floors)
            {
                if (spawns.All(s => s.ManhattanTo(p) >= distance))
                    spawns.Add(p);
                if (spawns.Count == count) break;
            }
            if (spawns.Count < count) distance--;
        }
        return spawns;
    }

    private static GridPos NearestFloorToCenter(TileGrid g, HashSet<GridPos> region)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        return region.Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .First();
    }

    private static void PlaceGold(TileGrid g, Random rng, int veins, HashSet<GridPos> region)
    {
        var candidates = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasRegionNeighbor(g, p, region))
            .ToList();
        Shuffle(candidates, rng);
        foreach (var p in candidates.Take(veins)) g.Set(p, TileType.GoldRock);
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
}
