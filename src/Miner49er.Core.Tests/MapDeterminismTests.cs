using System.Linq;
using Miner49er.Core;
using Xunit;

public class MapDeterminismTests
{
    private static MapConfig Config() => new() { Seed = 4242, PlayerCount = 4 };

    [Fact]
    public void Same_seed_produces_identical_grid()
    {
        var a = MapGenerator.Generate(Config());
        var b = MapGenerator.Generate(Config());

        Assert.Equal(a.Grid.Width, b.Grid.Width);
        Assert.Equal(a.Grid.Height, b.Grid.Height);
        foreach (var p in a.Grid.Positions())
            Assert.Equal(a.Grid.Get(p), b.Grid.Get(p));
    }

    [Fact]
    public void Same_seed_produces_identical_spawns()
    {
        var a = MapGenerator.Generate(Config());
        var b = MapGenerator.Generate(Config());
        Assert.Equal(a.Spawns.ToList(), b.Spawns.ToList());
    }
}
