namespace Miner49er.Core;

public enum MonsterKind { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino, WaterSnake, PriestSpirit }

/// <summary>A goat ambles until it spots a miner or hears a noise, stops to wind up, then charges
/// in a straight line until something stops it.</summary>
public enum GoatPhase { Wander, WindUp, Charge }

public static class MonsterKindExtensions
{
    /// <summary>Spirits drift over hazards, ignore cracks and mold, and a lantern banishes them.</summary>
    public static bool IsSpirit(this MonsterKind k) => k is MonsterKind.Ghost or MonsterKind.PriestSpirit;
}

public sealed class Monster
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public MonsterKind Kind { get; }
    public bool Alive { get; internal set; } = true;
    public bool Dormant { get; internal set; }

    public Direction ChargeDir { get; internal set; } = Direction.East;   // Goat heading
    public GoatPhase Phase { get; internal set; }                         // Goat only
    public double MoveCooldownRemaining { get; internal set; }            // per-kind cadence gate
    public double SlowTimer { get; internal set; }                        // seconds of mold-slow remaining
    public double SlowMultiplier { get; internal set; } = 1.0;           // >1 = slower; 1.0 = normal
    public double StunRemaining { get; internal set; }                    // Goat only: pickaxe stun seconds
    public double ChantRemaining { get; internal set; }                    // Priest only: seconds until the chant lands
    public double ChantCooldown { get; internal set; }                     // Priest only: seconds before it can chant again
    public double CrackDwell { get; internal set; }                        // seconds lingering on a crack (fall-through timer)

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
        Dormant = kind is MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino;
    }
}
