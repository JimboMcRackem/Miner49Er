using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationExpeditionTests
{
    // Grid with two gold veins the miner can mine instantly.
    private static (Simulation sim, Miner miner) Setup()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.GoldRock);
        grid.Set(new GridPos(4, 1), TileType.GoldRock);
        var cfg = new SimConfig { PickaxeSeconds = 0.1 };
        var sim = new Simulation(grid, cfg, escapeTile: new GridPos(0, 1));
        var miner = sim.AddMiner(1, new GridPos(1, 1));
        return (sim, miner);
    }

    [Fact]
    public void Escape_stays_shut_until_the_last_vein_is_cleared()
    {
        var (sim, miner) = Setup();

        miner.Facing = Direction.East;                 // faces (2,1) gold
        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                 // first vein cleared

        Assert.False(sim.AllGoldCleared);
        Assert.False(sim.EscapeOpen);

        // Walk to the second vein and clear it. A Tick between moves clears the per-tile
        // move-cooldown gate (TryMove refuses a second step while the cooldown is live).
        Assert.True(sim.TryMove(1, Direction.East));   // (1,1) -> (2,1)
        sim.Tick(0.2);                                 // let the move cooldown lapse
        Assert.True(sim.TryMove(1, Direction.East));   // (2,1) -> (3,1)
        miner.Facing = Direction.East;                 // faces (4,1) gold
        Assert.True(sim.TryStartMining(1));
        sim.Tick(0.1);                                 // second vein cleared

        Assert.True(sim.AllGoldCleared);
        Assert.True(sim.EscapeOpen);
        Assert.Contains(sim.DrainEvents(), e => e is EscapeOpened);
    }
}
