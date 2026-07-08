using System.Linq;
using Miner49er.Core;
using Xunit;

public class SimulationCrystalTests
{
    private static Simulation SetupCrystalEast(out GridPos crystalPos)
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        crystalPos = new GridPos(2, 1);
        grid.Set(crystalPos, TileType.CrystalRock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 1.0 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        return sim;
    }

    [Fact]
    public void Mining_CrystalRock_converts_tile_to_Floor()
    {
        var sim = SetupCrystalEast(out var pos);
        sim.TryStartMining(1);
        sim.Tick(1.0);
        Assert.Equal(TileType.Floor, sim.Grid.Get(pos));
    }

    [Fact]
    public void Mining_CrystalRock_emits_RockMined_and_CrystalShardDropped()
    {
        var sim = SetupCrystalEast(out var pos);
        sim.TryStartMining(1);
        sim.Tick(1.0);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is RockMined rm && rm.Pos == pos && !rm.WasGold);
        Assert.Contains(events, e => e is CrystalShardDropped csd && csd.Pos == pos);
    }

    [Fact]
    public void Mining_regular_Rock_does_not_emit_CrystalShardDropped()
    {
        var grid = new TileGrid(3, 3, TileType.Floor);
        grid.Set(new GridPos(2, 1), TileType.Rock);
        var sim = new Simulation(grid, new SimConfig { PickaxeSeconds = 1.0 });
        sim.AddMiner(1, new GridPos(1, 1));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartMining(1);
        sim.Tick(1.0);
        Assert.DoesNotContain(sim.DrainEvents(), e => e is CrystalShardDropped);
    }

    [Fact]
    public void Blasting_CrystalRock_converts_tile_to_Floor()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var crystalPos = new GridPos(2, 2);
        grid.Set(crystalPos, TileType.CrystalRock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.1, FuseSeconds = 0.5, BlastRockRadius = 1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartPlanting(1);
        sim.Tick(0.1); // finish planting
        sim.DrainEvents();
        sim.Tick(0.5); // burn fuse
        Assert.Equal(TileType.Floor, sim.Grid.Get(crystalPos));
    }

    [Fact]
    public void Blasting_CrystalRock_emits_CrystalShardDropped()
    {
        var grid = new TileGrid(5, 5, TileType.Floor);
        var crystalPos = new GridPos(2, 2);
        grid.Set(crystalPos, TileType.CrystalRock);
        var sim = new Simulation(grid, new SimConfig { PlantSeconds = 0.1, FuseSeconds = 0.5, BlastRockRadius = 1 });
        sim.AddMiner(1, new GridPos(1, 2));
        sim.TryMove(1, Direction.East);
        sim.DrainEvents();
        sim.TryStartPlanting(1);
        sim.Tick(0.1);
        sim.DrainEvents();
        sim.Tick(0.5);
        var events = sim.DrainEvents();
        Assert.Contains(events, e => e is CrystalShardDropped csd && csd.Pos == crystalPos);
    }
}
