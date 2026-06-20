using Miner49er.Core;
using Xunit;

public class RoundResolverExpeditionTests
{
    private static (Simulation sim, Miner miner) SetupNoGold(GridPos minerStart, GridPos exit)
    {
        var grid = new TileGrid(6, 3, TileType.Floor);   // no GoldRock -> AllGoldCleared from the start
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        var miner = sim.AddMiner(1, minerStart);
        return (sim, miner);
    }

    [Fact]
    public void Solo_run_is_not_won_just_because_one_miner_is_alive()
    {
        // All gold cleared, but the miner is NOT on the exit yet.
        var (sim, _) = SetupNoGold(new GridPos(2, 1), exit: new GridPos(0, 1));

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.False(result.IsOver);
    }

    [Fact]
    public void Reaching_the_exit_clears_the_floor_not_the_game()
    {
        var (sim, _) = SetupNoGold(new GridPos(0, 1), exit: new GridPos(0, 1));   // already on the exit

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.False(result.IsOver);
        Assert.True(result.FloorCleared);
        Assert.Equal(1, result.WinnerId);
    }

    [Fact]
    public void Chest_pickup_does_not_win_the_dungeon()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig { Seed = 1 }, escapeTile: null);
        sim.AddItem(new Item(new GridPos(1, 1), ItemKind.Chest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 1));
        sim.Tick(0.1);   // PickUpItems fires; chest rolls a buff

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.False(result.IsOver);   // picking up a chest is no longer a win condition
    }

    [Fact]
    public void Miner_death_loses_the_run()
    {
        var (sim, miner) = SetupNoGold(new GridPos(0, 1), exit: new GridPos(0, 1));
        sim.KillMiner(miner.Id);

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.True(result.IsOver);
        Assert.Equal(-1, result.WinnerId);   // loss
    }

    // Co-op helpers and tests

    private static Simulation SetupCoopNoGold(GridPos m1Start, GridPos m2Start, GridPos exit)
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        sim.AddMiner(1, m1Start);
        sim.AddMiner(2, m2Start);
        return sim;
    }

    [Fact]
    public void Coop_only_one_on_exit_does_not_clear_floor()
    {
        var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(3, 1), exit: new GridPos(0, 1));

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.False(result.FloorCleared);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Coop_both_on_exit_clears_floor()
    {
        var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(0, 1), exit: new GridPos(0, 1));

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.True(result.FloorCleared);
        Assert.False(result.IsOver);
    }

    [Fact]
    public void Coop_dead_partner_does_not_block_floor_clear()
    {
        var sim = SetupCoopNoGold(new GridPos(0, 1), new GridPos(3, 1), exit: new GridPos(0, 1));
        sim.KillMiner(2);

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);

        Assert.True(result.FloorCleared);
    }
}
