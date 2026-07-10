using System.Collections.Generic;

namespace Miner49er.Core.AI;

public static class BotPathfinder
{
    private static readonly Direction[] Dirs =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    /// <summary>Returns the Direction int (0=N,1=E,2=S,3=W) of the first step from
    /// <paramref name="from"/> toward <paramref name="to"/> via BFS, or -1 if already
    /// there or unreachable. passRock=true treats Rock and GoldRock as walkable
    /// (bot plans to mine through them). avoidHazards=true makes scree, crumbling
    /// floors, and rock adjacent to a lava vent impassable so the bot routes around them.</summary>
    public static int NextDir(TileGrid grid, GridPos from, GridPos to, bool passRock, bool avoidHazards = false)
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
            if (!Passable(grid, nb, passRock, avoidHazards)) continue;
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
                if (!Passable(grid, nb, passRock, avoidHazards)) continue;
                if (visited.Add(nb)) queue.Enqueue((nb, firstDir));
            }
        }

        return -1;
    }

    /// <summary>Returns the nearest reachable candidate GridPos (adjacent to a walkable
    /// tile), or null if candidates is empty or none are reachable.</summary>
    public static GridPos? Nearest(TileGrid grid, GridPos from,
        IEnumerable<GridPos> candidates, bool passRock, bool avoidHazards = false)
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
                if (!Passable(grid, nb, passRock, avoidHazards)) continue;
                if (visited.Add(nb)) queue.Enqueue(nb);
            }
        }

        return null;
    }

    // Bots avoid lethal tiles (DeepWater, Pit, Lava) — use IsWalkable, not IsEnterable.
    // With avoidHazards, also refuse scree, crumbling floors, and rock that borders a vent.
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
        if (t.IsScree()) return true;                                  // mining it triggers a collapse
        if (t is TileType.Cracked or TileType.Crumbling) return true;  // collapses underfoot
        if (t.IsMinable() && AdjacentToVent(grid, p)) return true;     // mining it breaches a vent
        return false;
    }

    private static bool AdjacentToVent(TileGrid grid, GridPos p)
    {
        foreach (var d in Dirs)
        {
            var off = d.ToOffset();
            var nb  = new GridPos(p.X + off.X, p.Y + off.Y);
            if (grid.InBounds(nb) && grid.Get(nb) == TileType.LavaVent) return true;
        }
        return false;
    }
}
