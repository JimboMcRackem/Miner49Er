using Miner49er.Core;
using Xunit;

public class TileTypeWaterTests
{
    [Theory]
    [InlineData(TileType.Floor, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.GoldRock, false)]
    [InlineData(TileType.ImpermeableRock, false)]
    public void IsEnterable_allows_floor_and_water_only(TileType t, bool expected)
        => Assert.Equal(expected, t.IsEnterable());

    [Theory]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.ShallowWater, false)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    public void IsLethal_is_deep_water_only(TileType t, bool expected)
        => Assert.Equal(expected, t.IsLethal());

    [Theory]
    [InlineData(TileType.Floor, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, false)]
    [InlineData(TileType.Rock, false)]
    public void IsWalkable_is_floor_and_shallow(TileType t, bool expected)
        => Assert.Equal(expected, t.IsWalkable());

    [Fact]
    public void ShallowWater_costs_double_to_move_through()
    {
        Assert.Equal(1.0, TileType.Floor.MoveCostMultiplier());
        Assert.Equal(2.0, TileType.ShallowWater.MoveCostMultiplier());
        Assert.Equal(2.0, TileTypeExtensions.ShallowSlowFactor);
    }

    [Theory]
    [InlineData(TileType.ShallowWater, false)]
    [InlineData(TileType.DeepWater, false)]
    public void Water_is_inert_to_tools(TileType t, bool expected)
    {
        Assert.Equal(expected, t.IsMinable());
        Assert.Equal(expected, t.IsBlastable());
    }
}
