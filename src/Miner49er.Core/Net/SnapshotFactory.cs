using System.Linq;

namespace Miner49er.Core.Net;

/// <summary>Captures authoritative simulation state into a transmittable snapshot.</summary>
public static class SnapshotFactory
{
    public static WorldSnapshot Capture(Simulation sim, int tick)
    {
        var miners = sim.Miners
            .Select(m => new MinerSnapshot(
                m.Id, m.Pos.X, m.Pos.Y, (int)m.Facing, m.Alive,
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining,
                sim.EffectiveMoveSeconds(m.Id), sim.EffectiveVisionRadius(m.Id),
                m.Held is { } h ? (int)h : -1))
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

        return new WorldSnapshot(tick, miners, charges, items, molds, (float)sim.SecondsRemaining);
    }
}
