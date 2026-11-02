using Miner49er.Core;
using Xunit;

public class PriestSpiritTests
{
    private static readonly SimConfig Config = new() { PriestChantSeconds = 1.0, MonsterSkeletonMoveSeconds = 0.1 };

    private static TileGrid Hall(int width = 16, int height = 5)
    {
        var grid = new TileGrid(width, height, TileType.MarbleWall);
        for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
                grid.Set(new GridPos(x, y), TileType.TempleFloor);
        return grid;
    }

    private static void Run(Simulation sim, double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds / 0.1); i++) sim.Tick(0.1);
    }

    [Fact]
    public void A_priest_stays_on_temple_floor()
    {
        var grid = Hall();
        grid.Set(new GridPos(14, 2), TileType.Floor);   // an opened doorway
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(14, 2));
        var priest = sim.AddMonster(1, new GridPos(4, 2), MonsterKind.PriestSpirit);

        for (int i = 0; i < 2000; i++)
        {
            sim.Tick(0.1);
            Assert.Equal(TileType.TempleFloor, grid.Get(priest.Pos));
        }
    }

    [Fact]
    public void A_priest_does_not_chant_at_once()
    {
        var sim = new Simulation(Hall(), Config);
        sim.AddMiner(1, new GridPos(12, 2));
        sim.AddMonster(1, new GridPos(8, 2), MonsterKind.PriestSpirit);
        var bones = sim.AddMonster(2, new GridPos(2, 2), MonsterKind.SkeletonHuman);

        Run(sim, 0.5);

        Assert.True(bones.Dormant);
    }

    [Fact]
    public void Its_chant_wakes_skeletons_six_tiles_away()
    {
        var sim = new Simulation(Hall(), Config);
        sim.AddMiner(1, new GridPos(13, 2));
        sim.AddMonster(1, new GridPos(8, 2), MonsterKind.PriestSpirit);
        var bones = sim.AddMonster(2, new GridPos(2, 2), MonsterKind.SkeletonHuman);

        Run(sim, 1.3);

        Assert.False(bones.Dormant);
    }

    [Fact]
    public void Its_chant_does_not_reach_seven_tiles()
    {
        var grid = Hall(24);
        var sim = new Simulation(grid, Config);
        sim.AddMiner(1, new GridPos(14, 2));
        sim.AddMonster(1, new GridPos(9, 2), MonsterKind.PriestSpirit);
        var bones = sim.AddMonster(2, new GridPos(2, 2), MonsterKind.SkeletonHuman);

        Run(sim, 1.3);

        Assert.True(bones.Dormant);
    }

    [Fact]
    public void A_priest_cannot_chant_at_a_miner_it_cannot_see()
    {
        var grid = Hall();
        for (int y = 0; y < 5; y++) grid.Set(new GridPos(10, y), TileType.MarbleWall);
        var sim = new Simulation(grid, Config);
        sim.AddMiner(1, new GridPos(13, 2));
        sim.AddMonster(1, new GridPos(8, 2), MonsterKind.PriestSpirit);
        var bones = sim.AddMonster(2, new GridPos(2, 2), MonsterKind.SkeletonHuman);

        Run(sim, 2.0);

        Assert.True(bones.Dormant);
    }

    [Fact]
    public void A_priest_shows_its_chant_coming()
    {
        var sim = new Simulation(Hall(), Config);
        sim.AddMiner(1, new GridPos(12, 2));
        var priest = sim.AddMonster(1, new GridPos(8, 2), MonsterKind.PriestSpirit);

        Run(sim, 0.2);

        Assert.Equal(GoatPhase.WindUp, priest.Phase);
    }

    [Fact]
    public void Its_touch_hurts_like_a_ghosts()
    {
        var sim = new Simulation(Hall(), new SimConfig { MonsterGhostMoveSeconds = 0.1 });
        var miner = sim.AddMiner(1, new GridPos(5, 2));
        sim.AddMonster(1, new GridPos(4, 2), MonsterKind.PriestSpirit);

        Run(sim, 3);

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Terrified, miner.DeathCause);
    }

    [Fact]
    public void A_lantern_banishes_a_priest()
    {
        var sim = new Simulation(Hall(), new SimConfig());
        sim.AddMiner(1, new GridPos(5, 2));
        sim.GiveItemForTest(1, ItemKind.Lantern);
        var priest = sim.AddMonster(1, new GridPos(6, 2), MonsterKind.PriestSpirit);

        Run(sim, 0.2);

        Assert.False(priest.Alive);
    }

    private static List<TempleSection> Sections()
    {
        var floor = new List<GridPos>();
        for (int y = 2; y < 40; y++)
            for (int x = 2; x < 40; x++)
                floor.Add(new GridPos(x, y));
        return new List<TempleSection> { new(0, floor, new List<GridPos>(), new List<GridPos>(), new GridPos(1, 2)) };
    }

    [Fact]
    public void The_temple_is_mostly_skeletons_ghosts_and_priests()
    {
        var roster = MonsterSpawner.PlaceTemple(Sections(), new TileGrid(42, 42, TileType.TempleFloor), 1000, seed: 7);
        double Share(MonsterKind k) => roster.Count(r => r.Kind == k) / (double)roster.Count;

        Assert.InRange(Share(MonsterKind.SkeletonHuman), 0.35, 0.45);
        Assert.InRange(Share(MonsterKind.Ghost), 0.25, 0.35);
        Assert.InRange(Share(MonsterKind.PriestSpirit), 0.15, 0.25);
    }

    [Fact]
    public void Every_section_has_a_priest()
    {
        var roster = MonsterSpawner.PlaceTemple(Sections(), new TileGrid(42, 42, TileType.TempleFloor), 1, seed: 7);

        Assert.Equal(MonsterKind.PriestSpirit, Assert.Single(roster).Kind);
    }

    [Fact]
    public void Temple_monsters_stand_on_open_temple_floor()
    {
        var grid = new TileGrid(42, 42, TileType.TempleFloor);
        for (int x = 2; x < 40; x++) grid.Set(new GridPos(x, 5), TileType.Column);
        var roster = MonsterSpawner.PlaceTemple(Sections(), grid, 200, seed: 7);

        Assert.All(roster, r => Assert.Equal(TileType.TempleFloor, grid.Get(r.Pos)));
        Assert.Equal(roster.Count, roster.Select(r => r.Pos).Distinct().Count());
    }

    [Fact]
    public void A_temple_floor_keeps_only_half_the_cave_monsters()
    {
        var map = MapGenerator.Generate(MapConfig.FloorConfig(4, 123));
        var ordinary = MonsterSpawner.PlaceFor(map, map.Spawns[0], 8, floor: 4, seed: 1);
        Assert.Equal(8, ordinary.Count);

        var (floor, run) = (from seed in Enumerable.Range(0, 60)
                            from f in Enumerable.Range(5, 16)
                            where MapConfig.IsTempleFloor(f, seed)
                            select (f, seed)).First();
        var temple = MapGenerator.Generate(MapConfig.FloorConfig(floor, MapConfig.FloorSeed(run, floor)));
        var roster = MonsterSpawner.PlaceFor(temple, temple.Spawns[0], 8, floor, seed: 1);

        Assert.Equal(4, roster.Count(r => temple.Grid.Get(r.Pos) != TileType.TempleFloor));
        Assert.Contains(roster, r => r.Kind == MonsterKind.PriestSpirit);
    }
}
