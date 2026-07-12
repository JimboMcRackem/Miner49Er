using System.Linq;
using Miner49er.Core;
using Xunit;

public class PrizeEventTests
{
    private static SimConfig Cfg() => new SimConfig
    {
        BaseMoveSeconds = 0.01, Mode = GameMode.GoldRush, PrizeEventsEnabled = true,
        PrizeFirstDelaySeconds = 1.0, PrizeTelegraphSeconds = 0.5, PrizeExpirySeconds = 2.0,
        PrizeIntervalSeconds = 1.0, PrizeJitterSeconds = 0.0, PrizeMinPlayerSpacing = 2,
    };
    private static TileGrid Grid(int w = 15, int h = 15) => new TileGrid(w, h, TileType.Floor);

    [Fact]
    public void Stays_idle_before_first_delay()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(0.5);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Arms_to_telegraph_then_active()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); // past first delay -> telegraph
        Assert.Equal(PrizeState.Telegraph, sim.PrizeState);
        Assert.Single(sim.DrainEvents().OfType<PrizeTelegraphed>());
        sim.Tick(0.6); // past telegraph -> active
        Assert.Equal(PrizeState.Active, sim.PrizeState);
    }

    [Fact]
    public void Unclaimed_event_expires_and_rearms()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); sim.Tick(0.6);          // active
        sim.Tick(2.1);                          // past expiry
        Assert.Contains(sim.DrainEvents(), e => e is PrizeExpired);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Disabled_never_leaves_idle()
    {
        var cfg = Cfg(); cfg.PrizeEventsEnabled = false;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(5.0);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Spawn_tile_is_open_and_clear_of_players()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(2, 2));
        sim.Tick(1.1); // telegraph -> pos chosen
        Assert.True(sim.Grid.Get(sim.PrizePos).IsEnterable());
        Assert.True(sim.PrizePos.ChebyshevTo(new GridPos(2, 2)) >= 2);
    }

    [Fact]
    public void GrabAndGo_claims_when_a_miner_steps_on_it_and_pays_gold()
    {
        var cfg = Cfg(); cfg.PrizeGoldReward = 25;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.ForcePrizeForTest(PrizeType.GrabAndGo, new GridPos(7, 7));
        sim.SetMinerPositionForTest(1, new GridPos(7, 7));
        sim.Tick(0.05);
        Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed pc && pc.MinerId == 1);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
        Assert.Equal(25, sim.GetMiner(1).GoldCollected);
    }
}
