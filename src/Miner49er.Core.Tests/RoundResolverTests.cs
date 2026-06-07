using Miner49er.Core;
using Xunit;

public class RoundResolverTests
{
    private static Simulation TwoMinerSim(GridPos? center = null, double? timeLimit = null)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig(),
            center, timeLimit);
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        return sim;
    }

    // --- Universal last-man-standing (applies in every mode) ---

    [Fact]
    public void Two_alive_miners_is_not_over()
    {
        var result = RoundResolver.Resolve(TwoMinerSim(), GameMode.LastManStanding);
        Assert.False(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void One_alive_miner_is_over_and_that_miner_wins()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Zero_alive_miners_is_over_with_no_winner()
    {
        var sim = TwoMinerSim();
        sim.GetMiner(1).Alive = false;
        sim.GetMiner(2).Alive = false;
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void Last_man_standing_applies_even_in_gold_rush()
    {
        var sim = TwoMinerSim(timeLimit: 120.0); // not expired
        sim.GetMiner(2).Alive = false;           // only miner 1 left
        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    // --- Gold Rush (timeout -> most gold) ---

    [Fact]
    public void Gold_rush_not_expired_with_two_alive_is_not_over()
    {
        var sim = TwoMinerSim(timeLimit: 120.0);
        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Gold_rush_timeout_awards_the_richest_living_miner()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.GetMiner(1).GoldCollected = 2;
        sim.GetMiner(2).GoldCollected = 7;
        sim.Tick(5.0); // expire the timer

        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(2, result.WinnerId); // miner 2 has the most gold
    }

    [Fact]
    public void Gold_rush_timeout_with_a_tie_is_a_draw()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.GetMiner(1).GoldCollected = 4;
        sim.GetMiner(2).GoldCollected = 4;
        sim.Tick(5.0);

        var result = RoundResolver.Resolve(sim, GameMode.GoldRush);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    // --- Reach Center (first to center wins) ---

    [Fact]
    public void Reach_center_is_not_over_until_someone_reaches_it()
    {
        var sim = TwoMinerSim(center: new GridPos(2, 2));
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Reach_center_winner_is_the_first_to_arrive()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        sim.TryMove(1, Direction.East); // miner 1 steps onto center

        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.True(result.IsOver);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Reach_center_does_not_award_a_drowned_reacher()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 0));
        sim.AddMiner(1, new GridPos(0, 0));
        sim.AddMiner(2, new GridPos(4, 4));
        sim.AddMiner(3, new GridPos(0, 4)); // a third so LMS doesn't fire when miner 1 dies
        sim.TryMove(1, Direction.East);     // miner 1 reaches centre alive (latches)
        sim.GetMiner(1).Alive = false;      // then dies (e.g. flooded)
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.False(result.IsOver);        // a dead reacher does not win; 2 still alive
    }

    [Fact]
    public void Last_man_standing_timeout_is_a_draw()
    {
        var sim = TwoMinerSim(timeLimit: 5.0);
        sim.Tick(5.0); // expire the clock; both miners still alive
        var result = RoundResolver.Resolve(sim, GameMode.LastManStanding);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }

    [Fact]
    public void Reach_center_timeout_without_arrival_is_a_draw()
    {
        var sim = TwoMinerSim(center: new GridPos(2, 2), timeLimit: 5.0);
        sim.Tick(5.0); // clock expires, nobody reached center
        var result = RoundResolver.Resolve(sim, GameMode.ReachCenter);
        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);
    }
}
