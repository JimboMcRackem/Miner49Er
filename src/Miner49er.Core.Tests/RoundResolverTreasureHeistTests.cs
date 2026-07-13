using System.Linq;
using Miner49er.Core;
using Xunit;

public class RoundResolverTreasureHeistTests
{
    private static Simulation Buzzer(out int nobody)
    {
        nobody = -1;
        var cfg = TreasureHeistTests.Cfg(); cfg.TreasureWinByCumulative = false;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        return sim;
    }

    [Fact]
    public void Not_over_before_time_expires()
    {
        var sim = Buzzer(out _);
        Assert.False(RoundResolver.Resolve(sim, GameMode.TreasureHeist).IsOver);
    }

    [Fact]
    public void Buzzer_holder_at_expiry_wins()
    {
        var sim = Buzzer(out _);
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        sim.Tick(0.1); // miner 1 grabs
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Cumulative_most_time_wins()
    {
        var cfg = TreasureHeistTests.Cfg(); cfg.TreasureWinByCumulative = true;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        for (int i = 0; i < 20; i++) sim.Tick(0.1); // miner 1 banks ~2s
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Death_match_wipe_resolves_by_cumulative_before_timer()
    {
        var cfg = TreasureHeistTests.Cfg();
        cfg.TreasureWinByCumulative = true; cfg.TreasureRespawnEnabled = false;
        var sim = new Simulation(TreasureHeistTests.Grid(), cfg);
        sim.AddMiner(1, new GridPos(3, 3));
        sim.AddMiner(2, new GridPos(6, 6));
        sim.ForceTreasureLooseForTest(new GridPos(3, 3));
        for (int i = 0; i < 10; i++) sim.Tick(0.1); // miner 1 banks time
        sim.KillMiner(1); sim.KillMiner(2);          // everyone eliminated, clock NOT expired
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHeist);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId); // most cumulative time wins
    }
}
