using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Overlays darkness: unexplored = opaque black, explored-but-not-visible
/// = dim, currently visible = clear.</summary>
public partial class FogRenderer : Node2D
{
    private Main _main = null!;
    private static readonly Color Unexplored = new(0, 0, 0, 1f);
    private static readonly Color Dim = new(0, 0, 0, 0.6f);

    public void Init(Main main) => _main = main;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_main == null) return;
        var grid = _main.Sim.Grid;
        var fog = _main.Fog;
        int ts = Main.TileSize;

        foreach (var p in grid.Positions())
        {
            if (fog.IsVisible(p)) continue; // clear
            var color = fog.IsExplored(p) ? Dim : Unexplored;
            DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), color);
        }
    }
}
