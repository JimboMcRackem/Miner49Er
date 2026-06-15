using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Chooses spawn positions spread as far apart as possible (max-min
/// dispersion), anchored to an extreme corner so two players land diagonally opposite,
/// four in the corners, and so on for any count. Pure and deterministic: ties break by
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

        // First pick: farthest from the candidates' bounding-box centre (an extreme).
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in candidates)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        double cx = (minX + maxX) / 2.0, cy = (minY + maxY) / 2.0;

        GridPos first = candidates[0];
        double bestCentre = -1;
        foreach (var p in candidates)
        {
            double dx = p.X - cx, dy = p.Y - cy;
            double d = dx * dx + dy * dy;
            if (d > bestCentre || (d == bestCentre && Before(p, first)))
            {
                bestCentre = d;
                first = p;
            }
        }
        chosen.Add(first);

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
