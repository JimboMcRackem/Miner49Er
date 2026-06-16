using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Chooses spawn positions spread as far apart as possible (max-min
/// dispersion). The first two are the diameter pair — the two candidates farthest apart —
/// so the common two-player case is optimal (spawns land at the true extremes, diagonally
/// opposite); each further pick maximises its minimum distance to those already chosen, so
/// three/four players spread to thirds/quarters. Pure and deterministic: ties break by
/// (Y, X), distance is squared Euclidean so the spread favours true geometric corners.</summary>
public static class SpawnPlacement
{
    public static List<GridPos> SelectFarthest(IReadOnlyList<GridPos> candidates, int count)
    {
        var chosen = new List<GridPos>();
        if (count <= 0 || candidates.Count == 0) return chosen;
        if (count >= candidates.Count)
        {
            chosen.AddRange(candidates);
            return chosen;
        }

        // First two: the diameter pair (the two candidates farthest apart). Scanning i<j over
        // (Y, X)-sorted candidates and keeping the first strictly-greater pair makes the choice
        // deterministic — the kept pair has the (Y, X)-smallest endpoints. O(n^2), negligible for
        // one-time map generation. `a` is the earlier endpoint, so it seeds the traversal.
        GridPos a = candidates[0], b = candidates[0];
        long bestPair = -1;
        for (int i = 0; i < candidates.Count; i++)
            for (int j = i + 1; j < candidates.Count; j++)
            {
                long dx = candidates[i].X - candidates[j].X, dy = candidates[i].Y - candidates[j].Y;
                long d = dx * dx + dy * dy;
                if (d > bestPair) { bestPair = d; a = candidates[i]; b = candidates[j]; }
            }
        chosen.Add(a);
        if (count >= 2) chosen.Add(b);

        // Each subsequent pick maximises the minimum distance to those already chosen.
        while (chosen.Count < count)
        {
            GridPos best = candidates[0];
            long bestMin = -1;
            foreach (var p in candidates)
            {
                long m = long.MaxValue;
                foreach (var s in chosen)
                {
                    long dx = p.X - s.X, dy = p.Y - s.Y;
                    long d = dx * dx + dy * dy;
                    if (d < m) m = d;
                }
                if (m > bestMin || (m == bestMin && Before(p, best)))
                {
                    bestMin = m;
                    best = p;
                }
            }
            chosen.Add(best);
        }
        return chosen;
    }

    // Deterministic tie-break: smaller Y, then smaller X.
    private static bool Before(GridPos a, GridPos b) =>
        a.Y < b.Y || (a.Y == b.Y && a.X < b.X);
}
