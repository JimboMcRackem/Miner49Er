using System.Collections.Generic;
using System.Linq;
using Miner49er.Core;
using Xunit;

public class SpawnPlacementTests
{
    // A full WxW field of candidate cells (0..W-1).
    private static List<GridPos> Field(int w)
    {
        var cands = new List<GridPos>();
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
                cands.Add(new GridPos(x, y));
        return cands;
    }

    private static readonly GridPos TL = new(0, 0);
    private static readonly GridPos TR = new(19, 0);
    private static readonly GridPos BL = new(0, 19);
    private static readonly GridPos BR = new(19, 19);

    [Fact]
    public void Two_points_pick_diagonally_opposite_corners()
    {
        var result = SpawnPlacement.SelectFarthest(Field(20), 2);

        Assert.Equal(2, result.Count);
        Assert.Contains(TL, result);
        Assert.Contains(BR, result);
    }

    [Fact]
    public void Four_points_pick_the_four_corners()
    {
        var result = SpawnPlacement.SelectFarthest(Field(20), 4);

        Assert.Equal(4, result.Count);
        Assert.Contains(TL, result);
        Assert.Contains(TR, result);
        Assert.Contains(BL, result);
        Assert.Contains(BR, result);
    }

    [Fact]
    public void Three_points_are_three_distinct_corners()
    {
        var corners = new HashSet<GridPos> { TL, TR, BL, BR };
        var result = SpawnPlacement.SelectFarthest(Field(20), 3);

        Assert.Equal(3, result.Count);
        Assert.Equal(3, result.Distinct().Count());
        Assert.All(result, p => Assert.Contains(p, corners));
    }

    [Fact]
    public void One_point_picks_an_extreme_corner()
    {
        var corners = new HashSet<GridPos> { TL, TR, BL, BR };
        var result = SpawnPlacement.SelectFarthest(Field(20), 1);

        Assert.Single(result);
        Assert.Contains(result[0], corners);
    }

    [Fact]
    public void Deterministic_for_same_input()
    {
        var a = SpawnPlacement.SelectFarthest(Field(20), 4);
        var b = SpawnPlacement.SelectFarthest(Field(20), 4);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Count_at_least_candidate_count_returns_all_candidates()
    {
        var cands = new List<GridPos> { new(1, 1), new(2, 2), new(3, 3) };

        var result = SpawnPlacement.SelectFarthest(cands, 5);

        Assert.Equal(3, result.Count);
        Assert.All(cands, c => Assert.Contains(c, result));
    }

    [Fact]
    public void Count_zero_or_empty_candidates_return_empty()
    {
        Assert.Empty(SpawnPlacement.SelectFarthest(Field(20), 0));
        Assert.Empty(SpawnPlacement.SelectFarthest(new List<GridPos>(), 3));
    }
}
