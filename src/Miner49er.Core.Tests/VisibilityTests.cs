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
}
