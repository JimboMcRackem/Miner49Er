using Godot;
using Miner49er.Core;

namespace Miner49er;

/// <summary>Darkens the visible area toward a dim ambient floor, then thins that darkness
/// wherever light reaches — a per-tile light-map composited over the world at ZIndex -8
/// (above the world, below ghosts and fog). Every living miner carries a torch; lanterns,
/// crystals, and lava are added in <see cref="AddSourceLights"/>. Colour comes from the
/// world's additive glow halos, which show through where the darkness is thinned.
/// Client-side presentation only.</summary>
public partial class LightRenderer : Node2D
{
    private MatchClient _client = null!;
    private readonly LightMap _lights = new();

    /// <summary>Set true when the local miner is dead — suppresses darkness so the full map reads clearly.</summary>
    public bool SpectatorMode { get; set; }

    // Alpha of full darkness on a visible-but-unlit tile. Lit tiles thin toward 0.
    private const float AmbientDarkAlpha = 0.55f;
    // +1 over the 4-tile blue torch halo (like the lantern's margin) so the interpolated
    // halo edge never slides onto a still-dark tile while the miner is moving.
    private const int   PlayerTorchRadius = 5;
    private const int LanternRadius = 6;
    private const int CrystalRadius = 4;
    private const int LavaRadius    = 3;

    public void Init(MatchClient client) => _client = client;

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_client == null || SpectatorMode) return;
        var grid = _client.Grid;
        var fog  = _client.Fog;
        int ts   = MatchClient.TileSize;

        BuildLights(grid);

        // Only darken on-screen tiles (same viewport-cull the fog renderer uses).
        Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
        int vx0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.X / ts) - 1);
        int vy0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.Y / ts) - 1);
        int vx1 = Mathf.Min(grid.Width  - 1, (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + 1);
        int vy1 = Mathf.Min(grid.Height - 1, (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + 1);

        for (int y = vy0; y <= vy1; y++)
            for (int x = vx0; x <= vx1; x++)
            {
                var p = new GridPos(x, y);
                if (!fog.IsVisible(p)) continue;            // fog owns non-visible darkness
                float bright = _lights.SampleClamped(p);
                float alpha  = AmbientDarkAlpha * (1f - bright);
                if (alpha <= 0.01f) continue;               // fully lit → nothing to draw
                DrawRect(new Rect2(p.X * ts, p.Y * ts, ts, ts), new Color(0f, 0f, 0f, alpha));
            }
    }

    private void BuildLights(TileGrid grid)
    {
        _lights.Clear();
        AddPlayerTorches(grid);
        AddSourceLights(grid);      // extended in Task 4
    }

    private void AddPlayerTorches(TileGrid grid)
    {
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            _lights.AddLight(grid, new GridPos(m.X, m.Y), PlayerTorchRadius);
        }
    }

    private void AddSourceLights(TileGrid grid)
    {
        // Held lanterns / crystal shards.
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            if (m.Held == (int)ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), LanternRadius);
            else if (m.Held == (int)ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), CrystalRadius);
        }

        // Dropped lanterns / shards on the ground.
        foreach (var it in _client.Items)
        {
            if (it.Placement != ItemPlacement.Loose) continue;
            if (it.Kind == ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), LanternRadius);
            else if (it.Kind == ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), CrystalRadius);
        }

        // Lava, vents, and crystal-rock walls glow. Scan only the on-screen window so this
        // stays bounded to viewport size (same order as the darkness draw).
        int ts = MatchClient.TileSize;
        Rect2 vw = GetViewport().CanvasTransform.AffineInverse() * GetViewportRect();
        int vx0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.X / ts) - 1);
        int vy0 = Mathf.Max(0, (int)Mathf.Floor(vw.Position.Y / ts) - 1);
        int vx1 = Mathf.Min(grid.Width  - 1, (int)Mathf.Floor((vw.Position.X + vw.Size.X) / ts) + 1);
        int vy1 = Mathf.Min(grid.Height - 1, (int)Mathf.Floor((vw.Position.Y + vw.Size.Y) / ts) + 1);
        for (int y = vy0; y <= vy1; y++)
            for (int x = vx0; x <= vx1; x++)
            {
                var p = new GridPos(x, y);
                var t = grid.Get(p);
                if (t == TileType.Lava || t == TileType.LavaVent)
                    _lights.AddLight(grid, p, LavaRadius);
                else if (t == TileType.CrystalRock)
                    _lights.AddLight(grid, p, CrystalRadius);
            }
    }
}
