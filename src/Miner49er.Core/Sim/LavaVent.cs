namespace Miner49er.Core;

internal sealed class LavaVent
{
    public GridPos Pos { get; init; }
    public bool Active { get; set; }
    public int Budget { get; set; }
    public double Timer { get; set; }
    public List<GridPos> Frontier { get; set; } = new();
}
