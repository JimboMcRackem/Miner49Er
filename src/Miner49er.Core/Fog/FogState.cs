namespace Miner49er.Core;

public sealed class FogState
{
    private readonly HashSet<GridPos> _explored = new();
    public HashSet<GridPos> Visible { get; private set; } = new();
    public IReadOnlySet<GridPos> Explored => _explored;

    public void Update(HashSet<GridPos> visible)
    {
        Visible = visible;
        foreach (var p in visible) _explored.Add(p);
    }

    public void Reset()
    {
        _explored.Clear();
        Visible = new HashSet<GridPos>();
    }

    public bool IsVisible(GridPos p) => Visible.Contains(p);
    public bool IsExplored(GridPos p) => _explored.Contains(p);
}
