using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecPendingFallTests
{
    private static WorldSnapshot Empty(IReadOnlyList<PendingFallSnapshot>? falls) =>
        new(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(), PendingFalls: falls);

    [Fact]
    public void PendingFalls_survive_round_trip()
    {
        var falls = new List<PendingFallSnapshot>
        {
            new(5, 6, 0.25f),
            new(9, 2, 0.80f),
        };
        var update = new TickUpdate(Empty(falls), new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.PendingFalls);
        Assert.Equal(2, back.Snapshot.PendingFalls!.Count);
        Assert.Equal(new PendingFallSnapshot(5, 6, 0.25f), back.Snapshot.PendingFalls[0]);
        Assert.Equal(new PendingFallSnapshot(9, 2, 0.80f), back.Snapshot.PendingFalls[1]);
    }

    [Fact]
    public void No_pending_falls_round_trips_as_null()
    {
        var update = new TickUpdate(Empty(null), new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.PendingFalls);
    }
}
