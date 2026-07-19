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

    [Fact]
    public void Default_scale_matches_unscaled_call()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var tile = new GridPos(5, 5);
        var a = new LightMap(); a.AddLight(grid, tile, radius: 3);
        var b = new LightMap(); b.AddLight(grid, tile, radius: 3, scale: 1f);
        Assert.Equal(a.SampleClamped(tile), b.SampleClamped(tile));
    }

    [Fact]
    public void AddField_matches_AddLight_for_the_same_source()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var origin = new GridPos(5, 5);

        var viaLight = new LightMap(); viaLight.AddLight(grid, origin, radius: 3);

        var field = LightField.Compute(grid, origin, radius: 3);
        var viaField = new LightMap(); viaField.AddField(field, scale: 1f);

        foreach (var p in grid.Positions())
            Assert.Equal(viaLight.SampleClamped(p), viaField.SampleClamped(p));
    }

    [Fact]
    public void AddField_applies_scale()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var origin = new GridPos(5, 5);
        var probe = new GridPos(5, 7); // partway out, lit below 1.0 so scaling shows pre-clamp

        var field = LightField.Compute(grid, origin, radius: 4);
        var full = new LightMap(); full.AddField(field, scale: 1f);
        var half = new LightMap(); half.AddField(field, scale: 0.5f);

        Assert.True(full.SampleClamped(probe) > 0f, "probe should be lit");
        Assert.True(System.Math.Abs(full.SampleClamped(probe) * 0.5f - half.SampleClamped(probe)) < 1e-4f);
    }

    [Fact]
    public void Scale_multiplies_added_intensity()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var tile = new GridPos(5, 5);

        var full = new LightMap(); full.AddLight(grid, tile, radius: 4);
        var half = new LightMap(); half.AddLight(grid, tile, radius: 4, scale: 0.5f);

        // A tile partway out is lit below 1.0, so halving is visible before the clamp.
        var probe = new GridPos(5, 7);
        Assert.True(full.SampleClamped(probe) > 0f, "probe should be lit");
        Assert.True(System.Math.Abs(full.SampleClamped(probe) * 0.5f - half.SampleClamped(probe)) < 1e-4f);
    }
}
