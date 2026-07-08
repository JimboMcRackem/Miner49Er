using Miner49er.Core;
using Xunit;

public class TileTypeCrystalTests
{
    [Fact]
    public void CrystalRock_is_minable_and_blastable()
    {
        Assert.True(TileType.CrystalRock.IsMinable());
        Assert.True(TileType.CrystalRock.IsBlastable());
    }

    [Fact]
    public void CrystalRock_blocks_sight()
    {
        Assert.True(TileType.CrystalRock.BlocksSight());
    }

    [Fact]
    public void CrystalRock_is_not_walkable_enterable_or_lethal()
    {
        Assert.False(TileType.CrystalRock.IsWalkable());
        Assert.False(TileType.CrystalRock.IsEnterable());
        Assert.False(TileType.CrystalRock.IsLethal());
    }

    [Fact]
    public void CrystalRock_is_not_water_or_bridgeable()
    {
        Assert.False(TileType.CrystalRock.IsWater());
        Assert.False(TileType.CrystalRock.IsBridgeable());
    }

    [Fact]
    public void CrystalShard_is_carried()
    {
        Assert.True(ItemKind.CrystalShard.IsCarried());
    }
}
