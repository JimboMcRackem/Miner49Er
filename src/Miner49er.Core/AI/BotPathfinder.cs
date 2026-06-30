using System.Collections.Generic;

namespace Miner49er.Core.AI;

public static class BotPathfinder
{
    private static readonly Direction[] Dirs =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    /// <summary>Returns the Direction int (0=N,1=E,2=S,3=W) of the first step from
    /// <paramref name="from"/> toward <paramref name="to"/> via BFS, or -1 if already
    /// there or unreachable. passRock=true treats Rock and GoldRock as walkable
    /// (bot plans to mine through them).</summary>
    public static int NextDir(TileGrid grid, GridPos from, GridPos to, bool passRock)
    {
        if (from == to) return -1;

        var visited = new HashSet<GridPos> { from };
        var queue   = new Queue<(GridPos pos, int firstDir)>();

        foreach (var d in Dirs)
        {
            var off = d.ToOffset();
            var nb  = new GridPos(from.X + off.X, from.Y + off.Y);
            if (!grid.InBounds(nb)) continue;
            if (nb == to) return (int)d;                       // adjacent to target
            if (!Passable(grid.Get(nb), passRock)) continue;
            if (visited.Add(nb)) queue.Enqueue((nb, (int)d));
        }

        while (queue.Count > 0)
        {
            var (pos, firstDir) = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var off = d.ToOffset();
                var nb  = new GridPos(pos.X + off.X, pos.Y + off.Y);
                if (!grid.InBounds(nb)) continue;
                if (nb == to) return firstDir;                 // adjacent to target
                if (!Passable(grid.Get(nb), passRock)) continue;
                if (visited.Add(nb)) queue.Enqueue((nb, firstDir));
            }
        }

        return -1;
    }

    /// <summary>Returns the nearest reachable candidate GridPos (adjacent to a walkable
    /// tile), or null if candidates is empty or none are reachable.</summary>
    public static GridPos? Nearest(TileGrid grid, GridPos from,
        IEnumerable<GridPos> candidates, bool passRock)
    {
        var candidateSet = new HashSet<GridPos>(candidates);
        if (candidateSet.Count == 0) return null;

        var visited = new HashSet<GridPos> { from };
        var queue   = new Queue<GridPos>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var off = d.ToOffset();
                var nb  = new GridPos(pos.X + off.X, pos.Y + off.Y);
                if (!grid.InBounds(nb)) continue;
                if (candidateSet.Contains(nb)) return nb;
                if (!Passable(grid.Get(nb), passRock)) continue;
                if (visited.Add(nb)) queue.Enqueue(nb);
            }
        }

        return null;
    }

    // Bots avoid lethal tiles (DeepWater, Pit, Lava) — use IsWalkable, not IsEnterable.
    private static bool Passable(TileType t, bool passRock) =>
        t.IsWalkable() || (passRock && t.IsMinable());
}
