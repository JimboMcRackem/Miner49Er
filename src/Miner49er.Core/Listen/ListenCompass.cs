using System;
using System.Collections.Generic;

namespace Miner49er.Core;

public enum CompassDirection { N, NE, E, SE, S, SW, W, NW }

/// <summary>Pure helper for the Listen compass: the 8-point direction from the
/// listener to the nearest other position. Caller passes only living rivals
/// (excluding self).</summary>
public static class ListenCompass
{
    public static CompassDirection? NearestDirection(GridPos self, IEnumerable<GridPos> others)
    {
        GridPos? best = null;
        long bestSq = long.MaxValue;
        foreach (var o in others)
        {
            long dx = o.X - self.X, dy = o.Y - self.Y;
            long sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; best = o; }
        }
        if (best is null) return null;
        return Bucket(best.Value.X - self.X, best.Value.Y - self.Y);
    }

    // North = up = -Y. Bearing measured clockwise from North, snapped to 8 sectors.
    internal static CompassDirection Bucket(int dx, int dy)
    {
        double degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (degrees < 0) degrees += 360.0;
        int sector = (int)Math.Round(degrees / 45.0) % 8;
        return (CompassDirection)sector;
    }
}
