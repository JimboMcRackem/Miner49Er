namespace Miner49er.Core;

/// <summary>A floor-tile charge planted by a miner. Detonates when any miner steps on the tile.</summary>
public sealed class TripCharge
{
    public int OwnerId { get; }
    public GridPos Pos  { get; }
    public int BlastBonus { get; }

    internal TripCharge(int ownerId, GridPos pos, int blastBonus)
    {
        OwnerId = ownerId; Pos = pos; BlastBonus = blastBonus;
    }
}
