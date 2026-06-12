using Miner49er.Core;
using Xunit;

public class TileTypePlankTests
{
    [Fact]
    public void Plank_is_walkable_and_enterable()
    {
        Assert.True(TileType.Plank.IsWalkable());
        Assert.True(TileType.Plank.IsEnterable());
    }

    [Fact]
    public void Plank_is_not_lethal_and_not_water()
    {
        Assert.False(TileType.Plank.IsLethal());
        Assert.False(TileType.Plank.IsWater());
    }

    [Fact]
    public void Plank_has_no_move_slowdown()
    {
        Assert.Equal(1.0, TileType.Plank.MoveCostMultiplier(), 3);
    }

    [Fact]
    public void Plank_cannot_be_mined_or_blasted()
    {
        Assert.False(TileType.Plank.IsMinable());
        Assert.False(TileType.Plank.IsBlastable());
    }
}
