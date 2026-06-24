namespace Miner49er.Core;

public enum ActivityKind { None, Mining, Planting, PlantingDetonator }

public sealed class Miner
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public bool Alive { get; internal set; } = true;
    public DeathCause DeathCause { get; internal set; } = DeathCause.None;
    public int GoldCollected { get; internal set; }

    public ItemKind? Held { get; internal set; }   // null = empty hand (1-slot inventory)

    public ActivityKind Activity { get; internal set; } = ActivityKind.None;
    public GridPos ActivityTarget { get; internal set; }
    public double ActivitySecondsRemaining { get; internal set; }

    public double MoveCooldownRemaining { get; internal set; }
    public double CrackDwell { get; internal set; }   // seconds stood on the current crack tile

    private readonly List<StatusEffect> _effects = new();
    public IReadOnlyList<StatusEffect> Effects => _effects;
    internal List<StatusEffect> EffectsInternal => _effects;

    public int PermSpeedLevel  { get; internal set; }
    public int PermVisionLevel { get; internal set; }
    public int PermBlastLevel  { get; internal set; }

    public int StoneCount { get; internal set; }

    public double InvulnerableRemaining { get; internal set; }

    internal Miner(int id, GridPos pos) { Id = id; Pos = pos; }
}
