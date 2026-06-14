using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationLavaTests
{
    private static int CountLava(TileGrid g) =>
        g.Positions().Count(p => g.Get(p) == TileType.Lava);

    [Fact]
    public void Stepping_onto_lava_burns_you()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Lava);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                                // the move resolves, then burns
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Burned, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerBurned mb && mb.MinerId == 1);
    }

    [Fact]
    public void Mining_rock_next_to_a_vent_wakes_it_and_lava_creeps()
    {
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);          // the rock capping the vent
        grid.Set(new GridPos(3, 1), TileType.LavaVent);      // vent behind it
        var cfg = new SimConfig { PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.5, LavaVentBudget = 8 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));
        m.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                       // mining completes -> (2,1) floor, vent wakes
        sim.Tick(0.5);                                       // one interval -> one ring

        Assert.Equal(TileType.Lava, grid.Get(new GridPos(2, 1)));   // crept into the breach
    }

    [Fact]
    public void Blasting_rock_next_to_a_vent_wakes_it()
    {
        var grid = new TileGrid(7, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);          // wall to plant on (blasted to floor)
        grid.Set(new GridPos(4, 2), TileType.LavaVent);      // vent beside the wall
        var cfg = new SimConfig
        {
            FuseSeconds = 0.1, PlantSeconds = 0.1, BlastRockRadius = 1, BlastKillRadius = 0,
            LavaSpreadIntervalSeconds = 0.5, LavaVentBudget = 8,
        };
        var sim = new Simulation(grid, cfg);
        var planter = sim.AddMiner(1, new GridPos(3, 3));
        planter.Facing = Direction.North;                    // faces (3,2)

        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.1);                                       // plant completes -> charge armed
        sim.Tick(0.1);                                       // fuse -> detonate -> (3,2) floor, vent wakes
        sim.Tick(0.5);                                       // one ring

        Assert.Equal(TileType.Lava, grid.Get(new GridPos(3, 2)));
    }

    [Fact]
    public void A_vent_spreads_one_ring_only_after_each_interval()
    {
        var grid = new TileGrid(9, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.Rock);
        grid.Set(new GridPos(3, 2), TileType.LavaVent);
        var cfg = new SimConfig { PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.5, LavaVentBudget = 50 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 2));
        m.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                       // wake (Timer = 0)

        sim.Tick(0.3);                                       // < interval -> no spread yet
        Assert.Equal(0, CountLava(grid));

        sim.Tick(0.3);                                       // 0.6 >= 0.5 -> exactly one ring
        Assert.Equal(4, CountLava(grid));                    // (2,2),(4,2),(3,1),(3,3): the vent's floor neighbors
    }

    [Fact]
    public void Lava_stops_after_spending_its_budget()
    {
        var grid = new TileGrid(15, 15, TileType.Floor);
        grid.Set(new GridPos(2, 7), TileType.Rock);
        grid.Set(new GridPos(3, 7), TileType.LavaVent);
        var cfg = new SimConfig { PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.1, LavaVentBudget = 6 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 7));
        m.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                       // wake
        for (int i = 0; i < 40; i++) sim.Tick(0.1);          // far more intervals than the budget

        Assert.Equal(6, CountLava(grid));                    // never exceeds LavaVentBudget
    }

    [Fact]
    public void Lava_meeting_water_solidifies_to_a_cracked_crust()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        grid.Set(new GridPos(3, 1), TileType.LavaVent);
        grid.Set(new GridPos(5, 1), TileType.ShallowWater);  // water two tiles east of the vent
        var cfg = new SimConfig { PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.1, LavaVentBudget = 30 };
        var sim = new Simulation(grid, cfg);
        var m = sim.AddMiner(1, new GridPos(1, 1));
        m.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                       // wake
        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        // (4,1) touches water (5,1) -> quenches to Cracked, lava stops there; the water is intact.
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(4, 1)));
        Assert.Equal(TileType.ShallowWater, grid.Get(new GridPos(5, 1)));
    }

    [Fact]
    public void Lava_spreading_onto_a_standing_miner_burns_them()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        grid.Set(new GridPos(3, 1), TileType.LavaVent);
        var cfg = new SimConfig { PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.1, LavaVentBudget = 30 };
        var sim = new Simulation(grid, cfg);
        var miner = sim.AddMiner(1, new GridPos(1, 1));       // breaches, then stands still
        miner.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                        // (2,1)->floor, vent wakes
        for (int i = 0; i < 10; i++) sim.Tick(0.1);          // flow reaches (1,1)

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Burned, miner.DeathCause);
    }

    [Fact]
    public void A_quenched_crust_collapses_like_cave_in_ground()
    {
        var grid = new TileGrid(9, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        grid.Set(new GridPos(3, 1), TileType.LavaVent);
        grid.Set(new GridPos(5, 1), TileType.ShallowWater);
        var cfg = new SimConfig
        {
            PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.1, LavaVentBudget = 30, CrackDwellSeconds = 0.5,
        };
        var sim = new Simulation(grid, cfg);
        var breacher = sim.AddMiner(1, new GridPos(1, 1));
        breacher.Facing = Direction.East;

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                        // wake
        for (int i = 0; i < 5; i++) sim.Tick(0.1);           // quench (4,1) -> Cracked
        Assert.Equal(TileType.Cracked, grid.Get(new GridPos(4, 1)));

        // A second miner lingers on the lava-made crust and it gives way -> Crushed.
        // No cave-in map toggle is involved; the fragility is purely tile-driven.
        var stander = sim.AddMiner(2, new GridPos(4, 1));
        sim.Tick(0.5);                                        // dwell >= CrackDwellSeconds
        Assert.False(stander.Alive);
        Assert.Equal(DeathCause.Crushed, stander.DeathCause);
        Assert.Equal(TileType.Pit, grid.Get(new GridPos(4, 1)));
    }
}
