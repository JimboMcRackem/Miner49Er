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
                m.GoldCollected, (int)m.Activity, m.ActivitySecondsRemaining))
            .ToList();

        var charges = sim.Charges
            .Select(c => new ChargeSnapshot(c.OwnerId, c.WallPos.X, c.WallPos.Y, c.FuseRemaining))
            .ToList();

        return new WorldSnapshot(tick, miners, charges, (float)sim.SecondsRemaining);
    }
}
