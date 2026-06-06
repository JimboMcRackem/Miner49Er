namespace Miner49er.Core;

public static class Visibility
{
    public static HashSet<GridPos> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var set = new HashSet<GridPos>();
        int r2 = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                var p = new GridPos(origin.X + dx, origin.Y + dy);
                if (grid.InBounds(p)) set.Add(p);
            }
        return set;
    }
}
