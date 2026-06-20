using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, bool FloorCleared, int WinnerId)
{
    public static RoundResult Ongoing()         => new(false, false, -1);
    public static RoundResult Win(int id)       => new(true,  false, id);
    public static RoundResult Loss()            => new(true,  false, -1);
    public static RoundResult NextFloor(int id) => new(false, true,  id);
}

/// <summary>Resolves a round per game mode. Last-man-standing is universal: any
/// mode ends the instant one or zero miners remain alive. Each mode may add a
/// second terminal condition layered on top.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim, GameMode mode)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();

        // Solo Expedition: a single miner means last-man-standing would auto-win on tick 1.
        // Instead: lose when the miner is dead; boss win on chest grab; floor clear on exit.
        if (mode == GameMode.Expedition)
        {
            if (alive.Count == 0) return RoundResult.Loss();
            if (sim.EscapeOpen && sim.EscapeTile is { } exit)
            {
                if (alive.All(m => m.Pos == exit))
                    return RoundResult.NextFloor(alive[0].Id);
            }
            return RoundResult.Ongoing();
        }

        // Universal last-man-standing.
        if (alive.Count <= 1)
            return new RoundResult(true, false, alive.Count == 1 ? alive[0].Id : -1);

        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                                      && sim.GetMiner(sim.FirstToReachCenter).Alive
                => RoundResult.Win(sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => RoundResult.Win(MostGoldWinner(alive)),
            _ when sim.TimeExpired
                => RoundResult.Loss(),
            _ => RoundResult.Ongoing(),
        };
    }

    private static int MostGoldWinner(List<Miner> alive)
    {
        if (alive.Count == 0) return -1;
        int max = alive.Max(m => m.GoldCollected);
        var leaders = alive.Where(m => m.GoldCollected == max).ToList();
        return leaders.Count == 1 ? leaders[0].Id : -1;
    }
}
