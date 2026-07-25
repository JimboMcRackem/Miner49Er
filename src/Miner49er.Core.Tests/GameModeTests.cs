using Miner49er.Core;
using Xunit;

public class GameModeTests
{
    [Fact]
    public void Untimed_sim_reports_minus_one_and_never_expires()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.Tick(10.0);
        Assert.Equal(-1, sim.SecondsRemaining);
        Assert.False(sim.TimeExpired);
    }

    [Fact]
    public void Timed_sim_counts_down_and_then_expires()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig(),
            timeLimitSeconds: 5.0);
        Assert.Equal(5.0, sim.SecondsRemaining);
        Assert.False(sim.TimeExpired);

        sim.Tick(2.0);
        Assert.Equal(3.0, sim.SecondsRemaining, 3);
        Assert.False(sim.TimeExpired);

        sim.Tick(4.0);                 // total 6 >= 5
        Assert.Equal(0.0, sim.SecondsRemaining);   // clamped, never negative
        Assert.True(sim.TimeExpired);
    }

    [Fact]
    public void Moving_onto_center_records_first_miner_and_emits_event()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(2, 1));
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East); // lands on (2,1) == center
        var events = sim.DrainEvents();

        Assert.Equal(1, sim.FirstToReachCenter);
        Assert.Contains(events, e => e is MinerReachedCenter rc && rc.MinerId == 1);
    }

    [Fact]
    public void Center_is_recorded_only_for_the_first_arrival()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(1, 1));
        sim.AddMiner(1, new GridPos(0, 1));
        sim.AddMiner(2, new GridPos(2, 1));

        sim.TryMove(1, Direction.East); // miner 1 reaches center first
        sim.TryMove(2, Direction.West); // miner 2 arrives second
        Assert.Equal(1, sim.FirstToReachCenter);
    }

    [Fact]
    public void No_center_means_FirstToReachCenter_stays_minus_one()
    {
        var sim = new Simulation(new TileGrid(3, 3, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        Assert.Equal(-1, sim.FirstToReachCenter);
    }

    [Fact]
    public void ReachCenter_requires_5_gold_when_map_has_gold_veins()
    {
        // 5×1 strip: [gold][gold][gold][gold][gold][floor=center] with miner at x=0
        var grid = new TileGrid(7, 1, TileType.Floor);
        for (int x = 0; x < 5; x++) grid.Set(new GridPos(x, 0), TileType.GoldRock);
        var center = new GridPos(6, 0);
        var sim = new Simulation(grid, new SimConfig(), center: center);
        sim.AddMiner(1, new GridPos(5, 0)); // adjacent to center, 0 gold so far

        // Arrive at center with 0 gold — should NOT trigger win.
        sim.TryMove(1, Direction.East);
        Assert.Equal(-1, sim.FirstToReachCenter);

        // Drain move cooldown, back off, grant 5 gold, return to center.
        sim.Tick(0.2);
        sim.TryMove(1, Direction.West);
        sim.SetGold(1, 5);
        sim.Tick(0.2);
        sim.TryMove(1, Direction.East);
        Assert.Equal(1, sim.FirstToReachCenter);
    }

    [Fact]
    public void ReachCenter_no_gold_requirement_when_map_has_no_gold()
    {
        // Gold-free map: the 5-gold requirement must NOT apply.
        var grid = new TileGrid(3, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), center: new GridPos(2, 1));
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);
        Assert.Equal(1, sim.FirstToReachCenter);
    }

    [Theory]
    [InlineData(GameMode.LastManStanding)]
    [InlineData(GameMode.DemolitionDerby)]
    [InlineData(GameMode.GrudgeMatch)]
    public void IsKillScored_true_for_combat_modes(GameMode mode)
        => Assert.True(mode.IsKillScored());

    [Theory]
    [InlineData(GameMode.GoldRush)]
    [InlineData(GameMode.ReachCenter)]
    [InlineData(GameMode.Expedition)]
    [InlineData(GameMode.TreasureHunt)]
    [InlineData(GameMode.TreasureHeist)]
    public void IsKillScored_false_for_non_combat_modes(GameMode mode)
        => Assert.False(mode.IsKillScored());
}
