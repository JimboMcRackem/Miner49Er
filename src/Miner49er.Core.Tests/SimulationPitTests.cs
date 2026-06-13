using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationPitTests
{
    [Fact]
    public void Moving_onto_a_pit_kills_with_Fell_and_emits_MinerFell()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Pit);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        bool moved = sim.TryMove(1, Direction.East);

        Assert.True(moved);                       // the move resolves (then kills)
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Fell, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerFell f && f.MinerId == 1);
    }

    [Fact]
    public void Moving_onto_deep_water_still_kills_with_Drowned()   // regression
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.DeepWater);
        var sim = new Simulation(grid, new SimConfig());
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);

        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Drowned, m.DeathCause);
        Assert.Contains(sim.DrainEvents(), e => e is MinerDrowned d && d.MinerId == 1);
    }
}
