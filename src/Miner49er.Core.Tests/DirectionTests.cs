using Miner49er.Core;
using Xunit;

public class DirectionTests
{
    [Fact]
    public void North_offset_points_up()
    {
        Assert.Equal(new GridPos(0, -1), Direction.North.ToOffset());
    }
}
