namespace Miner49er.Core;

public sealed class ReelCharge
{
    public int OwnerId { get; }
    public GridPos WallPos { get; }
    public int BlastBonus { get; }

    internal ReelCharge(int ownerId, GridPos wallPos, int blastBonus)
    {
        OwnerId = ownerId; WallPos = wallPos; BlastBonus = blastBonus;
    }
}
