using Miner49er.Core;
using Xunit;

public class TempleLayoutTests
{
    [Theory]
    [InlineData(4, 218157624u)]
    [InlineData(8, 3306006633u)]
    [InlineData(16, 3998286956u)]
    [InlineData(24, 2626397662u)]
    public void An_ordinary_floor_generates_exactly_as_before(int floor, uint expected)
    {
        Assert.Equal(expected, Fingerprint(MapGenerator.Generate(MapConfig.FloorConfig(floor, 123)).Grid));
    }

    [Fact]
    public void Every_temple_floor_has_at_least_one_section()
    {
        foreach (var map in TempleMaps()) Assert.NotEmpty(map.TempleSections);
    }

    [Fact]
    public void The_same_seed_gives_the_same_temple()
    {
        var (floor, seed) = TempleFloors().First();
        var a = MapGenerator.Generate(MapConfig.FloorConfig(floor, seed));
        var b = MapGenerator.Generate(MapConfig.FloorConfig(floor, seed));
        Assert.Equal(Fingerprint(a.Grid), Fingerprint(b.Grid));
        Assert.Equal(a.Sconces, b.Sconces);
    }

    [Fact]
    public void A_section_stays_sealed_from_the_caves()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
                foreach (var p in section.Floor)
                    foreach (var n in Neighbours(p))
                        Assert.NotEqual(TileType.Floor, map.Grid.Get(n));
    }

    [Fact]
    public void Solid_rock_hides_the_marble_from_the_caves()
    {
        foreach (var map in TempleMaps())
            foreach (var p in map.Grid.Positions().Where(p => map.Grid.Get(p) is TileType.MarbleWall or TileType.MarblePanel))
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var tile = map.Grid.Get(new GridPos(p.X + dx, p.Y + dy));
                        Assert.True(tile.IsTemple() || tile.IsMinable(), $"{tile} beside marble at {p}");
                    }
    }

    [Fact]
    public void Each_doorway_is_rock_that_can_be_mined()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
                Assert.True(map.Grid.Get(section.Doorway).IsMinable());
    }

    [Fact]
    public void A_miner_can_dig_from_the_spawn_to_every_section()
    {
        foreach (var map in TempleMaps())
        {
            var dug = DigFrom(map.Grid, map.Spawns[0]);
            foreach (var section in map.TempleSections)
                Assert.Contains(section.Floor[0], dug);
        }
    }

    [Fact]
    public void Rock_fallen_across_a_doorway_can_still_be_dug_through()
    {
        foreach (var map in TempleMaps())
        {
            foreach (var section in map.TempleSections)
                foreach (var n in Neighbours(section.Doorway))
                    if (map.Grid.Get(n) == TileType.Floor) map.Grid.Set(n, TileType.Rock);

            var dug = DigFrom(map.Grid, map.Spawns[0]);
            foreach (var section in map.TempleSections)
                Assert.Contains(section.Doorway, dug);
        }
    }

    [Fact]
    public void Every_part_of_a_room_can_be_reached_from_its_doorway()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
            {
                var dug = DigFrom(map.Grid, section.Doorway);
                var open = section.Floor.Where(p => map.Grid.Get(p) is TileType.TempleFloor or TileType.Jar);
                Assert.All(open, p => Assert.Contains(p, dug));
            }
    }

    [Fact]
    public void A_rockslide_leaves_temple_floor_alone()
    {
        var grid = new TileGrid(7, 7, TileType.TempleFloor);
        grid.Set(new GridPos(3, 3), TileType.VolatileRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(0, 3)).Facing = Direction.East;
        grid.Set(new GridPos(1, 3), TileType.VolatileRock);
        sim.TryStartMining(1);
        for (int i = 0; i < 5; i++) sim.Tick(0.1);

        Assert.Equal(TileType.TempleFloor, grid.Get(new GridPos(2, 2)));
    }

    [Fact]
    public void A_section_has_its_altar_and_columns()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
            {
                var tiles = Area(section).Select(map.Grid.Get).ToList();
                Assert.Equal(1, tiles.Count(t => t == TileType.Altar));
                Assert.Contains(TileType.Column, tiles);
                Assert.Contains(TileType.FallenColumn, tiles);
            }
    }

    [Fact]
    public void Jars_stand_in_clusters_of_three_to_six()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
            {
                var left = new HashSet<GridPos>(section.Jars);
                Assert.NotEmpty(left);
                while (left.Count > 0)
                {
                    var cluster = new Queue<GridPos>(new[] { left.First() });
                    left.Remove(cluster.Peek());
                    int size = 0;
                    while (cluster.Count > 0)
                    {
                        size++;
                        foreach (var n in Neighbours(cluster.Dequeue()))
                            if (left.Remove(n)) cluster.Enqueue(n);
                    }
                    Assert.InRange(size, 3, 6);
                }
            }
    }

    [Fact]
    public void A_section_has_panels_to_mine()
    {
        foreach (var map in TempleMaps())
            foreach (var section in map.TempleSections)
            {
                Assert.True(section.Panels.Count >= 2);
                Assert.All(section.Panels, p => Assert.Equal(TileType.MarblePanel, map.Grid.Get(p)));
            }
    }

    [Fact]
    public void Sconces_hang_on_marble_walls()
    {
        foreach (var map in TempleMaps())
        {
            Assert.NotEmpty(map.Sconces);
            Assert.All(map.Sconces, p => Assert.Equal(TileType.MarbleWall, map.Grid.Get(p)));
        }
    }

    [Fact]
    public void An_ordinary_floor_has_no_sections_or_sconces()
    {
        var map = MapGenerator.Generate(MapConfig.FloorConfig(4, 123));
        Assert.Empty(map.TempleSections);
        Assert.Empty(map.Sconces);
    }

    private static IEnumerable<(int Floor, int Seed)> TempleFloors() =>
        from seed in Enumerable.Range(0, 60)
        from floor in Enumerable.Range(5, 16)   // the smallest maps are the hardest to fit
        where MapConfig.IsTempleFloor(floor, seed)
        select (floor, seed);

    // Fresh copies each call, because one test drops rock on them. The list itself is cached:
    // deep floors take a second each, so regenerating per test takes minutes.
    private static readonly List<(int Floor, int Seed)> Sample = TempleFloors().Take(30).ToList();

    private static List<GeneratedMap> TempleMaps() =>
        Sample.Select(t => MapGenerator.Generate(MapConfig.FloorConfig(t.Floor, MapConfig.FloorSeed(t.Seed, t.Floor)))).ToList();

    private static IEnumerable<GridPos> Area(TempleSection s)
    {
        int x0 = s.Floor.Min(p => p.X), x1 = s.Floor.Max(p => p.X);
        int y0 = s.Floor.Min(p => p.Y), y1 = s.Floor.Max(p => p.Y);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                yield return new GridPos(x, y);
    }

    private static IEnumerable<GridPos> Neighbours(GridPos p)
    {
        yield return new GridPos(p.X + 1, p.Y);
        yield return new GridPos(p.X - 1, p.Y);
        yield return new GridPos(p.X, p.Y + 1);
        yield return new GridPos(p.X, p.Y - 1);
    }

    // Everywhere a miner could get to by walking and mining.
    private static HashSet<GridPos> DigFrom(TileGrid grid, GridPos start)
    {
        var seen = new HashSet<GridPos> { start };
        var queue = new Queue<GridPos>(seen);
        while (queue.Count > 0)
            foreach (var n in Neighbours(queue.Dequeue()))
            {
                if (!grid.InBounds(n) || seen.Contains(n)) continue;
                var t = grid.Get(n);
                if ((t.IsEnterable() && !t.IsLethal()) || t.IsMinable()) { seen.Add(n); queue.Enqueue(n); }
            }
        return seen;
    }

    private static uint Fingerprint(TileGrid grid)
    {
        uint h = 2166136261;
        foreach (var p in grid.Positions()) h = (h ^ (uint)grid.Get(p)) * 16777619;
        return h;
    }
}
