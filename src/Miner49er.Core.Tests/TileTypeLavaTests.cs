using Miner49er.Core;
using Xunit;

public class TileTypeLavaTests
{
    [Theory]
    [InlineData(TileType.Lava)]
    [InlineData(TileType.LavaVent)]
    public void Lava_is_lethal_terrain_you_can_step_into(TileType t)
    {
        Assert.True(t.IsEnterable());   // a miner can move onto it
        Assert.True(t.IsLethal());      // ...and dies on contact
        Assert.False(t.IsWalkable());   // never a spawn / safe / reachability tile
    }

    [Theory]
    [InlineData(TileType.Lava)]
    [InlineData(TileType.LavaVent)]
    public void Lava_cannot_be_mined_blasted_or_bridged(TileType t)
    {
        Assert.False(t.IsMinable());
        Assert.False(t.IsBlastable());
        Assert.False(t.IsBridgeable());   // planks burn — water is the counter
        Assert.False(t.BlocksSight());    // ground-level glow, transparent
        Assert.False(t.IsWater());
        Assert.Equal(1.0, t.MoveCostMultiplier());
    }
}
