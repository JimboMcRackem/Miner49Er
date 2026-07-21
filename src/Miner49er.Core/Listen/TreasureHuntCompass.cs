using System.Collections.Generic;
using Miner49er.Core.Net;

namespace Miner49er.Core;

/// <summary>Pure helper for the Treasure Hunt Listen compass: the nearest of the
/// local player's two assigned idols still in the world (buried in rock or loose
/// on the floor). Idols held or deposited are absent from <paramref name="items"/>,
/// so they naturally drop out. Returns null when neither assigned idol is present.</summary>
public static class TreasureHuntCompass
{
    public static GridPos? NearestIdolTarget(GridPos self, ItemKind idolA, ItemKind idolB,
        IEnumerable<ItemSnapshot> items)
    {
        GridPos? best = null;
        long bestSq = long.MaxValue;
        foreach (var it in items)
        {
            if (it.Kind != idolA && it.Kind != idolB) continue;
            if (it.Placement != ItemPlacement.Buried && it.Placement != ItemPlacement.Loose) continue;
            long dx = it.X - self.X, dy = it.Y - self.Y;
            long sq = dx * dx + dy * dy;
            if (sq < bestSq) { bestSq = sq; best = new GridPos(it.X, it.Y); }
        }
        return best;
    }
}
