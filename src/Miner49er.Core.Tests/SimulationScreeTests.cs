using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationScreeTests
{
    // Helper: 5x5 grid, scree tile at (2,2), miner at (1,2) facing East
    private static Simulation SetupScree(TileType screeType, int seed = 0)
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), screeType);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1, Seed = seed });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Mining_ScreeRock_converts_tile_to_Floor()
    {
        var sim = SetupScree(TileType.ScreeRock, seed: 99);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Regardless of whether collapse fires, the mined tile is always Floor
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2)));
    }

    [Fact]
    public void Mining_UnstableRock_converts_tile_to_Floor()
    {
        var sim = SetupScree(TileType.UnstableRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 2)));
    }

    [Fact]
    public void Mining_UnstableRock_always_emits_ScreeCollapsed()
    {
        var sim = SetupScree(TileType.UnstableRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Pos == new GridPos(2, 2) && sc.Radius == 1);
    }

    [Fact]
    public void Mining_VolatileRock_emits_ScreeCollapsed_with_radius_2()
    {
        var sim = SetupScree(TileType.VolatileRock);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Radius == 2);
    }

    [Fact]
    public void Mining_UnstableRock_fills_adjacent_floor_tiles_with_Rock()
    {
        var sim = SetupScree(TileType.UnstableRock, seed: 0);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Chebyshev radius 1 around (2,2). The adjacent floor tiles become Rock.
        // (1,2) holds the miner but is still Floor underneath -> becomes Rock too.
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(3, 2)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 3)));
    }

    [Fact]
    public void Mining_UnstableRock_emits_RockFell_for_each_filled_tile()
    {
        var sim = SetupScree(TileType.UnstableRock, seed: 0);
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var events = sim.DrainEvents();
        var rockFells = events.OfType<RockFell>().ToList();
        Assert.True(rockFells.Count >= 1, "Expected at least one RockFell event");
    }

    [Fact]
    public void Miner_in_collapse_zone_is_crushed()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2)); // will mine
        sim.AddMiner(2, new GridPos(3, 2)); // in zone
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        var m2 = sim.GetMiner(2);
        Assert.False(m2.Alive);
        Assert.Equal(DeathCause.Crushed, m2.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerCrushed mc && mc.MinerId == 2);
    }

    [Fact]
    public void Miner_outside_collapse_zone_survives()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(2, 3)); // will mine
        sim.AddMiner(2, new GridPos(6, 3)); // far away, outside radius 1
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.True(sim.GetMiner(2).Alive);
    }

    [Fact]
    public void VolatileRock_fills_radius_2_zone()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.VolatileRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // Chebyshev radius 2 around (3,3): tiles at (5,3) should be Rock
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(5, 3)));
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(3, 5)));
    }

    [Fact]
    public void Collapse_does_not_convert_non_Floor_tiles()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        grid.Set(new GridPos(3, 2), TileType.ImpermeableRock); // already rock, stays
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        // ImpermeableRock should not be changed
        Assert.Equal(TileType.ImpermeableRock, sim.Grid.Get(new GridPos(3, 2)));
    }

    [Fact]
    public void Blasting_UnstableRock_triggers_collapse()
    {
        var grid = new TileGrid(7, 7, TileType.Floor);
        grid.Set(new GridPos(3, 3), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig
        {
            PlantSeconds = 0.1, FuseSeconds = 0.5,
            BlastRockRadius = 1, BlastKillRadius = 0,
            PickaxeSeconds = 0.1
        });
        sim.AddMiner(1, new GridPos(2, 3));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartPlanting(1);
        sim.Tick(0.1);
        sim.DrainEvents();
        sim.Tick(0.5);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is ScreeCollapsed sc && sc.Pos == new GridPos(3, 3));
    }

    [Fact]
    public void ScreeRock_does_not_always_collapse()
    {
        // Run many seeds; verify at least some don't collapse (ScreeRock is probabilistic)
        int collapseCount = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            var sim = SetupScree(TileType.ScreeRock, seed);
            sim.TryStartMining(1);
            sim.Tick(0.1);
            var events = sim.DrainEvents();
            if (events.Any(e => e is ScreeCollapsed)) collapseCount++;
        }
        Assert.True(collapseCount > 0 && collapseCount < 20,
            $"Expected ~50% collapse rate, got {collapseCount}/20");
    }

    [Fact]
    public void Invulnerable_miner_in_zone_is_not_crushed()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(2, 2), TileType.UnstableRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 0.1 });
        sim.AddMiner(1, new GridPos(1, 2));
        var m2 = sim.AddMiner(2, new GridPos(3, 2));
        m2.InvulnerableRemaining = 5.0;
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(0.1);
        Assert.True(m2.Alive);
    }
}
