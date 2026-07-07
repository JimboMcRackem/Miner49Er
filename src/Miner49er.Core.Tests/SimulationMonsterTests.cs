using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMonsterTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    [Fact]
    public void AddMonster_registers_a_living_monster()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor));
        var mo = sim.AddMonster(1, new GridPos(2, 2), MonsterKind.Slime);

        Assert.True(mo.Alive);
        Assert.Equal(MonsterKind.Slime, mo.Kind);
        Assert.Single(sim.Monsters);
        Assert.Equal(new GridPos(2, 2), sim.Monsters[0].Pos);
    }

    [Fact]
    public void Slime_steps_toward_the_miner_when_within_sense_radius()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var sim = Sim(new TileGrid(9, 3, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);   // cooldown started at cadence (0.1) -> elapses this tick -> one step

        Assert.Equal(new GridPos(3, 1), slime.Pos);   // moved east, toward the miner
    }

    [Fact]
    public void Slime_is_blocked_by_rock()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);     // wall east of the slime
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));             // miner is east, slime wants to go east
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(2, 1), slime.Pos);     // rock blocked the step; stayed put
    }

    [Fact]
    public void Ghost_drifts_through_rock_toward_the_miner()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);     // solid wall between ghost and miner
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

        sim.Tick(0.1);   // steps east into the rock tile (phasing)

        Assert.Equal(new GridPos(3, 1), ghost.Pos);
    }

    [Fact]
    public void Goat_charges_in_a_straight_line()
    {
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 0 };
        var sim = Sim(new TileGrid(6, 3, TileType.Floor), cfg);
        var goat = sim.AddMonster(1, new GridPos(1, 1), MonsterKind.Goat);
        goat.ChargeDir = Direction.East;

        sim.Tick(0.1);
        sim.Tick(0.1);

        Assert.Equal(new GridPos(3, 1), goat.Pos);   // two straight steps east
    }

    [Fact]
    public void Goat_reaims_toward_the_miner_when_it_hits_a_wall()
    {
        // A miner due south makes the re-aim deterministic (toward = South), avoiding the
        // randomness of a wall-bounce with no target in range.
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(4, 4, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);   // wall directly east of the goat
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(1, 3));           // due south of the goat
        var goat = sim.AddMonster(1, new GridPos(1, 1), MonsterKind.Goat);
        goat.ChargeDir = Direction.East;

        sim.Tick(0.1);   // east is blocked: re-aims toward the miner, does not move this step

        Assert.Equal(new GridPos(1, 1), goat.Pos);
        Assert.Equal(Direction.South, goat.ChargeDir);
    }

    [Fact]
    public void Monster_stepping_onto_the_miner_mauls_them()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var sim = Sim(new TileGrid(5, 3, TileType.Floor), cfg);
        var miner = sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);   // one step east = onto the miner

        sim.Tick(0.1);

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Slimed, miner.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerMauled mm && mm.MinerId == 1);
    }

    [Fact]
    public void Miner_walking_into_a_monster_is_mauled()
    {
        var sim = Sim(new TileGrid(5, 3, TileType.Floor));
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);   // miner steps east into it

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);
        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Slimed, miner.DeathCause);
    }

    [Fact]
    public void Slime_chasing_across_a_pit_falls_in_and_dies()
    {
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 6 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Pit);      // pit between slime and miner
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);

        sim.Tick(0.1);   // steps east onto the pit
        Assert.False(slime.Alive);
        Assert.Equal(new GridPos(3, 1), slime.Pos);
        Assert.Contains(sim.DrainEvents(), e => e is MonsterKilled mk && mk.MonsterId == 1);
    }

    [Fact]
    public void Ghost_floats_over_a_pit_unharmed()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Pit);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

        sim.Tick(0.1);   // drifts east onto the pit tile

        Assert.True(ghost.Alive);
        Assert.Equal(new GridPos(3, 1), ghost.Pos);
    }

    [Fact]
    public void Blast_banishes_a_monster_in_range()
    {
        var cfg = new SimConfig
        {
            FuseSeconds = 0.1, PlantSeconds = 0.1, BlastKillRadius = 1, BlastRockRadius = 1,
            MonsterGhostMoveSeconds = 999,   // hold the ghost still so it stays in blast range
        };
        var grid = new TileGrid(7, 5, TileType.Floor);
        grid.Set(new GridPos(3, 2), TileType.Rock);     // wall to plant the charge on
        var sim = Sim(grid, cfg);
        var planter = sim.AddMiner(1, new GridPos(3, 3));
        planter.Facing = Direction.North;               // faces (3,2)
        var ghost = sim.AddMonster(1, new GridPos(3, 1), MonsterKind.Ghost);   // adjacent to the wall

        Assert.True(sim.TryStartPlanting(1));
        sim.Tick(0.1);   // plant completes -> charge armed
        sim.Tick(0.1);   // fuse fires -> detonation

        Assert.False(ghost.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MonsterKilled mk && mk.MonsterId == 1);
    }

    [Fact]
    public void Same_seed_reproduces_identical_wander_paths()
    {
        System.Collections.Generic.List<GridPos> Run()
        {
            var cfg = new SimConfig { Seed = 1234, MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 0 };
            var sim = Sim(new TileGrid(11, 11, TileType.Floor), cfg);
            var slime = sim.AddMonster(1, new GridPos(5, 5), MonsterKind.Slime);   // no miner -> pure wander
            var path = new System.Collections.Generic.List<GridPos>();
            for (int i = 0; i < 40; i++) { sim.Tick(0.1); path.Add(slime.Pos); }
            return path;
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Lava_creeping_under_a_stationary_monster_kills_it()
    {
        // Vent at (3,1) capped by rock at (2,1); a slime sits on (4,1) and never moves
        // (huge cadence). Mining the cap wakes the vent; the first ring spreads lava onto
        // (4,1) and the stationary slime must die.
        var cfg = new SimConfig
        {
            PickaxeSeconds = 0.1, LavaSpreadIntervalSeconds = 0.5, LavaVentBudget = 8,
            MonsterSlimeMoveSeconds = 999,   // hold the slime still
            MonsterSenseRadius = 0,
        };
        var grid = new TileGrid(7, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);       // cap
        grid.Set(new GridPos(3, 1), TileType.LavaVent);   // vent
        var sim = new Simulation(grid, cfg);
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        miner.Facing = Direction.East;
        var slime = sim.AddMonster(1, new GridPos(4, 1), MonsterKind.Slime);

        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);    // mining completes -> (2,1) floor, vent wakes
        sim.Tick(0.5);    // one spread interval -> lava reaches (4,1) under the slime

        Assert.Equal(TileType.Lava, grid.Get(new GridPos(4, 1)));
        Assert.False(slime.Alive);
        Assert.Contains(sim.DrainEvents(), e => e is MonsterKilled mk && mk.MonsterId == 1);
    }

    [Fact]
    public void Ghost_cannot_enter_deep_water()
    {
        var cfg = new SimConfig { MonsterGhostMoveSeconds = 0.1, MonsterSenseRadius = 99 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.DeepWater);   // deep water east of ghost
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));                 // miner is east, ghost wants to go east
        var ghost = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Ghost);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(2, 1), ghost.Pos);   // deep water blocked the ghost
    }

    [Fact]
    public void Slime_on_mold_tile_is_slowed()
    {
        // Slime cadence 0.1; slow factor 1.6 → effective cadence after landing = 0.1 * 1.6 = 0.16.
        // Initial cooldown = cadence (0.1), so first step lands exactly at Tick(0.1) with no carry.
        // Reset = 0.0 + 0.1 * 1.6 = 0.16; second Tick(0.1) leaves 0.06 remaining → no step.
        var cfg = new SimConfig { MonsterSlimeMoveSeconds = 0.1, MonsterSenseRadius = 99,
                                  MoldSlowFactor = 1.6, MoldSlowSeconds = 3.0 };
        var grid = new TileGrid(9, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var slime = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Slime);
        sim.DropMoldAt(new GridPos(3, 1));

        sim.Tick(0.1);   // slime moves from (2,1) to (3,1), lands on mold
        sim.Tick(0.1);   // cooldown = 0.16 - 0.1 = 0.06 → no step yet

        Assert.Equal(new GridPos(3, 1), slime.Pos);
    }

    [Fact]
    public void Goat_on_mold_tile_is_slowed()
    {
        // Goat cadence 0.15; slow factor 1.6 → reset after landing = 0.15 * 1.6 = 0.24.
        // Second Tick(0.15) leaves 0.09 remaining → no step.
        var cfg = new SimConfig { MonsterGoatMoveSeconds = 0.15, MonsterSenseRadius = 99,
                                  MoldSlowFactor = 1.6, MoldSlowSeconds = 3.0 };
        var grid = new TileGrid(9, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var goat = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.Goat);
        sim.DropMoldAt(new GridPos(3, 1));

        sim.Tick(0.15);   // goat steps onto (3,1), lands on mold
        sim.Tick(0.15);   // cooldown = 0.24 - 0.15 = 0.09 → no step yet

        Assert.Equal(new GridPos(3, 1), goat.Pos);
    }

    [Fact]
    public void ZombieMiner_steps_toward_miner_regardless_of_distance()
    {
        // Zombie has no sense radius — it always knows where the miner is.
        var cfg = new SimConfig { MonsterZombieMoveSeconds = 0.1, MonsterSenseRadius = 0 };
        var sim = Sim(new TileGrid(20, 3, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(19, 1));
        var zombie = sim.AddMonster(1, new GridPos(0, 1), MonsterKind.ZombieMiner);

        sim.Tick(0.1);   // distance = 19, well beyond any sense radius

        Assert.Equal(new GridPos(1, 1), zombie.Pos);   // still moved toward the miner
    }

    [Fact]
    public void ZombieMiner_is_blocked_by_rock()
    {
        var cfg = new SimConfig { MonsterZombieMoveSeconds = 0.1 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Rock);
        var sim = Sim(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var zombie = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.ZombieMiner);

        sim.Tick(0.1);

        Assert.Equal(new GridPos(2, 1), zombie.Pos);   // rock blocked the step
    }

    [Fact]
    public void ZombieMiner_mauls_miner_on_contact()
    {
        var cfg = new SimConfig { MonsterZombieMoveSeconds = 0.1 };
        var sim = Sim(new TileGrid(5, 3, TileType.Floor), cfg);
        var miner = sim.AddMiner(1, new GridPos(3, 1));
        var zombie = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.ZombieMiner);

        sim.Tick(0.1);   // zombie steps onto miner's tile

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Mauled, miner.DeathCause);
    }

    [Fact]
    public void WaterSnake_moves_fast_in_shallow_water()
    {
        var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.3, MonsterSenseRadius = 10 };
        var grid = new TileGrid(9, 3, TileType.ShallowWater);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

        sim.Tick(0.3);

        Assert.Equal(new GridPos(3, 1), snake.Pos);
    }

    [Fact]
    public void WaterSnake_moves_slow_on_floor()
    {
        var cfg = new SimConfig
        {
            MonsterWaterSnakeLandMoveSeconds  = 0.7,
            MonsterWaterSnakeWaterMoveSeconds = 0.35,
            MonsterSenseRadius = 10
        };
        var grid = new TileGrid(9, 3, TileType.Floor);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(8, 1));
        var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

        sim.Tick(0.35);  // less than land cadence — should NOT have moved yet

        Assert.Equal(new GridPos(2, 1), snake.Pos);

        sim.Tick(0.35);  // total 0.70s — now it should have moved

        Assert.Equal(new GridPos(3, 1), snake.Pos);
    }

    [Fact]
    public void WaterSnake_survives_deep_water()
    {
        var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.1, MonsterSenseRadius = 0 };
        var grid = new TileGrid(5, 3, TileType.DeepWater);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(0, 0));
        var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

        sim.Tick(0.1);
        sim.Tick(0.1);

        Assert.True(snake.Alive);
    }

    [Fact]
    public void WaterSnake_dies_on_lava()
    {
        var cfg = new SimConfig { MonsterWaterSnakeLandMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(3, 1), TileType.Lava);
        var sim = new Simulation(grid, cfg);
        sim.AddMiner(1, new GridPos(4, 1));
        var snake = sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

        sim.Tick(0.1);

        Assert.False(snake.Alive);
    }

    [Fact]
    public void WaterSnake_kills_miner_with_Bitten_cause()
    {
        var cfg = new SimConfig { MonsterWaterSnakeWaterMoveSeconds = 0.1, MonsterSenseRadius = 10 };
        var grid = new TileGrid(5, 3, TileType.ShallowWater);
        var sim = new Simulation(grid, cfg);
        var miner = sim.AddMiner(1, new GridPos(3, 1));
        sim.AddMonster(1, new GridPos(2, 1), MonsterKind.WaterSnake);

        sim.Tick(0.1);

        Assert.False(miner.Alive);
        Assert.Equal(DeathCause.Bitten, miner.DeathCause);
    }

    [Theory]
    [InlineData(5)] [InlineData(7)] [InlineData(12)] [InlineData(30)]
    public void KindsForFloor_includes_WaterSnake_from_floor_5(int floor)
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);
        var spawns = MonsterSpawner.Place(grid, start, 7, floor);
        Assert.Contains(spawns, s => s.Kind == MonsterKind.WaterSnake);
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(4)]
    public void KindsForFloor_excludes_WaterSnake_below_floor_5(int floor)
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var start = new GridPos(1, 1);
        var spawns = MonsterSpawner.Place(grid, start, 7, floor);
        Assert.DoesNotContain(spawns, s => s.Kind == MonsterKind.WaterSnake);
    }
}
