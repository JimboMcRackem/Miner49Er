using System.Collections.Generic;
using System.Linq;

namespace Miner49er.Core;

/// <summary>Why a round ended without a winner, so the results screen can tell the
/// contestant what happened instead of a generic "draw".</summary>
public enum RoundEndReason : byte
{
    None = 0,        // a win, or not applicable
    AllEliminated,   // every miner died (last-man-standing wipe)
    TimeExpired,     // the clock ran out with no qualifying winner
    TreasureLost,    // the treasure / idols were submerged and can never be recovered
    Tie,             // tied on the deciding metric (gold, hold time)
}

public readonly record struct RoundResult(bool IsOver, bool FloorCleared, int WinnerId,
                                           RoundEndReason Reason = RoundEndReason.None)
{
    public static RoundResult Ongoing()         => new(false, false, -1);
    public static RoundResult Win(int id)       => new(true,  false, id);
    public static RoundResult Loss(RoundEndReason reason = RoundEndReason.None) => new(true, false, -1, reason);
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

        // Grudge Match: infinite respawns make "all dead" transient, so it is never decided by
        // last-man-standing — only by most kills when the clock runs out. Resolved before the
        // universal rule for that reason.
        if (mode == GameMode.GrudgeMatch)
            return sim.TimeExpired ? MostKillsResult(sim.Miners.ToList()) : RoundResult.Ongoing();

        // Universal last-man-standing.
        if (alive.Count <= 1)
            return alive.Count == 1
                ? RoundResult.Win(alive[0].Id)
                : RoundResult.Loss(RoundEndReason.AllEliminated);

        return mode switch
        {
            GameMode.ReachCenter when sim.FirstToReachCenter >= 0
                                      && sim.GetMiner(sim.FirstToReachCenter).Alive
                => RoundResult.Win(sim.FirstToReachCenter),
            GameMode.GoldRush when sim.TimeExpired
                => GoldRushResult(alive),
            GameMode.DemolitionDerby when sim.TimeExpired
                => MostKillsResult(alive),
            GameMode.TreasureHunt when sim.TreasureWinner() >= 0
                => RoundResult.Win(sim.TreasureWinner()),
            GameMode.TreasureHunt when sim.TreasureHuntUnwinnable()
                => RoundResult.Loss(RoundEndReason.TreasureLost), // every alive player's idol is submerged

            _ when sim.TimeExpired
                => RoundResult.Loss(RoundEndReason.TimeExpired),
            _ => RoundResult.Ongoing(),
        };
    }

    // Gold Rush at time-up: the richest miner wins; a tie for the lead is a draw.
    private static RoundResult GoldRushResult(List<Miner> alive)
    {
        int w = MostGoldWinner(alive);
        return w >= 0 ? RoundResult.Win(w) : RoundResult.Loss(RoundEndReason.Tie);
    }

    private static int MostGoldWinner(List<Miner> alive)
    {
        if (alive.Count == 0) return -1;
        int max = alive.Max(m => m.GoldCollected);
        var leaders = alive.Where(m => m.GoldCollected == max).ToList();
        return leaders.Count == 1 ? leaders[0].Id : -1;
    }

    // At the buzzer, the sole miner with the most kills wins; a tie for the lead (including
    // everyone on zero) is a draw. Shared by timed Demolition Derby and Grudge Match.
    private static RoundResult MostKillsResult(List<Miner> contenders)
    {
        if (contenders.Count == 0) return RoundResult.Loss(RoundEndReason.Tie);
        int max = contenders.Max(m => m.Kills);
        var leaders = contenders.Where(m => m.Kills == max).ToList();
        return leaders.Count == 1 ? RoundResult.Win(leaders[0].Id)
                                  : RoundResult.Loss(RoundEndReason.Tie);
    }

    private static RoundResult ResolveTreasureHeist(Simulation sim)
    {
        // Treasure lost to a lethal tile (submerged by the flood, over a pit/lava) with no
        // holder: it can never be recovered, so the objective failed for everyone. Nobody wins
        // — a drowned holder does not "win" the treasure they lost. It's a draw.
        if (sim.TreasureSubmerged)
            return RoundResult.Loss(RoundEndReason.TreasureLost);

        // Death match wipe: everyone eliminated (respawns off) -> decide now by most time.
        // Guarded on !RespawnEnabled because in respawn mode "all dead" is a transient state.
        if (!sim.RespawnEnabled && sim.Miners.All(m => !m.Alive))
            return CumulativeWinnerOrDraw(sim, RoundEndReason.AllEliminated);

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
        if (best <= 0) return RoundResult.Loss(RoundEndReason.TimeExpired); // nobody ever held it
        return RoundResult.Ongoing(); // tied leaders -> keep ticking through sudden-death
    }

    private static RoundResult CumulativeWinnerOrDraw(Simulation sim, RoundEndReason drawReason)
    {
        double best = sim.Miners.Select(m => sim.HoldSecondsOf(m.Id)).DefaultIfEmpty(0).Max();
        if (best <= 0) return RoundResult.Loss(drawReason); // nobody ever held it -> draw
        var leaders = sim.Miners.Where(m => sim.HoldSecondsOf(m.Id) == best).ToList();
        return leaders.Count == 1 ? RoundResult.Win(leaders[0].Id) : RoundResult.Loss(RoundEndReason.Tie);
    }
}
