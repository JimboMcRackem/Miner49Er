using System.Collections.Generic;
using Miner49er.Core.Net;
using Xunit;

public class SnapshotCodecTreasureHeistTests
{
    [Fact]
    public void Treasure_snapshot_round_trips()
    {
        var snap = new WorldSnapshot(
            Tick: 7,
            Miners: new List<MinerSnapshot>(),
            Charges: new List<ChargeSnapshot>(),
            Items: new List<ItemSnapshot>(),
            Molds: new List<MoldSnapshot>(),
            Monsters: new List<MonsterSnapshot>(),
            Treasure: new TreasureSnapshot(1, 4, 9, 2, 0.5f),
            HoldTimes: new List<HoldTimeSnapshot> { new(2, 3.25f), new(1, 1.0f) });
        var update = new TickUpdate(snap, new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update)).Snapshot;

        Assert.NotNull(back.Treasure);
        Assert.Equal(4, back.Treasure!.Value.X);
        Assert.Equal(2, back.Treasure!.Value.HolderId);
        Assert.Equal(0.5f, back.Treasure!.Value.SuddenDeathProgress, 3);
        Assert.NotNull(back.HoldTimes);
        Assert.Equal(2, back.HoldTimes!.Count);
        Assert.Equal(3.25f, back.HoldTimes![0].Seconds, 3);
    }

    [Fact]
    public void TreasureToast_snapshot_round_trips()
    {
        var snap = new WorldSnapshot(
            Tick: 7,
            Miners: new List<MinerSnapshot>(),
            Charges: new List<ChargeSnapshot>(),
            Items: new List<ItemSnapshot>(),
            Molds: new List<MoldSnapshot>(),
            Monsters: new List<MonsterSnapshot>(),
            TreasureToast: new TreasureToastSnapshot(2, 5));
        var update = new TickUpdate(snap, new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update)).Snapshot;

        Assert.NotNull(back.TreasureToast);
        Assert.Equal(2, back.TreasureToast!.Value.Kind);
        Assert.Equal(5, back.TreasureToast!.Value.MinerId);
    }
}
