using Miner49er.Core;
using Xunit;

public class MapGeneratorShopTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    public void Shop_floor_has_ShopPos(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotNull(map.ShopPos);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public void Non_shop_floor_has_null_ShopPos(int floor)
    {
        var cfg = MapConfig.FloorConfig(floor, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.Null(map.ShopPos);
    }

    [Fact]
    public void ShopPos_is_a_Floor_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.Equal(TileType.Floor, map.Grid.Get(map.ShopPos!.Value));
    }

    [Fact]
    public void ShopPos_is_not_the_escape_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotEqual(map.EscapeTile, map.ShopPos);
    }

    [Fact]
    public void ShopPos_is_not_the_spawn_tile()
    {
        var cfg = MapConfig.FloorConfig(4, seed: 42);
        var map = MapGenerator.Generate(cfg);
        Assert.NotEqual(map.Spawns[0], map.ShopPos);
    }
}
