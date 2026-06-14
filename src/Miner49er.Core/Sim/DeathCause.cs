namespace Miner49er.Core;

/// <summary>Why a miner died, replicated to clients for the death banner/feed.
/// None means the miner is still alive.</summary>
public enum DeathCause { None, Drowned, Exploded, Left, Fell, Crushed, Burned }
