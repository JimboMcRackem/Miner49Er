using Miner49er.Core;
using Xunit;

public class FogMaskTests
{
    private const float Dim = 0.78f;
    private const int T = FogMask.TexelsPerTile;
    private const byte DimByte = 199; // 0.78 * 255, rounded

    // A 10x5 window: tiles left of `edge` are visible, the rest are never-seen.
    private static FogState VisibleLeftOf(int edge)
    {
        var visible = new HashSet<GridPos>();
        for (int y = 0; y < 5; y++)
            for (int x = 0; x < edge; x++)
                visible.Add(new GridPos(x, y));
        var fog = new FogState();
        fog.Update(visible);
        return fog;
    }

    private static byte At(byte[] mask, int texelX, int texelY) => mask[texelY * (10 * T) + texelX];

    [Fact]
    public void View_vignette_is_clear_at_the_centre_and_flat_past_the_radius_with_no_rim()
    {
        var view = new FogView(0f, 2.5f, 4f, 0.32f);
        var mask = FogMask.Build(VisibleLeftOf(10), 0, 0, 10, 5, Dim, false, view);

        Assert.True(At(mask, 0, 2 * T) < 3);
        byte previous = 0;
        for (int x = 0; x < 8 * T; x++)
        {
            byte v = At(mask, x, 2 * T + T / 2);
            Assert.True(v >= previous, $"texel {x} fell from {previous} to {v}");
            previous = v;
        }
        Assert.Equal(82, At(mask, 8 * T, 2 * T)); // 0.32 * 255, still dimmed well past the radius
    }

    [Fact]
    public void View_vignette_never_thins_hidden_tiles()
    {
        var view = new FogView(3f, 2.5f, 4f, 0.32f);
        var mask = FogMask.Build(VisibleLeftOf(6), 0, 0, 10, 5, Dim, false, view);
        for (int y = 0; y < 5 * T; y++)
            for (int x = 6 * T; x < 10 * T; x++)
                Assert.Equal(255, At(mask, x, y));
    }

    [Fact]
    public void Mask_is_sized_in_texels()
    {
        Assert.Equal(10 * T * 5 * T, FogMask.Build(new FogState(), 0, 0, 10, 5, Dim, false).Length);
    }

    [Fact]
    public void Visible_tiles_away_from_the_edge_are_clear()
    {
        var mask = FogMask.Build(VisibleLeftOf(6), 0, 0, 10, 5, Dim, false);
        Assert.Equal(0, At(mask, 3 * T, 2 * T));
    }

    [Fact]
    public void Every_texel_of_a_hidden_tile_stays_fully_covered()
    {
        var mask = FogMask.Build(VisibleLeftOf(6), 0, 0, 10, 5, Dim, false);
        for (int y = 0; y < 5 * T; y++)
            for (int x = 6 * T; x < 10 * T; x++)
                Assert.Equal(255, At(mask, x, y));
    }

    [Fact]
    public void The_texel_just_inside_the_view_is_also_opaque_so_filtering_cannot_thin_the_hidden_edge()
    {
        var mask = FogMask.Build(VisibleLeftOf(6), 0, 0, 10, 5, Dim, false);
        Assert.Equal(255, At(mask, 6 * T - 1, 2 * T));
    }

    [Fact]
    public void Explored_tiles_are_never_thinner_than_dim()
    {
        var fog = VisibleLeftOf(10);
        fog.Update(new HashSet<GridPos> { new(0, 2) }); // everything else now explored, not visible
        var mask = FogMask.Build(fog, 0, 0, 10, 5, Dim, false);
        for (int y = 0; y < 5 * T; y++)
            for (int x = 1 * T; x < 10 * T; x++)
                Assert.True(At(mask, x, y) >= DimByte, $"texel {x},{y} = {At(mask, x, y)}");
    }

    [Fact]
    public void The_fade_rises_steadily_through_several_levels()
    {
        var mask = FogMask.Build(VisibleLeftOf(6), 0, 0, 10, 5, Dim, false);
        var levels = new HashSet<byte>();
        for (int x = 3 * T; x < 6 * T; x++)
        {
            Assert.True(At(mask, x, 2 * T) <= At(mask, x + 1, 2 * T));
            levels.Add(At(mask, x, 2 * T));
        }
        Assert.True(levels.Count >= 4, $"only {levels.Count} levels");
    }

    [Fact]
    public void Without_a_view_visible_tiles_count_as_explored()
    {
        var mask = FogMask.Build(VisibleLeftOf(10), 0, 0, 10, 5, Dim, ignoreVisible: true);
        Assert.Equal(DimByte, At(mask, 5 * T, 2 * T));
    }
}
