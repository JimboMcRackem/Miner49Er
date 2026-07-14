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

        if (mode == GameMode.TreasureHeist)
            return ResolveTreasureHeist(sim);

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
            GameMode.TreasureHunt when sim.TreasureWinner() >= 0
                => RoundResult.Win(sim.TreasureWinner()),
            GameMode.TreasureHunt when sim.TreasureHuntUnwinnable()
                => RoundResult.Loss(), // every alive player's idol is submerged -> draw

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

    private static RoundResult ResolveTreasureHeist(Simulation sim)
    {
        // Treasure lost to a lethal tile (submerged by the flood, over a pit/lava) with no
        // holder: it can never be recovered, so the round is unwinnable. End now — longest
        // cumulative holder wins, else a draw.
        if (sim.TreasureSubmerged)
            return CumulativeWinnerOrDraw(sim);

        // Death match wipe: everyone eliminated (respawns off) -> decide now by most time.
        // Guarded on !RespawnEnabled because in respawn mode "all dead" is a transient state.
        if (!sim.RespawnEnabled && sim.Miners.All(m => !m.Alive))
            return CumulativeWinnerOrDraw(sim);

        if (!sim.TimeExpired)
            return RoundResult.Ongoing();

        var miners = sim.Miners.ToList();

        if (!sim.WinByCumulative())
        {
            // Buzzer: holder at expiry wins; else sudden-death.
            if (sim.TreasureHolderId >= 0) return RoundResult.Win(sim.TreasureHolderId);
            if (sim.SuddenDeathWinner >= 0) return RoundResult.Win(sim.SuddenDeathWinner);
            return RoundResult.Ongoing(); // keep ticking through sudden-death
        }

        // Most-time: highest cumulative; tie or all-zero -> holder, else sudden-death.
        double best = miners.Select(m => sim.HoldSecondsOf(m.Id)).DefaultIfEmpty(0).Max();
        if (best > 0)
        {
            var leaders = miners.Where(m => sim.HoldSecondsOf(m.Id) == best).ToList();
            if (leaders.Count == 1) return RoundResult.Win(leaders[0].Id);
        }
        if (sim.TreasureHolderId >= 0) return RoundResult.Win(sim.TreasureHolderId);
        if (sim.SuddenDeathWinner >= 0) return RoundResult.Win(sim.SuddenDeathWinner);
        if (best <= 0) return RoundResult.Loss(); // nobody ever held it and none left to contest -> draw
        return RoundResult.Ongoing();
    }

    private static RoundResult CumulativeWinnerOrDraw(Simulation sim)
    {
        double best = sim.Miners.Select(m => sim.HoldSecondsOf(m.Id)).DefaultIfEmpty(0).Max();
        if (best <= 0) return RoundResult.Loss(); // nobody ever held it -> draw
        var leaders = sim.Miners.Where(m => sim.HoldSecondsOf(m.Id) == best).ToList();
        return leaders.Count == 1 ? RoundResult.Win(leaders[0].Id) : RoundResult.Loss();
    }
}
