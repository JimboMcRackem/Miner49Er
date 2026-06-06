using Miner49er.Core;
using Xunit;

public class RoundResolverTests
{
    private static Simulation TwoMinerSim()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        return sim;
    }

    [Fact]
    public void Two_alive_miners_is_not_over()
    {
        var result = RoundResolver.Resolve(TwoMinerSim());
        Assert.False(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void One_alive_miner_is_over_and_that_miner_wins()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Zero_alive_miners_is_over_with_no_winner()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(1).Alive = false;
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }
}
