using Miner49er.Core;
using Xunit;

public class RoundResolverTreasureHuntTests
{
    private static Simulation TreasureSim(int seed = 1)
    {
        var cfg = new SimConfig { Seed = seed, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMiner(2, new GridPos(5, 1));
        return sim;
    }

    [Fact]
    public void Ongoing_when_no_winner_yet()
    {
        var sim = TreasureSim();
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Win_when_treasure_winner_detected()
    {
        var sim = TreasureSim();
        sim.TryUseItem(1); // place chest at (1,1)
        var (a, b) = TreasureAssignment.For(1, 1);
        sim.GiveItemForTest(1, a);
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West);
        sim.GiveItemForTest(1, b);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.East);
        sim.Tick(1.0);
        sim.TryMove(1, Direction.West);
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Last_man_standing_still_applies()
    {
        var sim = TreasureSim();
        sim.KillMiner(2);
        var result = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }
}
