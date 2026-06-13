using Miner49er.Core;
using Xunit;

public class TileTypePitTests
{
    [Fact]
    public void Pit_is_enterable_and_lethal()
    {
        Assert.True(TileType.Pit.IsEnterable());   // you can step onto it...
        Assert.True(TileType.Pit.IsLethal());      // ...and you die
    }

    [Fact]
    public void Deep_water_is_still_lethal()       // regression: lethal set widened, not replaced
        => Assert.True(TileType.DeepWater.IsLethal());

    [Fact]
    public void Pit_is_not_safe_ground()
    {
        Assert.False(TileType.Pit.IsWalkable());   // spawns/fog/reachability never treat it as safe
        Assert.False(TileType.Pit.IsMinable());
        Assert.False(TileType.Pit.IsBlastable());
        Assert.False(TileType.Pit.IsWater());
    }

    [Fact]
    public void Pit_is_transparent_to_sight()      // an open hole — you can see across it
        => Assert.False(TileType.Pit.BlocksSight());

    [Theory]
    [InlineData(TileType.ShallowWater, true)]
    [InlineData(TileType.DeepWater, true)]
    [InlineData(TileType.Pit, true)]
    [InlineData(TileType.Floor, false)]
    [InlineData(TileType.Rock, false)]
    [InlineData(TileType.Plank, false)]
    public void IsBridgeable_is_water_or_pit(TileType t, bool expected)
        => Assert.Equal(expected, t.IsBridgeable());
}
