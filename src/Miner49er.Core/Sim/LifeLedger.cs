namespace Miner49er.Core;

/// <summary>Who pays for a death in an Expedition run.
///
/// Humans share one pool; every bot carries its own private budget. Before this split the
/// bots drew from the same pool the humans did, and — because bots join the lobby as peers
/// — they also sized it, so adding three bots handed the party six extra lives and then let
/// the bots spend them. A reckless bot could strand the humans on a floor they had paid for.
///
/// Bot budgets refresh each floor; the human pool does not, and carries into the next floor
/// exactly as it did before.</summary>
public sealed class LifeLedger
{
    /// <summary>Private lives per bot, per floor.</summary>
    public const int BotLivesPerFloor = 2;

    /// <summary>Shared lives per human. Solo is three, which is the value a lone player had
    /// before bots existed, so adding bots neither taxes nor pads the run.</summary>
    public const int PoolLivesPerHuman = 3;

    private readonly Dictionary<int, int> _botLives = new();

    public LifeLedger(int humanCount, IEnumerable<int> botMinerIds)
    {
        PoolMax       = PoolLivesPerHuman * Math.Max(0, humanCount);
        PoolRemaining = PoolMax;
        foreach (int id in botMinerIds) _botLives[id] = BotLivesPerFloor;
    }

    /// <summary>A pool that does not scale with the party. The modes outside Expedition
    /// have always shown a single heart they never spend; this keeps that as it was.</summary>
    public static LifeLedger WithFixedPool(int poolMax) =>
        new(0, Array.Empty<int>()) { PoolMax = poolMax, PoolRemaining = poolMax };

    /// <summary>The humans' shared pool — what the HUD's ♥ count shows.</summary>
    public int PoolMax { get; private set; }
    public int PoolRemaining { get; private set; }

    /// <summary>Charge one death to whoever owns it. True if the miner may respawn; false
    /// means their lives are gone and they stay down.</summary>
    public bool TrySpend(int minerId)
    {
        if (_botLives.TryGetValue(minerId, out int left))
        {
            if (left <= 0) return false;
            _botLives[minerId] = left - 1;
            return true;
        }
        if (PoolRemaining <= 0) return false;
        PoolRemaining--;
        return true;
    }

    /// <summary>Seed the pool from a saved run, clamped to what the pool can hold.</summary>
    public void SetPoolRemaining(int remaining) => PoolRemaining = Math.Clamp(remaining, 0, PoolMax);

    /// <summary>Credit one life back — a shop LifePotion or a chest's LifeRestored — to the
    /// miner that earned it, capped at their own maximum.</summary>
    public void Restore(int minerId)
    {
        if (_botLives.TryGetValue(minerId, out int left))
        {
            _botLives[minerId] = Math.Min(left + 1, BotLivesPerFloor);
            return;
        }
        PoolRemaining = Math.Min(PoolRemaining + 1, PoolMax);
    }

    /// <summary>True when Restore would actually give this miner a life back.</summary>
    public bool CanRestore(int minerId) =>
        _botLives.TryGetValue(minerId, out int left) ? left < BotLivesPerFloor : PoolRemaining < PoolMax;

    /// <summary>Start-of-floor reset. Bots come back down with the party; the human pool
    /// carries over untouched.</summary>
    public void RefreshBots()
    {
        foreach (int id in _botLives.Keys.ToList()) _botLives[id] = BotLivesPerFloor;
    }
}
