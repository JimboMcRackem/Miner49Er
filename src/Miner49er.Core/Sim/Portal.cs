namespace Miner49er.Core
{
    /// <summary>Two true kinds. "Black void" is a derived state (partner not yet
    /// revealed), not a kind.</summary>
    public enum PortalKind { Stable, Unstable }

    /// <summary>Deterministic placement result handed from the map generator to the
    /// simulation. Id is unique per floor; LinkId is the partner's Id.</summary>
    public readonly record struct PortalSpec(int Id, GridPos Pos, PortalKind Kind, int LinkId);

    /// <summary>Live per-floor portal state inside the simulation.</summary>
    internal sealed class Portal
    {
        public int Id { get; init; }
        public GridPos Pos { get; init; }
        public PortalKind Kind { get; init; }
        public int LinkId { get; init; }
        public bool Collapsed { get; set; }
        public double CooldownRemaining { get; set; }
    }

    /// <summary>Read-only projection for tests and host-side inspection.</summary>
    public readonly record struct PortalReadModel(
        int Id, GridPos Pos, PortalKind Kind, int LinkId, bool Collapsed);
}
