using Miner49er.Core;
using Xunit;

public class DirectionTests
{
    [Theory]
    [InlineData(Direction.North, 0, -1)]
    [InlineData(Direction.East, 1, 0)]
    [InlineData(Direction.South, 0, 1)]
    [InlineData(Direction.West, -1, 0)]
    public void ToOffset_returns_expected(Direction d, int x, int y)
    {
        Assert.Equal(new GridPos(x, y), d.ToOffset());
    }

    [Fact]
    public void Add_and_operator_plus_agree()
    {
        var a = new GridPos(3, 4);
        Assert.Equal(new GridPos(4, 4), a + Direction.East.ToOffset());
        Assert.Equal(new GridPos(4, 4), a.Add(Direction.East.ToOffset()));
    }

    [Fact]
    public void Distances_are_correct()
    {
        var a = new GridPos(0, 0);
        var b = new GridPos(3, 2);
        Assert.Equal(5, a.ManhattanTo(b));
        Assert.Equal(3, a.ChebyshevTo(b));
    }
}
