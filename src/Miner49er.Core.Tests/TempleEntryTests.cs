using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class TempleEntryTests
{
    private static Simulation Sim()
    {
        var grid = new TileGrid(8, 3, TileType.Floor);
        grid.Set(new GridPos(4, 1), TileType.TempleFloor);
        return new Simulation(grid, new SimConfig());
    }

    [Fact]
    public void Nobody_has_entered_at_first()
    {
        var sim = Sim();
        sim.AddMiner(1, new GridPos(1, 1));
        sim.Tick(0.1);

        Assert.False(sim.TempleEntered);
    }

    [Fact]
    public void Standing_on_temple_floor_enters_it()
    {
        var sim = Sim();
        sim.AddMiner(1, new GridPos(4, 1));
        sim.Tick(0.1);

        Assert.True(sim.TempleEntered);
    }

    [Fact]
    public void Leaving_the_temple_does_not_undo_it()
    {
        var sim = Sim();
        sim.AddMiner(1, new GridPos(4, 1));
        sim.Tick(0.1);
        sim.SetMinerPositionForTest(1, new GridPos(1, 1));
        sim.Tick(0.1);

        Assert.True(sim.TempleEntered);
    }

    [Fact]
    public void The_host_tells_clients_about_the_temple_and_the_artifact()
    {
        var snap = new WorldSnapshot(1, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>(),
            ArtifactNeeded: true, TempleEntered: true);

        var decoded = SnapshotCodec.Read(SnapshotCodec.Write(new TickUpdate(snap, new List<TileChange>()))).Snapshot;

        Assert.True(decoded.ArtifactNeeded);
        Assert.True(decoded.TempleEntered);
    }
}
