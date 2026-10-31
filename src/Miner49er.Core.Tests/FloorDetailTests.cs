using Miner49er.Core;
using Xunit;

public class FloorDetailTests
{
    [Fact]
    public void A_tile_always_gets_the_same_detail()
    {
        for (int x = 0; x < 50; x++)
        {
            bool a = FloorDetail.TryPick(x, 7, out var s1, out int ox1, out int oy1);
            bool b = FloorDetail.TryPick(x, 7, out var s2, out int ox2, out int oy2);
            Assert.Equal(a, b);
            Assert.Same(s1, s2);
            Assert.Equal((ox1, oy1), (ox2, oy2));
        }
    }

    [Fact]
    public void Detail_is_scattered_on_a_minority_of_tiles_and_uses_every_kind()
    {
        int picked = 0;
        var kinds = new HashSet<FloorDetailKind>();
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                if (FloorDetail.TryPick(x, y, out var stamp, out _, out _))
                {
                    picked++;
                    kinds.Add(stamp!.Kind);
                }

        Assert.InRange(picked, 2000, 4500);
        Assert.Equal(Enum.GetValues<FloorDetailKind>().Length, kinds.Count);
    }

    [Fact]
    public void Offsets_stay_inside_the_anchor_tile_and_stamps_reach_at_most_one_tile()
    {
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
            {
                if (!FloorDetail.TryPick(x, y, out var stamp, out int ox, out int oy)) continue;
                Assert.InRange(ox, 0, 31);
                Assert.InRange(oy, 0, 31);
                foreach (var p in stamp!.Pixels)
                {
                    Assert.InRange(p.X, -32, 32);
                    Assert.InRange(p.Y, -32, 32);
                }
            }
    }
}
