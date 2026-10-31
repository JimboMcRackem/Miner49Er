using System.Linq;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SimulationChestTests
{
    private static Simulation Sim(TileGrid g, SimConfig? cfg = null) =>
        new Simulation(g, cfg ?? new SimConfig());

    [Fact]
    public void Chest_pickup_fires_ItemPickedUp_for_the_chest()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor));
        var pos = new GridPos(2, 2);
        sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));

        sim.Tick(0.01);

        Assert.Contains(sim.DrainEvents(), e => e is ItemPickedUp ip && ip.Kind == ItemKind.Chest);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void Chest_gives_a_buff_instead_of_a_life_nobody_can_use(int seed)
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor), new SimConfig { Seed = seed });
        sim.CanRestoreLife = _ => false;
        var pos = new GridPos(2, 2);
        var m = sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));

        sim.Tick(0.01);

        Assert.DoesNotContain(sim.DrainEvents(), e => e is LifeRestored);
        Assert.NotEmpty(m.EffectsInternal);
    }

    [Fact]
    public void Chest_records_what_it_gave_the_miner()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor), new SimConfig { Seed = 1 });
        sim.CanRestoreLife = _ => false;
        var pos = new GridPos(2, 2);
        var m = sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));

        sim.Tick(0.01);

        Assert.Equal(1, m.ChestsOpened);
        Assert.True(m.LastChestLoot is ItemKind.SpeedPotion or ItemKind.LongerVision or ItemKind.BiggerBlast);
    }

    [Fact]
    public void Chest_result_round_trips_through_the_snapshot_codec()
    {
        var sim = Sim(new TileGrid(5, 5, TileType.Floor), new SimConfig { Seed = 1 });
        sim.CanRestoreLife = _ => false;
        var pos = new GridPos(2, 2);
        var m = sim.AddMiner(1, pos);
        sim.AddItem(new Item(pos, ItemKind.Chest, ItemPlacement.Toolbox));
        sim.Tick(0.01);

        var bytes = SnapshotCodec.Write(new TickUpdate(SnapshotFactory.Capture(sim, tick: 0),
            System.Array.Empty<TileChange>()));
        var read = SnapshotCodec.Read(bytes).Snapshot.Miners[0];

        Assert.Equal(1, read.ChestsOpened);
        Assert.Equal((int)m.LastChestLoot!.Value, read.ChestLoot);
    }
}
