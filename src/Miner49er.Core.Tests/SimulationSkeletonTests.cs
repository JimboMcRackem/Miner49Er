using Miner49er.Core;
using System.Linq;
using Xunit;

public class SimulationSkeletonTests
{
    private static SimConfig SkeletonCfg() => new SimConfig
    {
        SkeletonExplosionWakeRadius = 12,
        SkeletonPickaxeWakeRadius   = 3,
        SkeletonStoneWakeRadius     = 8,
        MonsterSkeletonMoveSeconds      = 0.7,
        MonsterSkeletonDinoMoveSeconds  = 1.2,
        PickaxeSeconds  = 0.01,
        PlantSeconds    = 0.01,
        FuseSeconds     = 0.01,
        BlastRockRadius = 1,
        BlastKillRadius = 0,
    };

    private static TileGrid MakeGrid(int w = 15, int h = 15)
    {
        var g = new TileGrid(w, h, TileType.Floor);
        for (int x = 0; x < w; x++) { g.Set(new GridPos(x, 0), TileType.Rock); g.Set(new GridPos(x, h-1), TileType.Rock); }
        for (int y = 0; y < h; y++) { g.Set(new GridPos(0, y), TileType.Rock); g.Set(new GridPos(w-1, y), TileType.Rock); }
        return g;
    }

    [Fact]
    public void Skeleton_starts_dormant()
    {
        var sim = new Simulation(MakeGrid(), SkeletonCfg());
        sim.AddMonster(1, new GridPos(7, 7), MonsterKind.SkeletonHuman);
        Assert.True(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Dormant_skeleton_does_not_move()
    {
        var sim = new Simulation(MakeGrid(), SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.AddMonster(1, new GridPos(7, 7), MonsterKind.SkeletonHuman);
        sim.Tick(5.0);
        Assert.Equal(new GridPos(7, 7), sim.Monsters.First().Pos);
    }

    [Fact]
    public void Skeleton_wakes_on_nearby_explosion()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.05); // planting completes, charge added (not yet advanced)
        sim.Tick(0.05); // fuse fires, explosion noise → wake check runs
        Assert.False(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Skeleton_stays_dormant_if_explosion_is_far()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(2, 2), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(2, 3));
        sim.AddMonster(1, new GridPos(12, 12), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.05); // planting completes, charge added
        sim.Tick(0.05); // fuse fires — skeleton too far to wake
        Assert.True(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Skeleton_wakes_on_pickaxe_within_3_tiles()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        sim.TryStartMining(1);
        sim.Tick(0.05);
        Assert.False(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Skeleton_stays_dormant_if_pickaxe_is_beyond_3_tiles()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 10), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        sim.TryStartMining(1);
        sim.Tick(0.05);
        Assert.True(sim.Monsters.First().Dormant);
    }

    [Fact]
    public void Waking_emits_SkeletonAroused_event()
    {
        var grid = MakeGrid();
        grid.Set(new GridPos(5, 4), TileType.Rock);
        var sim = new Simulation(grid, SkeletonCfg());
        sim.AddMiner(1, new GridPos(5, 5));
        sim.AddMonster(1, new GridPos(5, 7), MonsterKind.SkeletonHuman);
        sim.TryMove(1, Direction.North);
        sim.TryStartPlanting(1);
        sim.Tick(0.05); // planting completes, charge added
        sim.Tick(0.05); // fuse fires, SkeletonAroused should be in this tick's events
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is SkeletonAroused a && a.MonsterId == 1);
    }
}
