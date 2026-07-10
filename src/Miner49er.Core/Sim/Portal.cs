namespace Miner49er.Core;

/// <summary>Two true kinds. "Black void" is a derived state (partner not yet
/// revealed), not a kind.</summary>
public enum PortalKind { Stable, Unstable }

/// <summary>Deterministic placement result handed from the map generator to the
/// simulation. Id is unique per floor; LinkId is the partner's Id.</summary>
public readonly record struct PortalSpec(int Id, GridPos Pos, PortalKind Kind, int LinkId);
