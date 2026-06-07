using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecTests
{
    [Fact]
    public void Round_trips_all_fields()
    {
        var update = new TickUpdate(
            new WorldSnapshot(
                Tick: 7,
                Miners: new List<MinerSnapshot>
                {
                    new(1, 3, 4, 2, true, 5, 1, 2.5),
                    new(2, 9, 0, 0, false, 0, 0, 0.0),
                },
                Charges: new List<ChargeSnapshot> { new(1, 8, 8, 1.25) },
                SecondsRemaining: 42.5f),
            TileChanges: new List<TileChange> { new(8, 8, true), new(2, 2, false) });

        byte[] bytes = SnapshotCodec.Write(update);
        TickUpdate back = SnapshotCodec.Read(bytes);

        Assert.Equal(7, back.Snapshot.Tick);
        Assert.Equal(42.5f, back.Snapshot.SecondsRemaining);
        Assert.Equal(2, back.Snapshot.Miners.Count);
        Assert.Equal(update.Snapshot.Miners[0], back.Snapshot.Miners[0]);
        Assert.Equal(update.Snapshot.Miners[1], back.Snapshot.Miners[1]);
        Assert.Equal(update.Snapshot.Charges[0], back.Snapshot.Charges[0]);
        Assert.Equal(2, back.TileChanges.Count);
        Assert.Equal(update.TileChanges[0], back.TileChanges[0]);
        Assert.Equal(update.TileChanges[1], back.TileChanges[1]);
    }

    [Fact]
    public void Round_trips_empty_collections()
    {
        var update = new TickUpdate(
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>()),
            new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Empty(back.Snapshot.Miners);
        Assert.Empty(back.Snapshot.Charges);
        Assert.Empty(back.TileChanges);
    }
}
