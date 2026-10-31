namespace Miner49er.Core;

/// <summary>Pure loot roll for a chest pickup. Probabilities:
/// LifePotion 40%, SpeedPotion 20%, LongerVision 20%, BiggerBlast 20%. When a life would
/// be wasted the chest never rolls one, and the three buffs share the whole table.</summary>
public static class ChestLootTable
{
    public static ItemKind Roll(Random rng, bool lifeIsUseful = true)
    {
        double r = lifeIsUseful ? rng.NextDouble() : 0.40 + rng.NextDouble() * 0.60;
        if (r < 0.40) return ItemKind.LifePotion;
        if (r < 0.60) return ItemKind.SpeedPotion;
        if (r < 0.80) return ItemKind.LongerVision;
        return ItemKind.BiggerBlast;
    }
}
