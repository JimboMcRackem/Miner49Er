using Godot;

namespace Miner49er;

/// <summary>Draws ghosts above the darkness overlay (ZIndex -7) so they render full-bright,
/// unaffected by the light-map. All ghost drawing lives in <see cref="WorldRenderer.DrawGhosts"/>;
/// this node just provides a canvas at the right layer. Client-side presentation only.</summary>
public partial class GhostOverlay : Node2D
{
    private WorldRenderer _world = null!;

    public void Init(WorldRenderer world) => _world = world;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_world != null) _world.DrawGhosts(this);
    }
}
