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

    [Fact]
    public void MineOut_accrues_while_standing_and_claims_at_threshold()
    {
        var cfg = Cfg(); cfg.PrizeMineSeconds = 1.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.ForcePrizeForTest(PrizeType.MineOut, new GridPos(7, 7));
        sim.Tick(0.5);
        Assert.True(sim.PrizeClaimProgress > 0.4 && sim.PrizeState == PrizeState.Active);
        sim.Tick(0.6);
        Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed pc && pc.MinerId == 1);
    }

    [Fact]
    public void MineOut_resets_when_channeler_leaves()
    {
        var cfg = Cfg(); cfg.PrizeMineSeconds = 2.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.ForcePrizeForTest(PrizeType.MineOut, new GridPos(7, 7));
        sim.Tick(0.5);
        Assert.True(sim.PrizeClaimProgress > 0);
        sim.SetMinerPositionForTest(1, new GridPos(9, 9));
        sim.Tick(0.1);
        Assert.Equal(0.0, sim.PrizeClaimProgress, 3);
    }

    [Fact]
    public void HoldPoint_claims_when_held_solo()
    {
        var cfg = Cfg(); cfg.PrizeHoldSeconds = 1.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.ForcePrizeForTest(PrizeType.HoldPoint, new GridPos(7, 7));
        sim.Tick(0.5);
        Assert.True(sim.PrizeClaimProgress > 0.4);
        sim.Tick(0.6);
        Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed pc && pc.MinerId == 1);
    }

    [Fact]
    public void HoldPoint_resets_when_contested()
    {
        var cfg = Cfg(); cfg.PrizeHoldSeconds = 3.0;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(7, 7));
        sim.AddMiner(2, new GridPos(14, 14)); // out of ring initially
        sim.ForcePrizeForTest(PrizeType.HoldPoint, new GridPos(7, 7));
        sim.Tick(0.5);
        Assert.True(sim.PrizeClaimProgress > 0);
        sim.SetMinerPositionForTest(2, new GridPos(8, 7)); // rival enters the ring
        sim.Tick(0.1);
        Assert.Equal(0.0, sim.PrizeClaimProgress, 3);
    }

    [Fact]
    public void Relic_is_picked_up_carried_to_spawn_and_banked()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(3, 3));          // spawn = (3,3)
        sim.ForcePrizeForTest(PrizeType.CarryRelic, new GridPos(10, 10));
        sim.SetMinerPositionForTest(1, new GridPos(10, 10)); // step on the relic
        sim.Tick(0.05);
        Assert.Equal(1, sim.PrizeHolderId);          // picked up
        Assert.Equal(PrizeState.Active, sim.PrizeState);
        sim.SetMinerPositionForTest(1, new GridPos(3, 3));   // carry home
        sim.Tick(0.05);
        Assert.Contains(sim.DrainEvents(), e => e is PrizeClaimed pc && pc.MinerId == 1);
        Assert.Equal(PrizeState.Idle, sim.PrizeState);
    }

    [Fact]
    public void Relic_drops_when_holder_dies()
    {
        var sim = new Simulation(Grid(), Cfg());
        sim.AddMiner(1, new GridPos(3, 3));
        sim.ForcePrizeForTest(PrizeType.CarryRelic, new GridPos(10, 10));
        sim.SetMinerPositionForTest(1, new GridPos(10, 10));
        sim.Tick(0.05);
        Assert.Equal(1, sim.PrizeHolderId);
        sim.SetMinerPositionForTest(1, new GridPos(6, 6));
        sim.KillMiner(1);
        sim.Tick(0.05);
        Assert.Equal(-1, sim.PrizeHolderId);          // dropped
        Assert.Equal(new GridPos(6, 6), sim.PrizePos); // at the death spot
        Assert.Equal(PrizeState.Active, sim.PrizeState);
    }

    // --- mode-appropriate rewards ---

    private static void ClaimGrabAndGo(Simulation sim, int minerId, GridPos at)
    {
        sim.ForcePrizeForTest(PrizeType.GrabAndGo, at);
        sim.SetMinerPositionForTest(minerId, at);
        sim.Tick(0.05);
    }

    [Fact]
    public void TreasureHunt_reward_credits_an_idol()
    {
        var cfg = Cfg(); cfg.Mode = GameMode.TreasureHunt;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        ClaimGrabAndGo(sim, 1, new GridPos(7, 7));
        Assert.Contains(sim.GetTreasureProgress(), p => p.MinerId == 1 && p.Found >= 1);
    }

    [Fact]
    public void LMS_reward_grants_invulnerability()
    {
        var cfg = Cfg(); cfg.Mode = GameMode.LastManStanding;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        ClaimGrabAndGo(sim, 1, new GridPos(7, 7));
        Assert.True(sim.GetMiner(1).InvulnerableRemaining > 0);
    }

    [Fact]
    public void ReachCenter_reward_applies_move_speed_buff()
    {
        var cfg = Cfg(); cfg.Mode = GameMode.ReachCenter; cfg.BaseMoveSeconds = 0.20; // realistic (above the min-clamp)
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        double baseSpeed = sim.EffectiveMoveSeconds(1);
        ClaimGrabAndGo(sim, 1, new GridPos(7, 7));
        Assert.True(sim.EffectiveMoveSeconds(1) < baseSpeed); // faster
    }

    [Fact]
    public void Derby_reward_grants_stones()
    {
        var cfg = Cfg(); cfg.Mode = GameMode.DemolitionDerby;
        var sim = new Simulation(Grid(), cfg);
        sim.AddMiner(1, new GridPos(2, 2));
        ClaimGrabAndGo(sim, 1, new GridPos(7, 7));
        Assert.True(sim.GetMiner(1).StoneCount >= 3);
    }
}
