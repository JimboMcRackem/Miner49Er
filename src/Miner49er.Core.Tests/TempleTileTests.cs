using Miner49er.Core;
using Xunit;

public class TempleTileTests
{
    [Fact]
    public void Temple_floor_can_be_walked_but_not_dug()
    {
        Assert.True(TileType.TempleFloor.IsWalkable());
        Assert.True(TileType.TempleFloor.IsEnterable());
        Assert.False(TileType.TempleFloor.IsMinable());
    }

    [Fact]
    public void Temple_floor_stays_out_of_the_cave_region()
    {
        Assert.False(TileType.TempleFloor.IsTraversable());
    }

    [Fact]
    public void Marble_walls_resist_everything()
    {
        Assert.False(TileType.MarbleWall.IsMinable());
        Assert.False(TileType.MarbleWall.IsBlastable());
        Assert.True(TileType.MarbleWall.BlocksSight());
    }

    [Fact]
    public void Marble_panels_can_be_broken()
    {
        Assert.True(TileType.MarblePanel.IsMinable());
        Assert.True(TileType.MarblePanel.IsBlastable());
        Assert.True(TileType.MarblePanel.BlocksSight());
    }

    [Fact]
    public void A_column_blocks_movement_and_sight()
    {
        Assert.False(TileType.Column.IsEnterable());
        Assert.False(TileType.Column.IsMinable());
        Assert.True(TileType.Column.BlocksSight());
    }

    [Theory]
    [InlineData(TileType.FallenColumn)]
    [InlineData(TileType.Altar)]
    public void Low_props_block_movement_but_not_sight(TileType t)
    {
        Assert.False(t.IsEnterable());
        Assert.False(t.IsMinable());
        Assert.False(t.BlocksSight());
    }

    [Theory]
    [InlineData(TileType.MarbleWall, true)]
    [InlineData(TileType.Jar, true)]
    [InlineData(TileType.TempleFloor, true)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.Floor, false)]
    public void IsTemple_covers_only_temple_tiles(TileType t, bool expected)
    {
        Assert.Equal(expected, t.IsTemple());
    }

    [Fact]
    public void A_jar_can_be_smashed()
    {
        Assert.True(TileType.Jar.IsMinable());
        Assert.True(TileType.Jar.IsBlastable());
        Assert.False(TileType.Jar.IsEnterable());
        Assert.False(TileType.Jar.BlocksSight());
    }
}
