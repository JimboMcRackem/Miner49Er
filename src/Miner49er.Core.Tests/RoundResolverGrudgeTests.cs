using Miner49er.Core;
using Xunit;

// Grudge Match (always timed, respawns, most kills wins) and timed Demolition Derby
// (most kills at the buzzer) both resolve through the shared most-kills rule.
public class RoundResolverGrudgeTests
{
    private static Simulation Sim(int miners = 2)
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        for (int i = 1; i <= miners; i++) sim.AddMiner(i, new GridPos(i - 1, 0));
        return sim;
    }

    // --- Grudge Match ---

    [Fact]
    public void Grudge_is_not_over_before_the_buzzer_even_if_everyone_is_momentarily_dead()
    {
        var sim = Sim();
        sim.GetMiner(1).Alive = false;
        sim.GetMiner(2).Alive = false; // transient — respawns are coming, clock still running
        var r = RoundResolver.Resolve(sim, GameMode.GrudgeMatch);
        Assert.False(r.IsOver);
    }

    [Fact]
    public void Timed_Grudge_awards_the_win_to_the_most_kills()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 3;
        sim.GetMiner(2).Kills = 1;
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.GrudgeMatch);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Timed_Grudge_tied_on_kills_is_a_draw()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 2;
        sim.GetMiner(2).Kills = 2;
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.GrudgeMatch);
        Assert.True(r.IsOver);
        Assert.Equal(-1, r.WinnerId);
        Assert.Equal(RoundEndReason.Tie, r.Reason);
    }

    [Fact]
    public void Timed_Grudge_with_no_kills_is_a_draw()
    {
        var sim = Sim();
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.GrudgeMatch);
        Assert.True(r.IsOver);
        Assert.Equal(RoundEndReason.Tie, r.Reason);
    }

    // A dead-at-buzzer miner still counts — Grudge kills are cumulative, survival is irrelevant.
    [Fact]
    public void Timed_Grudge_winner_can_be_dead_at_the_buzzer()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 5; sim.GetMiner(1).Alive = false; // died but topped the board
        sim.GetMiner(2).Kills = 2;
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.GrudgeMatch);
        Assert.Equal(1, r.WinnerId);
    }

    // --- Timed Demolition Derby ---

    [Fact]
    public void Timed_Derby_awards_the_win_to_the_most_kills()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 2;
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.DemolitionDerby);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Timed_Derby_tied_on_kills_is_a_draw()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 1;
        sim.GetMiner(2).Kills = 1;
        sim.SetTimeExpiredForTest();
        var r = RoundResolver.Resolve(sim, GameMode.DemolitionDerby);
        Assert.Equal(RoundEndReason.Tie, r.Reason);
    }

    // Untimed Derby is unchanged: still decided by last-man-standing, kills irrelevant.
    [Fact]
    public void Untimed_Derby_is_still_last_man_standing()
    {
        var sim = Sim();
        sim.GetMiner(1).Kills = 0;
        sim.GetMiner(2).Kills = 9;   // more kills, but about to be the last one standing loses...
        sim.GetMiner(2).Alive = false; // ...because miner 1 is the last alive
        var r = RoundResolver.Resolve(sim, GameMode.DemolitionDerby);
        Assert.True(r.IsOver);
        Assert.Equal(1, r.WinnerId);
    }

    [Fact]
    public void Untimed_Derby_with_two_alive_is_ongoing()
    {
        var r = RoundResolver.Resolve(Sim(), GameMode.DemolitionDerby);
        Assert.False(r.IsOver);
    }
}
