namespace Miner49er.Core;

/// <summary>Pure loot roll for a chest pickup. Probabilities:
/// LifePotion 40%, SpeedPotion 20%, LongerVision 20%, BiggerBlast 20%.</summary>
public static class ChestLootTable
{
    public static ItemKind Roll(Random rng)
    {
        double r = rng.NextDouble();
        if (r < 0.40) return ItemKind.LifePotion;
        if (r < 0.60) return ItemKind.SpeedPotion;
        if (r < 0.80) return ItemKind.LongerVision;
        return ItemKind.BiggerBlast;
    }
}
