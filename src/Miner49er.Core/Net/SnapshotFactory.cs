using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core.Net;

/// <summary>Captures authoritative simulation state into a transmittable snapshot.</summary>
public static class SnapshotFactory
{
    public static WorldSnapshot Capture(Simulation sim, int tick, int lives = 3)
    {
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
                m.Held is { } h ? (int)h : -1, m.DeathCause, (float)m.InvulnerableRemaining,
                m.StoneCount, (float)m.StunRemaining))
            .ToList();

        var charges = sim.Charges
            .Select(c => new ChargeSnapshot(c.OwnerId, c.WallPos.X, c.WallPos.Y, c.FuseRemaining))
            .ToList();

        var items = sim.Items
            .Select(it => new ItemSnapshot(it.Pos.X, it.Pos.Y, it.Kind, it.Placement))
            .ToList();

        var molds = sim.Molds
            .Select(mo => new MoldSnapshot(mo.Pos.X, mo.Pos.Y, mo.RemainingSeconds))
            .ToList();

        var monsters = sim.Monsters
            .Select(mo => new MonsterSnapshot(
                mo.Id, mo.Pos.X, mo.Pos.Y, (int)mo.Facing, mo.Kind, mo.Alive,
                (float)mo.StunRemaining))
            .ToList();

        OctopusSnapshot? octopus = sim.Octopus is { } oct
            ? new OctopusSnapshot(oct.Pos.X, oct.Pos.Y)
            : null;

        var reelCharges = sim.ReelCharges
            .Select(r => new ReelChargeSnapshot(r.OwnerId, r.WallPos.X, r.WallPos.Y))
            .ToList();

        IReadOnlyList<TreasureProgressSnapshot>? treasureProgress = null;
        IReadOnlyList<PlacedChestSnapshot>?      placedChests     = null;
        if (sim.Config.TreasureHuntMode)
        {
            treasureProgress = sim.GetTreasureProgress()
                .Select(p => new TreasureProgressSnapshot(p.MinerId, p.Found))
                .ToList();
            placedChests = sim.GetPlacedChests()
                .Select(c => new PlacedChestSnapshot(c.MinerId, c.X, c.Y))
                .ToList();
        }

        var tripCharges = sim.TripCharges
            .Select(tc => new TripChargeSnapshot(tc.OwnerId, tc.Pos.X, tc.Pos.Y))
            .ToList();

        var pendingFalls = sim.PendingFalls
            .Select(pf => new PendingFallSnapshot(pf.Pos.X, pf.Pos.Y, (float)pf.FractionElapsed))
            .ToList();

        return new WorldSnapshot(tick, miners, charges, items, molds, monsters,
            (float)sim.SecondsRemaining, sim.EscapeOpen, octopus, lives, reelCharges,
            treasureProgress, placedChests, tripCharges, pendingFalls);
    }
}
