namespace Miner49er.Core.AI;

public readonly struct BotAction
{
    public readonly int Dir;   // -1 = stand still
    public readonly bool Mine;
    public readonly bool Plant;
    public readonly bool Use;
    public readonly bool Throw;
    public readonly bool ThrowDynamite;
    public readonly bool Whistle;
    public readonly bool Listen;

    public BotAction(int dir, bool mine = false, bool plant = false, bool use = false,
                     bool throwStone = false, bool whistle = false, bool listen = false,
                     bool throwDynamite = false)
    {
        Dir = dir; Mine = mine; Plant = plant; Use = use; Throw = throwStone;
        ThrowDynamite = throwDynamite; Whistle = whistle; Listen = listen;
    }

    public static readonly BotAction Idle = new(-1);
}
