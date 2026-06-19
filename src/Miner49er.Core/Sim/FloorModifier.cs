namespace Miner49er.Core;

public enum FloorModifier { None, DarkMine, Unstable, MonsterSurge, Flooded, Haste }

public static class FloorModifiers
{
    private static readonly FloorModifier[] Pool =
    {
        FloorModifier.DarkMine,
        FloorModifier.Unstable,
        FloorModifier.MonsterSurge,
        FloorModifier.Flooded,
        FloorModifier.Haste,
    };

    public static FloorModifier Pick(int matchSeed, int floor)
    {
        if (floor >= 21 || floor % 4 == 0) return FloorModifier.None;
        var rng = new System.Random(matchSeed ^ (floor * 7919));
        return Pool[rng.Next(Pool.Length)];
    }

    public static void Apply(FloorModifier mod, MapConfig map, SimConfig sim)
    {
        switch (mod)
        {
            case FloorModifier.DarkMine:
                sim.VisionRadius = System.Math.Max(2, sim.VisionRadius / 2);
                break;
            case FloorModifier.Unstable:
                map.CaveIns = true;
                map.CrackSiteCount *= 2;
                break;
            case FloorModifier.MonsterSurge:
                sim.MonsterCountMultiplier = 1.5f;
                break;
            case FloorModifier.Flooded:
                map.PoolCount  += 3;
                map.RiverCount += 2;
                break;
            case FloorModifier.Haste:
                sim.BaseMoveSeconds         *= 0.7;
                sim.MonsterSlimeMoveSeconds *= 0.7;
                sim.MonsterGhostMoveSeconds *= 0.7;
                sim.MonsterGoatMoveSeconds  *= 0.7;
                break;
        }
    }

    public static string DisplayName(FloorModifier mod) => mod switch
    {
        FloorModifier.DarkMine     => "DARK MINE",
        FloorModifier.Unstable     => "UNSTABLE",
        FloorModifier.MonsterSurge => "MONSTER SURGE",
        FloorModifier.Flooded      => "FLOODED",
        FloorModifier.Haste        => "HASTE",
        _                          => "",
    };
}
