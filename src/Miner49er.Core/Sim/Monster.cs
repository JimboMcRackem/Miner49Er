namespace Miner49er.Core;

public enum MonsterKind { Slime, Ghost, Goat, ZombieMiner, SkeletonHuman, SkeletonDino }

public sealed class Monster
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public MonsterKind Kind { get; }
    public bool Alive { get; internal set; } = true;
    public bool Dormant { get; internal set; }

    public Direction ChargeDir { get; internal set; } = Direction.East;   // Goat heading
    public double MoveCooldownRemaining { get; internal set; }            // per-kind cadence gate
    public double SlowTimer { get; internal set; }                        // seconds of mold-slow remaining
    public double SlowMultiplier { get; internal set; } = 1.0;           // >1 = slower; 1.0 = normal
    public double StunRemaining { get; internal set; }                    // Goat only: pickaxe stun seconds

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
        Dormant = kind is MonsterKind.SkeletonHuman or MonsterKind.SkeletonDino;
    }
}
