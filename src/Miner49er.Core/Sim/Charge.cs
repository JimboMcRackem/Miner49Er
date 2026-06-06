namespace Miner49er.Core;

public sealed class Charge
{
    public int OwnerId { get; }
    public GridPos WallPos { get; }
    public double FuseRemaining { get; internal set; }

    internal Charge(int ownerId, GridPos wallPos, double fuse)
    {
        OwnerId = ownerId;
        WallPos = wallPos;
        FuseRemaining = fuse;
    }
}
