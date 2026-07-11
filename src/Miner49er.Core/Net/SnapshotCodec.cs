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
            w.Write(m.InvulRemaining); w.Write(m.StoneCount); w.Write(m.Listening);
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

        w.Write(snap.Monsters.Count);
        foreach (var mo in snap.Monsters)
        {
            w.Write(mo.Id); w.Write(mo.X); w.Write(mo.Y);
            w.Write(mo.Facing); w.Write((int)mo.Kind); w.Write(mo.Alive);
            w.Write(mo.Dormant);
        }

        w.Write(snap.EscapeOpen);
        w.Write(snap.Lives);

        w.Write(snap.ReelCharges?.Count ?? 0);
        foreach (var rc in snap.ReelCharges ?? System.Array.Empty<ReelChargeSnapshot>())
        { w.Write(rc.OwnerId); w.Write(rc.WallX); w.Write(rc.WallY); }

        w.Write(snap.Octopus is not null);
        if (snap.Octopus is { } oct)
        { w.Write(oct.X); w.Write(oct.Y); }

        w.Write(snap.TreasureProgress?.Count ?? 0);
        foreach (var tp in snap.TreasureProgress ?? System.Array.Empty<TreasureProgressSnapshot>())
        { w.Write(tp.MinerId); w.Write(tp.Found); }

        w.Write(snap.PlacedChests?.Count ?? 0);
        foreach (var pc in snap.PlacedChests ?? System.Array.Empty<PlacedChestSnapshot>())
        { w.Write(pc.MinerId); w.Write(pc.X); w.Write(pc.Y); }

        w.Write(snap.TripCharges?.Count ?? 0);
        foreach (var tc in snap.TripCharges ?? System.Array.Empty<TripChargeSnapshot>())
        { w.Write(tc.OwnerId); w.Write(tc.X); w.Write(tc.Y); }

        w.Write(snap.ScreeCollapses?.Count ?? 0);
        foreach (var sc in snap.ScreeCollapses ?? System.Array.Empty<ScreeCollapseSnapshot>())
        { w.Write(sc.X); w.Write(sc.Y); w.Write(sc.Radius); }

        w.Write(snap.Whistles?.Count ?? 0);
        foreach (var wh in snap.Whistles ?? System.Array.Empty<WhistleSnapshot>())
        { w.Write(wh.X); w.Write(wh.Y); }

        w.Write(snap.PortalUses?.Count ?? 0);
        foreach (var pu in snap.PortalUses ?? System.Array.Empty<PortalUseSnapshot>())
        { w.Write(pu.X); w.Write(pu.Y); w.Write(pu.ToX); w.Write(pu.ToY); w.Write((int)pu.Kind); }

        w.Write(snap.Throws?.Count ?? 0);
        foreach (var th in snap.Throws ?? System.Array.Empty<StoneThrowSnapshot>())
        { w.Write(th.ThrowerId); w.Write(th.FromX); w.Write(th.FromY); w.Write(th.ToX); w.Write(th.ToY); }

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
        {
            var msnap = new MinerSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadBoolean(), r.ReadInt32(), r.ReadInt32(), r.ReadDouble(), r.ReadDouble(),
                r.ReadInt32(), r.ReadInt32(), (DeathCause)r.ReadByte(), r.ReadSingle(), r.ReadInt32());
            miners.Add(msnap with { Listening = r.ReadBoolean() });
        }

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

        int monsterCount = r.ReadInt32();
        var monsters = new List<MonsterSnapshot>(monsterCount);
        for (int i = 0; i < monsterCount; i++)
            monsters.Add(new MonsterSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                r.ReadInt32(), (MonsterKind)r.ReadInt32(), r.ReadBoolean(),
                Dormant: r.ReadBoolean()));

        bool escapeOpen = r.ReadBoolean();
        int lives = r.ReadInt32();

        int reelCount = r.ReadInt32();
        var reelCharges = new List<ReelChargeSnapshot>(reelCount);
        for (int i = 0; i < reelCount; i++)
            reelCharges.Add(new ReelChargeSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

        OctopusSnapshot? octopus = null;
        if (r.ReadBoolean())
        {
            int ox = r.ReadInt32(), oy = r.ReadInt32();
            octopus = new OctopusSnapshot(ox, oy);
        }

        int tpCount = r.ReadInt32();
        List<TreasureProgressSnapshot>? treasureProgress = tpCount > 0
            ? new List<TreasureProgressSnapshot>(tpCount) : null;
        for (int i = 0; i < tpCount; i++)
            treasureProgress!.Add(new TreasureProgressSnapshot(r.ReadInt32(), r.ReadInt32()));

        int pcCount = r.ReadInt32();
        List<PlacedChestSnapshot>? placedChests = pcCount > 0
            ? new List<PlacedChestSnapshot>(pcCount) : null;
        for (int i = 0; i < pcCount; i++)
            placedChests!.Add(new PlacedChestSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

        int tcCount = r.ReadInt32();
        List<TripChargeSnapshot>? tripCharges = tcCount > 0
            ? new List<TripChargeSnapshot>(tcCount) : null;
        for (int i = 0; i < tcCount; i++)
            tripCharges!.Add(new TripChargeSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

        int screeCount = r.ReadInt32();
        List<ScreeCollapseSnapshot>? screeCollapses = screeCount > 0
            ? new List<ScreeCollapseSnapshot>(screeCount) : null;
        for (int i = 0; i < screeCount; i++)
            screeCollapses!.Add(new ScreeCollapseSnapshot(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

        int whistleCount = r.ReadInt32();
        List<WhistleSnapshot>? whistles = whistleCount > 0
            ? new List<WhistleSnapshot>(whistleCount) : null;
        for (int i = 0; i < whistleCount; i++)
            whistles!.Add(new WhistleSnapshot(r.ReadInt32(), r.ReadInt32()));

        int portalUseCount = r.ReadInt32();
        List<PortalUseSnapshot>? portalUses = portalUseCount > 0
            ? new List<PortalUseSnapshot>(portalUseCount) : null;
        for (int i = 0; i < portalUseCount; i++)
            portalUses!.Add(new PortalUseSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
                (PortalKind)r.ReadInt32()));

        int throwCount = r.ReadInt32();
        List<StoneThrowSnapshot>? throws = throwCount > 0
            ? new List<StoneThrowSnapshot>(throwCount) : null;
        for (int i = 0; i < throwCount; i++)
            throws!.Add(new StoneThrowSnapshot(
                r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

        int changeCount = r.ReadInt32();
        var changes = new List<TileChange>(changeCount);
        for (int i = 0; i < changeCount; i++)
            changes.Add(new TileChange(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), (TileType)r.ReadInt32()));

        return new TickUpdate(new WorldSnapshot(tick, miners, charges, items, molds,
            monsters, secondsRemaining, escapeOpen, octopus, lives, reelCharges,
            treasureProgress, placedChests, tripCharges, ScreeCollapses: screeCollapses,
            Whistles: whistles, PortalUses: portalUses, Throws: throws), changes);
    }
}
