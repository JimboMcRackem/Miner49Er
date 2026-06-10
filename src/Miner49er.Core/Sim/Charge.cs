namespace Miner49er.Core;

public sealed class Charge
{
    public int OwnerId { get; }
    public GridPos WallPos { get; }
    public double FuseRemaining { get; internal set; }
    public int BlastBonus { get; }   // owner's blast-radius bonus captured at plant time

    internal Charge(int ownerId, GridPos wallPos, double fuse, int blastBonus)
    {
        OwnerId = ownerId;
        WallPos = wallPos;
        FuseRemaining = fuse;
        BlastBonus = blastBonus;
    }
}
