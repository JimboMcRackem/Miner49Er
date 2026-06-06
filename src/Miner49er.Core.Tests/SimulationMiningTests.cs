using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationMiningTests
{
    private static (Simulation sim, Miner m) Setup(TileType ahead)
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), ahead); // tile east of (1,1)
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 6.0 });
        var m = sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);       // face east (blocked if rock — fine)
        sim.DrainEvents();
        return (sim, m);
    }

    [Fact]
    public void Start_mining_rock_begins_activity()
    {
        var (sim, m) = Setup(TileType.Rock);
        bool ok = sim.TryStartMining(1);
        Assert.True(ok);
        Assert.Equal(ActivityKind.Mining, m.Activity);
        Assert.Equal(new GridPos(2, 1), m.ActivityTarget);
        Assert.Equal(6.0, m.ActivitySecondsRemaining);
    }

    [Fact]
    public void Cannot_mine_floor()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East); // faces floor at (2,1)
        Assert.False(sim.TryStartMining(1));
    }

    [Fact]
    public void Mining_completes_after_full_duration_and_clears_rock()
    {
        var (sim, m) = Setup(TileType.Rock);
        sim.TryStartMining(1);

        sim.Tick(3.0);
        Assert.Equal(ActivityKind.Mining, m.Activity); // not done yet
        sim.Tick(3.0);

        Assert.Equal(ActivityKind.None, m.Activity);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Mining_goldrock_awards_gold_and_emits_event()
    {
        var (sim, m) = Setup(TileType.GoldRock);
        sim.TryStartMining(1);
        sim.DrainEvents();

        sim.Tick(6.0);

        Assert.Equal(1, m.GoldCollected);
        var mined = sim.DrainEvents().OfType<RockMined>().Single();
        Assert.True(mined.WasGold);
        Assert.Equal(TileType.Floor, sim.Grid.Get(new GridPos(2, 1)));
    }

    [Fact]
    public void Moving_cancels_mining()
    {
        var (sim, m) = Setup(TileType.Rock);
        sim.TryStartMining(1);
        sim.TryMove(1, Direction.West); // walkable, cancels
        Assert.Equal(ActivityKind.None, m.Activity);
        Assert.Equal(TileType.Rock, sim.Grid.Get(new GridPos(2, 1))); // unchanged
    }
}
