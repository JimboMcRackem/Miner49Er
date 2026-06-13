namespace Miner49er.Core;

/// <summary>Field-of-view via recursive shadowcasting over 8 octants. Tiles whose
/// <see cref="TileTypeExtensions.BlocksSight"/> is true cast shadows; the blocker
/// tile itself is visible (you see the rock face) but nothing behind it in that
/// cone is. Integer/rational slope math only, so the result is deterministic and
/// identical on host and every client.</summary>
public static class Visibility
{
    // Octant transforms: (xx, xy, yx, yy) for the 8 octants.
    private static readonly int[] Xx = { 1, 0, 0, -1, -1, 0, 0, 1 };
    private static readonly int[] Xy = { 0, 1, -1, 0, 0, -1, 1, 0 };
    private static readonly int[] Yx = { 0, 1, 1, 0, 0, -1, -1, 0 };
    private static readonly int[] Yy = { 1, 0, 0, 1, -1, 0, 0, -1 };

    public static HashSet<GridPos> Compute(TileGrid grid, GridPos origin, int radius)
    {
        var visible = new HashSet<GridPos>();
        if (grid.InBounds(origin)) visible.Add(origin);
        for (int oct = 0; oct < 8; oct++)
            CastLight(grid, origin, radius, visible, 1, 1.0, 0.0,
                      Xx[oct], Xy[oct], Yx[oct], Yy[oct]);
        return visible;
    }

    private static void CastLight(TileGrid grid, GridPos origin, int radius,
        HashSet<GridPos> visible, int row, double startSlope, double endSlope,
        int xx, int xy, int yx, int yy)
    {
        if (startSlope < endSlope) return;
        int r2 = radius * radius;
        double nextStartSlope = startSlope;

        for (int i = row; i <= radius; i++)
        {
            bool blocked = false;
            for (int dx = -i, dy = -i; dx <= 0; dx++)
            {
                double lSlope = (dx - 0.5) / (dy + 0.5);
                double rSlope = (dx + 0.5) / (dy - 0.5);
                if (startSlope < rSlope) continue;
                if (endSlope > lSlope) break;

                int mapX = origin.X + dx * xx + dy * xy;
                int mapY = origin.Y + dx * yx + dy * yy;
                var p = new GridPos(mapX, mapY);

                if (dx * dx + dy * dy <= r2 && grid.InBounds(p))
                    visible.Add(p);

                bool wall = !grid.InBounds(p) || grid.Get(p).BlocksSight();

                if (blocked)
                {
                    if (wall)
                    {
                        nextStartSlope = rSlope;
                        continue;
                    }
                    blocked = false;
                    startSlope = nextStartSlope;
                }
                else if (wall && i < radius)
                {
                    blocked = true;
                    CastLight(grid, origin, radius, visible, i + 1, startSlope, lSlope,
                              xx, xy, yx, yy);
                    nextStartSlope = rSlope;
                }
            }

            if (blocked) break;
        }
    }
}
