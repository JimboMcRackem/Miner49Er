using Miner49er.Core;
using Xunit;

public class TileGridTests
{
    [Fact]
    public void New_grid_is_filled_with_given_type()
    {
        var grid = new TileGrid(3, 2, TileType.Rock);
        Assert.Equal(3, grid.Width);
        Assert.Equal(2, grid.Height);
        Assert.Equal(TileType.Rock, grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Set_then_Get_roundtrips()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(1, 1), TileType.Floor);
        Assert.Equal(TileType.Floor, grid.Get(new GridPos(1, 1)));
    }

    [Fact]
    public void New_grid_starts_at_version_zero()
    {
        var grid = new TileGrid(3, 3);
        Assert.Equal(0, grid.Version);
    }

    [Fact]
    public void Set_to_a_new_value_bumps_version()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        int before = grid.Version;
        grid.Set(new GridPos(1, 1), TileType.Floor);
        Assert.True(grid.Version > before, "changing a tile should bump Version");
    }

    [Fact]
    public void Set_to_the_same_value_does_not_bump_version()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(1, 1), TileType.Floor);
        int after = grid.Version;
        grid.Set(new GridPos(1, 1), TileType.Floor); // no-op write
        Assert.Equal(after, grid.Version);
    }

    [Fact]
    public void InBounds_rejects_outside_positions()
    {
        var grid = new TileGrid(3, 3);
        Assert.True(grid.InBounds(new GridPos(0, 0)));
        Assert.True(grid.InBounds(new GridPos(2, 2)));
        Assert.False(grid.InBounds(new GridPos(-1, 0)));
        Assert.False(grid.InBounds(new GridPos(3, 0)));
    }

    [Fact]
    public void IsWalkable_only_true_for_floor_in_bounds()
    {
        var grid = new TileGrid(3, 3, TileType.Rock);
        grid.Set(new GridPos(1, 1), TileType.Floor);
        Assert.True(grid.IsWalkable(new GridPos(1, 1)));
        Assert.False(grid.IsWalkable(new GridPos(0, 0)));   // rock
        Assert.False(grid.IsWalkable(new GridPos(-1, 0)));  // out of bounds
    }

    [Theory]
    [InlineData(TileType.Rock, true, true)]
    [InlineData(TileType.GoldRock, true, true)]
    [InlineData(TileType.Floor, false, false)]
    [InlineData(TileType.ImpermeableRock, false, false)]
    public void Tile_capability_flags(TileType t, bool minable, bool blastable)
    {
        Assert.Equal(minable, t.IsMinable());
        Assert.Equal(blastable, t.IsBlastable());
    }
}
