using Miner49er.Core;
using Xunit;

public class LightFieldTests
{
    [Fact]
    public void Intensity_is_full_at_origin_and_falls_with_distance()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var origin = new GridPos(5, 5);
        var field = LightField.Compute(grid, origin, radius: 4);

        Assert.Equal(1f, field[origin]);
        float near = field[new GridPos(6, 5)]; // distance 1
        float far  = field[new GridPos(8, 5)]; // distance 3
        Assert.True(near > far);
        Assert.True(far > 0f);
    }

    [Fact]
    public void Tiles_beyond_the_radius_receive_no_light()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        var field = LightField.Compute(grid, new GridPos(5, 5), radius: 2);
        Assert.False(field.ContainsKey(new GridPos(8, 5))); // distance 3 > radius 2
    }

    [Fact]
    public void A_wall_blocks_light_from_reaching_tiles_behind_it()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        grid.Set(new GridPos(5, 3), TileType.Rock);       // pillar two tiles north of origin
        var field = LightField.Compute(grid, new GridPos(5, 5), radius: 6);

        Assert.False(field.ContainsKey(new GridPos(5, 2))); // umbra directly behind the pillar
        Assert.True(field.ContainsKey(new GridPos(2, 3)));  // well to the side: lit
    }

    [Fact]
    public void Non_positive_radius_yields_an_empty_field()
    {
        var grid = new TileGrid(11, 11, TileType.Floor);
        Assert.Empty(LightField.Compute(grid, new GridPos(5, 5), radius: 0));
    }
}
