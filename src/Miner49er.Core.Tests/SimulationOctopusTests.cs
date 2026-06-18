using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationOctopusTests
{
    [Fact]
    public void Miner_on_danger_tile_is_crushed()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddOctopus(new GridPos(5, 5));
        var dangerPos = sim.Octopus!.DangerTiles(grid).First();
        sim.AddMiner(1, dangerPos);

        sim.Tick(0.01);   // tiny tick — arm hasn't moved far; miner is still on danger tile

        var m = sim.Miners.First(x => x.Id == 1);
        Assert.False(m.Alive);
        Assert.Equal(DeathCause.Crushed, m.DeathCause);
    }

    [Fact]
    public void Miner_far_from_octopus_is_safe()
    {
        var grid = new TileGrid(20, 20, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig());
        sim.AddOctopus(new GridPos(10, 10));
        // (10,19) is 9 tiles south — outside arm length 5
        var miner = sim.AddMiner(1, new GridPos(10, 19));

        sim.Tick(0.033);

        Assert.True(miner.Alive);
    }

    [Fact]
    public void Chest_item_pickup_consumes_it_and_applies_a_perm_buff()
    {
        var grid  = new TileGrid(6, 3, TileType.Floor);
        var sim   = new Simulation(grid, new SimConfig { Seed = 1 });
        var chest = new GridPos(2, 1);
        sim.AddItem(new Item(chest, ItemKind.Chest, ItemPlacement.Toolbox));
        var m = sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);
        sim.Tick(0.5);

        Assert.Empty(sim.Items);   // chest consumed
        // at least one perm buff must have been applied
        Assert.True(m.PermSpeedLevel + m.PermVisionLevel + m.PermBlastLevel > 0
                    || sim.DrainEvents().Any(e => e is LifeRestored));
    }

    [Fact]
    public void Chest_pickup_does_not_win_the_dungeon()
    {
        var grid = new TileGrid(6, 3, TileType.Floor);
        var sim  = new Simulation(grid, new SimConfig { Seed = 1 }, escapeTile: null);
        sim.AddItem(new Item(new GridPos(2, 1), ItemKind.Chest, ItemPlacement.Toolbox));
        sim.AddMiner(1, new GridPos(1, 1));

        sim.TryMove(1, Direction.East);
        sim.Tick(0.5);

        var result = RoundResolver.Resolve(sim, GameMode.Expedition);
        Assert.False(result.IsOver);
    }
}
