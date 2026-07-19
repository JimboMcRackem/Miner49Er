using System.Collections.Generic;
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

    // Static terrain lights (lava / vents / crystal-rock walls) never move and their shadowcast
    // field is identical every frame — only the flicker scale varies. Computing a fresh field
    // (a HashSet + Dictionary allocation + recursive shadowcast) for every on-screen glowing tile
    // EVERY frame was the dominant GC/FPS cost on lava floors. We compute each source's field once,
    // key it by origin, and re-add it each frame with only the flicker applied. The cache is
    // invalidated whenever the grid instance changes (new floor) or its Version bumps (any tile
    // mutation: mining, lava creep, quench) — then rebuilt lazily as tiles come back on-screen.
    private readonly Dictionary<GridPos, Dictionary<GridPos, float>> _terrainCache = new();
    private TileGrid? _terrainCacheGrid;
    private int _terrainCacheVersion = -1;

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
        double now = Time.GetTicksMsec() / 1000.0;
        AddPlayerTorches(grid);        // steady — no flicker scale
        AddSourceLights(grid, now);
    }

    private void AddPlayerTorches(TileGrid grid)
    {
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            _lights.AddLight(grid, new GridPos(m.X, m.Y), PlayerTorchRadius);
        }
    }

    private void AddSourceLights(TileGrid grid, double now)
    {
        // Held lanterns / crystal shards — keyed by miner id so the flicker phase
        // doesn't jump as the miner walks tile to tile.
        foreach (var m in _client.Miners)
        {
            if (!m.Alive) continue;
            if (m.Held == (int)ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), LanternRadius,
                    Flicker.Multiplier(HeldSeed(m.Id), now, Flicker.Fire));
            else if (m.Held == (int)ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(m.X, m.Y), CrystalRadius,
                    Flicker.Multiplier(HeldSeed(m.Id), now, Flicker.Crystal));
        }

        // Dropped lanterns / shards on the ground — stationary, keyed by tile.
        foreach (var it in _client.Items)
        {
            if (it.Placement != ItemPlacement.Loose) continue;
            if (it.Kind == ItemKind.Lantern)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), LanternRadius,
                    Flicker.Multiplier(TileSeed(it.X, it.Y), now, Flicker.Fire));
            else if (it.Kind == ItemKind.CrystalShard)
                _lights.AddLight(grid, new GridPos(it.X, it.Y), CrystalRadius,
                    Flicker.Multiplier(TileSeed(it.X, it.Y), now, Flicker.Crystal));
        }

        // A cart carrying a lantern is a moving light source — track its VISUAL tile so the
        // light rolls with the cart during a momentum launch instead of jumping to the far end.
        foreach (var ct in _client.Carts)
            if (ct.Cargo == CartCargo.Lantern)
            {
                var vp = _client.CartVisualPos(ct.Id, ct.X, ct.Y);
                var tile = new GridPos((int)(vp.X / MatchClient.TileSize), (int)(vp.Y / MatchClient.TileSize));
                _lights.AddLight(grid, tile, LanternRadius, Flicker.Multiplier(HeldSeed(ct.Id), now, Flicker.Fire));
            }

        // Lava, vents, and crystal-rock walls glow. These are STATIC sources — their light field
        // is cached (see _terrainCache) and only the per-frame flicker scale varies. Scan only the
        // on-screen window so this stays bounded to viewport size (same order as the darkness draw).
        if (!ReferenceEquals(grid, _terrainCacheGrid) || grid.Version != _terrainCacheVersion)
        {
            _terrainCache.Clear();
            _terrainCacheGrid = grid;
            _terrainCacheVersion = grid.Version;
        }
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
                    _lights.AddField(TerrainField(grid, p, LavaRadius),
                        Flicker.Multiplier(TileSeed(x, y), now, Flicker.Fire));
                else if (t == TileType.CrystalRock)
                    _lights.AddField(TerrainField(grid, p, CrystalRadius),
                        Flicker.Multiplier(TileSeed(x, y), now, Flicker.Crystal));
            }
    }

    // Cached shadowcast field for a static terrain source. Within one grid Version the tile at
    // this origin keeps its type (and therefore its radius), so a field cached by origin is valid
    // until the next Version bump clears the whole cache.
    private Dictionary<GridPos, float> TerrainField(TileGrid grid, GridPos origin, int radius)
    {
        if (!_terrainCache.TryGetValue(origin, out var field))
        {
            field = LightField.Compute(grid, origin, radius);
            _terrainCache[origin] = field;
        }
        return field;
    }

    // Stable per-source flicker seeds. Tile-based for stationary sources; miner-based
    // for held sources so their phase stays continuous while walking.
    private static int TileSeed(int x, int y) => (x * 73856093) ^ (y * 19349663);
    private static int HeldSeed(int minerId) => (minerId * 83492791) ^ unchecked((int)0x9E3779B1);
}
