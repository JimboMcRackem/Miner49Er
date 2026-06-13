using Miner49er.Core;
using Xunit;

public class BlocksSightTests
{
    [Theory]
    [InlineData(TileType.Rock)]
    [InlineData(TileType.GoldRock)]
    [InlineData(TileType.ImpermeableRock)]
    public void Rock_family_blocks_sight(TileType t)
    {
        Assert.True(t.BlocksSight());
    }

    [Theory]
    [InlineData(TileType.Floor)]
    [InlineData(TileType.ShallowWater)]
    [InlineData(TileType.DeepWater)]
    [InlineData(TileType.Plank)]
    public void Open_tiles_are_transparent(TileType t)
    {
        Assert.False(t.BlocksSight());
    }
}
