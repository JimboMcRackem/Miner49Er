using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotPathfinderTests
{
    [Fact]
    public void NextDir_returns_east_for_target_directly_east()
    {
        var grid = new TileGrid(5, 3, TileType.Floor);
        int dir = BotPathfinder.NextDir(grid, new GridPos(0, 1), new GridPos(2, 1), passRock: false);
        Assert.Equal((int)Direction.East, dir);
    }

    [Fact]
    public void NextDir_returns_minus1_when_already_at_target()
    {
        var grid = new TileGrid(5, 3, TileType.Floor);
        int dir = BotPathfinder.NextDir(grid, new GridPos(2, 1), new GridPos(2, 1), passRock: false);
        Assert.Equal(-1, dir);
    }

    [Fact]
    public void NextDir_navigates_around_rock_wall_when_passRock_false()
    {
        // Rock blocks direct east path; only route is south first.
        // 3×2 grid: bot at (0,0), target at (2,0), Rock at (1,0).
        var grid = new TileGrid(3, 2, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.Rock);
        int dir = BotPathfinder.NextDir(grid, new GridPos(0, 0), new GridPos(2, 0), passRock: false);
        Assert.Equal((int)Direction.South, dir); // forced south to detour
    }

    [Fact]
    public void NextDir_goes_through_rock_when_passRock_true()
    {
        // Rock at (1,1); with passRock=true, BFS traverses through it.
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.Rock);
        int dir = BotPathfinder.NextDir(grid, new GridPos(0, 1), new GridPos(2, 1), passRock: true);
        Assert.Equal((int)Direction.East, dir);
    }

    [Fact]
    public void NextDir_returns_direction_into_GoldRock_target()
    {
        // GoldRock at (1,1); passRock=false; bot at (0,1) — must head east to mine it.
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.GoldRock);
        int dir = BotPathfinder.NextDir(grid, new GridPos(0, 1), new GridPos(1, 1), passRock: false);
        Assert.Equal((int)Direction.East, dir);
    }

    [Fact]
    public void NextDir_returns_minus1_when_completely_unreachable()
    {
        // 3×3: only (0,0) and (2,2) are Floor; surrounded by Rock.
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(0, 0), TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Floor);
        int dir = BotPathfinder.NextDir(grid, new GridPos(0, 0), new GridPos(2, 2), passRock: false);
        Assert.Equal(-1, dir);
    }

    [Fact]
    public void Nearest_returns_closest_GoldRock()
    {
        // 7×1 row of Floor; GoldRocks at x=2 and x=5; bot at (0,0).
        var grid = new TileGrid(7, 1, TileType.Floor);
        grid.Set(new GridPos(5, 0), TileType.GoldRock);
        grid.Set(new GridPos(2, 0), TileType.GoldRock);
        var candidates = new[] { new GridPos(5, 0), new GridPos(2, 0) };
        var nearest = BotPathfinder.Nearest(grid, new GridPos(0, 0), candidates, passRock: false);
        Assert.Equal(new GridPos(2, 0), nearest);
    }

    [Fact]
    public void Nearest_returns_null_for_empty_candidates()
    {
        var grid = new TileGrid(5, 3, TileType.Floor);
        var nearest = BotPathfinder.Nearest(grid, new GridPos(0, 0), System.Array.Empty<GridPos>(), passRock: false);
        Assert.Null(nearest);
    }
}
