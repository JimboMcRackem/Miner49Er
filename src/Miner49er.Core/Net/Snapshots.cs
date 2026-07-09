using System.Collections.Generic;

namespace Miner49er.Core.Net;

public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius, int Held,
    DeathCause Cause = DeathCause.None, float InvulRemaining = 0f, int StoneCount = 0,
    float StunRemaining = 0f);

public readonly record struct ChargeSnapshot(int OwnerId, int X, int Y, double FuseRemaining);

public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind, ItemPlacement Placement);

public readonly record struct MoldSnapshot(int X, int Y, double RemainingSeconds);

public readonly record struct MonsterSnapshot(
    int Id, int X, int Y, int Facing, MonsterKind Kind, bool Alive,
    float StunRemaining = 0f, bool Dormant = false);

public readonly record struct ReelChargeSnapshot(int OwnerId, int WallX, int WallY);

/// <summary>One floor cell that changed; FromBlast drives the flash, NewType is
/// the tile it became (Floor for mining/blasts, water for the flood).</summary>
public readonly record struct TileChange(int X, int Y, bool FromBlast, TileType NewType = TileType.Floor);

public readonly record struct OctopusSnapshot(int X, int Y);

public readonly record struct TreasureProgressSnapshot(int MinerId, int Found);
public readonly record struct PlacedChestSnapshot(int MinerId, int X, int Y);
public readonly record struct TripChargeSnapshot(int OwnerId, int X, int Y);
public readonly record struct PendingFallSnapshot(int X, int Y, float FractionElapsed);
public readonly record struct ScreeCollapseSnapshot(int X, int Y, int Radius);

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, IReadOnlyList<MoldSnapshot> Molds,
    IReadOnlyList<MonsterSnapshot> Monsters,
    float SecondsRemaining = -1f, bool EscapeOpen = false,
    OctopusSnapshot? Octopus = null, int Lives = 3,
    IReadOnlyList<ReelChargeSnapshot>? ReelCharges = null,
    IReadOnlyList<TreasureProgressSnapshot>? TreasureProgress = null,
    IReadOnlyList<PlacedChestSnapshot>?      PlacedChests     = null,
    IReadOnlyList<TripChargeSnapshot>?       TripCharges      = null,
    IReadOnlyList<PendingFallSnapshot>?      PendingFalls     = null,
    IReadOnlyList<ScreeCollapseSnapshot>?    ScreeCollapses   = null);

public sealed record TickUpdate(WorldSnapshot Snapshot, IReadOnlyList<TileChange> TileChanges);
