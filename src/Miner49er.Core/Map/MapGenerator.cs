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
        var spawns = PlaceSpawns(grid, rng, config.PlayerCount, config.MinSpawnDistance);
        var center = NearestFloorToCenter(grid);
        PlaceGold(grid, rng, config.GoldVeinCount);

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

    private static List<GridPos> PlaceSpawns(TileGrid g, Random rng, int count, int minDistance)
    {
        var floors = g.Positions().Where(p => g.Get(p) == TileType.Floor).ToList();
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

    private static GridPos NearestFloorToCenter(TileGrid g)
    {
        var c = new GridPos(g.Width / 2, g.Height / 2);
        return g.Positions()
            .Where(p => g.Get(p) == TileType.Floor)
            .OrderBy(p => p.ManhattanTo(c))
            .First();
    }

    private static void PlaceGold(TileGrid g, Random rng, int veins)
    {
        var candidates = g.Positions()
            .Where(p => g.Get(p) == TileType.Rock && HasFloorNeighbor(g, p))
            .ToList();
        Shuffle(candidates, rng);
        foreach (var p in candidates.Take(veins)) g.Set(p, TileType.GoldRock);
    }

    private static bool HasFloorNeighbor(TileGrid g, GridPos p)
    {
        foreach (var d in Card)
        {
            var n = p + d.ToOffset();
            if (g.InBounds(n) && g.Get(n) == TileType.Floor) return true;
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
