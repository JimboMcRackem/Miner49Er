using Miner49er.Core;
using Miner49er.Core.Net;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class SnapshotTreasureHuntTests
{
    private static Simulation TreasureSim()
    {
        var cfg = new SimConfig { Seed = 1, TreasureHuntMode = true };
        var sim = new Simulation(new TileGrid(10, 10, TileType.Floor), cfg);
        sim.AddMiner(1, new GridPos(1, 1));
        return sim;
    }

    [Fact]
    public void Capture_includes_treasure_progress_when_treasure_hunt_mode()
    {
        var sim = TreasureSim();
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        Assert.NotNull(snap.TreasureProgress);
        Assert.Single(snap.TreasureProgress!);
        Assert.Equal(1, snap.TreasureProgress![0].MinerId);
        Assert.Equal(0, snap.TreasureProgress![0].Found);
    }

    [Fact]
    public void Capture_includes_empty_placed_chests_before_any_placed()
    {
        var sim = TreasureSim();
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        Assert.NotNull(snap.PlacedChests);
        Assert.Empty(snap.PlacedChests!);
    }

    [Fact]
    public void Capture_includes_placed_chests_after_placement()
    {
        var sim = TreasureSim();
        sim.TryUseItem(1);
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        Assert.NotNull(snap.PlacedChests);
        Assert.Single(snap.PlacedChests!);
        Assert.Equal(1, snap.PlacedChests![0].MinerId);
    }

    [Fact]
    public void Codec_roundtrip_preserves_treasure_progress()
    {
        var sim = TreasureSim();
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.NotNull(decoded.Snapshot.TreasureProgress);
        Assert.Equal(snap.TreasureProgress![0].Found,
                     decoded.Snapshot.TreasureProgress![0].Found);
        Assert.Equal(snap.TreasureProgress![0].MinerId,
                     decoded.Snapshot.TreasureProgress![0].MinerId);
    }

    [Fact]
    public void Codec_roundtrip_preserves_placed_chests()
    {
        var sim = TreasureSim();
        sim.TryUseItem(1);
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.NotNull(decoded.Snapshot.PlacedChests);
        Assert.Single(decoded.Snapshot.PlacedChests!);
        Assert.Equal(snap.PlacedChests![0].X, decoded.Snapshot.PlacedChests![0].X);
        Assert.Equal(snap.PlacedChests![0].Y, decoded.Snapshot.PlacedChests![0].Y);
    }

    [Fact]
    public void Codec_roundtrip_without_treasure_hunt_leaves_fields_null()
    {
        var sim = new Simulation(new TileGrid(5, 5, TileType.Floor), new SimConfig());
        sim.AddMiner(1, new GridPos(1, 1));
        var snap = SnapshotFactory.Capture(sim, 1, 1);
        var update = new TickUpdate(snap, new List<TileChange>());
        var bytes = SnapshotCodec.Write(update);
        var decoded = SnapshotCodec.Read(bytes);
        Assert.Null(decoded.Snapshot.TreasureProgress);
        Assert.Null(decoded.Snapshot.PlacedChests);
    }
}
