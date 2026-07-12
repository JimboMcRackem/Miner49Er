using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecPrizeTests
{
    private static WorldSnapshot Empty(PrizeEventSnapshot? prize) =>
        new(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(), PrizeEvent: prize);

    [Fact]
    public void Prize_event_survives_round_trip()
    {
        var prize = new PrizeEventSnapshot(2, 2, 11, 7, 0.5f, 3, 42.0f);
        var update = new TickUpdate(Empty(prize), new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.PrizeEvent);
        Assert.Equal(prize, back.Snapshot.PrizeEvent!.Value);
    }

    [Fact]
    public void No_prize_round_trips_as_null()
    {
        var update = new TickUpdate(Empty(null), new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.PrizeEvent);
    }
}
