using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

/// <summary>Chooses monster spawn tiles for an Expedition: floor cells placed as far as
/// possible from the start (and from each other) by farthest-first dispersion seeded with
/// the start tile, then assigns kinds round-robin (Slime, Ghost, Goat). Pure and
/// deterministic from the grid + start, so host and any future client agree.</summary>
public static class MonsterSpawner
{
    private static MonsterKind[] KindsForFloor(int floor)
    {
        if (floor >= 12)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner, MonsterKind.SkeletonHuman, MonsterKind.SkeletonDino };
        if (floor >= 8)
            return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner, MonsterKind.SkeletonHuman };
        return new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat, MonsterKind.ZombieMiner };
    }

    public static List<(GridPos Pos, MonsterKind Kind)> Place(TileGrid grid, GridPos start, int count, int floor = 0)
    {
        var result = new List<(GridPos, MonsterKind)>();
        if (count <= 0) return result;

        var floors = grid.Positions()
            .Where(p => grid.Get(p).IsTraversable() && p != start)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();
        if (floors.Count == 0) return result;

        // Farthest-first, seeded by the start so every pick maximises its minimum distance
        // to the start and to previously chosen spawns. Ties resolve to (Y, X) order.
        var chosen = new List<GridPos>();
        var anchors = new List<GridPos> { start };
        var taken = new HashSet<GridPos>();
        while (chosen.Count < count && chosen.Count < floors.Count)
        {
            GridPos best = floors[0];
            long bestMin = -1;
            foreach (var p in floors)
            {
                if (taken.Contains(p)) continue;
                long min = long.MaxValue;
                foreach (var a in anchors)
                {
                    long dx = p.X - a.X, dy = p.Y - a.Y;
                    long d = dx * dx + dy * dy;
                    if (d < min) min = d;
                }
                if (min > bestMin) { bestMin = min; best = p; }
            }
            chosen.Add(best);
            anchors.Add(best);
            taken.Add(best);
        }

        var kinds = KindsForFloor(floor);
        for (int i = 0; i < chosen.Count; i++)
            result.Add((chosen[i], kinds[i % kinds.Length]));
        return result;
    }
}
