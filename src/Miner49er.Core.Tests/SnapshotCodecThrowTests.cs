using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecThrowTests
{
    private static WorldSnapshot Empty(IReadOnlyList<StoneThrowSnapshot>? throws) =>
        new(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(), Throws: throws);

    [Fact]
    public void Throws_survive_round_trip()
    {
        var throws = new List<StoneThrowSnapshot>
        {
            new(2, 5, 6, 9, 6),
            new(1, 0, 0, 3, 4),
        };
        var update = new TickUpdate(Empty(throws), new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.Throws);
        Assert.Equal(2, back.Snapshot.Throws!.Count);
        Assert.Equal(new StoneThrowSnapshot(2, 5, 6, 9, 6), back.Snapshot.Throws[0]);
        Assert.Equal(new StoneThrowSnapshot(1, 0, 0, 3, 4), back.Snapshot.Throws[1]);
    }

    [Fact]
    public void No_throws_round_trips_as_null()
    {
        var update = new TickUpdate(Empty(null), new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.Throws);
    }
}
