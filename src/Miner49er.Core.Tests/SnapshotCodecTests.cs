using System.Collections.Generic;
using Miner49er.Core;
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
                    new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8, (int)ItemKind.WaterPlank),
                    new(2, 9, 0, 0, false, 0, 0, 0.0, 0.24, 5, -1),
                },
                Charges: new List<ChargeSnapshot> { new(1, 8, 8, 1.25) },
                Items: new List<ItemSnapshot>
                {
                    new(6, 1, ItemKind.SpeedPotion, ItemPlacement.Toolbox),
                    new(2, 5, ItemKind.BiggerBlast, ItemPlacement.Buried),
                    new(3, 3, ItemKind.LongerVision, ItemPlacement.Loose),
                },
                Molds: new List<MoldSnapshot> { new(4, 6, 12.5), new(0, 1, 3.0) },
                Monsters: new List<MonsterSnapshot>
                {
                    new(1, 7, 2, (int)Direction.South, MonsterKind.Slime, true),
                    new(2, 0, 9, (int)Direction.East, MonsterKind.Ghost, false),
                    new(3, 5, 5, (int)Direction.West, MonsterKind.Goat, true),
                },
                SecondsRemaining: 42.5f,
                EscapeOpen: true),
            TileChanges: new List<TileChange> { new(8, 8, true, TileType.DeepWater), new(2, 2, false) });

        byte[] bytes = SnapshotCodec.Write(update);
        TickUpdate back = SnapshotCodec.Read(bytes);

        Assert.Equal(7, back.Snapshot.Tick);
        Assert.Equal(42.5f, back.Snapshot.SecondsRemaining);
        Assert.Equal(0.09, back.Snapshot.Miners[0].MoveSeconds, 3);
        Assert.Equal(2, back.Snapshot.Miners.Count);
        Assert.Equal(update.Snapshot.Miners[0], back.Snapshot.Miners[0]);
        Assert.Equal(update.Snapshot.Miners[1], back.Snapshot.Miners[1]);
        Assert.Equal(update.Snapshot.Charges[0], back.Snapshot.Charges[0]);
        Assert.Equal(2, back.TileChanges.Count);
        Assert.Equal(TileType.DeepWater, back.TileChanges[0].NewType);
        Assert.Equal(update.TileChanges[0], back.TileChanges[0]);
        Assert.Equal(update.TileChanges[1], back.TileChanges[1]);
        Assert.Equal(8, back.Snapshot.Miners[0].VisionRadius);
        Assert.Equal(5, back.Snapshot.Miners[1].VisionRadius);
        Assert.Equal(3, back.Snapshot.Items.Count);
        Assert.Equal(update.Snapshot.Items[0], back.Snapshot.Items[0]);
        Assert.Equal(update.Snapshot.Items[1], back.Snapshot.Items[1]);
        Assert.Equal(update.Snapshot.Items[2], back.Snapshot.Items[2]);
        Assert.Equal(ItemPlacement.Buried, back.Snapshot.Items[1].Placement);
        Assert.Equal((int)ItemKind.WaterPlank, back.Snapshot.Miners[0].Held);
        Assert.Equal(-1, back.Snapshot.Miners[1].Held);
        Assert.Equal(2, back.Snapshot.Molds.Count);
        Assert.Equal(update.Snapshot.Molds[0], back.Snapshot.Molds[0]);
        Assert.Equal(update.Snapshot.Molds[1], back.Snapshot.Molds[1]);
        Assert.Equal(3, back.Snapshot.Monsters.Count);
        Assert.Equal(update.Snapshot.Monsters[0], back.Snapshot.Monsters[0]);
        Assert.Equal(update.Snapshot.Monsters[1], back.Snapshot.Monsters[1]);
        Assert.Equal(update.Snapshot.Monsters[2], back.Snapshot.Monsters[2]);
        Assert.Equal(MonsterKind.Ghost, back.Snapshot.Monsters[1].Kind);
        Assert.False(back.Snapshot.Monsters[1].Alive);
        Assert.True(back.Snapshot.EscapeOpen);
    }

    [Fact]
    public void Round_trips_death_cause()
    {
        var update = new TickUpdate(
            new WorldSnapshot(1,
                new List<MinerSnapshot>
                {
                    new(1, 0, 0, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Drowned),
                    new(2, 1, 1, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Exploded),
                    new(3, 2, 2, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Left),
                    new(5, 4, 4, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Crushed),
                    new(4, 3, 3, 0, true,  0, 0, 0.0, 0.1, 5, -1),
                    new(6, 5, 5, 0, false, 0, 0, 0.0, 0.1, 5, -1, DeathCause.Burned),
                },
                new List<ChargeSnapshot>(), new List<ItemSnapshot>(), new List<MoldSnapshot>(),
                new List<MonsterSnapshot>()),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.Equal(DeathCause.Drowned, back.Snapshot.Miners[0].Cause);
        Assert.Equal(DeathCause.Exploded, back.Snapshot.Miners[1].Cause);
        Assert.Equal(DeathCause.Left, back.Snapshot.Miners[2].Cause);
        Assert.Equal(DeathCause.Crushed, back.Snapshot.Miners[3].Cause);
        Assert.Equal(DeathCause.None, back.Snapshot.Miners[4].Cause);
        Assert.Equal(DeathCause.Burned, back.Snapshot.Miners[5].Cause);
    }

    [Fact]
    public void Round_trips_empty_collections()
    {
        var update = new TickUpdate(
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>()),
            new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Empty(back.Snapshot.Miners);
        Assert.Empty(back.Snapshot.Charges);
        Assert.Empty(back.Snapshot.Items);
        Assert.Empty(back.Snapshot.Molds);
        Assert.Empty(back.TileChanges);
        Assert.Empty(back.Snapshot.Monsters);
        Assert.False(back.Snapshot.EscapeOpen);
    }
}
