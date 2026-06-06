using System.Linq;

namespace Miner49er.Core;

public readonly record struct RoundResult(bool IsOver, int WinnerId);

/// <summary>Last-man-standing resolution. A round is over when one or zero
/// miners remain alive; the sole survivor (if any) is the winner.</summary>
public static class RoundResolver
{
    public static RoundResult Resolve(Simulation sim)
    {
        var alive = sim.Miners.Where(m => m.Alive).ToList();
        if (alive.Count <= 1)
            return new RoundResult(true, alive.Count == 1 ? alive[0].Id : -1);
        return new RoundResult(false, -1);
    }
}
