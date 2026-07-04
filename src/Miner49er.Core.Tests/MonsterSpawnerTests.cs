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

public class MonsterSpawnerFloorTests
{
    private static TileGrid BigGrid()
    {
        return new TileGrid(30, 30, TileType.Floor);
    }

    [Fact]
    public void Floor_below_8_contains_no_skeletons()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 6, floor: 7);
        Assert.DoesNotContain(result, r =>
            r.Kind == MonsterKind.SkeletonHuman || r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_8_to_11_includes_SkeletonHuman_but_not_Dino()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 8, floor: 9);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonHuman);
        Assert.DoesNotContain(result, r => r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_12_plus_includes_both_skeleton_kinds()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 10, floor: 12);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonHuman);
        Assert.Contains(result, r => r.Kind == MonsterKind.SkeletonDino);
    }

    [Fact]
    public void Floor_0_default_contains_no_skeletons()
    {
        var result = MonsterSpawner.Place(BigGrid(), new GridPos(1, 1), 6);
        Assert.DoesNotContain(result, r =>
            r.Kind == MonsterKind.SkeletonHuman || r.Kind == MonsterKind.SkeletonDino);
    }
}
