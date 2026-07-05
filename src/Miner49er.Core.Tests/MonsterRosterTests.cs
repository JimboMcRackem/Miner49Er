using Miner49er.Core;
using Xunit;

public class MonsterRosterTests
{
    [Fact]
    public void Small_map_gets_the_floor_of_three()
    {
        Assert.Equal(3, MonsterRoster.CountFor(24, 24));
    }

    [Fact]
    public void Large_map_returns_area_scaled_count()
    {
        Assert.Equal(5, MonsterRoster.CountFor(40, 40));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void Never_below_three(int w, int h)
    {
        Assert.True(MonsterRoster.CountFor(w, h) >= 3);
    }

    [Fact]
    public void Floor_bonus_zero_before_floor_8()
    {
        Assert.Equal(3, MonsterRoster.CountFor(24, 24, 7));
    }

    [Fact]
    public void Floor_8_adds_one()
    {
        Assert.Equal(4, MonsterRoster.CountFor(24, 24, 8));
    }

    [Fact]
    public void Floor_14_adds_two()
    {
        Assert.Equal(5, MonsterRoster.CountFor(24, 24, 14));
    }

    [Fact]
    public void Floor_bonus_is_capped_at_floor_max()
    {
        Assert.Equal(9, MonsterRoster.CountFor(48, 48, 20));
        Assert.Equal(10, MonsterRoster.CountFor(200, 200, 20));
    }
}
