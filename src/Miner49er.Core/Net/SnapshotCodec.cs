using System.Collections.Generic;
using System.IO;

namespace Miner49er.Core.Net;

/// <summary>Compact binary serialization for a per-tick world update.
/// Engine-free so it is unit-testable; the Godot layer only transports bytes.</summary>
public static class SnapshotCodec
{
    public static byte[] Write(TickUpdate update)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        var snap = update.Snapshot;
        w.Write(snap.Tick);
        w.Write(snap.SecondsRemaining);

        w.Write(snap.Miners.Count);
        foreach (var m in snap.Miners)
        {
            w.Write(m.Id); w.Write(m.X); w.Write(m.Y); w.Write(m.Facing);
            w.Write(m.Alive); w.Write(m.Gold); w.Write(m.Activity); w.Write(m.ActivityRemaining);
            w.Write(m.MoveSeconds); w.Write(m.VisionRadius); w.Write(m.Held); w.Write((byte)m.Cause);
        }

        w.Write(snap.Charges.Count);
        foreach (var c in snap.Charges)
        {
            w.Write(c.OwnerId); w.Write(c.X); w.Write(c.Y); w.Write(c.FuseRemaining);
        }

        w.Write(snap.Items.Count);
        foreach (var it in snap.Items)
        {
            w.Write(it.X); w.Write(it.Y); w.Write((int)it.Kind); w.Write((int)it.Placement);
        }

        w.Write(snap.Molds.Count);
        foreach (var mo in snap.Molds)
        {
            w.Write(mo.X); w.Write(mo.Y); w.Write(mo.RemainingSeconds);
        }

        w.Write(update.TileChanges.Count);
        foreach (var t in update.TileChanges)
        {
            w.Write(t.X); w.Write(t.Y); w.Write(t.FromBlast); w.Write((int)t.NewType);
        }

        w.Flush();
        return ms.ToArray();
    }

    public static TickUpdate Read(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var r = new BinaryReader(ms);

        int tick = r.ReadInt32();
        float secondsRemaining = r.ReadSingle();

        int minerCount = r.ReadInt32();
        var miners = new List<MinerSnapshot>(minerCount);
        for (int i = 0; i < minerCount; i++)
            miners.Add(new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
                r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte()));

        int chargeCount = r.ReadInt32();
        var charges = new List<ChargeSnapshot>(chargeCount);
        for (int i = 0; i < chargeCount; i++)
            charges.Add(new ChargeSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble()));

        int itemCount = r.ReadInt32();
        var items = new List<ItemSnapshot>(itemCount);
        for (int i = 0; i < itemCount; i++)
            items.Add(new ItemSnapshot(r.ReadInt32(), r.ReadInt32(), (ItemKind)r.ReadInt32(), (ItemPlacement)r.ReadInt32()));

        int moldCount = r.ReadInt32();
        var molds = new List<MoldSnapshot>(moldCount);
        for (int i = 0; i < moldCount; i++)
            molds.Add(new MoldSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadDouble()));

        int changeCount = r.ReadInt32();
        var changes = new List<TileChange>(changeCount);
        for (int i = 0; i < changeCount; i++)
            changes.Add(new TileChange(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), (TileType)r.ReadInt32()));

        return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds, secondsRemaining), changes);
    }
}
