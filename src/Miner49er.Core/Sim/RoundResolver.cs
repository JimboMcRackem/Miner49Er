using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, int WinnerId);

/// <summary>Resolves a round per game mode. Last-man-standing is universal: any
/// mode ends the instant one or zero miners remain alive. Each mode may add a
/// second terminal condition layered on top.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim, GameMode mode)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();

        // Solo Expedition: a single miner means last-man-standing would auto-win on tick 1.
        // Instead: lose when the miner is dead, win only on the objective (all gold + on exit).
        if (mode == GameMode.Expedition)
        {
            if (alive.Count == 0) return new RoundResult(true, -1);
            if (sim.AllGoldCleared && sim.EscapeTile is { } exit)
            {
                var winner = alive.FirstOrDefault(m => m.Pos == exit);
                if (winner is not null) return new RoundResult(true, winner.Id);
            }
            return new RoundResult(false, -1);
        }

        // Universal last-man-standing.
        if (alive.Count <= 1)
            return new RoundResult(true, alive.Count == 1 ? alive[0].Id : -1);

        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                                      && sim.GetMiner(sim.FirstToReachCenter).Alive
                => new RoundResult(true, sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => new RoundResult(true, MostGoldWinner(alive)),
            _ when sim.TimeExpired
                => new RoundResult(true, -1),   // any timed mode whose clock ran out → draw
            _ => new RoundResult(false, -1),
        };
    }

    /// <summary>Id of the unique living miner with the strictly-highest gold;
    /// -1 if the top gold value is tied between two or more (a draw).</summary>
    private static int MostGoldWinner(List<Miner> alive)
    {
        if (alive.Count == 0) return -1;
        int max = alive.Max(m => m.GoldCollected);
        var leaders = alive.Where(m => m.GoldCollected == max).ToList();
        return leaders.Count == 1 ? leaders[0].Id : -1;
    }
}
