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
    public void Large_map_is_capped_at_five()
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
}
