using System.Linq;
using Miner49er.Core;
using Xunit;

public class MonsterSpawnerTests
{
    [Fact]
    public void Places_the_requested_count_on_floor_away_from_the_start()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);

        var spawns = MonsterSpawner.Place(grid, start, 4);

        Assert.Equal(4, spawns.Count);
        Assert.All(spawns, s => Assert.Equal(TileType.Floor, grid.Get(s.Pos)));
        Assert.DoesNotContain(spawns, s => s.Pos == start);
        // farthest-first from the start: the nearest spawn is still well away from it.
        Assert.All(spawns, s => Assert.True(s.Pos.ManhattanTo(start) >= 10,
            $"spawn too close to start: {s.Pos}"));
    }

    [Fact]
    public void Kinds_cycle_deterministically_and_results_are_stable()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);

        var a = MonsterSpawner.Place(grid, start, 3);
        var b = MonsterSpawner.Place(grid, start, 3);

        Assert.Equal(a, b);   // deterministic
        Assert.Equal(new[] { MonsterKind.Slime, MonsterKind.Ghost, MonsterKind.Goat },
                     a.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void Zero_or_negative_count_yields_nothing()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        Assert.Empty(MonsterSpawner.Place(grid, new GridPos(1, 1), 0));
    }
}
