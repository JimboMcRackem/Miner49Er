using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationKillTests
{
    private static Simulation OneMinerSim()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(0, 0));
        return sim;
    }

    [Fact]
    public void KillMiner_sets_alive_false_clears_activity_and_emits_event()
    {
        var sim = OneMinerSim();
        sim.KillMiner(1);

        var m = sim.GetMiner(1);
        Assert.False(m.Alive);
        Assert.Equal(ActivityKind.None, m.Activity);

        var events = sim.DrainEvents();
        var killed = Assert.Single(events.OfType<MinerKilled>());
        Assert.Equal(1, killed.MinerId);
    }

    [Fact]
    public void KillMiner_is_idempotent_and_emits_event_only_once()
    {
        var sim = OneMinerSim();
        sim.KillMiner(1);
        sim.KillMiner(1);

        var killed = sim.DrainEvents().OfType<MinerKilled>().ToList();
        Assert.Single(killed);
    }
}
