using System.Collections.Generic;

namespace Miner49er.Core;

/// <summary>Accumulates light intensity from many sources into one per-tile field.
/// Overlapping lights sum; <see cref="SampleClamped"/> caps the total at 1.0. The client
/// renderer rebuilds it each frame: <see cref="Clear"/> then one <see cref="AddLight"/>
/// per source. Pure C#; no Godot dependency.</summary>
public sealed class LightMap
{
    private readonly Dictionary<GridPos, float> _acc = new();

    public void Clear() => _acc.Clear();

    public void AddLight(TileGrid grid, GridPos origin, int radius)
    {
        foreach (var (p, intensity) in LightField.Compute(grid, origin, radius))
            _acc[p] = (_acc.TryGetValue(p, out var cur) ? cur : 0f) + intensity;
    }

    /// <summary>Total light at a tile, clamped to [0,1]. Unlit tiles return 0.</summary>
    public float SampleClamped(GridPos p) =>
        _acc.TryGetValue(p, out var v) ? (v > 1f ? 1f : v) : 0f;
}
