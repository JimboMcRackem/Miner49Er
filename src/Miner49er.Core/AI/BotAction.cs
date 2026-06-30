namespace Miner49er.Core.AI;

public readonly struct BotAction
{
    public readonly int Dir;   // -1 = stand still
    public readonly bool Mine;
    public readonly bool Plant;
    public readonly bool Use;
    public readonly bool Throw;

    public BotAction(int dir, bool mine = false, bool plant = false, bool use = false, bool throwStone = false)
    {
        Dir = dir; Mine = mine; Plant = plant; Use = use; Throw = throwStone;
    }

    public static readonly BotAction Idle = new(-1);
}
