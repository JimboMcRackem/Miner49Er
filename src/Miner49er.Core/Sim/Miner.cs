namespace Miner49er.Core;

public enum ActivityKind { None, Mining, Planting }

public sealed class Miner
{
    public int Id { get; }
    public GridPos Pos { get; internal set; }
    public Direction Facing { get; internal set; } = Direction.South;
    public bool Alive { get; internal set; } = true;
    public int GoldCollected { get; internal set; }

    public ActivityKind Activity { get; internal set; } = ActivityKind.None;
    public GridPos ActivityTarget { get; internal set; }
    public double ActivitySecondsRemaining { get; internal set; }

    public double MoveCooldownRemaining { get; internal set; }

    private readonly List<StatusEffect> _effects = new();
    public IReadOnlyList<StatusEffect> Effects => _effects;
    internal List<StatusEffect> EffectsInternal => _effects;

    internal Miner(int id, GridPos pos) { Id = id; Pos = pos; }
}
