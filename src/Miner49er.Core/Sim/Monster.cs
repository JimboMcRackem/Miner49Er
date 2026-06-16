namespace Miner49er.Core;

public enum MonsterKind { Slime, Ghost, Goat }

public sealed class Monster
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public MonsterKind Kind { get; }
    public bool Alive { get; internal set; } = true;

    public Direction ChargeDir { get; internal set; } = Direction.East;   // Goat heading
    public double MoveCooldownRemaining { get; internal set; }            // per-kind cadence gate

    internal Monster(int id, GridPos pos, MonsterKind kind)
    {
        Id = id; Pos = pos; Kind = kind;
    }
}
