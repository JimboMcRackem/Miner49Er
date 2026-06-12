namespace Miner49er.Core;

/// <summary>A timed trap patch dropped by the slow-mold item. Any miner who steps
/// onto its tile is slowed; the patch decays after a configured lifetime.</summary>
public sealed class MoldPatch
{
    public GridPos Pos { get; }
    public double RemainingSeconds { get; internal set; }

    internal MoldPatch(GridPos pos, double seconds) { Pos = pos; RemainingSeconds = seconds; }
}
