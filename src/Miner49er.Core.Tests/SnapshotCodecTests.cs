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
                    new(1, 3, 4, 2, true, 5, 1, 2.5, 0.09, 8, (int)ItemKind.WaterPlank, StoneCount: 4),
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
        Assert.Equal(4, back.Snapshot.Miners[0].StoneCount);
        Assert.Equal(0, back.Snapshot.Miners[1].StoneCount);
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

    [Fact]
    public void Round_trips_octopus_snapshot()
    {
        var update = new TickUpdate(
            new WorldSnapshot(1,
                new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(),
                new List<MonsterSnapshot>(),
                Octopus: new OctopusSnapshot(20, 17)),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.Octopus);
        Assert.Equal(20, back.Snapshot.Octopus!.Value.X);
        Assert.Equal(17, back.Snapshot.Octopus!.Value.Y);
    }

    [Fact]
    public void Round_trips_null_octopus()
    {
        var update = new TickUpdate(
            new WorldSnapshot(1,
                new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(),
                new List<MonsterSnapshot>()),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.Octopus);
    }

    [Fact]
    public void Round_trips_invul_remaining_and_lives()
    {
        var miners = new List<MinerSnapshot>
        {
            new(1, 0, 0, 0, true,  0, 0, 0.0, 0.12, 5, -1, DeathCause.None, 1.5f),
            new(2, 1, 1, 0, true,  0, 0, 0.0, 0.12, 5, -1, DeathCause.None, 0f),
        };
        var update = new TickUpdate(
            new WorldSnapshot(1, miners,
                new List<ChargeSnapshot>(), new List<ItemSnapshot>(),
                new List<MoldSnapshot>(),   new List<MonsterSnapshot>(),
                Lives: 2),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.Equal(1.5f, back.Snapshot.Miners[0].InvulRemaining, 3);
        Assert.Equal(0f,   back.Snapshot.Miners[1].InvulRemaining, 3);
        Assert.Equal(2,    back.Snapshot.Lives);
    }

    [Fact]
    public void Dormant_field_round_trips_through_codec()
    {
        var grid = new TileGrid(10, 10, TileType.Floor);
        var sim = new Simulation(grid, new SimConfig());
        sim.AddMonster(1, new GridPos(5, 5), MonsterKind.SkeletonHuman);   // starts dormant
        var snap = SnapshotFactory.Capture(sim, tick: 0);
        Assert.True(snap.Monsters[0].Dormant);

        var bytes = SnapshotCodec.Write(new TickUpdate(snap, System.Array.Empty<TileChange>()));
        var decoded = SnapshotCodec.Read(bytes).Snapshot;
        Assert.True(decoded.Monsters[0].Dormant);
    }

    [Fact]
    public void Round_trips_scree_collapses()
    {
        var update = new TickUpdate(
            new WorldSnapshot(3, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>(),
                ScreeCollapses: new List<ScreeCollapseSnapshot> { new(4, 5, 1), new(9, 2, 2) }),
            new List<TileChange> { new(4, 5, false, TileType.Rock) });

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.ScreeCollapses);
        Assert.Equal(2, back.Snapshot.ScreeCollapses!.Count);
        Assert.Equal(new ScreeCollapseSnapshot(4, 5, 1), back.Snapshot.ScreeCollapses[0]);
        Assert.Equal(new ScreeCollapseSnapshot(9, 2, 2), back.Snapshot.ScreeCollapses[1]);
        // TileChanges still decode correctly after the scree block
        Assert.Single(back.TileChanges);
        Assert.Equal(TileType.Rock, back.TileChanges[0].NewType);
    }

    [Fact]
    public void Null_scree_collapses_round_trips_as_null()
    {
        var update = new TickUpdate(
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>()),
            new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.ScreeCollapses);
    }

    [Fact]
    public void Round_trips_whistles()
    {
        var update = new TickUpdate(
            new WorldSnapshot(2, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>(),
                Whistles: new List<WhistleSnapshot> { new(6, 7), new(1, 0) }),
            new List<TileChange>());

        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));

        Assert.NotNull(back.Snapshot.Whistles);
        Assert.Equal(2, back.Snapshot.Whistles!.Count);
        Assert.Equal(new WhistleSnapshot(6, 7), back.Snapshot.Whistles[0]);
        Assert.Equal(new WhistleSnapshot(1, 0), back.Snapshot.Whistles[1]);
    }

    [Fact]
    public void Null_whistles_round_trips_as_null()
    {
        var update = new TickUpdate(
            new WorldSnapshot(0, new List<MinerSnapshot>(), new List<ChargeSnapshot>(),
                new List<ItemSnapshot>(), new List<MoldSnapshot>(), new List<MonsterSnapshot>()),
            new List<TileChange>());
        var back = SnapshotCodec.Read(SnapshotCodec.Write(update));
        Assert.Null(back.Snapshot.Whistles);
    }
}
