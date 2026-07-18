using System.Collections.Generic;
using Miner49er.Core;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecCartTests
{
    private static WorldSnapshot Empty(IReadOnlyList<CartSnapshot>? carts) =>
        new(1,
            new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
            new List<ItemSnapshot>(), new List<MoldSnapshot>(),
            new List<MonsterSnapshot>(), Carts: carts);

    [Fact]
    public void Carts_survive_round_trip()
    {
        var carts = new List<CartSnapshot>
        {
            new(1, 4, 5, (int)Direction.East, CartCargo.None, 0),
            new(2, 7, 3, (int)Direction.North, CartCargo.Lantern, 1.2),
        };
        var update = new TickUpdate(Empty(carts), new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.Carts);
        Assert.Equal(2, back.Snapshot.Carts!.Count);
        Assert.Equal(carts[0], back.Snapshot.Carts[0]);
        Assert.Equal(carts[1], back.Snapshot.Carts[1]);
    }

    [Fact]
    public void No_carts_round_trips_as_null()
    {
        var update = new TickUpdate(Empty(null), new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.Carts);
    }
}
