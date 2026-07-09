using Miner49er.Core;
using Xunit;

public class TileTypeScreeTests
{
    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_minable(TileType t) => Assert.True(t.IsMinable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_blastable(TileType t) => Assert.True(t.IsBlastable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_block_sight(TileType t) => Assert.True(t.BlocksSight());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_not_walkable(TileType t) => Assert.False(t.IsWalkable());

    [Theory]
    [InlineData(TileType.ScreeRock)]
    [InlineData(TileType.UnstableRock)]
    [InlineData(TileType.VolatileRock)]
    public void Scree_tiles_are_not_enterable(TileType t) => Assert.False(t.IsEnterable());

    [Fact]
    public void IsScree_returns_true_for_all_scree_types()
    {
        Assert.True(TileType.ScreeRock.IsScree());
        Assert.True(TileType.UnstableRock.IsScree());
        Assert.True(TileType.VolatileRock.IsScree());
    }

    [Fact]
    public void IsScree_returns_false_for_non_scree()
    {
        Assert.False(TileType.Rock.IsScree());
        Assert.False(TileType.Floor.IsScree());
        Assert.False(TileType.CrystalRock.IsScree());
    }

    [Fact]
    public void ScreeRock_has_radius_1() => Assert.Equal(1, TileType.ScreeRock.ScreeCollapseRadius());

    [Fact]
    public void UnstableRock_has_radius_1() => Assert.Equal(1, TileType.UnstableRock.ScreeCollapseRadius());

    [Fact]
    public void VolatileRock_has_radius_2() => Assert.Equal(2, TileType.VolatileRock.ScreeCollapseRadius());

    [Fact]
    public void ScreeRock_trigger_chance_is_50_percent() =>
        Assert.Equal(0.5, TileType.ScreeRock.ScreeTriggerChance());

    [Fact]
    public void UnstableRock_trigger_chance_is_100_percent() =>
        Assert.Equal(1.0, TileType.UnstableRock.ScreeTriggerChance());

    [Fact]
    public void VolatileRock_trigger_chance_is_100_percent() =>
        Assert.Equal(1.0, TileType.VolatileRock.ScreeTriggerChance());
}
