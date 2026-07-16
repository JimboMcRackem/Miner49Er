using Miner49er.Core;
using Xunit;

public class LightMapTests
{
    [Fact]
    public void Unlit_tile_samples_zero()
    {
        var map = new LightMap();
        Assert.Equal(0f, map.SampleClamped(new GridPos(0, 0)));
    }

    [Fact]
    public void Overlapping_lights_sum_and_clamp_to_one()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var tile = new GridPos(5, 5);
        // Two lights both centred on the tile contribute 1.0 each → 2.0, clamped to 1.0.
        map.AddLight(grid, tile, radius: 3);
        map.AddLight(grid, tile, radius: 3);
        Assert.Equal(1f, map.SampleClamped(tile));
    }

    [Fact]
    public void Two_offset_lights_sum_on_a_shared_tile()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var shared = new GridPos(5, 5);

        map.AddLight(grid, new GridPos(4, 5), radius: 3); // distance 1 from shared
        float oneLight = map.SampleClamped(shared);
        map.AddLight(grid, new GridPos(6, 5), radius: 3); // symmetric, adds more
        float twoLights = map.SampleClamped(shared);

        Assert.True(twoLights > oneLight);
    }

    [Fact]
    public void Clear_resets_the_accumulator()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var map = new LightMap();
        var tile = new GridPos(5, 5);
        map.AddLight(grid, tile, radius: 3);
        map.Clear();
        Assert.Equal(0f, map.SampleClamped(tile));
    }
}
