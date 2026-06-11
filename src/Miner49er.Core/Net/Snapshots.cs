using System.Collections.Generic;

namespace Miner49er.Core.Net;

public readonly record struct MinerSnapshot(
    int Id, int X, int Y, int Facing, bool Alive, int Gold, int Activity,
    double ActivityRemaining, double MoveSeconds, int VisionRadius);

public readonly record struct ChargeSnapshot(int OwnerId, int X, int Y, double FuseRemaining);

public readonly record struct ItemSnapshot(int X, int Y, ItemKind Kind, ItemPlacement Placement);

/// <summary>One floor cell that changed; FromBlast drives the flash, NewType is
/// the tile it became (Floor for mining/blasts, water for the flood).</summary>
public readonly record struct TileChange(int X, int Y, bool FromBlast, TileType NewType = TileType.Floor);

public sealed record WorldSnapshot(
    int Tick, IReadOnlyList<MinerSnapshot> Miners, IReadOnlyList<ChargeSnapshot> Charges,
    IReadOnlyList<ItemSnapshot> Items, float SecondsRemaining = -1f);

public sealed record TickUpdate(WorldSnapshot Snapshot, IReadOnlyList<TileChange> TileChanges);
