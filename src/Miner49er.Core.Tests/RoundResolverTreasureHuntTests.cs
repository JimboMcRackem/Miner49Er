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

    [Fact]
    public void Draw_when_every_alive_players_idol_is_submerged()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig { Seed = 1, TreasureHuntMode = true });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMiner(2, new GridPos(5, 1));
        var (a1, _) = TreasureAssignment.For(1, 1);
        var (a2, _) = TreasureAssignment.For(1, 2);
        // one assigned idol per player, loose on a deep-water (lethal) tile — unrecoverable
        sim.AddItem(new Item(new GridPos(3, 3), a1, ItemPlacement.Loose));
        grid.Set(new GridPos(3, 3), TileType.DeepWater);
        sim.AddItem(new Item(new GridPos(7, 7), a2, ItemPlacement.Loose));
        grid.Set(new GridPos(7, 7), TileType.DeepWater);
        var r = RoundResolver.Resolve(sim, GameMode.TreasureHunt);
        Assert.True(r.IsOver);
        Assert.Equal(-1, r.WinnerId); // unwinnable -> draw
        Assert.Equal(RoundEndReason.TreasureLost, r.Reason);
    }

    [Fact]
    public void Continues_when_one_player_can_still_win()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig { Seed = 1, TreasureHuntMode = true });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.AddMiner(2, new GridPos(5, 1));
        var (a1, _) = TreasureAssignment.For(1, 1);
        // only player 1's idol is submerged; player 2's set is untouched -> match continues
        sim.AddItem(new Item(new GridPos(3, 3), a1, ItemPlacement.Loose));
        grid.Set(new GridPos(3, 3), TileType.DeepWater);
        Assert.False(RoundResolver.Resolve(sim, GameMode.TreasureHunt).IsOver);
    }
}
