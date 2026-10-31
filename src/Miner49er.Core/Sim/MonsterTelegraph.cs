using Miner49er.Core.Net;

namespace Miner49er.Core;

/// <summary>Whether a monster's next step can land on a miner, which is how they strike.
/// Client-side warning only; goats telegraph through their own wind-up.</summary>
public static class MonsterTelegraph
{
    public static bool IsAboutToStrike(MonsterSnapshot mo, GridPos miner) =>
        mo.Alive && !mo.Dormant && mo.StunRemaining <= 0f && mo.Kind != MonsterKind.Goat
        && new GridPos(mo.X, mo.Y).ManhattanTo(miner) == 1;
}
