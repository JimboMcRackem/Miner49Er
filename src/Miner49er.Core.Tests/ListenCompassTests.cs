using System.Collections.Generic;
using Miner49er.Core;
using Xunit;

public class ListenCompassTests
{
    private static GridPos Self => new(10, 10);

    [Fact]
    public void Empty_others_returns_null()
    {
        Assert.Null(ListenCompass.NearestDirection(Self, new List<GridPos>()));
    }

    [Theory]
    [InlineData(10, 5, CompassDirection.N)]    // straight up (-Y)
    [InlineData(15, 10, CompassDirection.E)]   // right
    [InlineData(10, 15, CompassDirection.S)]   // down (+Y)
    [InlineData(5, 10, CompassDirection.W)]    // left
    [InlineData(13, 7, CompassDirection.NE)]   // up-right
    [InlineData(13, 13, CompassDirection.SE)]  // down-right
    [InlineData(7, 13, CompassDirection.SW)]   // down-left
    [InlineData(7, 7, CompassDirection.NW)]    // up-left
    public void Single_other_buckets_to_expected_direction(int x, int y, CompassDirection expected)
    {
        var dir = ListenCompass.NearestDirection(Self, new[] { new GridPos(x, y) });
        Assert.Equal(expected, dir);
    }

    [Fact]
    public void Picks_the_nearest_of_several()
    {
        var others = new[] { new GridPos(5, 10), new GridPos(20, 10) }; // west dist 5, east dist 10
        Assert.Equal(CompassDirection.W, ListenCompass.NearestDirection(Self, others));
    }
}
