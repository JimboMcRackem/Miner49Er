using System.Linq;
using Miner49er.Core;
using Xunit;

public class VisibilityTests
{
    [Fact]
    public void Visible_set_is_a_radius_disc_clipped_to_bounds()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 2);

        Assert.Contains(new GridPos(5, 5), visible);
        Assert.Contains(new GridPos(7, 5), visible);     // distance 2 on axis
        Assert.DoesNotContain(new GridPos(8, 5), visible); // distance 3
        Assert.DoesNotContain(new GridPos(7, 7), visible); // euclidean > 2
    }

    [Fact]
    public void Visible_set_clips_at_grid_edges()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(0, 0), radius: 3);
        Assert.All(visible, p => Assert.True(grid.InBounds(p)));
    }

    [Fact]
    public void FogState_accumulates_explored_across_updates()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var fog = new FogState();

        fog.Update(Visibility.Compute(grid, new GridPos(2, 2), 1));
        Assert.True(fog.IsExplored(new GridPos(2, 2)));
        Assert.True(fog.IsVisible(new GridPos(2, 2)));

        fog.Update(Visibility.Compute(grid, new GridPos(6, 6), 1));
        Assert.True(fog.IsExplored(new GridPos(2, 2)));   // remembered
        Assert.False(fog.IsVisible(new GridPos(2, 2)));   // no longer in view
        Assert.True(fog.IsVisible(new GridPos(6, 6)));
    }

    [Fact]
    public void Origin_is_always_visible()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        var visible = Visibility.Compute(grid, new GridPos(3, 3), radius: 4);
        Assert.Contains(new GridPos(3, 3), visible);
    }

    [Fact]
    public void Rock_wall_blocks_tiles_behind_it()
    {
        // Solid horizontal rock wall across row y=3; miner stands south at (5,5).
        var grid = new TileGrid(11, 11, TileType.Floor);
        for (int x = 0; x < 11; x++) grid.Set(new GridPos(x, 3), TileType.Rock);

        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.Contains(new GridPos(5, 4), visible);     // near-side floor: seen
        Assert.Contains(new GridPos(5, 3), visible);     // the wall face itself: seen
        Assert.DoesNotContain(new GridPos(5, 2), visible); // directly behind the wall: hidden
        Assert.DoesNotContain(new GridPos(5, 1), visible); // farther behind: hidden
    }

    [Fact]
    public void Single_pillar_casts_a_shadow_directly_behind_it()
    {
        // One rock pillar two tiles north of the miner.
        var grid = new TileGrid(11, 11, TileType.Floor);
        grid.Set(new GridPos(5, 3), TileType.Rock);

        var visible = Visibility.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.Contains(new GridPos(5, 3), visible);       // pillar face: seen
        Assert.DoesNotContain(new GridPos(5, 2), visible); // umbra directly behind: hidden
        Assert.Contains(new GridPos(2, 3), visible);       // well to the side: seen
        Assert.Contains(new GridPos(8, 3), visible);       // well to the side: seen
    }

    [Fact]
    public void Visibility_is_symmetric_on_open_ground()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var a = new GridPos(4, 4);
        var b = new GridPos(6, 5);

        var fromA = Visibility.Compute(grid, a, radius: 5);
        var fromB = Visibility.Compute(grid, b, radius: 5);

        Assert.Equal(fromA.Contains(b), fromB.Contains(a));
    }
}
