using Miner49er.Core;
using Miner49er.Core.AI;
using Xunit;

public class BotBrainTests
{
    private static Simulation MakeSim(TileGrid grid) =>
        new Simulation(grid, new SimConfig());

    [Fact]
    public void Greenhorn_returns_valid_action_every_tick()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 2));
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 42);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.InRange(action.Dir, -1, 3);
    }

    [Fact]
    public void Miner_heads_toward_gold_rock()
    {
        // GoldRock at east end; miner at west end; open corridor.
        var grid = new TileGrid(5, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 1));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Miner_sets_Mine_when_next_step_is_GoldRock()
    {
        // GoldRock immediately east of miner.
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(1, 1), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 1));
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
        Assert.True(action.Mine);
    }

    [Fact]
    public void Foreman_sets_Use_when_holding_SpeedPotion()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var sim  = MakeSim(grid);
        sim.AddMiner(1, new GridPos(2, 2));
        sim.GiveItemForTest(1, ItemKind.SpeedPotion);
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.True(action.Use);
    }

    [Fact]
    public void DynamiteDan_sets_Plant_when_adjacent_to_three_GoldRocks()
    {
        // Bot at (1,1); GoldRocks north, east, south.
        var grid = new TileGrid(5, 5, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.GoldRock);
        grid.Set(new GridPos(2, 1), TileType.GoldRock);
        grid.Set(new GridPos(1, 2), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(1, 1));
        var brain = new BotBrain(1, BotSkill.DynamiteDan, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.True(action.Plant);
    }

    [Fact]
    public void Foreman_does_not_step_into_scree_on_the_way_to_gold()
    {
        // Bot at (0,0). GoldRock target at (2,0). Direct east tile (1,0) is UnstableRock.
        // A detour south exists. Foreman is hazard-aware, so it must not step east into the scree.
        var grid = new TileGrid(3, 2, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.UnstableRock);
        grid.Set(new GridPos(2, 0), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 0));
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.NotEqual((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Foreman_falls_back_through_scree_when_it_is_the_only_route()
    {
        // 3×1 corridor: bot (0,0), gold (2,0), scree (1,0) is the ONLY path.
        // Hazard-aware pass finds nothing; fallback routes east through the scree so the bot isn't frozen.
        var grid = new TileGrid(3, 1, TileType.Floor);
        grid.Set(new GridPos(1, 0), TileType.ScreeRock);
        grid.Set(new GridPos(2, 0), TileType.GoldRock);
        var sim = MakeSim(grid);
        sim.AddMiner(1, new GridPos(0, 0));
        var brain = new BotBrain(1, BotSkill.Foreman, seed: 0);

        var action = brain.Think(sim, GameMode.GoldRush);

        Assert.Equal((int)Direction.East, action.Dir);
    }

    [Fact]
    public void Miner_on_open_exit_whistles_once_per_floor()
    {
        // Gold-less floor so the escape opens immediately; bot spawns on the escape tile.
        var grid = new TileGrid(5, 5, TileType.Floor);
        var exit = new GridPos(2, 2);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        sim.AddMiner(1, exit);
        var brain = new BotBrain(1, BotSkill.Miner, seed: 0);

        var first = brain.Think(sim, GameMode.Expedition);
        Assert.True(first.Whistle);

        // Standing on the exit again the next tick: no repeat whistle.
        var second = brain.Think(sim, GameMode.Expedition);
        Assert.False(second.Whistle);
    }

    [Fact]
    public void Greenhorn_on_open_exit_does_not_whistle()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var exit = new GridPos(2, 2);
        var sim = new Simulation(grid, new SimConfig(), escapeTile: exit);
        sim.AddMiner(1, exit);
        var brain = new BotBrain(1, BotSkill.Greenhorn, seed: 0);

        Assert.False(brain.Think(sim, GameMode.Expedition).Whistle);
    }
}
