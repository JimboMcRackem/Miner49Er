namespace Miner49er.Core;

/// <summary>A rare happening that takes over the mine for a while. One runs at a time.</summary>
public enum MineEventKind : byte { None, Tremor, Blackout, Firedamp, FlashFlood, Awakening, GoldSeam, GhostShift }

public static class MineEvents
{
    /// <summary>The events a mode can roll. A gold seam only means something where gold is the goal.</summary>
    public static IReadOnlyList<MineEventKind> PoolFor(GameMode mode)
    {
        var pool = new List<MineEventKind>
        {
            MineEventKind.Tremor, MineEventKind.Blackout, MineEventKind.Firedamp,
            MineEventKind.FlashFlood, MineEventKind.Awakening, MineEventKind.GhostShift,
        };
        if (mode == GameMode.Expedition) pool.Add(MineEventKind.GoldSeam);
        return pool;
    }
}
