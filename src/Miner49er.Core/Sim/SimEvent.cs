namespace Miner49er.Core;

public abstract record SimEvent;
public sealed record MinerMoved(int MinerId, GridPos From, GridPos To) : SimEvent;
public sealed record ActivityStarted(int MinerId, ActivityKind Kind, GridPos Target) : SimEvent;
public sealed record RockMined(int MinerId, GridPos Pos, bool WasGold) : SimEvent;
public sealed record ChargePlanted(int MinerId, GridPos WallPos) : SimEvent;
public sealed record Explosion(GridPos WallPos, IReadOnlyList<GridPos> DestroyedRock) : SimEvent;
public sealed record MinerKilled(int MinerId) : SimEvent;
public sealed record MinerDrowned(int MinerId) : SimEvent;
public sealed record MinerReachedCenter(int MinerId) : SimEvent;
