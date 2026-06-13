using Miner49er.Core;
using Xunit;

public class TileTypeCrackTests
{
    [Theory]
    [InlineData(TileType.Cracked)]
    [InlineData(TileType.Crumbling)]
    public void Cracks_are_safe_walkable_floor_not_instant_death(TileType t)
    {
        Assert.True(t.IsWalkable());     // spawns/fog/reachability treat a crack as ground
        Assert.True(t.IsEnterable());    // you can step onto it
        Assert.False(t.IsLethal());      // ...and it does not kill you on contact
    }

    [Theory]
    [InlineData(TileType.Cracked)]
    [InlineData(TileType.Crumbling)]
    public void Cracks_are_open_floor_not_rock(TileType t)
    {
        Assert.False(t.BlocksSight());   // open floor — transparent
        Assert.False(t.IsMinable());
        Assert.False(t.IsBlastable());
        Assert.False(t.IsWater());
        Assert.Equal(1.0, t.MoveCostMultiplier());
    }

    [Theory]
    [InlineData(TileType.Cracked, true)]
    [InlineData(TileType.Crumbling, true)]
    [InlineData(TileType.Pit, true)]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.Plank, false)]
    public void IsBridgeable_now_includes_cracks(TileType t, bool expected)
        => Assert.Equal(expected, t.IsBridgeable());
}
